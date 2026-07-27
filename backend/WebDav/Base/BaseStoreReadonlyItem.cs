using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreReadonlyItem : BaseStoreItem
{
    /// <summary>
    /// Throttle scope for refused writes. Coarser than <see cref="UniqueKey"/> so
    /// per-instance keys (e.g. empty-file staging GUIDs) cannot bypass the window.
    /// </summary>
    protected virtual string WriteRejectionScopeKey => GetType().Name;

    protected override Task<DavStatusCode> UploadFromStreamAsync(UploadFromStreamRequest request)
    {
        ReadonlyWriteRejectionLog.Rejected("upload item", Name, Name, WriteRejectionScopeKey);
        return Task.FromResult(DavStatusCode.Forbidden);
    }

    protected override Task<StoreItemResult> CopyAsync(CopyRequest request)
    {
        ReadonlyWriteRejectionLog.Rejected("copy item", request.Name, Name, WriteRejectionScopeKey);
        return Task.FromResult(new StoreItemResult(DavStatusCode.Forbidden));
    }
}
