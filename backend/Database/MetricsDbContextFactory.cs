using Microsoft.EntityFrameworkCore.Design;

namespace NzbWebDAV.Database;

/// <summary>
/// Allows EF Core design-time tools (dotnet-ef) to create a MetricsDbContext
/// without bootstrapping the full application host (Program.cs).
/// </summary>
public class MetricsDbContextFactory : IDesignTimeDbContextFactory<MetricsDbContext>
{
    public MetricsDbContext CreateDbContext(string[] args)
    {
        return new MetricsDbContext();
    }
}
