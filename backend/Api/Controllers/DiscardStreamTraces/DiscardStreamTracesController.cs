using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.Controllers.SetStreamTracing;
using NzbWebDAV.Services.StreamTrace;
using Serilog;

namespace NzbWebDAV.Api.Controllers.DiscardStreamTraces;

[ApiController]
[Route("api/discard-stream-traces")]
public sealed class DiscardStreamTracesController(
    StreamTraceBuffer buffer,
    StreamTraceStatusBroadcaster broadcaster) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var before = buffer.GetStatus();
        var status = buffer.Discard();
        Log.Information(
            "Discarded {Events:n0} retained stream-trace events from the UI",
            before.EventCount);
        await broadcaster.BroadcastAsync(status).ConfigureAwait(false);
        return Ok(SetStreamTracingResponse.From(status));
    }
}
