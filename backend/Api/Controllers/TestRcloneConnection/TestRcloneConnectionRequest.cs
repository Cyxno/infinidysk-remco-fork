using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;

namespace NzbWebDAV.Api.Controllers.TestRcloneConnection;

public class TestRcloneConnectionRequest
{
    public string Host { get; init; }
    public string? User { get; init; }
    public string? Pass { get; init; }

    public TestRcloneConnectionRequest(HttpContext context, ConfigManager configManager)
    {
        Host = context.Request.Form["host"].FirstOrDefault()
               ?? throw new BadHttpRequestException("Rclone host is required");

        User = context.Request.Form["user"].FirstOrDefault();
        Pass = RclonePassResolver.Resolve(context.Request.Form["pass"].FirstOrDefault(), configManager);
    }
}
