using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.PictureShrinker;

/// <summary>
/// Start again's requests to Jellyfin: each one is a refresh in Jellyfin's own queue, at low priority, that refreshes
/// the pictures in full and fills in only missing metadata. With replace all images false, Jellyfin downloads only the
/// pictures that are missing (the ones moved aside); with it true, it replaces every picture. A refresh of a library,
/// or of any folder such as a series, also refreshes everything in it, as Jellyfin's own Refresh metadata does.
/// </summary>
public sealed class JellyfinRefresher : IPictureRefresher
{
    private readonly ILibraryManager _library;
    private readonly IProviderManager _providers;
    private readonly IFileSystem _fileSystem;

    public JellyfinRefresher(ILibraryManager library, IProviderManager providers, IFileSystem fileSystem)
    {
        _library = library;
        _providers = providers;
        _fileSystem = fileSystem;
    }

    public IReadOnlyList<Guid> Libraries()
        => _library.GetVirtualFolders()
            .Select(f => Guid.TryParse(f.ItemId, CultureInfo.InvariantCulture, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

    public bool RefreshItem(Guid id, bool replaceAllImages)
    {
        var item = id == Guid.Empty ? null : _library.GetItemById(id);
        if (item is null)
        {
            return false;
        }

        _providers.QueueRefresh(item.Id, Options(replaceAllImages), RefreshPriority.Low);
        return true;
    }

    public bool RefreshPerson(string name, bool replaceAllImages)
    {
        var person = _library.GetPerson(name);
        if (person is null)
        {
            return false;
        }

        _providers.QueueRefresh(person.Id, Options(replaceAllImages), RefreshPriority.Low);
        return true;
    }

    /// <summary>Pictures in full, metadata only where it is missing, replace all images as asked.</summary>
    internal MetadataRefreshOptions Options(bool replaceAllImages) => new(new DirectoryService(_fileSystem))
    {
        MetadataRefreshMode = MetadataRefreshMode.Default,
        ImageRefreshMode = MetadataRefreshMode.FullRefresh,
        ReplaceAllImages = replaceAllImages,
        ReplaceAllMetadata = false,
        ForceSave = true,
        IsAutomated = false
    };
}
