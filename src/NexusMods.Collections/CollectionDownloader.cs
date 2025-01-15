using System.Diagnostics;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Collections;
using NexusMods.Abstractions.Jobs;
using NexusMods.Abstractions.Library;
using NexusMods.Abstractions.Library.Models;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.NexusModsLibrary;
using NexusMods.Abstractions.NexusModsLibrary.Models;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.CrossPlatform.Process;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Abstractions.DatomIterators;
using NexusMods.MnemonicDB.Abstractions.IndexSegments;
using NexusMods.MnemonicDB.Abstractions.Query;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Networking.NexusWebApi;
using NexusMods.Paths;
using OneOf;
using Reloaded.Memory.Extensions;

namespace NexusMods.Collections;

/// <summary>
/// Methods for collection downloads.
/// </summary>
[PublicAPI]
public class CollectionDownloader
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IConnection _connection;
    private readonly ILoginManager _loginManager;
    private readonly TemporaryFileManager _temporaryFileManager;
    private readonly NexusModsLibrary _nexusModsLibrary;
    private readonly ILibraryService _libraryService;
    private readonly IOSInterop _osInterop;
    private readonly HttpClient _httpClient;
    private readonly IJobMonitor _jobMonitor;

    /// <summary>
    /// Constructor.
    /// </summary>
    public CollectionDownloader(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<CollectionDownloader>>();
        _connection = serviceProvider.GetRequiredService<IConnection>();
        _loginManager = serviceProvider.GetRequiredService<ILoginManager>();
        _temporaryFileManager = serviceProvider.GetRequiredService<TemporaryFileManager>();
        _nexusModsLibrary = serviceProvider.GetRequiredService<NexusModsLibrary>();
        _libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        _osInterop = serviceProvider.GetRequiredService<IOSInterop>();
        _httpClient = serviceProvider.GetRequiredService<HttpClient>();
        _jobMonitor = serviceProvider.GetRequiredService<IJobMonitor>();
    }

    private async ValueTask<bool> CanDirectDownload(CollectionDownloadExternal.ReadOnly download, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Testing if `{Uri}` can be downloaded directly", download.Uri);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, download.Uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("application/"))
            {
                _logger.LogInformation("Download at `{Uri}` can't be downloaded automatically because Content-Type `{ContentType}` doesn't indicate a binary download", download.Uri, contentType);
                return false;
            }

            if (!response.Content.Headers.ContentLength.HasValue)
            {
                _logger.LogInformation("Download at `{Uri}` can't be downloaded automatically because the response doesn't have a Content-Length", download.Uri);
                return false;
            }

            var size = Size.FromLong(response.Content.Headers.ContentLength.Value);
            if (size != download.Size)
            {
                _logger.LogWarning("Download at `{Uri}` can't be downloaded automatically because the Content-Length `{ContentLength}` doesn't match the expected size `{ExpectedSize}`", download.Uri, size, download.Size);
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception while checking if `{Uri}` can be downloaded directly", download.Uri);
            return false;
        }
    }

    /// <summary>
    /// Downloads an external file.
    /// </summary>
    public async ValueTask Download(CollectionDownloadExternal.ReadOnly download, bool onlyDirectDownloads, CancellationToken cancellationToken)
    {
        if (await CanDirectDownload(download, cancellationToken))
        {
            _logger.LogInformation("Downloading external file at `{Uri}` directly", download.Uri);
            var job = ExternalDownloadJob.Create(_serviceProvider, download.Uri, download.Md5, download.AsCollectionDownload().Name);
            await _libraryService.AddDownload(job);
        }
        else
        {
            if (onlyDirectDownloads) return;

            _logger.LogInformation("Unable to direct download `{Uri}`, using browse as a fallback", download.Uri);
            await _osInterop.OpenUrl(download.Uri, logOutput: false, fireAndForget: true, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Downloads an external file or opens the browser if the file can't be downloaded automatically.
    /// </summary>
    public ValueTask Download(CollectionDownloadExternal.ReadOnly download, CancellationToken cancellationToken)
    {
        return Download(download, onlyDirectDownloads: false, cancellationToken);
    }

    /// <summary>
    /// Downloads a file from nexus mods for premium users or opens the download page in the browser.
    /// </summary>
    public async ValueTask Download(CollectionDownloadNexusMods.ReadOnly download, CancellationToken cancellationToken)
    {
        if (_loginManager.IsPremium)
        {
            await using var tempPath = _temporaryFileManager.CreateFile();
            var job = await _nexusModsLibrary.CreateDownloadJob(tempPath, download.FileMetadata, cancellationToken: cancellationToken);
            await _libraryService.AddDownload(job);
        }
        else
        {
            await _osInterop.OpenUrl(download.FileMetadata.GetUri(), logOutput: false, fireAndForget: true, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Returns an observable with the number of downloaded items.
    /// </summary>
    public IObservable<int> DownloadedItemCountObservable(CollectionRevisionMetadata.ReadOnly revisionMetadata, ItemType itemType)
    {
        return _connection
            .ObserveDatoms(CollectionDownload.CollectionRevision, revisionMetadata)
            .AsEntityIds()
            .Transform(datom => CollectionDownload.Load(_connection.Db, datom.E))
            .FilterImmutable(download => DownloadMatchesItemType(download, itemType))
            .TransformOnObservable(download => GetStatusObservable(download, Optional<LoadoutId>.None))
            .FilterImmutable(static status => !status.IsNotDownloaded() && !status.IsBundled())
            .QueryWhenChanged(query => query.Count)
            .Prepend(0);
    }

    /// <summary>
    /// Counts the items.
    /// </summary>
    public static int CountItems(CollectionRevisionMetadata.ReadOnly revisionMetadata, ItemType itemType)
    {
        return revisionMetadata.Downloads
            .Where(download => DownloadMatchesItemType(download, itemType))
            .Count(download => download.IsCollectionDownloadNexusMods() || download.IsCollectionDownloadExternal());
    }

    /// <summary>
    /// Returns whether the item matches the given item type.
    /// </summary>
    internal static bool DownloadMatchesItemType(CollectionDownload.ReadOnly download, ItemType itemType)
    {
        if (download.IsOptional && itemType.HasFlagFast(ItemType.Optional)) return true;
        if (download.IsRequired && itemType.HasFlagFast(ItemType.Required)) return true;
        return false;
    }

    /// <summary>
    /// Checks whether the items in the collection were downloaded.
    /// </summary>
    public static bool IsFullyDownloaded(CollectionDownload.ReadOnly[] items, IDb db)
    {
        return items.All(download => !GetStatus(download, db).IsNotDownloaded());
    }

    [Flags, PublicAPI]
    public enum ItemType
    {
        Required = 1,
        Optional = 2,
    };

    /// <summary>
    /// Downloads everything in the revision.
    /// </summary>
    public async ValueTask DownloadItems(
        CollectionRevisionMetadata.ReadOnly revisionMetadata,
        ItemType itemType,
        IDb db,
        int maxDegreeOfParallelism = -1,
        CancellationToken cancellationToken = default)
    {
        var job = new DownloadCollectionJob
        {
            Downloader = this,
            RevisionMetadata = revisionMetadata,
            Db = db,
            ItemType = itemType,
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        };

        await _jobMonitor.Begin<DownloadCollectionJob, R3.Unit>(job);
    }

    /// <summary>
    /// Checks whether the collection is installed.
    /// </summary>
    public IObservable<bool> IsCollectionInstalled(CollectionRevisionMetadata.ReadOnly revision, LoadoutId loadout)
    {
        var observables = revision.Downloads
            .Where(download => DownloadMatchesItemType(download, ItemType.Required))
            .Select(download => GetStatusObservable(download, loadout).Select(static status => status.IsInstalled(out _)));

        return observables.CombineLatest(static list => list.All(static installed => installed));
    }

    private static CollectionDownloadStatus GetStatus(CollectionDownloadBundled.ReadOnly download, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db)
    {
        if (!collectionGroup.HasValue) return new CollectionDownloadStatus.Bundled();

        var entityIds = db.Datoms(
            (NexusCollectionBundledLoadoutGroup.BundleDownload, download),
            (LoadoutItem.ParentId, collectionGroup.Value)
        );

        foreach (var entityId in entityIds)
        {
            var loadoutItem = LoadoutItem.Load(db, entityId);
            if (loadoutItem.IsValid()) return new CollectionDownloadStatus.Installed(loadoutItem);
        }

        return new CollectionDownloadStatus.Bundled();
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownloadBundled.ReadOnly download,
        Optional<LoadoutId> loadout)
    {
        if (!loadout.HasValue) return Observable.Return(new CollectionDownloadStatus(new CollectionDownloadStatus.Bundled()));

        return _connection
            .ObserveDatoms(NexusCollectionBundledLoadoutGroup.BundleDownload, download)
            .QueryWhenChanged(query => query.Items.FirstOrOptional(static _ => true))
            .DistinctUntilChanged(OptionalDatomComparer.Instance)
            .Select(optional =>
            {
                if (!optional.HasValue) return (CollectionDownloadStatus) new CollectionDownloadStatus.Bundled();

                var loadoutItem = LoadoutItem.Load(_connection.Db, optional.Value.E);
                Debug.Assert(loadoutItem.IsValid());
                return new CollectionDownloadStatus.Installed(loadoutItem);
            });
    }

    private static CollectionDownloadStatus GetStatus(CollectionDownloadNexusMods.ReadOnly download, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db)
    {
        var datoms = db.Datoms(NexusModsLibraryItem.FileMetadata, download.FileMetadata);
        if (datoms.Count == 0) return new CollectionDownloadStatus.NotDownloaded();

        var libraryItem = default(NexusModsLibraryItem.ReadOnly);
        foreach (var datom in datoms)
        {
            libraryItem = NexusModsLibraryItem.Load(db, datom.E);
            if (libraryItem.IsValid()) break;
        }

        if (!libraryItem.IsValid()) return new CollectionDownloadStatus.NotDownloaded();
        return GetStatus(libraryItem.AsLibraryItem(), collectionGroup, db);
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownloadNexusMods.ReadOnly download,
        Optional<LoadoutId> loadout)
    {
        return _connection
            .ObserveDatoms(NexusModsLibraryItem.FileMetadata, download.FileMetadata)
            .QueryWhenChanged(query => query.Items.FirstOrOptional(static _ => true))
            .DistinctUntilChanged(OptionalDatomComparer.Instance)
            .SelectMany(optional =>
            {
                if (!optional.HasValue) return Observable.Return<CollectionDownloadStatus>(new CollectionDownloadStatus.NotDownloaded());

                var libraryItem = LibraryItem.Load(_connection.Db, optional.Value.E);
                Debug.Assert(libraryItem.IsValid());

                return GetStatusObservable(download.AsCollectionDownload().CollectionRevision, libraryItem, loadout);
            });
    }

    private static CollectionDownloadStatus GetStatus(CollectionDownloadExternal.ReadOnly download, Optional<CollectionGroup.ReadOnly> collectionGroup, IDb db)
    {
        var libraryFile = default(LibraryFile.ReadOnly);

        var directDownloadDatoms = db.Datoms(DirectDownloadLibraryFile.Md5, download.Md5);
        if (directDownloadDatoms.Count > 0)
        {
            foreach (var datom in directDownloadDatoms)
            {
                libraryFile = DirectDownloadLibraryFile.Load(db, datom.E).AsLibraryFile();
                if (libraryFile.IsValid()) break;
            }
        }

        if (!libraryFile.IsValid())
        {
            var locallyAddedDatoms = db.Datoms(LocalFile.Md5, download.Md5);
            if (locallyAddedDatoms.Count > 0)
            {
                foreach (var datom in locallyAddedDatoms)
                {
                    libraryFile = LocalFile.Load(db, datom.E).AsLibraryFile();
                    if (libraryFile.IsValid()) break;
                }
            }
        }

        if (!libraryFile.IsValid()) return new CollectionDownloadStatus.NotDownloaded();
        return GetStatus(libraryFile.AsLibraryItem(), collectionGroup, db);
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownloadExternal.ReadOnly download,
        Optional<LoadoutId> loadout)
    {
        var directDownloads = _connection.ObserveDatoms(SliceDescriptor.Create(DirectDownloadLibraryFile.Md5, download.Md5, _connection.AttributeCache));
        var locallyAdded = _connection.ObserveDatoms(SliceDescriptor.Create(LocalFile.Md5, download.Md5, _connection.AttributeCache));

        return directDownloads.MergeChangeSets(locallyAdded)
            .QueryWhenChanged(query => query.Items.FirstOrOptional(static _ => true))
            .DistinctUntilChanged(OptionalDatomComparer.Instance)
            .SelectMany(optional =>
            {
                if (!optional.HasValue) return Observable.Return<CollectionDownloadStatus>(new CollectionDownloadStatus.NotDownloaded());

                var libraryItem = LibraryItem.Load(_connection.Db, optional.Value.E);
                Debug.Assert(libraryItem.IsValid());

                return GetStatusObservable(download.AsCollectionDownload().CollectionRevision, libraryItem, loadout);
            });
    }

    private static CollectionDownloadStatus GetStatus(
        LibraryItem.ReadOnly libraryItem,
        Optional<CollectionGroup.ReadOnly> collectionGroup,
        IDb db)
    {
        if (!collectionGroup.HasValue) return new CollectionDownloadStatus.InLibrary(libraryItem);

        var entityIds = db.Datoms(
            (LibraryLinkedLoadoutItem.LibraryItem, libraryItem),
            (LoadoutItem.ParentId, collectionGroup.Value)
        );

        if (entityIds.Count == 0) return new CollectionDownloadStatus.InLibrary(libraryItem);

        foreach (var entityId in entityIds)
        {
            var loadoutItem = LoadoutItem.Load(db, entityId);
            if (!loadoutItem.IsValid()) continue;
            return new CollectionDownloadStatus.Installed(loadoutItem);
        }

        return new CollectionDownloadStatus.InLibrary(libraryItem);
    }

    private IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionRevisionMetadata.ReadOnly revision,
        LibraryItem.ReadOnly libraryItem,
        Optional<LoadoutId> loadoutId)
    {
        return _connection
            .ObserveDatoms(LibraryLinkedLoadoutItem.LibraryItemId, libraryItem.LibraryItemId)
            .TransformImmutable(datom => LibraryLinkedLoadoutItem.Load(_connection.Db, datom.E))
            .FilterImmutable(item => !loadoutId.HasValue || item.AsLoadoutItemGroup().AsLoadoutItem().LoadoutId == loadoutId.Value)
            .TransformImmutable(item =>
            {
                if (!LoadoutItem.Parent.TryGetValue(item, out var parentId)) return (item, default(NexusCollectionLoadoutGroup.ReadOnly));
                return (item, NexusCollectionLoadoutGroup.Load(_connection.Db, parentId));
            })
            .QueryWhenChanged(query =>
            {
                var optional = query.Items.FirstOrOptional(tuple =>
                {
                    var (_, collectionGroup) = tuple;
                    return collectionGroup.IsValid() && collectionGroup.RevisionId == revision;
                });

                CollectionDownloadStatus status = optional.HasValue
                    ? new CollectionDownloadStatus.Installed(optional.Value.Item1.AsLoadoutItemGroup().AsLoadoutItem())
                    : new CollectionDownloadStatus.InLibrary(libraryItem);

                return status;
            })
            .Prepend(new CollectionDownloadStatus.InLibrary(libraryItem));
    }

    /// <summary>
    /// Gets the status of a download as an observable.
    /// </summary>
    public IObservable<CollectionDownloadStatus> GetStatusObservable(
        CollectionDownload.ReadOnly download,
        Optional<LoadoutId> loadout)
    {
        if (download.TryGetAsCollectionDownloadBundled(out var bundled))
        {
            return GetStatusObservable(bundled, loadout).DistinctUntilChanged();
        }

        if (download.TryGetAsCollectionDownloadNexusMods(out var nexusModsDownload))
        {
            return GetStatusObservable(nexusModsDownload, loadout).DistinctUntilChanged();
        }

        if (download.TryGetAsCollectionDownloadExternal(out var externalDownload))
        {
            return GetStatusObservable(externalDownload, loadout).DistinctUntilChanged();
        }

        throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the status of a download.
    /// </summary>
    public static CollectionDownloadStatus GetStatus(CollectionDownload.ReadOnly download, IDb db)
    {
        return GetStatus(download, new Optional<CollectionGroup.ReadOnly>(), db);
    }

    /// <summary>
    /// Gets the status of a download.
    /// </summary>
    public static CollectionDownloadStatus GetStatus(
        CollectionDownload.ReadOnly download,
        Optional<CollectionGroup.ReadOnly> collectionGroup,
        IDb db)
    {
        if (download.TryGetAsCollectionDownloadBundled(out var bundled))
        {
            return GetStatus(bundled, collectionGroup, db);
        }

        if (download.TryGetAsCollectionDownloadNexusMods(out var nexusModsDownload))
        {
            return GetStatus(nexusModsDownload, collectionGroup, db);
        }

        if (download.TryGetAsCollectionDownloadExternal(out var externalDownload))
        {
            return GetStatus(externalDownload, collectionGroup, db);
        }

        throw new NotSupportedException();
    }

    /// <summary>
    /// Deletes all associated collection loadout groups.
    /// </summary>
    public async ValueTask DeleteCollectionLoadoutGroup(CollectionRevisionMetadata.ReadOnly revision, CancellationToken cancellationToken)
    {
        var db = _connection.Db;
        using var tx = _connection.BeginTransaction();

        var groupDatoms = db.Datoms(NexusCollectionLoadoutGroup.Revision, revision);
        foreach (var datom in groupDatoms)
        {
            tx.Delete(datom.E, recursive: true);
        }

        await tx.Commit();
    }

    /// <summary>
    /// Returns all items of the desired type (required/optional).
    /// </summary>
    public static CollectionDownload.ReadOnly[] GetItems(CollectionRevisionMetadata.ReadOnly revision, ItemType itemType)
    {
        var res = new CollectionDownload.ReadOnly[revision.Downloads.Count];

        var i = 0;
        foreach (var download in revision.Downloads)
        {
            if (!DownloadMatchesItemType(download, itemType)) continue;
            res[i++] = download;
        }

        Array.Resize(ref res, newSize: i);
        return res;
    }

    /// <summary>
    /// Gets the library file for the collection.
    /// </summary>
    public NexusModsCollectionLibraryFile.ReadOnly GetLibraryFile(CollectionRevisionMetadata.ReadOnly revisionMetadata)
    {
        var datoms = _connection.Db.Datoms(
            (NexusModsCollectionLibraryFile.CollectionSlug, revisionMetadata.Collection.Slug),
            (NexusModsCollectionLibraryFile.CollectionRevisionNumber, revisionMetadata.RevisionNumber)
        );

        if (datoms.Count == 0) throw new Exception($"Unable to find collection file for revision `{revisionMetadata.Collection.Slug}` (`{revisionMetadata.RevisionNumber}`)");
        var source = NexusModsCollectionLibraryFile.Load(_connection.Db, datoms[0]);
        return source;
    }

    public static Optional<NexusCollectionLoadoutGroup.ReadOnly> GetCollectionGroup(
        CollectionRevisionMetadata.ReadOnly revisionMetadata,
        LoadoutId loadoutId,
        IDb db)
    {
        var entityIds = db.Datoms(
            (NexusCollectionLoadoutGroup.Revision, revisionMetadata),
            (LoadoutItem.Loadout, loadoutId)
        );

        if (entityIds.Count == 0) return Optional.None<NexusCollectionLoadoutGroup.ReadOnly>();
        foreach (var entityId in entityIds)
        {
            var group = NexusCollectionLoadoutGroup.Load(db, entityId);
            if (group.IsValid()) return group;
        }

        return new Optional<NexusCollectionLoadoutGroup.ReadOnly>();
    }
}

/// <summary>
/// Represents the current status of a download in a collection.
/// </summary>
[PublicAPI]
public readonly struct CollectionDownloadStatus : IEquatable<CollectionDownloadStatus>
{
    /// <summary>
    /// Value.
    /// </summary>
    public readonly OneOf<NotDownloaded, Bundled, InLibrary, Installed> Value;

    /// <summary>
    /// Constructor.
    /// </summary>
    public CollectionDownloadStatus(OneOf<NotDownloaded, Bundled, InLibrary, Installed> value)
    {
        Value = value;
    }

    /// <summary>
    /// Item hasn't been downloaded yet.
    /// </summary>
    public readonly struct NotDownloaded;

    /// <summary>
    /// For bundled downloads.
    /// </summary>
    public readonly struct Bundled;

    /// <summary>
    /// For items that have been downloaded and added to the library.
    /// </summary>
    public readonly struct InLibrary : IEquatable<InLibrary>
    {
        /// <summary>
        /// The library item.
        /// </summary>
        public readonly LibraryItem.ReadOnly LibraryItem;

        /// <summary>
        /// Constructor.
        /// </summary>
        public InLibrary(LibraryItem.ReadOnly libraryItem)
        {
            LibraryItem = libraryItem;
        }

        /// <inheritdoc/>
        public bool Equals(InLibrary other) => LibraryItem.LibraryItemId == other.LibraryItem.LibraryItemId;
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is InLibrary other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => LibraryItem.Id.GetHashCode();
    }

    /// <summary>
    /// For items that have been installed.
    /// </summary>
    public readonly struct Installed : IEquatable<Installed>
    {
        /// <summary>
        /// The loadout item.
        /// </summary>
        public readonly LoadoutItem.ReadOnly LoadoutItem;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Installed(LoadoutItem.ReadOnly loadoutItem)
        {
            LoadoutItem = loadoutItem;
        }

        /// <inheritdoc/>
        public bool Equals(Installed other) => LoadoutItem.LoadoutItemId == other.LoadoutItem.LoadoutItemId;
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Installed other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => LoadoutItem.Id.GetHashCode();
    }

    public bool IsNotDownloaded() => Value.IsT0;
    public bool IsBundled() => Value.IsT1;

    public bool IsInLibrary(out LibraryItem.ReadOnly libraryItem)
    {
        if (!Value.TryPickT2(out var value, out _))
        {
            libraryItem = default(LibraryItem.ReadOnly);
            return false;
        }

        libraryItem = value.LibraryItem;
        return true;
    }

    public bool IsInstalled(out LoadoutItem.ReadOnly loadoutItem)
    {
        if (!Value.TryPickT3(out var value, out _))
        {
            loadoutItem = default(LoadoutItem.ReadOnly);
            return false;
        }

        loadoutItem = value.LoadoutItem;
        return true;
    }

    public static implicit operator CollectionDownloadStatus(NotDownloaded x) => new(x);
    public static implicit operator CollectionDownloadStatus(Bundled x) => new(x);
    public static implicit operator CollectionDownloadStatus(InLibrary x) => new(x);
    public static implicit operator CollectionDownloadStatus(Installed x) => new(x);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CollectionDownloadStatus other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(CollectionDownloadStatus other)
    {
        var (index, otherIndex) = (Value.Index, other.Value.Index);
        if (index != otherIndex) return false;

        if (IsNotDownloaded()) return true;
        if (IsBundled()) return true;

        if (Value.TryPickT2(out var inLibrary, out _))
        {
            return inLibrary.Equals(other.Value.AsT2);
        }

        if (Value.TryPickT3(out var installed, out _))
        {
            return installed.Equals(other.Value.AsT3);
        }

        throw new UnreachableException();
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();
}

internal class OptionalDatomComparer : IEqualityComparer<Optional<Datom>>
{
    public static readonly IEqualityComparer<Optional<Datom>> Instance = new OptionalDatomComparer();

    public bool Equals(Optional<Datom> x, Optional<Datom> y)
    {
        var (a, b) = (x.HasValue, y.HasValue);
        return (a, b) switch
        {
            (false, false) => true,
            (false, true) => false,
            (true, false) => false,
            (true, true) => x.Value.E.Equals(y.Value.E),
        };
    }

    public int GetHashCode(Optional<Datom> datom)
    {
        return datom.GetHashCode();
    }
}
