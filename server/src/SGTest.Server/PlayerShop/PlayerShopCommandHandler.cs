using SGTest.Server.Gateway;
using SGTest.Server.Wire;

namespace SGTest.Server.PlayerShop;

/// <summary>Handles replay and stale commands on one authenticated connection.</summary>
internal sealed class PlayerShopCommandHandler(
    PlayerShopCoordinator coordinator, WorldResultRouter router)
{
    private readonly PlayerShopSequenceGate _sequence = new();

    public async Task HandleAsync(TrustedPlayerShopCommand request)
    {
        var decision = _sequence.Inspect(request);
        if (decision == PlayerShopSequenceDecision.New)
        {
            var receipt = await coordinator.HandleNewAsync(request, router).ConfigureAwait(false);
            _sequence.Record(request, receipt);
            return;
        }

        bool replay = decision == PlayerShopSequenceDecision.ExactReplay;
        var result = replay ? _sequence.LastReceipt : new PlayerShopResultWire(
            request.RequestMapEpoch, request.ShopSequence, request.Action,
            PlayerShopResultStatus.StaleSequence, request.SellerActorId,
            request.ListingId, 0, 0, 0, 0, -1, 0, 0, 0);
        var sent = router.QueuePlayerShopResult(request, result);
        if (sent != GatewaySendStatus.Enqueued)
            throw new IOException($"Reliable player-shop {(replay ? "replay" : "stale result")} could not be queued: {sent}");
    }
}
