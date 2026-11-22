using System.Buffers.Binary;
using SGTest.Server.Transport;
using SGTest.Server.Wire;

/// <summary>
/// Integration checks that previously chose passwordless synthetic characters
/// now establish a secondary password over the same authenticated AES socket.
/// This helper is test-only; production never guesses a password.
/// </summary>
internal static class CharacterChooseTestHarness
{
    public const string DefaultProof = "2468";

    public static async Task ChooseAsync(AesCbcFrameStream stream,long characterId,
        string password=DefaultProof,CancellationToken cancellationToken=default)
    {
        // Legacy test characters may lack character-creation settings. A
        // genuine authenticated management request provisions their proof.
        // Existing protected characters retain their already assigned proof.
        await stream.WriteAsync(WireProtocol.EncodeCharacterManage(
            new(CharacterManageAction.ChangePassword,characterId,null,DefaultProof)),cancellationToken);
        await stream.FlushAsync();
        var frame=await ReadFrameAsync(stream,cancellationToken);
        Checks.Require(WireProtocol.TryDecodeCharacterManageResult(frame,out var result)
            && result.Action==CharacterManageAction.ChangePassword
            && result.CharacterId==characterId,
            "character choose test setup lacked management receipt");
        if(result.Status==CharacterManageStatus.Success)
        {
            Checks.Require(WireProtocol.TryDecodeCharacterList(
                await ReadFrameAsync(stream,cancellationToken),out _),
                "character choose test setup lacked refreshed character list");
        }
        else Checks.Require(result.Status is CharacterManageStatus.InvalidPassword or
             CharacterManageStatus.NotOwned,
             "character choose test setup unexpectedly failed");
        await stream.WriteAsync(WireProtocol.EncodeCharacterChoose(
            new(characterId,password)),cancellationToken);
        await stream.FlushAsync();
    }

    private static async Task<byte[]> ReadFrameAsync(AesCbcFrameStream stream,CancellationToken cancellationToken)
    {
        var framePrefix=new byte[WireProtocol.HeaderBytes];
        await stream.ReadExactlyAsync(framePrefix,cancellationToken);
        int length=BinaryPrimitives.ReadInt32LittleEndian(framePrefix.AsSpan(0,4));
        Checks.Require(length>=WireProtocol.HeaderBytes &&
            length<=WireProtocol.MaximumFrameBytes,"character choose test invalid frame length");
        var frame=new byte[length];
        framePrefix.CopyTo(frame,0);
        if(length>framePrefix.Length)
            await stream.ReadExactlyAsync(frame.AsMemory(framePrefix.Length),cancellationToken);
        return frame;
    }
}
