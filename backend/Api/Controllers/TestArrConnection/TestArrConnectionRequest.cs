using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;

namespace NzbWebDAV.Api.Controllers.TestArrConnection;

public class TestArrConnectionRequest
{
    public string Host { get; init; }
    public string ApiKey { get; init; }

    public TestArrConnectionRequest(HttpContext context, ConfigManager configManager)
    {
        Host = context.Request.Form["host"].FirstOrDefault()
               ?? throw new BadHttpRequestException("Arr host is required");

        var submittedApiKey = context.Request.Form["apiKey"].FirstOrDefault()
               ?? throw new BadHttpRequestException("Arr apiKey is required");
        ApiKey = ArrApiKeyResolver.Resolve(submittedApiKey, configManager);
    }
}
