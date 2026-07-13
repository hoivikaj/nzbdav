using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Services;

public class SearchExcludeSyncServiceTests
{
    [Fact]
    public void ParsePayload_ValuesObject()
    {
        var items = SearchExcludeSyncService.ParsePayload("""{"values":["a","b"]}""");
        Assert.Equal(["a", "b"], items);
    }

    [Fact]
    public void ParsePayload_PatternObjects()
    {
        var items = SearchExcludeSyncService.ParsePayload(
            """[{"pattern":"a"},{"pattern":"b","name":"x","score":5}]""");
        Assert.Equal(["a", "b"], items);
    }

    [Fact]
    public void ParsePayload_BareStringArray()
    {
        var items = SearchExcludeSyncService.ParsePayload("""["a","b"]""");
        Assert.Equal(["a", "b"], items);
    }

    [Fact]
    public void ParsePayload_SkipsObjectsWithoutPattern()
    {
        var items = SearchExcludeSyncService.ParsePayload("""[{"name":"x"},{"pattern":"a"}]""");
        Assert.Equal(["a"], items);
    }

    [Fact]
    public void ParsePayload_UnexpectedObject_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SearchExcludeSyncService.ParsePayload("""{"foo":1}"""));
    }
}
