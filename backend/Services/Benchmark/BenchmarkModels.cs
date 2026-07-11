namespace NzbWebDAV.Services.Benchmark;

/// <summary>
/// How aggressive the benchmark is. The trade-off is accuracy vs. data used:
/// faster connections need more transferred bytes to measure reliably, so
/// "Thorough" simply moves more data per step.
/// </summary>
public enum BenchmarkIntensity
{
    Quick,
    Thorough,
}

/// <summary>
/// Tunable knobs for a single benchmark run, derived from the chosen intensity.
/// All byte values are decimal megabytes (1 MB = 1,000,000 bytes) so the numbers
/// line up with what download clients display.
/// </summary>
public sealed class BenchmarkProfile
{
    public required int LatencySamples { get; init; }
    public required int MaxCorpusSegments { get; init; }

    /// <summary>Target bytes to transfer at each connection level before moving on.</summary>
    public required long PerLevelBytes { get; init; }

    /// <summary>Hard wall-clock cap per level so a slow/stalled line can't hang the run.</summary>
    public required TimeSpan PerLevelMaxDuration { get; init; }

    /// <summary>Absolute backstop on total data moved across the whole run.</summary>
    public required long HardTotalBytes { get; init; }

    /// <summary>Connection counts to sweep (ascending), before clamping to the ceiling/cap.</summary>
    public required int[] SweepLevels { get; init; }

    /// <summary>Connection count used while comparing pipelining on vs. off.</summary>
    public required int PipelineTestConnections { get; init; }

    /// <summary>Pipeline depths to compare against the non-pipelined baseline.</summary>
    public required int[] PipelineDepths { get; init; }

    public static BenchmarkProfile For(BenchmarkIntensity intensity) => intensity switch
    {
        BenchmarkIntensity.Thorough => new BenchmarkProfile
        {
            LatencySamples = 8,
            MaxCorpusSegments = 8000,
            PerLevelBytes = 20_000_000,
            PerLevelMaxDuration = TimeSpan.FromSeconds(10),
            HardTotalBytes = 650_000_000,
            SweepLevels = [1, 2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 50],
            PipelineTestConnections = 6,
            PipelineDepths = [4, 8, 16],
        },
        _ => new BenchmarkProfile
        {
            LatencySamples = 5,
            MaxCorpusSegments = 2500,
            PerLevelBytes = 8_000_000,
            PerLevelMaxDuration = TimeSpan.FromSeconds(8),
            HardTotalBytes = 160_000_000,
            SweepLevels = [1, 2, 4, 8, 16, 24],
            PipelineTestConnections = 4,
            PipelineDepths = [8, 16],
        },
    };
}

public sealed class BenchmarkLatency
{
    public double MinMs { get; set; }
    public double AvgMs { get; set; }
    public int Samples { get; set; }
}

public sealed class BenchmarkSweepPoint
{
    public int Connections { get; set; }
    public double MbPerSec { get; set; }
}

public sealed class BenchmarkPipeliningPoint
{
    public int Depth { get; set; }
    public double MbPerSec { get; set; }
}

public sealed class BenchmarkPipelining
{
    public int TestedAtConnections { get; set; }
    public double BaselineMbPerSec { get; set; }
    public List<BenchmarkPipeliningPoint> Tested { get; set; } = [];
    public bool RecommendEnabled { get; set; }
    public int RecommendedDepth { get; set; }
}

/// <summary>
/// The full structured outcome of a benchmark run. Returned from the POST and
/// mirrored (in part) over the websocket progress topic while running.
/// </summary>
public sealed class BenchmarkResult
{
    public BenchmarkLatency? Latency { get; set; }
    public bool ThroughputTested { get; set; }

    /// <summary>True when the run only measured pipelining depth (connection count left untouched).</summary>
    public bool PipeliningOnly { get; set; }
    public List<BenchmarkSweepPoint> Sweep { get; set; } = [];
    public int? RecommendedConnections { get; set; }

    /// <summary>The connection count the provider refused to exceed, if we hit it.</summary>
    public int? ProviderConnectionCap { get; set; }

    public BenchmarkPipelining? Pipelining { get; set; }
    public long DataUsedBytes { get; set; }
    public List<string> Warnings { get; set; } = [];
}

/// <summary>Live progress payload pushed over the BenchmarkProgress websocket topic.</summary>
public sealed class BenchmarkProgressUpdate
{
    public required string Phase { get; init; } // latency | corpus | sweep | pipelining | done
    public required string Status { get; init; }
    public int Percent { get; init; }
    public int? CurrentConnections { get; init; }
    public long DataUsedBytes { get; init; }
    public List<BenchmarkSweepPoint> Sweep { get; init; } = [];
}
