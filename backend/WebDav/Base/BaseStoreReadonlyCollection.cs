using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreReadonlyCollection : BaseStoreCollection
{
    // NWebDav's DavStatusCode enum has no 405 member. SetStatus only assigns
    // response.StatusCode, so the reason phrase falls back to the server default.
    private const DavStatusCode MethodNotAllowed = (DavStatusCode)405;

    protected override Task<StoreItemResult> CopyAsync(CopyRequest request)
    {
        LogRejected("copy item", request.Name);
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    protected override Task<StoreItemResult> CreateItemAsync(CreateItemRequest request)
    {
        LogRejected("create item", request.Name);
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    protected override async Task<StoreCollectionResult> CreateCollectionAsync(CreateCollectionRequest request)
    {
        // RFC 4918 9.3.1: MKCOL may only be executed on an unmapped URL, so a directory
        // that is already there is 405, not 403. Clients read 403 as a permission problem
        // worth retrying; 405 tells them the directory exists and they can move on.
        var existing = await GetItemAsync(request.Name, request.CancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return new StoreCollectionResult(MethodNotAllowed);

        LogRejected("create directory", request.Name);
        return new StoreCollectionResult(DavStatusCode.Forbidden);
    }

    protected override Task<StoreItemResult> MoveItemAsync(MoveItemRequest request)
    {
        LogRejected("move item", request.SourceName);
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }

    protected override Task<DavStatusCode> DeleteItemAsync(DeleteItemRequest request)
    {
        LogRejected("delete item", request.Name);
        return Task.FromResult(DavStatusCode.Forbidden);
    }

    protected override bool SupportsFastMove(SupportsFastMoveRequest request)
    {
        return false;
    }

    /// <summary>
    /// Throttle scope for refused writes. Deliberately coarser than <see cref="UniqueKey"/>:
    /// a client writing metadata sidecars touches one directory per release, so keying on
    /// the directory produced one warning per release and still flooded the buffer. The
    /// concrete store type is the safe default; mounts that need a shared key (e.g.
    /// completed-symlinks) override this. The refused path is still reported in the message.
    /// </summary>
    protected virtual string WriteRejectionScopeKey => GetType().Name;

    private void LogRejected(string operation, string itemName) =>
        ReadonlyWriteRejectionLog.Rejected(operation, itemName, Name, WriteRejectionScopeKey);
}
