using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

internal sealed class DeferredArticleBodyCallback
{
    private readonly object _lock = new();
    private Action<ArticleBodyResult>? _target;
    private ArticleBodyResult? _deferredResult;
    private bool _discarded;

    public void Invoke(ArticleBodyResult result)
    {
        Action<ArticleBodyResult>? target;
        lock (_lock)
        {
            if (_discarded) return;
            target = _target;
            if (target == null)
            {
                _deferredResult ??= result;
                return;
            }
        }

        target(result);
    }

    public void Activate(Action<ArticleBodyResult> target)
    {
        ArticleBodyResult? deferredResult;
        lock (_lock)
        {
            if (_discarded) return;
            _target = target;
            deferredResult = _deferredResult;
            _deferredResult = null;
        }

        if (deferredResult.HasValue)
        {
            target(deferredResult.Value);
        }
    }

    public void Discard()
    {
        lock (_lock)
        {
            _discarded = true;
            _target = null;
            _deferredResult = null;
        }
    }
}
