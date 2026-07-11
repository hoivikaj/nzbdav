using System.Text.Json;
using NzbWebDAV.Config;

namespace NzbWebDAV.Tests.Config;

public class ConfigSecretMaskerTests
{
    [Fact]
    public void IndexerApiKeysAreMaskedAndResolvedForRoundTripUpdates()
    {
        const string configName = "indexers.instances";
        const string stored =
            """{"Indexers":[{"Name":"One","ApiKey":"first-secret"},{"Name":"Two","ApiKey":"second-secret"}]}""";
        var masker = new ConfigSecretMasker("test-signing-key");

        var masked = masker.MaskForResponse(configName, stored);

        using (var document = JsonDocument.Parse(masked))
        {
            var indexers = document.RootElement.GetProperty("Indexers");
            Assert.All(indexers.EnumerateArray(), indexer =>
                Assert.StartsWith(
                    ConfigSecretMasker.MaskPrefix,
                    indexer.GetProperty("ApiKey").GetString()!));
        }

        var resolved = masker.ResolveForUpdate(configName, masked, stored);
        using var resolvedDocument = JsonDocument.Parse(resolved);
        var resolvedIndexers = resolvedDocument.RootElement.GetProperty("Indexers");
        Assert.Equal("first-secret", resolvedIndexers[0].GetProperty("ApiKey").GetString());
        Assert.Equal("second-secret", resolvedIndexers[1].GetProperty("ApiKey").GetString());
    }

    [Fact]
    public void IndexerApiKeyCanBeReplacedWhileOtherMaskedKeysRoundTrip()
    {
        const string configName = "indexers.instances";
        const string stored =
            """{"Indexers":[{"Name":"One","ApiKey":"first-secret"},{"Name":"Two","ApiKey":"second-secret"}]}""";
        var masker = new ConfigSecretMasker("test-signing-key");
        var masked = masker.MaskForResponse(configName, stored);
        using var maskedDocument = JsonDocument.Parse(masked);
        var secondToken = maskedDocument.RootElement
            .GetProperty("Indexers")[1]
            .GetProperty("ApiKey")
            .GetString();
        var submitted =
            $$"""{"Indexers":[{"Name":"One","ApiKey":"replacement"},{"Name":"Two","ApiKey":"{{secondToken}}"}]}""";

        var resolved = masker.ResolveForUpdate(configName, submitted, stored);

        using var resolvedDocument = JsonDocument.Parse(resolved);
        var resolvedIndexers = resolvedDocument.RootElement.GetProperty("Indexers");
        Assert.Equal("replacement", resolvedIndexers[0].GetProperty("ApiKey").GetString());
        Assert.Equal("second-secret", resolvedIndexers[1].GetProperty("ApiKey").GetString());
    }
}
