using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NzbWebDAV.Database.Interceptors;

/// <summary>
/// Applies metrics-tuned PRAGMAs: WAL, relaxed durability (NORMAL — losing one
/// second of metrics on a crash is acceptable), memory temp store, a 256 MB mmap
/// window, a 64 MB page cache, and a capped WAL journal. Incremental auto-vacuum
/// lets the retention sweep reclaim space without a full VACUUM.
/// </summary>
public class SqliteMetricsPragmas : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA journal_mode = WAL;" +
            "PRAGMA synchronous = NORMAL;" +
            "PRAGMA temp_store = MEMORY;" +
            "PRAGMA mmap_size = 268435456;" +
            "PRAGMA cache_size = -65536;" +
            "PRAGMA journal_size_limit = 67108864;" +
            "PRAGMA auto_vacuum = INCREMENTAL;";
        command.ExecuteNonQuery();
    }
}
