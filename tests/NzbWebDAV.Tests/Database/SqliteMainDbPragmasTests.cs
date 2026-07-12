using NzbWebDAV.Database.Interceptors;

namespace NzbWebDAV.Tests.Database;

public class SqliteMainDbPragmasTests
{
    [Theory]
    [InlineData("Data Source=db.sqlite;Mode=ReadOnly", true)]
    [InlineData("Data Source=db.sqlite;mode=readonly", true)]
    [InlineData("Data Source=db.sqlite;mode=read-only", true)]
    [InlineData("Data Source=db.sqlite", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsExplicitlyReadOnly_DetectsReadOnlyModes(string? connectionString, bool expected)
    {
        Assert.Equal(expected, SqliteMainDbPragmas.IsExplicitlyReadOnly(connectionString));
    }
}
