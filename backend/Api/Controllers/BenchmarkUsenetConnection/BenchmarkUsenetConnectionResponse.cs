using NzbWebDAV.Services.Benchmark;

namespace NzbWebDAV.Api.Controllers.BenchmarkUsenetConnection;

public class BenchmarkUsenetConnectionResponse : BaseApiResponse
{
    public BenchmarkResult? Result { get; set; }
}
