using SGTest.Server.Gateway;

namespace SGTest.Server.Trade;

/// <summary>Orders one socket's trade requests and checks its current binding.</summary>
internal sealed class TradeCommandIngress(
    PlayerTradeCoordinator coordinator, WorldResultRouter router) : IDisposable
{
    private readonly SemaphoreSlim _order = new(1, 1);

    public async Task HandleAsync(TrustedTradeCommand request)
    {
        var binding = new ConnectionBinding(
            request.ActorId, request.ConnectionEpoch, request.RequestMapEpoch);
        // FinalConfirm acquires both participants' gameplay gates in the coordinator.
        await _order.WaitAsync().ConfigureAwait(false);
        try
        {
            if (router.IsCurrentBinding(binding))
                await coordinator.HandleAsync(request, router).ConfigureAwait(false);
        }
        finally { _order.Release(); }
    }

    public void Dispose() => _order.Dispose();
}
