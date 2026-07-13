using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// Classifies NNTP article responses as definitive "missing" vs connection-level failures.
/// </summary>
public static class UsenetArticleAvailability
{
    /// <summary>
    /// Provider-specific "article unavailable" (e.g. Giganews/UsenetBucket DMCA).
    /// Treated like a clean 430 — the article is gone from that storage group.
    /// </summary>
    public const int ArticleUnavailable = 451;

    /// <summary>
    /// True for a definitive missing-article response: RFC 430, or provider 451.
    /// Connection-level codes (e.g. buffered 400 goodbye) must remain false so they stay retryable.
    /// </summary>
    public static bool IsDefinitiveMissing(UsenetResponse response) =>
        response.ResponseType == UsenetResponseType.NoArticleWithThatMessageId
        || response.ResponseCode == ArticleUnavailable;
}
