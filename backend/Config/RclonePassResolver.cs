using NzbWebDAV.Utils;

namespace NzbWebDAV.Config;

/// <summary>
/// Resolves an rclone RC password that may be a UI mask token back to the
/// stored plaintext, so test-connection can auth without forcing re-entry.
/// </summary>
public static class RclonePassResolver
{
    public static string? Resolve(string? submittedPass, ConfigManager configManager)
    {
        if (submittedPass is null || !ConfigSecretMasker.IsMaskToken(submittedPass))
            return submittedPass;

        var masker = new ConfigSecretMasker(
            EnvironmentUtil.GetRequiredVariable("FRONTEND_BACKEND_API_KEY"));
        return masker.ResolveForUpdate(
            ConfigKeys.RclonePass,
            submittedPass,
            configManager.GetRclonePass());
    }
}
