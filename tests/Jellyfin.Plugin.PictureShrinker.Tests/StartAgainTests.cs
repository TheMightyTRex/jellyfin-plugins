using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.PictureShrinker.Tests;

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

/// <summary>A fake Jellyfin: a library manager that knows some items, people and libraries, and a provider manager that records each refresh.</summary>
public sealed class FakeJellyfin
{
    public HashSet<Guid> Items { get; } = new();

    public Dictionary<string, Guid> People { get; } = new(StringComparer.Ordinal);

    public List<Guid> Libraries { get; } = new();

    public List<(Guid Id, MetadataRefreshOptions Options, RefreshPriority Priority)> Refreshed { get; } = new();

    public JellyfinRefresher Refresher()
    {
        var library = Fake<ILibraryManager>.Make((m, a) => m.Name switch
        {
            nameof(ILibraryManager.GetItemById) when a.Length == 1 && a[0] is Guid id => Items.Contains(id) ? new Movie { Id = id } : null,
            nameof(ILibraryManager.GetPerson) => People.TryGetValue((string)a[0]!, out var pid) ? new Person { Name = (string)a[0]!, Id = pid } : null,
            nameof(ILibraryManager.GetVirtualFolders) => Libraries.Select(l => new VirtualFolderInfo { ItemId = l.ToString("N") }).ToList(),
            _ => throw new NotSupportedException("The fake library manager has no " + m.Name)
        });
        var providers = Fake<IProviderManager>.Make((m, a) =>
        {
            if (m.Name != nameof(IProviderManager.QueueRefresh))
            {
                throw new NotSupportedException("The fake provider manager has no " + m.Name);
            }

            Refreshed.Add(((Guid)a[0]!, (MetadataRefreshOptions)a[1]!, (RefreshPriority)a[2]!));
            return null;
        });
        return new JellyfinRefresher(library, providers, Fake<IFileSystem>.Make((_, _) => null));
    }
}

public sealed class StartAgainTests : IDisposable
{
    private static readonly Guid _film = Guid.Parse("ab3f1c2d4e5f60718293a4b5c6d7e8f9");
    private static readonly Guid _series = Guid.Parse("cd0123456789abcdef0123456789abcd");
    private static readonly Guid _gone = Guid.Parse("ef0123456789abcdef0123456789abcd");
    private readonly string _dir;
    private readonly string _meta;
    private readonly string _backup;
    private DateTimeOffset _now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    public StartAgainTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ps-again-" + Guid.NewGuid().ToString("N"));
        _meta = Path.Combine(_dir, "metadata");
        _backup = Path.Combine(_dir, "data", "picture-shrinker", "originals");
        var from = Path.Combine(AppContext.BaseDirectory, "pictures", "metadata");
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var t = Path.Combine(_meta, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t);
        }

        // Posters in Jellyfin's own folders, named by the item id, beside the shared test pictures.
        foreach (var id in new[] { _film, _series, _gone })
        {
            var folder = ItemFolder(id);
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(_meta, "library", "0a", "0a1b", "poster.jpg"), Path.Combine(folder, "poster.jpg"));
        }

        File.Copy(Path.Combine(_meta, "library", "0a", "0a1b", "backdrop.jpg"), Path.Combine(ItemFolder(_film), "backdrop.jpg"));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private string ItemFolder(Guid id) => Path.Combine(_meta, "library", id.ToString("N")[..2], id.ToString("N"));

    private string Poster(Guid id) => Path.Combine(ItemFolder(id), "poster.jpg");

    private string Alice => Path.Combine(_meta, "People", "A", "Alice Example", "folder.jpg");

    private ShrinkSettings Settings(string choice = Shrinker.Keep) => new() { MetadataRoot = _meta, Choice = choice, BackupRoot = _backup };

    private Shrinker Make(Func<ShrinkSettings> s) => new(s, new StateStore(Path.Combine(_dir, "state.json")), NullLogger.Instance)
    {
        Now = () => _now,
        Pause = _ => { }
    };

    private static FakeJellyfin Jellyfin()
    {
        var j = new FakeJellyfin();
        j.Items.Add(_film);
        j.Items.Add(_series);
        foreach (var name in new[] { "Alice Example", "Bob Example", "Cara Example", "Dan Example" })
        {
            j.People[name] = Guid.NewGuid();
        }

        return j;
    }

    private static Dictionary<string, string> Hashes(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    // Refused while anything else is going -----------------------------------------------------------------------

    [Theory]
    [InlineData("shrink", "a run is going")]
    [InlineData("restore", "a restore is going")]
    [InlineData("start-again", "another Start again is going")]
    public void StartAgainIsRefusedWhileSomethingIsGoing(string busy, string words)
    {
        var shrinker = Make(() => Settings());
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        var before = Hashes(_dir);
        var jellyfin = Jellyfin();
        using (shrinker.Hold(busy))
        {
            foreach (var kind in new[] { Shrinker.PicturesItShrinks, Shrinker.EveryPicture })
            {
                var result = shrinker.StartAgain(kind, jellyfin.Refresher());
                Assert.False(result.Done);
                Assert.Equal($"Not started again: {words}. Try again when it has finished", result.Said);
            }
        }

        Assert.Equal(before, Hashes(_dir)); // nothing deleted, cleared or moved
        Assert.Empty(jellyfin.Refreshed);
    }

    // What is deleted and cleared ----------------------------------------------------------------------------------

    [Fact]
    public void StartAgainDeletesOnlyTheBackupAndClearsTheRecordsButNotTheChoice()
    {
        var s = Settings(Shrinker.Keep);
        var shrinker = Make(() => s);
        Assert.True(shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None)!.Shrunk > 0);
        shrinker.Store.Update(st => st.LastFullRunMonth = "2026-11");
        var notOurs = Path.Combine(_backup, "notes.txt");
        File.WriteAllText(notOurs, "not a picture the shrinker made");
        var outside = Path.Combine(_dir, "keep-me.jpg");
        File.WriteAllText(outside, "outside");
        File.CreateSymbolicLink(Path.Combine(_backup, "library", "link.jpg"), outside);
        var live = Hashes(_meta);

        var result = shrinker.StartAgain(Shrinker.EveryPicture, Jellyfin().Refresher());

        Assert.True(result.Done);
        Assert.Equal(15, result.Record!.BackupFiles); // the fourteen originals (a film backdrop among them) and the link itself
        Assert.Equal(1, Directory.GetFiles(_backup, "*", SearchOption.AllDirectories).Length);
        Assert.True(File.Exists(notOurs));
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.Equal(live, Hashes(_meta)); // every picture: nothing is moved aside
        var st = shrinker.Store.Load();
        Assert.Empty(st.Runs);
        Assert.Equal(0, st.Shrunk);
        Assert.Equal(0, st.Before - st.After);
        Assert.Equal(string.Empty, st.LastFullRunMonth);
        Assert.Null(st.LastDelete);
        Assert.Equal(Shrinker.EveryPicture, st.LastStartAgain!.Kind);
        Assert.Equal(Shrinker.Keep, s.Choice); // the choice is kept: the next run backs up again
        Assert.Equal(0, shrinker.Measure().Saved);
        Assert.StartsWith("Starting again: the backup is deleted and Jellyfin is downloading every picture", result.Said, StringComparison.Ordinal);
    }

    [Fact]
    public void StartAgainWorksBeforeTheChoiceIsMade()
    {
        var shrinker = Make(() => Settings(string.Empty));
        var result = shrinker.StartAgain(Shrinker.PicturesItShrinks, Jellyfin().Refresher());
        Assert.True(result.Done);
        Assert.StartsWith("Starting again: there was no backup to delete and Jellyfin is downloading 8 pictures again", result.Said, StringComparison.Ordinal);
    }

    // Posters and photos -------------------------------------------------------------------------------------------

    [Fact]
    public void PicturesItShrinksAreMovedAsideAndTheirItemsRefreshed()
    {
        var link = Path.Combine(_meta, "library", "12", "12345678901234567890123456789012");
        Directory.CreateDirectory(link);
        File.CreateSymbolicLink(Path.Combine(link, "poster.jpg"), Poster(_film));
        var shared = Path.Combine(_meta, "library", "0a", "0a1b", "poster.jpg");
        var sharedBytes = File.ReadAllBytes(shared);
        var aliceBytes = File.ReadAllBytes(Alice);
        var jellyfin = Jellyfin();
        var shrinker = Make(() => Settings());

        var result = shrinker.StartAgain(Shrinker.PicturesItShrinks, jellyfin.Refresher());

        Assert.True(result.Done);
        // Moved aside to a hidden file in the same folder.
        Assert.False(File.Exists(Poster(_film)));
        Assert.True(File.Exists(Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old")));
        Assert.False(File.Exists(Path.Combine(ItemFolder(_film), "backdrop.jpg"))); // backdrops too, though not ticked
        Assert.True(File.Exists(Path.Combine(ItemFolder(_film), ".backdrop.jpg.picture-shrinker-old")));
        Assert.False(File.Exists(Alice));
        Assert.Equal(aliceBytes, File.ReadAllBytes(Path.Combine(_meta, "People", "A", "Alice Example", ".folder.jpg.picture-shrinker-old")));
        // Not moved: a folder that is not an item id, and a picture that is a link.
        Assert.Equal(sharedBytes, File.ReadAllBytes(shared));
        Assert.NotNull(new FileInfo(Path.Combine(link, "poster.jpg")).LinkTarget);

        // The item ids and the people, each once, with replace all images false, at low priority.
        var people = jellyfin.People.ToDictionary(p => p.Value, p => p.Key);
        Assert.Equal(new[] { _film, _series }.OrderBy(g => g), jellyfin.Refreshed.Select(r => r.Id).Where(id => !people.ContainsKey(id)).OrderBy(g => g));
        Assert.Equal(new[] { "Alice Example", "Bob Example", "Cara Example", "Dan Example" }, jellyfin.Refreshed.Where(r => people.ContainsKey(r.Id)).Select(r => people[r.Id]).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(jellyfin.Refreshed, r =>
        {
            Assert.False(r.Options.ReplaceAllImages);
            Assert.Equal(MetadataRefreshMode.FullRefresh, r.Options.ImageRefreshMode);
            Assert.Equal(MetadataRefreshMode.Default, r.Options.MetadataRefreshMode);
            Assert.False(r.Options.ReplaceAllMetadata);
            Assert.Equal(RefreshPriority.Low, r.Priority);
        });

        // An item no longer in Jellyfin cannot get a new poster, so its own is put back at once.
        Assert.True(File.Exists(Poster(_gone)));
        Assert.Equal(1, result.Record!.NotFound);
        var st = shrinker.Store.Load();
        Assert.Equal(7, st.Aside.Count); // two posters, a backdrop and four photos waiting
        Assert.Equal(8, result.Record.MovedAside);
        Assert.Contains("Jellyfin is downloading 8 pictures again", result.Said, StringComparison.Ordinal);
    }

    [Fact]
    public void StartAgainCoversEveryKindIncludingBackdropsWhateverIsTicked()
    {
        // Start again is a reset: every kind is moved aside, even with Shrink all picture types off and nothing ticked.
        var nothingTicked = new ShrinkSettings
        {
            MetadataRoot = _meta, Choice = Shrinker.Keep, BackupRoot = _backup, ShrinkAll = false,
            Posters = false, People = false, Backdrops = false, Thumbs = false, Logos = false, Banners = false, Discs = false, Clearart = false
        };
        var jellyfin = Jellyfin();
        var shrinker = Make(() => nothingTicked);
        var backdrop = Path.Combine(ItemFolder(_film), "backdrop.jpg");
        var old = File.ReadAllBytes(backdrop);
        foreach (var name in new[] { "logo.png", "landscape.jpg", "banner.jpg", "disc.png", "clearart.png" })
        {
            File.WriteAllText(Path.Combine(ItemFolder(_series), name), name);
        }

        var result = shrinker.StartAgain(Shrinker.PicturesItShrinks, jellyfin.Refresher());

        Assert.Equal(13, result.Record!.MovedAside);
        foreach (var name in new[] { "logo.png", "landscape.jpg", "banner.jpg", "disc.png", "clearart.png" })
        {
            Assert.Equal(name, File.ReadAllText(Path.Combine(ItemFolder(_series), "." + name + ".picture-shrinker-old")));
        }

        Assert.Single(jellyfin.Refreshed, r => r.Id == _series); // its poster and five other pictures: one refresh
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(ItemFolder(_film), ".backdrop.jpg.picture-shrinker-old")));
        Assert.False(File.Exists(Alice));
        var film = Assert.Single(jellyfin.Refreshed, r => r.Id == _film); // its poster and backdrop: one refresh
        Assert.False(film.Options.ReplaceAllImages);
        Assert.Contains(shrinker.Store.Load().Aside, a => a.Path == backdrop && a.Kind == "backdrop");

        // Jellyfin's new backdrop may have another number: any visible backdrop picture replaces the old one.
        File.WriteAllText(Path.Combine(ItemFolder(_film), "backdrop1.jpg"), "a new backdrop from Jellyfin");
        Assert.Equal(1, shrinker.Settle()!.Replaced);
        Assert.False(File.Exists(Path.Combine(ItemFolder(_film), ".backdrop.jpg.picture-shrinker-old")));
        Assert.True(File.Exists(Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old"))); // a backdrop is not a poster
    }

    // Settling the pictures moved aside ------------------------------------------------------------------------------

    [Fact]
    public void SettlingDeletesTheOldPictureOnceJellyfinHasANewOne()
    {
        var shrinker = Make(() => Settings());
        shrinker.StartAgain(Shrinker.PicturesItShrinks, Jellyfin().Refresher());
        File.WriteAllText(Path.Combine(ItemFolder(_film), "poster.png"), "a new poster from Jellyfin"); // any poster.*
        File.WriteAllText(Path.Combine(_meta, "People", "A", "Alice Example", "folder.webp"), "a new photo"); // any picture
        File.WriteAllText(Path.Combine(ItemFolder(_series), ".poster.jpg.picture-shrinker-tmp"), "not a new poster: hidden");

        var got = shrinker.Settle()!;

        Assert.Equal(2, got.Replaced);
        Assert.Equal(0, got.PutBack);
        Assert.Equal(5, got.Waiting);
        Assert.True(File.Exists(Path.Combine(ItemFolder(_film), ".backdrop.jpg.picture-shrinker-old"))); // a new poster is not a new backdrop
        Assert.False(File.Exists(Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old")));
        Assert.False(File.Exists(Path.Combine(_meta, "People", "A", "Alice Example", ".folder.jpg.picture-shrinker-old")));
        Assert.True(File.Exists(Path.Combine(ItemFolder(_series), ".poster.jpg.picture-shrinker-old")));
        Assert.Equal(2, shrinker.Store.Load().Replaced);
    }

    [Fact]
    public void SettlingPutsTheOldPictureBackAfterADay()
    {
        var shrinker = Make(() => Settings());
        var original = File.ReadAllBytes(Poster(_series));
        shrinker.StartAgain(Shrinker.PicturesItShrinks, Jellyfin().Refresher());

        _now = _now.AddHours(23);
        Assert.Equal(new SettleResult(0, 0, 7), shrinker.Settle());
        Assert.False(File.Exists(Poster(_series)));

        _now = _now.AddHours(1);
        Assert.Equal(new SettleResult(0, 7, 0), shrinker.Settle());
        Assert.Equal(original, File.ReadAllBytes(Poster(_series)));
        Assert.True(File.Exists(Alice));
        Assert.Empty(Directory.GetFiles(_meta, "*.picture-shrinker-old", SearchOption.AllDirectories));
        Assert.Equal(7, shrinker.Store.Load().PutBack);
    }

    [Fact]
    public void SettlingNeverTouchesAPictureThatIsALink()
    {
        var shrinker = Make(() => Settings());
        var outside = Path.Combine(_dir, "not-in-jellyfin.jpg");
        File.WriteAllText(outside, "outside");
        var aside = Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old");
        File.Delete(Poster(_film));
        File.CreateSymbolicLink(aside, outside);
        File.WriteAllText(Poster(_film), "a new poster from Jellyfin");
        shrinker.Store.Update(st => st.Aside.Add(new AsideRecord { Path = Poster(_film), Kind = "poster", At = _now.AddDays(-2) }));

        var got = shrinker.Settle()!;

        Assert.Equal(new SettleResult(0, 0, 0), got);
        Assert.NotNull(new FileInfo(aside).LinkTarget); // the link is still there
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.Equal("a new poster from Jellyfin", File.ReadAllText(Poster(_film)));
    }

    [Fact]
    public void ARunSettlesFirst()
    {
        var shrinker = Make(() => Settings(Shrinker.JustShrink));
        shrinker.StartAgain(Shrinker.PicturesItShrinks, Jellyfin().Refresher());
        File.Copy(Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old"), Poster(_film));
        shrinker.Run("shrink", TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old")));
        Assert.Equal(1, shrinker.Store.Load().Replaced);
    }

    [Fact]
    public void TheWaitingListSurvivesARestart()
    {
        Make(() => Settings()).StartAgain(Shrinker.PicturesItShrinks, Jellyfin().Refresher());
        var again = Make(() => Settings()); // a new shrinker reads the same state file, as after a restart
        _now = _now.AddHours(25);
        Assert.Equal(7, again.Settle()!.PutBack);
        Assert.True(File.Exists(Poster(_film)));
    }

    // Every picture --------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryPictureRefreshesEachLibraryAndEachPersonReplacingAll()
    {
        var films = Guid.NewGuid();
        var shows = Guid.NewGuid();
        var jellyfin = Jellyfin();
        jellyfin.Libraries.AddRange(new[] { films, shows });
        jellyfin.Items.Add(films);
        jellyfin.Items.Add(shows);
        var paused = 0;
        var shrinker = Make(() => Settings());
        shrinker.Pause = t =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(50), t);
            paused++;
        };
        var live = Hashes(_meta);

        var result = shrinker.StartAgain(Shrinker.EveryPicture, jellyfin.Refresher());

        Assert.True(result.Done);
        Assert.Equal(live, Hashes(_meta));
        Assert.Equal(new[] { films, shows }, jellyfin.Refreshed.Take(2).Select(r => r.Id)); // each library, which Jellyfin refreshes with all in it
        var people = jellyfin.People.ToDictionary(p => p.Value, p => p.Key);
        Assert.Equal(new[] { "Alice Example", "Bob Example", "Cara Example", "Dan Example" }, jellyfin.Refreshed.Skip(2).Select(r => people[r.Id]));
        Assert.All(jellyfin.Refreshed, r =>
        {
            Assert.True(r.Options.ReplaceAllImages);
            Assert.Equal(MetadataRefreshMode.FullRefresh, r.Options.ImageRefreshMode);
            Assert.Equal(MetadataRefreshMode.Default, r.Options.MetadataRefreshMode);
            Assert.Equal(RefreshPriority.Low, r.Priority);
        });
        Assert.Equal(5, paused); // a pause between each two of the six refreshes
        Assert.Equal(2, result.Record!.Libraries);
        Assert.Equal(4, result.Record.People);
        Assert.Empty(shrinker.Store.Load().Aside);
    }

    // Finding the item of a picture ------------------------------------------------------------------------------

    [Fact]
    public void EachPictureIsMatchedToItsItemOrPerson()
    {
        Assert.Equal(new RefreshTarget(_film, null), Shrinker.TargetOf(_meta, Poster(_film)));
        Assert.Equal(new RefreshTarget(Guid.Empty, "Alice Example"), Shrinker.TargetOf(_meta, Alice));
        Assert.Equal(new RefreshTarget(Guid.Empty, "!!!"), Shrinker.TargetOf(_meta, Path.Combine(_meta, "People", "!!!", "folder.jpg")));
        Assert.Null(Shrinker.TargetOf(_meta, Path.Combine(_meta, "library", "0a", "0a1b", "poster.jpg")));
        Assert.Null(Shrinker.TargetOf(_meta, Path.Combine(_meta, "library", "zz", _film.ToString("N"), "poster.jpg")));
        Assert.Equal(Path.Combine(ItemFolder(_film), ".poster.jpg.picture-shrinker-old"), Shrinker.AsidePathOf(Poster(_film)));
    }
}
