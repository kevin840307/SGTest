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

    public IdentityTlsAuthorization(SqliteIdentityStore identity, ServerWorldHost world,
                                    PortalCatalog? portals = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _portals = portals; // runtime-owned audited Converter NPC content; null disables portal RPC
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
                if (attached) boundConnection = connection;
            }
            if (!attached)
            {
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
                    await _identity.UpdateCharacterSpawnAsync(snapshot.ActorId,
                        new CharacterSpawn(snapshot.MapId, snapshot.X, snapshot.Y))
                        .ConfigureAwait(false);
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
            Func<TrustedNpcInteraction, Task<NpcPortalOutcome>>? npcHandler =
                _portals is null ? null : async request =>
                {
                    // The Gateway added ActorId/ConnectionEpoch from the
                    // authenticated connection. Portal destination is never
                    // supplied by this client request.
                    return await router.TrustedNpcInteractAsync(
                        _world, _portals!,
                        new ConnectionBinding(request.ActorId, request.ConnectionEpoch,
                            request.RequestMapEpoch),
                        request.InteractionSequence, request.NpcSpawnId).ConfigureAwait(false);
                };
            return await TlsWireConnection.RunServerAsync(client, certificate,
                Authorize, _world.TryPostMove, options, cancellationToken, Release,
                npcHandler, router.TryRequestResync).ConfigureAwait(false);
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
                router.TryDetach(abandoned);
                var binding = abandoned.Binding;
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
        bool completed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.Success, snapshot.ActorId, snapshot.MapId,
                snapshot.X, snapshot.Y, snapshot.MapEpoch)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var gateway = new GatewayConnection(snapshot);
            completed = true;
            return gateway;
        }
        finally
        {
            if (!completed)
            {
                // Failed/cancelled handshake after TrustedRegister must not leave
                // a ghost actor that blocks this character's next login.
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
