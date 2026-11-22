using System.Threading.Channels;

namespace SGTest.Server.Gateway;

/// <summary>
/// One existing per-command-type bounded FIFO and sequential consumer within
/// a single authenticated TLS session. Does not own a socket, gameplay gate,
/// identity, World state, or an additional dispatch path.
/// </summary>
internal sealed class SessionCommandWorker<T>
{
    private readonly Channel<T> _commands;
    private readonly CancellationTokenSource _sessionCts;

    public SessionCommandWorker(
        int capacity, Func<T, Task>? handler, CancellationTokenSource sessionCts)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _sessionCts = sessionCts ?? throw new ArgumentNullException(nameof(sessionCts));
        _commands = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        Post = handler is null ? null : request => _commands.Writer.TryWrite(request);
        Completion = handler is null ? Task.CompletedTask : RunAsync(handler);
    }

    public Func<T, bool>? Post { get; }
    public Task Completion { get; }
    public Exception? Fault { get; private set; }

    public void Complete() => _commands.Writer.TryComplete();

    private async Task RunAsync(Func<T, Task> handler)
    {
        try
        {
            await foreach (var request in _commands.Reader
                .ReadAllAsync(_sessionCts.Token).ConfigureAwait(false))
                await handler(request).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_sessionCts.IsCancellationRequested) { }
        catch (Exception error)
        {
            Fault = error;
            _sessionCts.Cancel();
        }
    }
}
