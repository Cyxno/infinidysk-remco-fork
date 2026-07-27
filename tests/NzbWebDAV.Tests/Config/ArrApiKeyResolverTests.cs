using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Config;

[Collection(nameof(SecretResolverCollection))]
public class ArrApiKeyResolverTests
{
    [Fact]
    public void Resolve_ReturnsPlaintextUnchanged()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();

        var resolved = ArrApiKeyResolver.Resolve("typed-api-key", configManager);

        Assert.Equal("typed-api-key", resolved);
    }

    [Fact]
    public void Resolve_UnmasksStoredArrApiKey()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var stored = JsonSerializer.Serialize(new ArrConfig
        {
            RadarrInstances =
            [
                new ArrConfig.ConnectionDetails
                {
                    Host = "http://radarr:7878",
                    ApiKey = "stored-radarr-key",
                }
            ],
            SonarrInstances =
            [
                new ArrConfig.ConnectionDetails
                {
                    Host = "http://sonarr:8989",
                    ApiKey = "stored-sonarr-key",
                }
            ],
        });
        var configManager = new ConfigManager();
        configManager.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.ArrInstances, ConfigValue = stored }
        ]);

        var masker = new ConfigSecretMasker("test-signing-key");
        var masked = masker.MaskForResponse(ConfigKeys.ArrInstances, stored);
        using var document = JsonDocument.Parse(masked);
        var token = document.RootElement
            .GetProperty("SonarrInstances")[0]
            .GetProperty("ApiKey")
            .GetString()!;

        var resolved = ArrApiKeyResolver.Resolve(token, configManager);

        Assert.Equal("stored-sonarr-key", resolved);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownMaskToken()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();
        configManager.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.ArrInstances,
                ConfigValue = JsonSerializer.Serialize(new ArrConfig
                {
                    RadarrInstances =
                    [
                        new ArrConfig.ConnectionDetails
                        {
                            Host = "http://radarr:7878",
                            ApiKey = "stored-secret",
                        }
                    ],
                })
            }
        ]);

        var forged = $"{ConfigSecretMasker.MaskPrefix}AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        Assert.Throws<BadHttpRequestException>(() =>
            ArrApiKeyResolver.Resolve(forged, configManager));
    }

    private static IDisposable TempEnv(string name, string value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new RestoreEnv(name, previous);
    }

    private sealed class RestoreEnv(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }
}
