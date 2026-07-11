using System.IO.Compression;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.Warden;

[ApiController]
[Route("api/warden-import")]
public class WardenImportController(WardenStore warden) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var ct = HttpContext.RequestAborted;
        var form = HttpContext.Request.HasFormContentType ? HttpContext.Request.Form : null;
        var action = form?["action"].ToString() ?? "";

        if (action == "clear")
        {
            var removed = warden.Clear();
            return Ok(new WardenImportResponse { Status = true, Added = 0, Total = warden.Count, Cleared = removed });
        }

        if (form is null || form.Files.Count == 0)
            throw new BadHttpRequestException("No file was uploaded.");

        var target = form["target"].ToString();
        var file = form.Files[0];

        await using var buffered = await BufferAndDecompressAsync(file, ct);

        if (target == "separate")
        {
            var name = form["name"].ToString();
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(file.FileName).Replace(".ndjson", "");
            if (string.IsNullOrWhiteSpace(name)) name = "Imported list";
            var trust = form["trust"].ToString();
            var (sourceId, count) = await warden.ImportAsNewSourceAsync(buffered, name, trust, ct);
            return Ok(new WardenImportResponse { Status = true, Added = count, Total = warden.Count, Cleared = 0, SourceId = sourceId });
        }

        var before = warden.LocalCount;
        await warden.MergeIntoLocalAsync(buffered, ct);
        var after = warden.LocalCount;
        return Ok(new WardenImportResponse { Status = true, Added = Math.Max(0, after - before), Total = warden.Count, Cleared = 0 });
    }

    private static async Task<Stream> BufferAndDecompressAsync(IFormFile file, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await using (var raw = file.OpenReadStream())
            await raw.CopyToAsync(ms, ct);
        ms.Position = 0;
        if (ms.Length >= 2)
        {
            var head = ms.GetBuffer();
            if (head[0] == 0x1f && head[1] == 0x8b)
            {
                var decompressed = new MemoryStream();
                await using (var gz = new GZipStream(ms, CompressionMode.Decompress, leaveOpen: true))
                    await gz.CopyToAsync(decompressed, ct);
                decompressed.Position = 0;
                return decompressed;
            }
        }
        ms.Position = 0;
        return ms;
    }
}

public class WardenImportResponse : BaseApiResponse
{
    [JsonPropertyName("added")] public required int Added { get; init; }
    [JsonPropertyName("total")] public required int Total { get; init; }
    [JsonPropertyName("cleared")] public required int Cleared { get; init; }
    [JsonPropertyName("sourceId")] public string? SourceId { get; init; }
}
