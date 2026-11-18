using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AudioDescription.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>
/// Find and tag: reads the sound tracks of every film and episode through Jellyfin's own library, tags each one with a
/// described track, and its series, with the tag in rules.json, and takes the tag off when the track goes. While the
/// collection is active, the films and series go into the collection named in rules.json too. Every Jellyfin app,
/// televisions included, shows tags and collections, so this works everywhere. It reads ids a page at a time and
/// writes only the items whose tag changes.
/// </summary>
public sealed class Tagger
{
    private readonly ILibraryManager _library;
    private readonly IMediaSourceManager _media;
    private readonly ICollectionManager _collections;
    private readonly Func<PluginConfiguration> _config;
    private readonly StateStore _store;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly ConcurrentDictionary<Guid, DateTime> _pending = new();

    public Tagger(ILibraryManager library, IMediaSourceManager media, ICollectionManager collections, Func<PluginConfiguration> config, StateStore store, ILogger log)
    {
        _library = library;
        _media = media;
        _collections = collections;
        _config = config;
        _store = store;
        _log = log;
    }

    public static string Tag => Rules.Current.Tag;

    public static string CollectionName => Rules.Current.Collection.Name;

    public bool Running => _one.CurrentCount == 0;

    /// <summary>Whether an item has a described sound track, by Jellyfin's own record of its streams.</summary>
    public bool HasDescribedTrack(BaseItem item) => Words.DescribedTrack(_media.GetMediaStreams(item.Id)) is not null;

    /// <summary>Titles with audio description: every tagged film and episode with a described track, by series.</summary>
    public TitlesAnswer Titles(DateTimeOffset? checkedAt)
        => DescribedTitles.Build(
            _library,
            id => Words.DescribedTrack(_media.GetMediaStreams(id)) is { } s ? (s.DisplayTitle ?? s.Title ?? "Audio description") : null,
            checkedAt,
            _config().FilmsShown,
            _config().SeriesShown);

    /// <summary>A whole pass over the library. Returns null when a pass is already running.</summary>
    public async Task<TagRecord?> RunAsync(string why, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _one.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rec = new TagRecord { At = DateTimeOffset.Now, Why = why };
        try
        {
            var ids = _library.GetItemIds(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                Recursive = true,
                IsVirtualItem = false
            });
            var describedSeries = new HashSet<Guid>();
            var describedFilms = new List<Guid>();
            for (var i = 0; i < ids.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = _library.GetItemById(ids[i]);
                if (item is null)
                {
                    continue;
                }

                rec.Checked++;
                var has = HasDescribedTrack(item);
                if (has && item is Episode ep)
                {
                    rec.Episodes++;
                    if (ep.SeriesId != Guid.Empty)
                    {
                        describedSeries.Add(ep.SeriesId);
                    }
                }
                else if (has)
                {
                    rec.Films++;
                    describedFilms.Add(item.Id);
                }

                await SetTagAsync(item, has, rec, cancellationToken).ConfigureAwait(false);
                progress?.Report(90.0 * (i + 1) / Math.Max(1, ids.Count));
            }

            foreach (var sid in _library.GetItemIds(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true }))
            {
                var series = _library.GetItemById(sid);
                if (series is not null)
                {
                    await SetTagAsync(series, describedSeries.Contains(sid), rec, cancellationToken).ConfigureAwait(false);
                }
            }

            rec.Series = describedSeries.Count;
            if (_config().Collection)
            {
                await KeepCollectionAsync(describedFilms.Concat(describedSeries).ToHashSet(), rec).ConfigureAwait(false);
            }

            progress?.Report(100);
            _log.LogInformation(
                "Audio description: found {Films} films, {Episodes} episodes and {Series} series with a described track; tagged {Tagged}, took the tag off {Untagged}",
                rec.Films,
                rec.Episodes,
                rec.Series,
                rec.Tagged,
                rec.Untagged);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            rec.Problem = e.Message;
            _log.LogWarning(e, "Audio description: Find and tag did not finish");
        }
        finally
        {
            _store.Update(s => s.LastTag = rec);
            _one.Release();
        }

        return rec;
    }

    /// <summary>
    /// After a track is added, Jellyfin reads the film again in its own time. The film is noted here, and tagged when
    /// Jellyfin next says it changed (ItemUpdated), without a whole pass.
    /// </summary>
    public void Expect(Guid itemId) => _pending[itemId] = DateTime.UtcNow;

    public bool IsExpected(Guid itemId)
    {
        foreach (var old in _pending.Where(kv => DateTime.UtcNow - kv.Value > TimeSpan.FromDays(1)).Select(kv => kv.Key).ToList())
        {
            _pending.TryRemove(old, out _);
        }

        return _pending.ContainsKey(itemId);
    }

    /// <summary>Tags one item that Jellyfin has just read again, its series too, and adds it to the collection.</summary>
    public async Task<bool> TagOneAsync(BaseItem item, CancellationToken cancellationToken)
    {
        if (!HasDescribedTrack(item))
        {
            return false;
        }

        _pending.TryRemove(item.Id, out _);
        var rec = new TagRecord();
        await SetTagAsync(item, true, rec, cancellationToken).ConfigureAwait(false);
        var forCollection = item.Id;
        if (item is Episode ep && ep.SeriesId != Guid.Empty && _library.GetItemById(ep.SeriesId) is { } series)
        {
            await SetTagAsync(series, true, rec, cancellationToken).ConfigureAwait(false);
            forCollection = series.Id;
        }

        if (_config().Collection)
        {
            await AddToCollectionAsync(new[] { forCollection }).ConfigureAwait(false);
        }

        return true;
    }

    private async Task SetTagAsync(BaseItem item, bool want, TagRecord rec, CancellationToken cancellationToken)
    {
        var tags = item.Tags ?? Array.Empty<string>();
        var has = tags.Contains(Tag, StringComparer.OrdinalIgnoreCase);
        if (want == has)
        {
            return;
        }

        item.Tags = want ? tags.Append(Tag).ToArray() : tags.Where(t => !string.Equals(t, Tag, StringComparison.OrdinalIgnoreCase)).ToArray();
        await _library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        if (want)
        {
            rec.Tagged++;
        }
        else
        {
            rec.Untagged++;
        }
    }

    /// <summary>The collection named in rules.json, or null when there is none yet.</summary>
    public BoxSet? FindCollection()
        => _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.BoxSet }, Recursive = true, Name = CollectionName })
            .OfType<BoxSet>()
            .FirstOrDefault(b => string.Equals(b.Name, CollectionName, StringComparison.OrdinalIgnoreCase));

    private static HashSet<Guid> Members(BoxSet box)
        => (box.LinkedChildren ?? Array.Empty<LinkedChild>()).Where(c => c.ItemId is { } id && id != Guid.Empty).Select(c => c.ItemId!.Value).ToHashSet();

    private async Task KeepCollectionAsync(HashSet<Guid> want, TagRecord rec)
    {
        var box = FindCollection();
        if (box is null)
        {
            if (want.Count == 0)
            {
                return;
            }

            await _collections.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = CollectionName,
                IsLocked = false,
                ItemIdList = want.Select(g => g.ToString("N")).ToArray()
            }).ConfigureAwait(false);
            rec.AddedToCollection = want.Count;
            return;
        }

        var now = Members(box);
        var add = want.Where(id => !now.Contains(id)).ToList();
        var take = now.Where(id => !want.Contains(id)).ToList();
        if (add.Count > 0)
        {
            await _collections.AddToCollectionAsync(box.Id, add).ConfigureAwait(false);
        }

        if (take.Count > 0)
        {
            await _collections.RemoveFromCollectionAsync(box.Id, take).ConfigureAwait(false);
        }

        rec.AddedToCollection = add.Count;
        rec.TakenFromCollection = take.Count;
    }

    private async Task AddToCollectionAsync(IReadOnlyList<Guid> ids)
    {
        var box = FindCollection();
        if (box is null)
        {
            await _collections.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = CollectionName,
                IsLocked = false,
                ItemIdList = ids.Select(g => g.ToString("N")).ToArray()
            }).ConfigureAwait(false);
            return;
        }

        var now = Members(box);
        var add = ids.Where(id => !now.Contains(id)).ToList();
        if (add.Count > 0)
        {
            await _collections.AddToCollectionAsync(box.Id, add).ConfigureAwait(false);
        }
    }
}
