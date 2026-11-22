using System.Buffers.Binary;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class CharacterAppearanceWireChecks
{
    internal const string EmptyHex="1A00000001000C00000302000000000000000001030007020102";
    internal const string ListHex="4C00000001000C00020B000000000000000006004C6567616379160000000000000001060043686F73656E030000000000000000010103000702010202000000000000000001030007020102";
    internal const string CreateHex="1800000001000F0001060043686F73656E01030007020102";
    internal const string ResultHex="210000000100100000160000000000000001060043686F73656E01030007020102";
    internal const string BodyLegListHex="4800000001000C00020B000000000000000006004C6567616379160000000000000001060043686F73656E0200000000000000010103000702010200000000000000010300070201";
    internal const string BodyLegCreateHex="1700000001000F0001060043686F73656E010300070201";
    internal const string BodyLegResultHex="200000000100100000160000000000000001060043686F73656E010300070201";
    internal const string LegacyListHex="4000000001000c00020b000000000000000006004c6567616379160000000000000001060043686f73656e010000000000010103000702000000000001030007";
    internal const string LegacyCreateHex="1500000001000f0001060043686f73656e01030007";
    internal const string LegacyResultHex="1e0000000100100000160000000000000001060043686f73656e01030007";
    private static readonly CharacterAppearance Look=new(1,3,7,2,1,2);
    private static readonly CharacterAppearance[] Choices=[new(0,0,0),Look];

    internal static void Verify()
    {
        Checks.Run("character appearance Wire V1 matches Godot golden and rejects malformed extensions",Golden);
        Checks.Run("character appearance list bounds 32 names and 7000 unique choices",Bounds);
    }

    private static void Golden()
    {
        var empty=WireProtocol.EncodeCharacterList(new([],Choices));
        var list=WireProtocol.EncodeCharacterList(new([new(11,0,"Legacy"),new(22,1,"Chosen",Look)],Choices));
        var create=WireProtocol.EncodeCharacterCreate(new(1,"Chosen",Look));
        var result=WireProtocol.EncodeCharacterCreateResult(new(CharacterCreateWireStatus.Success,22,1,"Chosen",Look));
        foreach(var pair in new[]{(empty,EmptyHex),(list,ListHex),(create,CreateHex),(result,ResultHex)})
            Checks.Require(Convert.ToHexString(pair.Item1).Equals(pair.Item2,StringComparison.OrdinalIgnoreCase),"appearance golden differs");
        var bodyLegList=Convert.FromHexString(BodyLegListHex);
        var bodyLegCreate=Convert.FromHexString(BodyLegCreateHex);
        var bodyLegResult=Convert.FromHexString(BodyLegResultHex);
        Checks.Require(
            WireProtocol.TryDecodeCharacterList(bodyLegList,out var bodyLegDecoded) &&
            bodyLegDecoded.Characters[1].Appearance==new CharacterAppearance(1,3,7,2,1) &&
            WireProtocol.TryDecodeCharacterCreate(bodyLegCreate,out var bodyLegCreateDecoded) &&
            bodyLegCreateDecoded.Appearance==new CharacterAppearance(1,3,7,2,1) &&
            WireProtocol.TryDecodeCharacterCreateResult(bodyLegResult,out var bodyLegResultDecoded) &&
            bodyLegResultDecoded.Appearance==new CharacterAppearance(1,3,7,2,1),
            "legacy six-byte body/leg appearance frames are no longer readable");
        var legacyList=Convert.FromHexString(LegacyListHex);
        var legacyCreate=Convert.FromHexString(LegacyCreateHex);
        var legacyResult=Convert.FromHexString(LegacyResultHex);
        Checks.Require(
            WireProtocol.TryDecodeCharacterList(legacyList,out var oldList) &&
            oldList.Characters[1].Appearance==new CharacterAppearance(1,3,7) &&
            WireProtocol.TryDecodeCharacterCreate(legacyCreate,out var oldCreate) &&
            oldCreate.Appearance==new CharacterAppearance(1,3,7) &&
            WireProtocol.TryDecodeCharacterCreateResult(legacyResult,out var oldResult) &&
            oldResult.Appearance==new CharacterAppearance(1,3,7),
            "legacy four-byte appearance frames are no longer readable");
        Checks.Require(WireProtocol.TryDecodeCharacterList(list,out var decoded) &&
            decoded.Characters[0].Appearance is null && decoded.Characters[1].Appearance==Look &&
            decoded.CreationAppearances!.SequenceEqual(Choices),"appearance list lost known/unknown/allowed values");
        Checks.Require(WireProtocol.TryDecodeCharacterCreate(create,out var request) && request.Appearance==Look,
            "appearance request roundtrip failed");
        var protectedCreate=WireProtocol.EncodeCharacterCreate(new(
            2,"Protected",Look,new CharacterCreationWire(3,2,2,1,"2468")));
        Checks.Require(WireProtocol.TryDecodeCharacterCreate(protectedCreate,out var protectedRequest) &&
            protectedRequest.Appearance==Look &&
            protectedRequest.Creation is { Strength:3,Dexterity:2,Constitution:2,Wisdom:1,CharacterPassword:"2468" },
            "extended creation attributes/password roundtrip failed");
        Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(result,out var receipt) && receipt.Appearance==Look,
            "appearance result roundtrip failed");
        foreach(int at in new[]{0,8,10,11,12,13,14,15,16,17,18,19})
        {
            var truncated=empty[..at];
            if(at>=8) BinaryPrimitives.WriteInt32LittleEndian(truncated,at);
            Checks.Require(!WireProtocol.TryDecodeCharacterList(truncated,out _),"truncated menu accepted");
        }
        foreach(int at in new[]{9,12,15,19,22})
        {
            var bad=empty.ToArray(); bad[at]=255;
            Checks.Require(!WireProtocol.TryDecodeCharacterList(bad,out _),"invalid menu schema/race/color accepted");
        }
        var duplicate=empty.ToArray(); Array.Copy(duplicate,12,duplicate,19,7);
        Checks.Require(!WireProtocol.TryDecodeCharacterList(duplicate,out _),"duplicate appearance choice accepted");
        var unknown=list.ToArray(); unknown[45]=1;
        Checks.Require(!WireProtocol.TryDecodeCharacterList(unknown,out _),"unknown appearance has nonzero reserved data");
        var badCreate=create.ToArray(); badCreate[20]=8;
        Checks.Require(!WireProtocol.TryDecodeCharacterCreate(badCreate,out _),"bad appearance color accepted");
        var badResult=result.ToArray(); badResult[8]=(byte)CharacterCreateWireStatus.InvalidAppearance;
        Checks.Require(!WireProtocol.TryDecodeCharacterCreateResult(badResult,out _),"failed create may not claim a character/appearance");
    }

    private static void Bounds()
    {
        var choices=Enumerable.Range(0,7000).Select(h=>new CharacterAppearance(0,h,0)).ToArray();
        var characters=Enumerable.Range(0,32).Select(i=>new CharacterSummaryWire(i+1,(byte)i,new string('x',96),Look)).ToArray();
        var frame=WireProtocol.EncodeCharacterList(new(characters,choices));
        Checks.Require(frame.Length==WireProtocol.MaxCharacterListFrameBytes &&
            WireProtocol.TryDecodeCharacterList(frame,out var decoded) && decoded.CreationAppearances!.Count==7000,
            "maximum bounded appearance list failed");
        bool rejected=false;
        try { WireProtocol.EncodeCharacterList(new([],choices.Append(new(0,7000,0)).ToArray())); }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Checks.Require(rejected,"oversized appearance menu encoded");
    }
}
