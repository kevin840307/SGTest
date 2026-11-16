using SGTest.Server.Wire;

internal static class IdentityWireChecks
{
    public static void Verify()
    {
        Checks.Run("identity wire AuthRequest strict UTF8 length and roundtrip", AuthRequest);
        Checks.Run("identity wire CharacterList Choose and result roundtrip", Characters);
    }

    private static void AuthRequest()
    {
        var encoded = WireProtocol.EncodeAuthRequest(new("user_01", "Password-??-123!"));
        Checks.Require(WireProtocol.TryDecodeAuthRequest(encoded, out var decoded) &&
            decoded.Username == "user_01" && decoded.Password == "Password-??-123!",
            "auth request roundtrip");
        var invalidUtf8 = (byte[])encoded.Clone();
        invalidUtf8[12] = 0xff;
        Checks.Require(!WireProtocol.TryDecodeAuthRequest(invalidUtf8, out _),
            "invalid UTF8 accepted");
        var badLength = (byte[])encoded.Clone();
        badLength[8] = 33; badLength[9] = 0;
        Checks.Require(!WireProtocol.TryDecodeAuthRequest(badLength, out _),
            "auth username length mismatch accepted");
    }

    private static void Characters()
    {
        var list = new CharacterListWire(new[]
        {
            new CharacterSummaryWire(11, 0, "Hero"),
            new CharacterSummaryWire(22, 1, "???")
        });
        var frame = WireProtocol.EncodeCharacterList(list);
        Checks.Require(WireProtocol.TryDecodeCharacterList(frame, out var decoded) &&
            decoded.Characters.Count == 2 && decoded.Characters[1].CharacterId == 22 &&
            decoded.Characters[1].Slot == 1 && decoded.Characters[1].Name == "???",
            "character list roundtrip");
        bool duplicateRejected = false;
        try
        {
            WireProtocol.EncodeCharacterList(new(new[]
            {
                new CharacterSummaryWire(11, 0, "A"),
                new CharacterSummaryWire(11, 1, "B")
            }));
        }
        catch (ArgumentOutOfRangeException) { duplicateRejected = true; }
        Checks.Require(duplicateRejected, "duplicate character IDs encoded");

        var choose = WireProtocol.EncodeCharacterChoose(new(22));
        Checks.Require(WireProtocol.TryDecodeCharacterChoose(choose, out var selected) && selected.CharacterId == 22,
            "CharacterChoose roundtrip");
        var success = WireProtocol.EncodeCharacterChooseResult(new(
            CharacterChooseWireStatus.Success, 22, 15, 7872, 11296, 3));
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(success, out var result) &&
            result.Status == CharacterChooseWireStatus.Success && result.CharacterId == 22 &&
            result.MapId == 15 && result.X == 7872 && result.Y == 11296 && result.MapEpoch == 3,
            "CharacterChooseResult success roundtrip");
        var denied = WireProtocol.EncodeCharacterChooseResult(new(
            CharacterChooseWireStatus.NotOwned, 0, 0, 0, 0, 0));
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(denied, out result) &&
            result.Status == CharacterChooseWireStatus.NotOwned,
            "CharacterChooseResult denial roundtrip");
    }
}
