using SGTest.Server.Gateway;

internal static class TlsCommandWorkerChecks
{
    private static void Require(bool ok, string reason) => Checks.Require(ok, reason);

    public static void Verify()
    {
        Checks.Run("TLS per-command FIFO preserves order and bounded rejection",
            () => BoundedOrdered().GetAwaiter().GetResult());
        Checks.Run("TLS command worker fault cancels authenticated session",
            () => FaultCancelsSession().GetAwaiter().GetResult());
        Checks.Run("TLS command worker cancellation and disabled handler are safe",
            () => CancelAndDisabled().GetAwaiter().GetResult());
    }

    private static async Task BoundedOrdered()
    {
        using var session = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<int>();
        var worker = new TlsCommandWorker<int>(2, async n =>
        {
            if (n == 1)
            {
                started.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            }
            seen.Add(n);
        }, session);
        var post = worker.Post ?? throw new Exception("enabled FIFO has no ingress");
        Require(post(1), "initial command was rejected");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(post(2) && post(3), "bounded FIFO rejected commands before capacity");
        Require(!post(4), "bounded FIFO exceeded its queue capacity");
        worker.Complete();
        Require(!post(5), "completed FIFO accepted a command");
        release.TrySetResult(true);
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Require(seen.SequenceEqual(new[] { 1, 2, 3 }),
            "FIFO reordered, duplicated, or dropped accepted commands");
        Require(worker.Fault is null, "successful consumer recorded a fault");
        Require(!session.IsCancellationRequested, "clean worker completion cancelled session");
    }

    private static async Task FaultCancelsSession()
    {
        using var session = new CancellationTokenSource();
        var worker = new TlsCommandWorker<int>(1,
            _ => Task.FromException(new InvalidOperationException("forced command failure")),
            session);
        Require(worker.Post is { } post && post(42), "fault fixture enqueue failed");
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Require(worker.Fault is InvalidOperationException error &&
                error.Message == "forced command failure",
            "worker did not retain originating handler fault");
        Require(session.IsCancellationRequested,
            "handler exception did not stop authenticated socket session");
    }

    private static async Task CancelAndDisabled()
    {
        using var session = new CancellationTokenSource();
        var disabled = new TlsCommandWorker<int>(2, null, session);
        Require(disabled.Post is null && disabled.Completion.IsCompletedSuccessfully,
            "disabled gameplay command became a new ingress");
        var worker = new TlsCommandWorker<int>(2,
            _ => Task.CompletedTask, session);
        session.Cancel();
        worker.Complete();
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Require(worker.Fault is null, "expected socket cancellation recorded a handler fault");
    }
}
