using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>A stand-in for any Jellyfin interface: each call goes to one function.</summary>
public class Fake<T> : DispatchProxy
    where T : class
{
    private Func<MethodInfo, object?[], object?> _handler = (_, _) => null;

    public static T Make(Func<MethodInfo, object?[], object?> handler)
    {
        var proxy = Create<T, Fake<T>>();
        ((Fake<T>)(object)proxy)._handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!, args ?? Array.Empty<object?>());
}

/// <summary>A logger that keeps what was said, for the tests that check the log.</summary>
public sealed class ListLogger : ILogger
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Lines.Add(formatter(state, exception));
}

/// <summary>
/// A fake Jellyfin: a library manager that knows some films, episodes, series and collections, a media source manager
/// that knows each item's streams, a collection manager and a provider manager that record what they are asked.
/// </summary>
public sealed class FakeJellyfin
{
    public Dictionary<Guid, BaseItem> Items { get; } = new();

    public Dictionary<Guid, List<MediaStream>> Streams { get; } = new();

    public List<Guid> Updated { get; } = new();

    public List<string> CollectionCalls { get; } = new();

    public List<Guid> Refreshed { get; } = new();

    public static MediaStream Audio(int index, string title) => new() { Type = MediaStreamType.Audio, Index = index, Title = title };

    public Movie Film(string name, int year, string path = "", params MediaStream[] streams)
    {
        var m = new Movie { Id = Guid.NewGuid(), Name = name, ProductionYear = year, Path = path };
        Add(m, streams.Length > 0 ? streams : new[] { Audio(1, "English") });
        return m;
    }

    public Series Show(string name)
    {
        var s = new Series { Id = Guid.NewGuid(), Name = name };
        Add(s, Array.Empty<MediaStream>());
        return s;
    }

    public Episode Ep(Series series, int season, int number, string path = "", params MediaStream[] streams)
    {
        var e = new Episode { Id = Guid.NewGuid(), Name = $"Episode {number}", SeriesId = series.Id, SeriesName = series.Name, ParentIndexNumber = season, IndexNumber = number, Path = path };
        Add(e, streams.Length > 0 ? streams : new[] { Audio(1, "English") });
        return e;
    }

    public void Add(BaseItem item, IEnumerable<MediaStream> streams)
    {
        Items[item.Id] = item;
        Streams[item.Id] = streams.ToList();
    }

    public IEnumerable<BoxSet> Boxes => Items.Values.OfType<BoxSet>();

    private static bool Is(BaseItem i, BaseItemKind k) => k switch
    {
        BaseItemKind.Movie => i is Movie,
        BaseItemKind.Episode => i is Episode,
        BaseItemKind.Series => i is Series,
        BaseItemKind.BoxSet => i is BoxSet,
        _ => false
    };

    private IEnumerable<BaseItem> Query(InternalItemsQuery q)
    {
        var items = Items.Values.Where(i => q.IncludeItemTypes.Any(k => Is(i, k)));
        if (!string.IsNullOrEmpty(q.Name))
        {
            items = items.Where(i => string.Equals(i.Name, q.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(q.SearchTerm))
        {
            var first = q.SearchTerm.Split(' ')[0];
            items = items.Where(i => i.Name.Contains(first, StringComparison.OrdinalIgnoreCase));
        }

        if (q.Tags.Length > 0)
        {
            items = items.Where(i => (i.Tags ?? Array.Empty<string>()).Any(t => q.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)));
        }

        if (q.AncestorIds.Length > 0)
        {
            items = items.Where(i => i is Episode e && q.AncestorIds.Contains(e.SeriesId));
        }

        if (q.ParentIndexNumber is int season)
        {
            items = items.Where(i => i.ParentIndexNumber == season);
        }

        if (q.OrderBy.Count > 0 && q.OrderBy[0].OrderBy == ItemSortBy.DateCreated)
        {
            items = items.OrderByDescending(i => i.DateCreated).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
        }

        items = items.Skip(q.StartIndex ?? 0);
        return q.Limit is int n ? items.Take(n) : items;
    }

    public ILibraryManager Library() => Fake<ILibraryManager>.Make((m, a) => m.Name switch
    {
        nameof(ILibraryManager.GetCount) => Query((InternalItemsQuery)a[0]!).Count(),
        nameof(ILibraryManager.GetItemIds) => Query((InternalItemsQuery)a[0]!).Select(i => i.Id).ToList(),
        nameof(ILibraryManager.GetItemList) when a.Length == 1 => Query((InternalItemsQuery)a[0]!).ToList(),
        nameof(ILibraryManager.GetItemById) when a.Length >= 1 && a[0] is Guid id => Items.GetValueOrDefault(id),
        nameof(ILibraryManager.UpdateItemAsync) => Record((BaseItem)a[0]!),
        _ => throw new NotSupportedException("The fake library manager has no " + m.Name)
    });

    private Task Record(BaseItem item)
    {
        Updated.Add(item.Id);
        return Task.CompletedTask;
    }

    public IMediaSourceManager Media() => Fake<IMediaSourceManager>.Make((m, a) => m.Name switch
    {
        nameof(IMediaSourceManager.GetMediaStreams) when a.Length == 1 && a[0] is Guid id => Streams.GetValueOrDefault(id)?.ToList() ?? new List<MediaStream>(),
        _ => throw new NotSupportedException("The fake media source manager has no " + m.Name)
    });

    public ICollectionManager Collections() => Fake<ICollectionManager>.Make((m, a) =>
    {
        switch (m.Name)
        {
            case nameof(ICollectionManager.CreateCollectionAsync):
                var o = (CollectionCreationOptions)a[0]!;
                var box = new BoxSet
                {
                    Id = Guid.NewGuid(),
                    Name = o.Name,
                    LinkedChildren = o.ItemIdList.Select(s => new LinkedChild { ItemId = Guid.Parse(s) }).ToArray()
                };
                Items[box.Id] = box;
                CollectionCalls.Add("create " + o.Name + " " + o.ItemIdList.Count);
                return Task.FromResult(box);
            case nameof(ICollectionManager.AddToCollectionAsync):
                var add = Items[(Guid)a[0]!] as BoxSet;
                var ids = ((IEnumerable<Guid>)a[1]!).ToList();
                add!.LinkedChildren = add.LinkedChildren.Concat(ids.Select(i => new LinkedChild { ItemId = i })).ToArray();
                CollectionCalls.Add("add " + ids.Count);
                return Task.CompletedTask;
            case nameof(ICollectionManager.RemoveFromCollectionAsync):
                var take = Items[(Guid)a[0]!] as BoxSet;
                var gone = ((IEnumerable<Guid>)a[1]!).ToHashSet();
                take!.LinkedChildren = take.LinkedChildren.Where(c => !gone.Contains(c.ItemId ?? Guid.Empty)).ToArray();
                CollectionCalls.Add("remove " + gone.Count);
                return Task.CompletedTask;
            default:
                throw new NotSupportedException("The fake collection manager has no " + m.Name);
        }
    });

    public IProviderManager Providers() => Fake<IProviderManager>.Make((m, a) =>
    {
        if (m.Name != nameof(IProviderManager.QueueRefresh))
        {
            throw new NotSupportedException("The fake provider manager has no " + m.Name);
        }

        Refreshed.Add((Guid)a[0]!);
        return null;
    });
}
