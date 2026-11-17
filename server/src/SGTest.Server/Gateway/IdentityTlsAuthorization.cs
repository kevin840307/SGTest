using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

/// <summary>
/// TLS-only login/character selection boundary. It never creates accounts and it
/// never trusts a client-supplied ActorId: CharacterChoose is matched against the
/// authenticated account, then the persistent character_id is passed to the
/// trusted World lifecycle API.
/// </summary>
public sealed class IdentityTlsAuthorization
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PortalCatalog? _portals;
    private readonly CharacterPositionCheckpoint? _positions;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;

    public IdentityTlsAuthorization(SqliteIdentityStore identity, ServerWorldHost world,
                                    PortalCatalog? portals = null,
                                    CharacterPositionCheckpoint? positions = null,
                                    PlayerCombatSettlement? combat = null,
                                     long pickupWorldGeneration = 0)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _portals = portals;
        _positions = positions;
        _combat = combat;
        if (pickupWorldGeneration is < 0 or > 0x3fffffff)
            throw new ArgumentOutOfRangeException(nameof(pickupWorldGeneration));
        _pickupWorldGeneration = pickupWorldGeneration;
    }

    /// <summary>
    /// The official authenticated socket lifecycle for a future production
    /// listener: binds the selected character, registers the receipt route,
    /// and releases BOTH the route and the World actor after the socket ends.
    /// No listener, credentials or plaintext fallback are provided here.
    /// </summary>
    public async Task<TlsWireConnectionResult> RunAuthenticatedSocketAsync(
        TcpClient client, X509Certificate2 certificate, WorldResultRouter router,
        TlsWireConnectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(router);
        var bindingGate = new object();
        bool sessionFinished = false;
        GatewayConnection? boundConnection = null;

        async ValueTask<GatewayConnection?> Authorize(SslStream stream, CancellationToken token)
        {
            var connection = await AuthorizeAndBindAsync(stream, token).ConfigureAwait(false);
            if (connection is null) return null;
            var binding = connection.Binding;
            bool attached;
            lock (bindingGate)
            {
                // The TLS outer authorization timeout may win its WaitAsync race
                // just before this callback returns. Never attach after the
                // socket runner's finally has already released the session.
                attached = !sessionFinished && !token.IsCancellationRequested &&
                    router.TryAttach(connection);
                if (attached && _combat is not null &&
                    !_combat.ResumeIfAttached(
                        binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch))
                {
                    router.TryDetach(connection);
                    attached = false;
                }
                if (attached) boundConnection = connection;
            }
            if (!attached)
            {
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                connection.Close();
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
                return null;
            }
            return connection;
        }

        async Task Release(ConnectionBinding binding)
        {
            GatewayConnection? connection;
            lock (bindingGate)
            {
                connection = boundConnection;
                boundConnection = null;
            }
            // Drain committed combat BEFORE detaching the Gateway. The settlement
            // worker queues each reliable damage/death frame only after SQLite
            // commits, so reconnect cannot observe old HP and the live socket
            // cannot lose a committed result merely because release won a race.
            if (_combat is not null)
                await _combat.CloseAndDrainAsync(
                    binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
            if (connection is not null) router.TryDetach(connection);

            // Keep the character registered while its final authoritative
            // coordinates are committed. This prevents a reconnect from reading
            // the old SQLite spawn in the small DB-write window.
            ActorSnapshot? final = await _world.TrustedPrepareDisconnectAsync(
                binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                .ConfigureAwait(false);
            try
            {
                if (final is { } snapshot)
                {
                    if (_positions is not null)
                        await _positions.FinalizeAsync(snapshot).ConfigureAwait(false);
                    else
                        await _identity.UpdateCharacterSpawnAsync(snapshot.ActorId,
                            new CharacterSpawn(snapshot.MapId, snapshot.X, snapshot.Y))
                            .ConfigureAwait(false);
                }
            }
            finally
            {
                var release = final ?? new ActorSnapshot(
                    binding.ActorId, 0, binding.ConnectionEpoch, binding.MapEpoch,
                    0, 0, 0, 0, 0, false);
                await _world.TrustedDisconnectAsync(
                    release.ActorId, release.ConnectionEpoch, release.MapEpoch)
                    .ConfigureAwait(false);
            }
        }

        try
        {
            // Map transfers and pickup settlement for this authenticated
            // socket must not race one another across separate async workers.
            using var gameplayGate = new SemaphoreSlim(1,1);
            Func<TrustedNpcInteraction, Task<NpcPortalOutcome>>? npcHandler =
                _portals is null ? null : async request =>
                {
                    await gameplayGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                    if (_combat is not null)
                        await _combat.SuspendAndDrainAsync(
                            request.ActorId, request.ConnectionEpoch).ConfigureAwait(false);

                    var outcome = await router.TrustedNpcInteractAsync(
                        _world, _portals!,
                        new ConnectionBinding(request.ActorId, request.ConnectionEpoch,
                            request.RequestMapEpoch),
                        request.InteractionSequence, request.NpcSpawnId).ConfigureAwait(false);

                    if (_combat is not null)
                    {
                        long nextEpoch = outcome.TransferredActor?.MapEpoch
                            ?? request.RequestMapEpoch;
                        if (!_combat.ResumeIfAttached(
                                request.ActorId, request.ConnectionEpoch, nextEpoch))
                            throw new InvalidOperationException(
                                "Combat generation could not resume after NPC interaction");
                    }
                    return outcome;
                    }
                    finally { gameplayGate.Release(); }
                };
            async Task RespawnHandler(TrustedPlayerRespawn request)
            {
                var binding = new ConnectionBinding(
                    request.ActorId, request.ConnectionEpoch, request.RequestMapEpoch);
                if (!router.IsCurrentBinding(binding))
                    return;

                PlayerRespawnOutcome outcome;
                ActorSnapshot? revived = null;
                if (_combat is null)
                {
                    outcome = new(PlayerRespawnStatus.CombatUnavailable, 0, 0);
                }
                else
                {
                    outcome = await _combat.RespawnFullAsync(
                        request.ActorId, request.ConnectionEpoch,
                        request.RequestMapEpoch, request.RespawnSequence)
                        .ConfigureAwait(false);

                    if (outcome.Status == PlayerRespawnStatus.Accepted)
                    {
                        revived = await _world.TrustedSetCombatDeadAsync(
                            request.ActorId, request.ConnectionEpoch,
                            request.RequestMapEpoch, false).ConfigureAwait(false);
                        if (revived is null)
                            outcome = new(PlayerRespawnStatus.StaleGeneration,
                                outcome.CurrentHp, outcome.MaxHp);
                    }
                }

                var sent = router.QueuePlayerRespawnResult(request, outcome);
                if (sent != GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable player respawn result could not be queued: " + sent);

                // Queue the authoritative full-HP baseline BEFORE reopening AI
                // targetability, so later PlayerDamage cannot overtake RespawnResult.
                if (outcome.Status == PlayerRespawnStatus.Accepted &&
                    revived is { } actor &&
                    (_combat is null || !_combat.TryResume(actor)))
                    throw new InvalidOperationException(
                        "Combat generation could not resume after player respawn");
            }

            async Task PickupHandler(TrustedGroundPickup request)
            {
                var binding = new ConnectionBinding(
                    request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!router.IsCurrentBinding(binding))
                        return;
                    ActorSnapshot? snapshot = await _world.TrustedGetActorAsync(
                        request.ActorId).ConfigureAwait(false);
                    PickupResult result;
                    if (snapshot is not { } actor ||
                        actor.ConnectionEpoch != request.ConnectionEpoch ||
                        actor.MapEpoch != request.RequestMapEpoch ||
                        actor.ActorId != request.ActorId)
                        result = new(PickupStatus.StaleGeneration,request.DropId);
                    else if (_combat is not null && _combat.CurrentHp(actor) <= 0)
                        result = new(PickupStatus.PlayerDead,request.DropId);
                    else
                        result = await _identity.ClaimGroundDropAsync(
                            new TrustedPickupContext(actor.ActorId,
                                _pickupWorldGeneration == 0
                                    ? actor.ConnectionEpoch // isolated fixture only
                                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                        _pickupWorldGeneration,actor.ConnectionEpoch),
                                actor.MapEpoch,
                                request.PickupSequence,actor.MapId,actor.X,actor.Y),
                            request.DropId).ConfigureAwait(false);

                    // SQLite commits first. Never remove on rejection or
                    // client intent alone. A failed notification is terminal,
                    // not a fabricated successful response.
                    if (result.Status == PickupStatus.Granted &&
                        !router.TryPostGroundDropClaimed(result.DropId))
                        throw new IOException(
                            "Committed drop claimed but shared AOI notification was full");

                    var sent = router.QueueGroundPickupResult(request,result);
                    if (sent != GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable pickup result could not be queued: " + sent);
                }
                finally { gameplayGate.Release(); }
            }

            return await TlsWireConnection.RunServerAsync(client, certificate,
                Authorize, _world.TryPostMove, options, cancellationToken, Release,
                npcHandler, router.TryRequestResync,
                router.TryRequestNormalAttack, RespawnHandler,
                PickupHandler).ConfigureAwait(false);
        }
        finally
        {
            // Also covers a rare outer WaitAsync timeout while the auth callback
            // has already attached a Gateway but has not yet returned it.
            GatewayConnection? abandoned;
            lock (bindingGate)
            {
                sessionFinished = true;
                abandoned = boundConnection;
                boundConnection = null;
            }
            if (abandoned is not null)
            {
                var binding = abandoned.Binding;
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                router.TryDetach(abandoned);
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
            }
        }
    }

    public async ValueTask<GatewayConnection?> AuthorizeAndBindAsync(
        SslStream tls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tls);
        byte[]? authFrame = await ReadFrameAsync(tls, cancellationToken).ConfigureAwait(false);
        if (authFrame is null || !WireProtocol.TryDecodeAuthRequest(authFrame, out var request)) return null;

        var authentication = await _identity.AuthenticateAsync(
            request.Username, request.Password, cancellationToken).ConfigureAwait(false);
        AuthWireStatus authStatus = authentication.Status switch
        {
            AuthenticationStatus.Success => AuthWireStatus.Success,
            AuthenticationStatus.Disabled => AuthWireStatus.Disabled,
            _ => AuthWireStatus.InvalidCredentials
        };
        await WriteFrameAsync(tls, WireProtocol.EncodeAuthResult(new(authStatus)), cancellationToken)
            .ConfigureAwait(false);
        if (authentication.Status != AuthenticationStatus.Success || authentication.Account is null)
            return null;

        var account = authentication.Account;
        var summaries = account.Characters
            .Select(c => new CharacterSummaryWire(c.CharacterId, checked((byte)c.Slot), c.Name))
            .ToArray();
        await WriteFrameAsync(tls, WireProtocol.EncodeCharacterList(new(summaries)), cancellationToken)
            .ConfigureAwait(false);
        if (summaries.Length == 0) return null; // character creation is a later protocol slice.

        byte[]? chooseFrame = await ReadFrameAsync(tls, cancellationToken).ConfigureAwait(false);
        if (chooseFrame is null || !WireProtocol.TryDecodeCharacterChoose(chooseFrame, out var choose)) return null;
        CharacterRecord? character = account.Characters.FirstOrDefault(c => c.CharacterId == choose.CharacterId);
        if (character is null)
        {
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.NotOwned, 0, 0, 0, 0, 0)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        CharacterCombatProfile? combat = await _identity.GetCharacterCombatProfileAsync(
            character.CharacterId, cancellationToken).ConfigureAwait(false);
        TrustedCombatProfile? trustedCombat = combat is null ? null : new(
            combat.Data.Attack, combat.Data.Defense, combat.Data.Hit, combat.Data.Dodge,
            combat.Data.AttackRange, combat.Data.AttackCooldownTicks);

        // Do not cancel ONLY the waiter: the trusted World queue may finish
        // registration after the TLS authorization timeout, leaking a character.
        ActorSnapshot? actor;
        try
        {
            actor = await _world.TrustedRegisterAsync(character.CharacterId,
                character.MapId, character.X, character.Y).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            actor = null;
        }
        if (actor is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.WorldRejected, 0, 0, 0, 0, 0)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        var snapshot = actor.Value;
        bool combatAttached = false;
        bool completed = false;
        try
        {
            if (combat is not null && _combat is not null)
            {
                combatAttached = _combat.TryAttach(snapshot, combat.Data);
                if (!combatAttached)
                    throw new InvalidOperationException(
                        "Combat settlement rejected authenticated character generation");
                if (combat.Data.CurrentHp == 0)
                {
                    var dead = await _world.TrustedSetCombatDeadAsync(
                        snapshot.ActorId, snapshot.ConnectionEpoch, snapshot.MapEpoch, true)
                        .ConfigureAwait(false);
                    if (dead is null)
                        throw new InvalidOperationException(
                            "Stored dead combat state could not gate authoritative World actor");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.Success, snapshot.ActorId, snapshot.MapId,
                snapshot.X, snapshot.Y, snapshot.MapEpoch)), cancellationToken).ConfigureAwait(false);

            if (combat is not null)
            {
                await WriteFrameAsync(tls, WireProtocol.EncodePlayerCombatState(new(
                    snapshot.MapEpoch, combat.Data.CurrentHp, combat.Data.MaxHp)),
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var gateway = new GatewayConnection(
                snapshot, combatProfile: trustedCombat);
            completed = true;
            return gateway;
        }
        finally
        {
            if (!completed)
            {
                if (combatAttached && _combat is not null)
                    await _combat.CloseAndDrainAsync(
                        snapshot.ActorId, snapshot.ConnectionEpoch).ConfigureAwait(false);
                await _world.TrustedDisconnectAsync(snapshot.ActorId,
                    snapshot.ConnectionEpoch, snapshot.MapEpoch).ConfigureAwait(false);
            }
        }
    }

    private static async Task<byte[]?> ReadFrameAsync(SslStream tls, CancellationToken cancellationToken)
    {
        byte[] header = new byte[WireProtocol.HeaderBytes];
        try { await tls.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false); }
        catch (EndOfStreamException) { return null; }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        if (length < WireProtocol.HeaderBytes || length > WireProtocol.MaximumFrameBytes) return null;
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        try
        {
            if (length > header.Length)
                await tls.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException) { return null; }
        return WireProtocol.TryReadHeader(frame, out _) ? frame : null;
    }

    private static async Task WriteFrameAsync(SslStream tls, byte[] frame, CancellationToken cancellationToken)
    {
        await tls.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
