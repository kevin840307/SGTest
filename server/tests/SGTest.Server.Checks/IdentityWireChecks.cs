using SGTest.Server.Wire;

internal static class IdentityWireChecks
{
    public static void Verify()
    {
        Checks.Run("identity wire AuthRequest strict UTF8 length and roundtrip", AuthRequest);
        Checks.Run("identity wire AccountRegister strict UTF8 length status and roundtrip",
            AccountRegister);
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


    private static void AccountRegister()
    {
        var request=new AccountRegisterRequestWire(
            "new_user","Registration-Password-123!");
        var frame=WireProtocol.EncodeAccountRegisterRequest(request);
        Checks.Require(
            WireProtocol.TryDecodeAccountRegisterRequest(frame,out var decoded)&&
            decoded==request,
            "account register request roundtrip");
        var invalidUtf8=(byte[])frame.Clone();
        invalidUtf8[12]=0xff;
        Checks.Require(
            !WireProtocol.TryDecodeAccountRegisterRequest(invalidUtf8,out _),
            "account register invalid UTF8 accepted");
        foreach(AccountRegisterWireStatus status in Enum.GetValues<AccountRegisterWireStatus>())
        {
            var result=WireProtocol.EncodeAccountRegisterResult(new(status));
            Checks.Require(
                WireProtocol.TryDecodeAccountRegisterResult(result,out var back)&&
                back.Status==status,
                "account register result status roundtrip");
        }
        var invalidStatus=WireProtocol.EncodeAccountRegisterResult(
            new(AccountRegisterWireStatus.Created));
        invalidStatus[8]=255;
        Checks.Require(
            !WireProtocol.TryDecodeAccountRegisterResult(invalidStatus,out _),
            "unknown account register status accepted");
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
