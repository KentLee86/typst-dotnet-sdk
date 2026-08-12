namespace Cetz.Renderer.Core;

/// <summary>
/// Common latest-request-wins rendering controller. It renders off the UI thread,
/// applies only the newest successful document to the view, and keeps the last
/// successful document when a later render fails.
/// </summary>
public sealed class CetzRenderController : IDisposable
{
    private readonly object _sync = new();
    private readonly ICetzDocumentView _view;
    private readonly CetzDocumentRenderer _renderer;
    private readonly SynchronizationContext? _viewContext;
    private CancellationTokenSource? _activeRender;
    private long _generation;
    private bool _isRendering;
    private bool _disposed;

    public CetzRenderController(
        ICetzDocumentView view,
        CetzRendererOptions? rendererOptions = null,
        SynchronizationContext? viewContext = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _renderer = new CetzDocumentRenderer(rendererOptions);
        _viewContext = viewContext ?? SynchronizationContext.Current;
    }

    public event EventHandler? StateChanged;

    public bool IsRendering { get { lock (_sync) return _isRendering; } }
    public Exception? LastError { get; private set; }

    public Task<CetzRenderedDocument?> RenderSourceAsync(string source, string virtualPath = "main.typ",
        CetzDocumentRenderOptions? options = null, CancellationToken cancellationToken = default)
        => RenderLatestAsync(token => _renderer.RenderSourceAsync(source, virtualPath, options, token), cancellationToken);

    public Task<CetzRenderedDocument?> RenderProjectAsync(CetzProject project,
        CetzDocumentRenderOptions? options = null, CancellationToken cancellationToken = default)
        => RenderLatestAsync(token => _renderer.RenderProjectAsync(project, options, token), cancellationToken);

    public Task<CetzRenderedDocument?> RenderFileAsync(string path,
        CetzDocumentRenderOptions? options = null, CancellationToken cancellationToken = default)
        => RenderLatestAsync(token => _renderer.RenderFileAsync(path, options, token), cancellationToken);

    public void Cancel()
    {
        lock (_sync) _activeRender?.Cancel();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _activeRender?.Cancel();
            _activeRender = null;
            _isRendering = false;
        }
        _renderer.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<CetzRenderedDocument?> RenderLatestAsync(
        Func<CancellationToken, Task<CetzRenderedDocument>> render,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource cancellation;
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeRender?.Cancel();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeRender = cancellation;
            generation = ++_generation;
            _isRendering = true;
            LastError = null;
        }
        RaiseStateChanged();

        try
        {
            var document = await render(cancellation.Token).ConfigureAwait(false);
            if (!IsCurrent(generation, cancellation)) return null;
            await InvokeViewAsync(() => _view.SetDocument(document), cancellation.Token).ConfigureAwait(false);
            return IsCurrent(generation, cancellation) ? document : null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            if (IsCurrent(generation, cancellation)) LastError = exception;
            throw;
        }
        finally
        {
            var notify = false;
            lock (_sync)
            {
                if (generation == _generation)
                {
                    _isRendering = false;
                    if (ReferenceEquals(_activeRender, cancellation)) _activeRender = null;
                    notify = true;
                }
            }
            cancellation.Dispose();
            if (notify) RaiseStateChanged();
        }
    }

    private bool IsCurrent(long generation, CancellationTokenSource cancellation)
    {
        lock (_sync) return !_disposed && generation == _generation && !cancellation.IsCancellationRequested;
    }

    private Task InvokeViewAsync(Action action, CancellationToken cancellationToken)
    {
        if (_viewContext is null || ReferenceEquals(SynchronizationContext.Current, _viewContext))
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _viewContext.Post(_ =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        }, null);
        return completion.Task;
    }

    private void RaiseStateChanged()
    {
        if (_viewContext is null || ReferenceEquals(SynchronizationContext.Current, _viewContext))
            StateChanged?.Invoke(this, EventArgs.Empty);
        else
            _viewContext.Post(_ => StateChanged?.Invoke(this, EventArgs.Empty), null);
    }
}
