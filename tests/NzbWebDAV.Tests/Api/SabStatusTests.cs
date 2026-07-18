using System.Text.Json;
using NzbWebDAV.Api.SabControllers;
using NzbWebDAV.Api.SabControllers.GetStatus;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.Api;

public class SabStatusTests
{
    [Fact]
    public void StatusResponse_SerializesStatusAsObject()
    {
        var response = new GetStatusResponse
        {
            Status = new SabStatusObject { CompleteDir = "/downloads/complete" },
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("status").ValueKind);
        Assert.Equal(
            "/downloads/complete",
            json.RootElement.GetProperty("status").GetProperty("completedir").GetString());
    }

    [Fact]
    public void CompletedDir_UsesStrmDirectoryForStrmImports()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.ApiImportStrategy,
                ConfigValue = "strm",
            },
            new ConfigItem
            {
                ConfigName = ConfigKeys.ApiCompletedDownloadsDir,
                ConfigValue = "/data/strm",
            },
        ]);

        Assert.Equal("/data/strm", SabPathResolver.GetCompletedDir(config));
    }

    [Fact]
    public void CompletedDir_UsesSymlinkFolderForSymlinkImports()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.RcloneMountDir,
                ConfigValue = "/mnt/nzbdav",
            },
        ]);

        Assert.Equal(
            Path.Join("/mnt/nzbdav", DavItem.SymlinkFolder.Name),
            SabPathResolver.GetCompletedDir(config));
    }
}
