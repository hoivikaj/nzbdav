using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Middlewares;
using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Middlewares;

public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task MissingArticleAfterResponseStarted_AbortsConnection()
    {
        var lifetimeFeature = new TestHttpRequestLifetimeFeature();
        var context = CreateContext(hasStarted: true, lifetimeFeature);
        var middleware = CreateMiddleware(
            _ => throw new UsenetArticleNotFoundException("missing-segment"));

        await middleware.InvokeAsync(context);

        Assert.True(lifetimeFeature.Aborted);
    }

    [Fact]
    public async Task MissingArticleBeforeResponseStarted_ReturnsNotFoundWithoutAborting()
    {
        var lifetimeFeature = new TestHttpRequestLifetimeFeature();
        var context = CreateContext(hasStarted: false, lifetimeFeature);
        var middleware = CreateMiddleware(
            _ => throw new UsenetArticleNotFoundException("missing-segment"));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(lifetimeFeature.Aborted);
    }

    private static ExceptionMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new ExceptionMiddleware(
            next,
            new ConfigManager(),
            new StreamingFailureTracker());
    }

    private static DefaultHttpContext CreateContext(
        bool hasStarted,
        TestHttpRequestLifetimeFeature lifetimeFeature)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new TestHttpResponseFeature(hasStarted));
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetimeFeature);
        return context;
    }

    private sealed class TestHttpResponseFeature(bool hasStarted) : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; } = hasStarted;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class TestHttpRequestLifetimeFeature : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }
        public CancellationToken RequestAborted { get; set; }

        public void Abort()
        {
            Aborted = true;
        }
    }
}
