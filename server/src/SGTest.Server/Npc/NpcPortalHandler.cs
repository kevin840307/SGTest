using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.PlayerShop;
using SGTest.Server.Trade;
using SGTest.Server.World;

namespace SGTest.Server.Npc;

/// <summary>Coordinates a trusted map transfer and the old map's gameplay lifetime.</summary>
internal sealed class NpcPortalHandler(
    ServerWorldHost world, PortalCatalog portals, PlayerCombatSettlement? combat,
    PlayerShopCoordinator? playerShops, PlayerTradeCoordinator? trades,
    WorldResultRouter router, SemaphoreSlim gameplayGate)
{
    public async Task<NpcPortalOutcome> HandleAsync(TrustedNpcInteraction request)
    {
        // The shared gameplay gate also protects item settlement and disconnect.
        await gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (combat is not null)
                await combat.SuspendAndDrainAsync(
                    request.ActorId, request.ConnectionEpoch).ConfigureAwait(false);

            var oldBinding = new ConnectionBinding(
                request.ActorId, request.ConnectionEpoch, request.RequestMapEpoch);
            var outcome = await router.TrustedNpcInteractAsync(
                world, portals, oldBinding,
                request.InteractionSequence, request.NpcSpawnId).ConfigureAwait(false);

            // Trade cannot span a MapEpoch. The old binding stored by the
            // trade coordinator is intentionally used to cancel the
            // pre-transfer session after a successful authoritative portal.
            if (outcome.Status == NpcPortalStatus.Accepted)
            {
                if (playerShops is not null && outcome.TransferredActor is { } shopAfter)
                    await playerShops.CloseForBindingAsync(
                        ConnectionBinding.FromTrustedSnapshot(shopAfter), router)
                        .ConfigureAwait(false);
                trades?.Disconnect(oldBinding, router);
                if (trades is not null &&
                   (outcome.TransferredActor is not { } after ||
                    !trades.TryAdvanceGameplayGate(
                        oldBinding, after, gameplayGate)))
                    throw new InvalidOperationException(
                        "Trade gameplay lock did not advance with trusted NPC map generation");
            }

            if (combat is not null)
            {
                long nextEpoch = outcome.TransferredActor?.MapEpoch
                    ?? request.RequestMapEpoch;
                if (!combat.ResumeIfAttached(
                        request.ActorId, request.ConnectionEpoch, nextEpoch))
                    throw new InvalidOperationException(
                        "Combat generation could not resume after NPC interaction");
            }
            return outcome;
        }
        finally { gameplayGate.Release(); }
    }
}
