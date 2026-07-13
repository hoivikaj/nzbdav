using NzbWebDAV.Clients.Usenet;
using UsenetSharp.Models;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class UsenetArticleAvailabilityTests
{
    [Theory]
    [InlineData(430, true)]
    [InlineData(451, true)]
    [InlineData(400, false)]
    [InlineData(223, false)]
    [InlineData(222, false)]
    [InlineData(423, false)]
    public void IsDefinitiveMissing_ClassifiesResponseCodes(int responseCode, bool expected)
    {
        var response = new UsenetStatResponse
        {
            ResponseCode = responseCode,
            ResponseMessage = $"{responseCode} test",
            ArticleExists = responseCode == 223,
        };

        Assert.Equal(expected, UsenetArticleAvailability.IsDefinitiveMissing(response));
    }
}
