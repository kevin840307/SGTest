using System.Buffers.Binary;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

internal static class CharacterAppearanceWireChecks
{
    internal const string EmptyHex="1400000001000c00000102000000000001030007";
    internal const string ListHex="4000000001000c00020b000000000000000006004c6567616379160000000000000001060043686f73656e010000000000010103000702000000000001030007";
    internal const string CreateHex="1500000001000f0001060043686f73656e01030007";
    internal const string ResultHex="1e0000000100100000160000000000000001060043686f73656e01030007";
    private static readonly CharacterAppearance Look=new(1,3,7);
    private static readonly CharacterAppearance[] Choices=[new(0,0,0),Look];

    internal static void Verify()
    {
        Checks.Run("character appearance Wire V1 matches Godot golden and rejects malformed extensions",Golden);
        Checks.Run("character appearance list bounds 32 names and 4096 unique choices",Bounds);
    }

    private static void Golden()
    {
        var empty=WireProtocol.EncodeCharacterList(new([],Choices));
        var list=WireProtocol.EncodeCharacterList(new([new(11,0,"Legacy"),new(22,1,"Chosen",Look)],Choices));
        var create=WireProtocol.EncodeCharacterCreate(new(1,"Chosen",Look));
        var result=WireProtocol.EncodeCharacterCreateResult(new(CharacterCreateWireStatus.Success,22,1,"Chosen",Look));
        foreach(var pair in new[]{(empty,EmptyHex),(list,ListHex),(create,CreateHex),(result,ResultHex)})
            Checks.Require(Convert.ToHexString(pair.Item1).Equals(pair.Item2,StringComparison.OrdinalIgnoreCase),"appearance golden differs");
        Checks.Require(WireProtocol.TryDecodeCharacterList(list,out var decoded) &&
            decoded.Characters[0].Appearance is null && decoded.Characters[1].Appearance==Look &&
            decoded.CreationAppearances!.SequenceEqual(Choices),"appearance list lost known/unknown/allowed values");
        Checks.Require(WireProtocol.TryDecodeCharacterCreate(create,out var request) && request.Appearance==Look,
            "appearance request roundtrip failed");
        Checks.Require(WireProtocol.TryDecodeCharacterCreateResult(result,out var receipt) && receipt.Appearance==Look,
            "appearance result roundtrip failed");
        foreach(int at in new[]{0,8,10,11,12,13,14,15,16,17,18,19})
        {
            var truncated=empty[..at];
            if(at>=8) BinaryPrimitives.WriteInt32LittleEndian(truncated,at);
            Checks.Require(!WireProtocol.TryDecodeCharacterList(truncated,out _),"truncated menu accepted");
        }
        foreach(int at in new[]{9,12,15,16,19})
        {
            var bad=empty.ToArray(); bad[at]=255;
            Checks.Require(!WireProtocol.TryDecodeCharacterList(bad,out _),"invalid menu schema/count/race/color accepted");
        }
        var duplicate=empty.ToArray(); Array.Copy(duplicate,12,duplicate,16,4);
        Checks.Require(!WireProtocol.TryDecodeCharacterList(duplicate,out _),"duplicate appearance choice accepted");
        var unknown=list.ToArray(); unknown[45]=1;
        Checks.Require(!WireProtocol.TryDecodeCharacterList(unknown,out _),"unknown appearance has nonzero reserved data");
        var badCreate=create.ToArray(); badCreate[^1]=8;
        Checks.Require(!WireProtocol.TryDecodeCharacterCreate(badCreate,out _),"bad appearance color accepted");
        var badResult=result.ToArray(); badResult[8]=(byte)CharacterCreateWireStatus.InvalidAppearance;
        Checks.Require(!WireProtocol.TryDecodeCharacterCreateResult(badResult,out _),"failed create may not claim a character/appearance");
    }

    private static void Bounds()
    {
        var choices=Enumerable.Range(0,4096).Select(h=>new CharacterAppearance(0,h,0)).ToArray();
        var characters=Enumerable.Range(0,32).Select(i=>new CharacterSummaryWire(i+1,(byte)i,new string('x',96),Look)).ToArray();
        var frame=WireProtocol.EncodeCharacterList(new(characters,choices));
        Checks.Require(frame.Length==WireProtocol.MaxCharacterListFrameBytes &&
            WireProtocol.TryDecodeCharacterList(frame,out var decoded) && decoded.CreationAppearances!.Count==4096,
            "maximum bounded appearance list failed");
        bool rejected=false;
        try { WireProtocol.EncodeCharacterList(new([],choices.Append(new(0,4096,0)).ToArray())); }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Checks.Require(rejected,"oversized appearance menu encoded");
    }
}
