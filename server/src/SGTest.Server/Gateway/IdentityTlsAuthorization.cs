using System.Buffers.Binary;
using System.Net.Security;
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

    public IdentityTlsAuthorization(SqliteIdentityStore identity, ServerWorldHost world)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _world = world ?? throw new ArgumentNullException(nameof(world));
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

        ActorSnapshot? actor;
        try
        {
            actor = await _world.TrustedRegisterAsync(character.CharacterId,
                character.MapId, character.X, character.Y).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            actor = null;
        }
        if (actor is null)
        {
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.WorldRejected, 0, 0, 0, 0, 0)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        var snapshot = actor.Value;
        await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
            CharacterChooseWireStatus.Success, snapshot.ActorId, snapshot.MapId,
            snapshot.X, snapshot.Y, snapshot.MapEpoch)), cancellationToken).ConfigureAwait(false);
        return new GatewayConnection(snapshot);
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
