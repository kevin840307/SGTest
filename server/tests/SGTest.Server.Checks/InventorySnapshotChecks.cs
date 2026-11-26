using SGTest.Server.Wire;

internal static class InventorySnapshotChecks
{
    private static readonly string Golden =
        "51000000010024000B0000000000000000000000010200591B0000000000008D0A000002000000F50100000000000002500000005A1B000000000000750E000003000000F60100000000000007FFFFFFFF";

    public static void Verify()
    {
        Checks.Run("inventory V1 CSharp/Godot golden and bounded chunk validation", WireGolden);
        Checks.Run("inventory 128-slot chunk ordering and strict maximum", ChunkLimits);
    }
    private static void WireGolden()
    {
        var expected=new InventorySnapshotWire(11,0,true,[
            new InventoryItemWire(7001,2701,2,501,2,80),
            new InventoryItemWire(7002,3701,3,502,7,-1)
        ]);
        byte[] payload=WireProtocol.EncodeInventorySnapshot(expected);
        Checks.Require(Convert.ToHexString(payload)==Golden &&
            WireProtocol.TryDecodeInventorySnapshot(payload,out var decoded) &&
            decoded is not null && decoded.CharacterId==11 &&
            decoded.StartIndex==0 && decoded.Complete &&
            decoded.Items.SequenceEqual(expected.Items),
            "cross-language durable item baseline golden mismatch");
        var empty=WireProtocol.EncodeInventorySnapshot(
            new(11,0,true,Array.Empty<InventoryItemWire>()));
        Checks.Require(empty.Length==23 &&
            WireProtocol.TryDecodeInventorySnapshot(empty,out var decodedEmpty) &&
            decodedEmpty is not null && decodedEmpty.Complete &&
            decodedEmpty.Items.Count==0,
            "zero-item final baseline frame was not emitted");
        var wallet=WireProtocol.EncodeInventorySnapshot(new(11,0,true,Array.Empty<InventoryItemWire>(),123456));
        Checks.Require(wallet.Length==31 && WireProtocol.TryDecodeInventorySnapshot(wallet,out var walletDecoded) &&
            walletDecoded?.WalletMoney==123456,"authoritative wallet trailer round trip");
        wallet[20]=0;
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(wallet,out _),"nonfinal wallet trailer accepted");
        wallet[20]=1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(wallet.AsSpan(23),-1);
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(wallet,out _),"negative wallet trailer accepted");

        byte[] bad=(byte[])payload.Clone();
        bad[20]=2; // complete flag may only be 0 or 1
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(bad,out _),
            "invalid inventory finality flag accepted");
        bad=(byte[])payload.Clone();
        bad[23]=0;bad[24]=0; // UID must be nonzero
        bad[25]=0;bad[26]=0;
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(bad,out _),
            "zero item UID accepted");
        bad=(byte[])payload.Clone();
        bad[52]=bad[23];bad[53]=bad[24];bad[54]=bad[25];bad[55]=bad[26];
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(bad,out _),
            "duplicate item UID accepted");
        bad=(byte[])payload.Clone();
        bad[48]=0xFE;bad[49]=0xFF;bad[50]=0xFF;bad[51]=0xFF;
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(bad,out _),
            "durability below -1 accepted");
        Checks.Require(!WireProtocol.TryDecodeInventorySnapshot(
                WireProtocol.EncodeGroundPickup(new(9,1,501)),out _),
            "outbound pickup forged inventory snapshot");
    }
    private static void ChunkLimits()
    {
        var all=Enumerable.Range(0,4095)
            .Select(x=>new InventoryItemWire(10000+x,2701,1,40000+x))
            .ToArray();
        int start=0,batches=0,frames=0;
        while(start<all.Length)
        {
            var chunk=all.Skip(start).Take(128).ToArray();
            var frame=WireProtocol.EncodeInventorySnapshot(new(
                11,start,start+chunk.Length==all.Length,chunk));
            Checks.Require(frame.Length==23+chunk.Length*29 &&
                WireProtocol.TryDecodeInventorySnapshot(frame,out var result) &&
                result is not null && result.StartIndex==start &&
                result.Items.SequenceEqual(chunk),
                "bounded inventory chunk changed item UIDs");
            start+=chunk.Length;
            batches++;
            frames+=frame.Length;
        }
        Checks.Require(start==4095 && batches==32 && frames<120500,
            "full inventory chunking unexpectedly exceeds bounds");
        bool rejected=false;
        try
        {
            WireProtocol.EncodeInventorySnapshot(new(
                11,0,false,all.Take(129).ToArray()));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"129-item inventory chunk accepted");
        rejected=false;
        try
        {
            WireProtocol.EncodeInventorySnapshot(new(
                11,4095,false,[new InventoryItemWire(7001,2701,1,501)]));
        }
        catch(ArgumentOutOfRangeException){rejected=true;}
        Checks.Require(rejected,"inventory >4095-item snapshot silently truncated");
    }
}
