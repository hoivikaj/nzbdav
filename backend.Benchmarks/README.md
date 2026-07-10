# Backend benchmarks

The benchmarks establish a repeatable baseline for Usenet segment decoding. They
are intentionally excluded from CI because BenchmarkDotNet results are sensitive
to runner contention and hardware.

Run them from the repository root:

```bash
dotnet run --project backend.Benchmarks -c Release
```

Use the same machine and runtime when comparing results across UsenetSharp or
streaming changes.
