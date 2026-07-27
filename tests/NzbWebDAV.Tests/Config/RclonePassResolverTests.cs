using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Config;

[Collection(nameof(SecretResolverCollection))]
public class RclonePassResolverTests
{
    [Fact]
    public void Resolve_ReturnsPlaintextUnchanged()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();

        var resolved = RclonePassResolver.Resolve("typed-password", configManager);

        Assert.Equal("typed-password", resolved);
    }

    [Fact]
    public void Resolve_ReturnsNullUnchanged()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();

        var resolved = RclonePassResolver.Resolve(null, configManager);

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_UnmasksStoredRclonePassword()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        const string stored = "stored-rclone-pass";
        var configManager = new ConfigManager();
        configManager.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.RclonePass, ConfigValue = stored }
        ]);

        var masker = new ConfigSecretMasker("test-signing-key");
        var token = masker.MaskForResponse(ConfigKeys.RclonePass, stored);

        var resolved = RclonePassResolver.Resolve(token, configManager);

        Assert.Equal(stored, resolved);
    }

    [Fact]
    public void Resolve_ThrowsForUnknownMaskToken()
    {
        using var _ = TempEnv("FRONTEND_BACKEND_API_KEY", "test-signing-key");
        var configManager = new ConfigManager();
        configManager.UpdateValues(
        [
            new ConfigItem { ConfigName = ConfigKeys.RclonePass, ConfigValue = "stored-secret" }
        ]);

        var forged = $"{ConfigSecretMasker.MaskPrefix}AAAAAAAAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        Assert.Throws<BadHttpRequestException>(() =>
            RclonePassResolver.Resolve(forged, configManager));
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
