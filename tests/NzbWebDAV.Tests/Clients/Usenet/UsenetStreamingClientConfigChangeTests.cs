using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class UsenetStreamingClientConfigChangeTests
{
    [Theory]
    [InlineData(ConfigKeys.UsenetNntpReadTimeoutSeconds, true)]
    [InlineData(ConfigKeys.UsenetReconnectDelayMilliseconds, true)]
    [InlineData(ConfigKeys.UsenetStreamingPriority, false)]
    [InlineData(ConfigKeys.UsenetStreamingReadTimeoutSeconds, false)]
    public void RequiresProviderPoolRebuild_RecognizesCapturedPoolSettings(
        string configKey,
        bool expected)
    {
        var changedConfig = new Dictionary<string, string> { [configKey] = "changed" };

        Assert.Equal(expected, UsenetStreamingClient.RequiresProviderPoolRebuild(changedConfig));
    }
}
