using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using System.Buffers.Binary;
using System.Text;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Wire;

/// <summary>Trade, player shop, chat, party and identity codec.</summary>
public static partial class WireProtocol
{

    public static byte[] EncodeTradeCommand(TradeCommandWire value)
    {
        if(!ValidTradeCommand(value))throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.TradeCommand,TradeCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.TradeSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.TradeId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37,8),value.ItemUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(45,8),value.Money);
        return frame;
    }

    public static bool TryDecodeTradeCommand(ReadOnlySpan<byte> frame,out TradeCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.TradeCommand)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (TradeCommandAction)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(45,8)));
        return ValidTradeCommand(value);
    }

    private static bool ValidTradeCommand(TradeCommandWire value)
    {
        if(value.MapEpoch<=0||value.TradeSequence==0||
           value.Action is < TradeCommandAction.Request or > TradeCommandAction.Cancel||
           value.TradeId<0||value.TargetActorId<0||value.ItemUid<0||
           value.Money<0||value.Money>SqliteIdentityStore.MaximumMoney)return false;
        return value.Action switch
        {
            TradeCommandAction.Request => value.TradeId==0&&value.TargetActorId>0&&
                value.ItemUid==0&&value.Money==0,
            TradeCommandAction.Accept or TradeCommandAction.Reject =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid==0&&value.Money==0,
            TradeCommandAction.AddItem or TradeCommandAction.RemoveItem =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid>0&&value.Money==0,
            TradeCommandAction.SetMoney =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid==0,
            TradeCommandAction.Lock or TradeCommandAction.FinalConfirm or TradeCommandAction.Cancel =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid==0&&value.Money==0,
            _ => false
        };
    }

    public static byte[] EncodeTradeSnapshot(TradeSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!ValidTradeSnapshot(value))throw new ArgumentOutOfRangeException(nameof(value));
        int count=value.MyItems.Count+value.OtherItems.Count;
        var frame=NewFrame(WireOpcode.TradeSnapshot,
            TradeSnapshotHeaderBytes+count*TradeOfferItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.TradeId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        frame[28]=(byte)value.State;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.PeerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37,8),value.MyMoney);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(45,8),value.OtherMoney);
        frame[53]=value.MyLocked?(byte)1:(byte)0;
        frame[54]=value.OtherLocked?(byte)1:(byte)0;
        frame[55]=value.MyConfirmed?(byte)1:(byte)0;
        frame[56]=value.OtherConfirmed?(byte)1:(byte)0;
        frame[57]=checked((byte)value.MyItems.Count);
        frame[58]=checked((byte)value.OtherItems.Count);
        int offset=TradeSnapshotHeaderBytes;
        foreach(var item in value.MyItems.Concat(value.OtherItems))
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
            frame[offset+24]=checked((byte)item.EnhancementLevel);
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(offset+25,4),item.DurabilityRaw);
            offset+=TradeOfferItemBytes;
        }
        return frame;
    }

    public static bool TryDecodeTradeSnapshot(ReadOnlySpan<byte> frame,out TradeSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.TradeSnapshot||
           frame.Length<TradeSnapshotHeaderBytes)return false;
        int myCount=frame[57],otherCount=frame[58];
        if(myCount>TradeMaxItemsPerSide||otherCount>TradeMaxItemsPerSide||
           frame.Length!=TradeSnapshotHeaderBytes+(myCount+otherCount)*TradeOfferItemBytes)
            return false;
        var my=new List<TradeOfferItemWire>(myCount);
        var other=new List<TradeOfferItemWire>(otherCount);
        int offset=TradeSnapshotHeaderBytes;
        for(int i=0;i<myCount+otherCount;i++)
        {
            var item=new TradeOfferItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)),
                frame[offset+24],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+25,4)));
            (i<myCount?my:other).Add(item);
            offset+=TradeOfferItemBytes;
        }
        var decoded=new TradeSnapshotWire(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4)),
            (TradeSessionWireState)frame[28],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(45,8)),
            frame[53]==1,frame[54]==1,frame[55]==1,frame[56]==1,my,other);
        if(frame[53]>1||frame[54]>1||frame[55]>1||frame[56]>1||
           !ValidTradeSnapshot(decoded))return false;
        value=decoded;
        return true;
    }

    private static bool ValidTradeSnapshot(TradeSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.TradeId<=0||value.Revision==0||value.PeerActorId<=0||
           value.State is < TradeSessionWireState.InviteReceived or > TradeSessionWireState.AwaitingFinalConfirm||
           value.MyMoney<0||value.OtherMoney<0||
           value.MyMoney>SqliteIdentityStore.MaximumMoney||value.OtherMoney>SqliteIdentityStore.MaximumMoney||
           value.MyItems is null||value.OtherItems is null||
           value.MyItems.Count>TradeMaxItemsPerSide||value.OtherItems.Count>TradeMaxItemsPerSide||
           (value.MyConfirmed&&!value.MyLocked)||(value.OtherConfirmed&&!value.OtherLocked))
            return false;
        var seen=new HashSet<long>();
        foreach(var item in value.MyItems.Concat(value.OtherItems))
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw< -1||
               !seen.Add(item.ItemUid))
                return false;
        bool invite=value.State is TradeSessionWireState.InviteReceived or TradeSessionWireState.InviteSent;
        if(invite)
            return value.MyMoney==0&&value.OtherMoney==0&&
                !value.MyLocked&&!value.OtherLocked&&!value.MyConfirmed&&!value.OtherConfirmed&&
                value.MyItems.Count==0&&value.OtherItems.Count==0;
        if(value.State==TradeSessionWireState.AwaitingFinalConfirm)
            return value.MyLocked&&value.OtherLocked;
        return true;
    }

    public static byte[] EncodeTradeTerminal(TradeTerminalWire value)
    {
        if(!ValidTradeTerminal(value))throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.TradeTerminal,TradeTerminalFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.TradeId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        frame[28]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.WalletMoney);
        return frame;
    }

    public static bool TryDecodeTradeTerminal(ReadOnlySpan<byte> frame,out TradeTerminalWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.TradeTerminal)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4)),
            (TradeTerminalStatus)frame[28],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)));
        return ValidTradeTerminal(value);
    }

    private static bool ValidTradeTerminal(TradeTerminalWire value)
    {
        if(value.MapEpoch<=0||value.TradeId<=0||value.Revision==0||
           value.Status is < TradeTerminalStatus.Success or > TradeTerminalStatus.StaleGeneration||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney)return false;
        return value.Status==TradeTerminalStatus.Success||value.WalletMoney==0;
    }

    public static byte[] EncodePlayerShopCommand(PlayerShopCommandWire value)
    {
        if(!ValidPlayerShopCommand(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        byte[] name=Array.Empty<byte>();
        if(value.Action==PlayerShopCommandAction.SetName)
        {
            try { name=StrictUtf8.GetBytes(value.Name.Trim()); }
            catch(EncoderFallbackException) { throw new ArgumentOutOfRangeException(nameof(value)); }
        }
        int size=value.Action==PlayerShopCommandAction.Open&&value.Quantity>0
            ?PlayerShopCommandQuantityFrameBytes
            :name.Length==0
                ?PlayerShopCommandFrameBytes
                :PlayerShopCommandNameHeaderBytes+name.Length;
        var frame=NewFrame(WireOpcode.PlayerShopCommand,size);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.SellerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.ListingId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37,8),value.ItemUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(45,8),value.Price);
        if(value.Action==PlayerShopCommandAction.Open&&value.Quantity>0)
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(53,4),value.Quantity);
        else if(name.Length>0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(53,2),(ushort)name.Length);
            name.AsSpan().CopyTo(frame.AsSpan(PlayerShopCommandNameHeaderBytes));
        }
        return frame;
    }

    public static bool TryDecodePlayerShopCommand(
        ReadOnlySpan<byte> frame,out PlayerShopCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopCommand)
            return false;
        var action=(PlayerShopCommandAction)frame[20];
        string name=string.Empty;
        int quantity=0;
        if(frame.Length==PlayerShopCommandQuantityFrameBytes)
        {
            if(action!=PlayerShopCommandAction.Open)return false;
            quantity=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(53,4));
        }
        else if(frame.Length!=PlayerShopCommandFrameBytes)
        {
            if(action!=PlayerShopCommandAction.SetName||
               frame.Length<PlayerShopCommandNameHeaderBytes+1)return false;
            int length=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(53,2));
            if(length is <1 or >PlayerShopMaximumNameBytes||
               frame.Length!=PlayerShopCommandNameHeaderBytes+length)
                return false;
            try { name=StrictUtf8.GetString(frame.Slice(PlayerShopCommandNameHeaderBytes,length)); }
            catch(DecoderFallbackException) { return false; }
        }
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            action,
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(45,8)),name,quantity);
        return ValidPlayerShopCommand(value);
    }

    public static byte[] EncodePlayerShopResult(PlayerShopResultWire value)
    {
        if(!ValidPlayerShopResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerShopResult,PlayerShopResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.SellerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(30,8),value.ListingId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(38,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(46,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(50,4),value.Quantity);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(54,8),value.SourceDropId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(62,4),value.DurabilityRaw);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(66,8),value.Price);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(74,8),value.SellerWallet);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(82,8),value.BuyerWallet);
        frame[90]=checked((byte)value.EnhancementLevel);
        return frame;
    }

    public static bool TryDecodePlayerShopResult(
        ReadOnlySpan<byte> frame,out PlayerShopResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PlayerShopCommandAction)frame[20],
            (PlayerShopResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(30,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(38,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(46,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(50,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(54,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(62,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(66,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(74,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(82,8)),
            frame[90]);
        return ValidPlayerShopResult(value);
    }

    private static bool ValidPlayerShopCommand(PlayerShopCommandWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||
           value.Action is <PlayerShopCommandAction.Open or >PlayerShopCommandAction.CloseAll||
           value.SellerActorId<0||value.ListingId<0||value.ItemUid<0||
           value.Quantity<0||value.Quantity>1_000_000||
           value.Price<0||value.Price>SqliteIdentityStore.MaximumMoney)
            return false;
        string name=(value.Name??string.Empty).Trim();
        int runes=name.EnumerateRunes().Count();
        int nameBytes;
        try { nameBytes=StrictUtf8.GetByteCount(name); }
        catch(EncoderFallbackException) { return false; }
        return value.Action switch
        {
            PlayerShopCommandAction.Open =>
                value.SellerActorId==0&&value.ListingId==0&&
                value.ItemUid>0&&value.Price>0&&nameBytes==0,
            PlayerShopCommandAction.Close =>
                value.SellerActorId==0&&value.ListingId>0&&
                value.ItemUid==0&&value.Price==0&&nameBytes==0&&value.Quantity==0,
            PlayerShopCommandAction.Inspect =>
                value.SellerActorId>0&&value.ListingId==0&&
                value.ItemUid==0&&value.Price==0&&nameBytes==0&&value.Quantity==0,
            PlayerShopCommandAction.Buy =>
                value.SellerActorId>0&&value.ListingId>0&&
                value.ItemUid==0&&value.Price==0&&nameBytes==0&&value.Quantity==0,
            PlayerShopCommandAction.SetName =>
                value.SellerActorId==0&&value.ListingId==0&&
                value.ItemUid==0&&value.Price==0&&value.Quantity==0&&
                runes is >=1 and <=20&&nameBytes is >=1 and <=PlayerShopMaximumNameBytes,
            PlayerShopCommandAction.CloseAll =>
                value.SellerActorId==0&&value.ListingId==0&&
                value.ItemUid==0&&value.Price==0&&nameBytes==0&&value.Quantity==0,
            _=>false
        };
    }

    private static bool ValidPlayerShopResult(PlayerShopResultWire value)=>
        value.MapEpoch>0&&value.ShopSequence>0&&
        value.Action is >=PlayerShopCommandAction.Open and <=PlayerShopCommandAction.CloseAll&&
        (byte)value.Status<=18&&
        value.SellerActorId>=0&&value.ListingId>=0&&value.ItemUid>=0&&
        value.ItemDefinitionId is >=0 and <=100_000_000&&
        value.Quantity is >=0 and <=1_000_000&&value.SourceDropId>=0&&
        value.DurabilityRaw>=-1&&value.EnhancementLevel is >=0 and <=255&&
        value.Price>=0&&value.Price<=SqliteIdentityStore.MaximumMoney&&
        value.SellerWallet>=0&&value.SellerWallet<=SqliteIdentityStore.MaximumMoney&&
        value.BuyerWallet>=0&&value.BuyerWallet<=SqliteIdentityStore.MaximumMoney;

    public static byte[] EncodePlayerShopSnapshot(PlayerShopSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.MapEpoch<=0||value.ShopSequence==0||value.SellerActorId<=0||
           value.BuyerWallet<0||value.BuyerWallet>SqliteIdentityStore.MaximumMoney||
           value.Listings is null||
           value.Listings.Count>SqliteIdentityStore.PlayerShopMaximumActiveListings)
            throw new ArgumentOutOfRangeException(nameof(value));
        int size=PlayerShopSnapshotHeaderBytes+value.Listings.Count*PlayerShopListingBytes;
        var frame=NewFrame(WireOpcode.PlayerShopSnapshot,size);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.SellerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.BuyerWallet);
        frame[36]=checked((byte)value.Listings.Count);
        int offset=37;
        foreach(var listing in value.Listings)
        {
            if(!ValidPlayerShopListing(listing))
                throw new ArgumentOutOfRangeException(nameof(value));
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),listing.ListingId);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+8,8),listing.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+16,4),listing.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+20,4),listing.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+24,8),listing.SourceDropId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+32,4),listing.DurabilityRaw);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+36,8),listing.Price);
            frame[offset+44]=checked((byte)listing.EnhancementLevel);
            offset+=PlayerShopListingBytes;
        }
        return frame;
    }

    public static bool TryDecodePlayerShopSnapshot(
        ReadOnlySpan<byte> frame,out PlayerShopSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopSnapshot||
           frame.Length<PlayerShopSnapshotHeaderBytes||
           (frame.Length-PlayerShopSnapshotHeaderBytes)%PlayerShopListingBytes!=0)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        long seller=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8));
        long wallet=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8));
        int count=frame[36];
        if(epoch<=0||sequence==0||seller<=0||wallet<0||
           wallet>SqliteIdentityStore.MaximumMoney||
           count>SqliteIdentityStore.PlayerShopMaximumActiveListings||
           frame.Length!=PlayerShopSnapshotHeaderBytes+count*PlayerShopListingBytes)
            return false;
        var listings=new List<PlayerShopListingWire>(count);
        int offset=37;
        for(int i=0;i<count;i++,offset+=PlayerShopListingBytes)
        {
            var listing=new PlayerShopListingWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+8,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+16,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+20,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+24,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+32,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+36,8)),
                frame[offset+44]);
            if(!ValidPlayerShopListing(listing))return false;
            listings.Add(listing);
        }
        value=new(epoch,sequence,seller,wallet,listings);
        return true;
    }

    private static bool ValidPlayerShopListing(PlayerShopListingWire value)=>
        value.ListingId>0&&value.ItemUid>0&&
        value.ItemDefinitionId is >=1 and <=100_000_000&&
        value.Quantity is >=1 and <=1_000_000&&
        value.SourceDropId>=0&&value.DurabilityRaw>=-1&&
        value.Price is >=1 and <=SqliteIdentityStore.MaximumMoney&&
        value.EnhancementLevel is >=0 and <=255;

    public static byte[] EncodePlayerShopSold(PlayerShopSoldWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ListingId<=0||
           value.BuyerActorId<=0||value.ItemUid<=0||
           value.SellerWallet<0||value.SellerWallet>SqliteIdentityStore.MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerShopSold,PlayerShopSoldFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ListingId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.BuyerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.ItemUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.SellerWallet);
        return frame;
    }

    public static bool TryDecodePlayerShopSold(
        ReadOnlySpan<byte> frame,out PlayerShopSoldWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopSold)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)));
        return value.ReceiverMapEpoch>0&&value.ListingId>0&&
            value.BuyerActorId>0&&value.ItemUid>0&&
            value.SellerWallet>=0&&value.SellerWallet<=SqliteIdentityStore.MaximumMoney;
    }

    public static byte[] EncodePlayerShopPresentation(PlayerShopPresentationWire value)
    {
        string name=(value.ShopName??string.Empty).Trim();
        byte[] bytes;
        try { bytes=StrictUtf8.GetBytes(name); }
        catch(EncoderFallbackException) { throw new ArgumentOutOfRangeException(nameof(value)); }
        int runes=name.EnumerateRunes().Count();
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||value.OwnerActorId<=0||
           value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0||value.Revision==0||
           (value.Present
               ? runes is <1 or >20 || bytes.Length is <1 or >PlayerShopMaximumNameBytes
               : bytes.Length!=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerShopPresentation,
            PlayerShopPresentationHeaderBytes+bytes.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48,4),value.Revision);
        frame[52]=value.Present?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(53,2),(ushort)bytes.Length);
        bytes.AsSpan().CopyTo(frame.AsSpan(PlayerShopPresentationHeaderBytes));
        return frame;
    }

    public static bool TryDecodePlayerShopPresentation(
        ReadOnlySpan<byte> frame,out PlayerShopPresentationWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopPresentation||
           frame.Length<PlayerShopPresentationHeaderBytes)return false;
        long receiver=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long tick=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8));
        long ownerEpoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8));
        uint revision=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(48,4));
        byte present=frame[52];
        int length=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(53,2));
        if(receiver<=0||tick<0||actor<=0||connection<=0||ownerEpoch<=0||revision==0||
           present>1||length>PlayerShopMaximumNameBytes||
           frame.Length!=PlayerShopPresentationHeaderBytes+length)return false;
        string name;
        try { name=StrictUtf8.GetString(frame.Slice(PlayerShopPresentationHeaderBytes,length)); }
        catch(DecoderFallbackException) { return false; }
        int runes=name.EnumerateRunes().Count();
        if((present==1 && (runes is <1 or >20 || string.IsNullOrWhiteSpace(name)))||
           (present==0 && length!=0))return false;
        value=new(receiver,tick,actor,connection,ownerEpoch,revision,present==1,name);
        return true;
    }

    public static byte[] EncodeChatCommand(ChatCommandWire value)
    {
        byte[] message;
        try { message=StrictUtf8.GetBytes(value.Message??string.Empty); }
        catch(EncoderFallbackException) { throw new ArgumentOutOfRangeException(nameof(value)); }
        if(value.MapEpoch<=0||value.ChatSequence==0||(byte)value.Channel>4||
           value.BubbleType>5||message.Length is <1 or >ChatMaxMessageBytes||
           string.IsNullOrWhiteSpace(value.Message))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ChatCommand,ChatCommandHeaderBytes+message.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ChatSequence);
        frame[20]=(byte)value.Channel; frame[21]=value.BubbleType;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(22,2),(ushort)message.Length);
        message.AsSpan().CopyTo(frame.AsSpan(ChatCommandHeaderBytes));
        return frame;
    }

    public static bool TryDecodeChatCommand(ReadOnlySpan<byte> frame,out ChatCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ChatCommand||
           frame.Length<ChatCommandHeaderBytes+1)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var channel=(ChatChannel)frame[20]; byte bubble=frame[21];
        int length=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(22,2));
        if(epoch<=0||sequence==0||(byte)channel>4||bubble>5||
           length is <1 or >ChatMaxMessageBytes||frame.Length!=ChatCommandHeaderBytes+length)
            return false;
        try
        {
            string message=StrictUtf8.GetString(frame.Slice(ChatCommandHeaderBytes,length));
            if(string.IsNullOrWhiteSpace(message))return false;
            value=new(epoch,sequence,channel,bubble,message);
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodeEmotionCommand(EmotionCommandWire value)
    {
        if(value.MapEpoch<=0||value.EmotionSequence==0||value.EmotionId is <1 or >12)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EmotionCommand,EmotionCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.EmotionSequence);
        frame[20]=value.EmotionId;
        return frame;
    }

    public static bool TryDecodeEmotionCommand(ReadOnlySpan<byte> frame,out EmotionCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.EmotionCommand||
           frame.Length!=EmotionCommandFrameBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        byte emotion=frame[20];
        if(epoch<=0||sequence==0||emotion is <1 or >12)return false;
        value=new(epoch,sequence,emotion);
        return true;
    }

    public static byte[] EncodeEmotionVisual(EmotionVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ActorId<=0||value.ConnectionEpoch<=0||
           value.EmotionSequence==0||value.EmotionId is <1 or >12)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EmotionVisual,EmotionVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(32,4),value.EmotionSequence);
        frame[36]=value.EmotionId;
        return frame;
    }

    public static bool TryDecodeEmotionVisual(ReadOnlySpan<byte> frame,out EmotionVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.EmotionVisual||
           frame.Length!=EmotionVisualFrameBytes)return false;
        long receiver=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(32,4));
        byte emotion=frame[36];
        if(receiver<=0||actor<=0||connection<=0||sequence==0||emotion is <1 or >12)
            return false;
        value=new(receiver,actor,connection,sequence,emotion);
        return true;
    }

    public static byte[] EncodeWhisperCommand(WhisperCommandWire value)
    {
        string targetNameText=value.TargetName??string.Empty;
        byte[] message,targetName;
        try
        {
            message=StrictUtf8.GetBytes(value.Message??string.Empty);
            targetName=StrictUtf8.GetBytes(targetNameText);
        }
        catch(EncoderFallbackException)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        bool byName=targetName.Length>0;
        if(value.MapEpoch<=0||value.ChatSequence==0||
           (value.TargetActorId>0)==byName||value.TargetActorId<0||
           value.BubbleType>5||message.Length is <1 or >ChatMaxMessageBytes||
           string.IsNullOrWhiteSpace(value.Message)||
           targetName.Length>ChatMaxSenderNameBytes||
           (byName&&(string.IsNullOrWhiteSpace(targetNameText)||
                     targetNameText!=targetNameText.Trim())))
            throw new ArgumentOutOfRangeException(nameof(value));
        int frameBytes=WhisperCommandHeaderBytes+message.Length+
            (byName?2+targetName.Length:0);
        var frame=NewFrame(WireOpcode.WhisperCommand,frameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(16,4),value.ChatSequence);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(20,8),value.TargetActorId);
        frame[28]=value.BubbleType;
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(29,2),(ushort)message.Length);
        message.AsSpan().CopyTo(frame.AsSpan(WhisperCommandHeaderBytes));
        if(byName)
        {
            int offset=WhisperCommandHeaderBytes+message.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(
                frame.AsSpan(offset,2),(ushort)targetName.Length);
            targetName.AsSpan().CopyTo(frame.AsSpan(offset+2));
        }
        return frame;
    }

    public static bool TryDecodeWhisperCommand(
        ReadOnlySpan<byte> frame,out WhisperCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.WhisperCommand||
           frame.Length<WhisperCommandHeaderBytes+1)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        long target=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8));
        byte bubble=frame[28];
        int messageLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(29,2));
        if(epoch<=0||sequence==0||target<0||bubble>5||
           messageLength is <1 or >ChatMaxMessageBytes||
           frame.Length<WhisperCommandHeaderBytes+messageLength)
            return false;
        try
        {
            string message=StrictUtf8.GetString(
                frame.Slice(WhisperCommandHeaderBytes,messageLength));
            if(string.IsNullOrWhiteSpace(message))return false;
            int offset=WhisperCommandHeaderBytes+messageLength;
            if(target>0)
            {
                if(frame.Length!=offset)return false;
                value=new(epoch,sequence,target,bubble,message,string.Empty);
                return true;
            }
            if(frame.Length<offset+2)return false;
            int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset,2));
            if(nameLength is <1 or >ChatMaxSenderNameBytes||
               frame.Length!=offset+2+nameLength)
                return false;
            string targetName=StrictUtf8.GetString(frame.Slice(offset+2,nameLength));
            if(string.IsNullOrWhiteSpace(targetName)||targetName!=targetName.Trim())
                return false;
            value=new(epoch,sequence,0,bubble,message,targetName);
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodeFriendMessageCommand(FriendMessageCommandWire value)
    {
        byte[] message;
        try { message=StrictUtf8.GetBytes(value.Message??string.Empty); }
        catch(EncoderFallbackException)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if(value.MapEpoch<=0||value.ChatSequence==0||value.TargetActorId<=0||
           message.Length is <1 or >FriendMessageMaxMessageBytes||
           string.IsNullOrEmpty(value.Message))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.FriendMessageCommand,
            FriendMessageCommandHeaderBytes+message.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ChatSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.TargetActorId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(28,2),(ushort)message.Length);
        message.AsSpan().CopyTo(frame.AsSpan(FriendMessageCommandHeaderBytes));
        return frame;
    }

    public static bool TryDecodeFriendMessageCommand(
        ReadOnlySpan<byte> frame,out FriendMessageCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.FriendMessageCommand||
           frame.Length<FriendMessageCommandHeaderBytes+1)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        long target=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8));
        int messageLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(28,2));
        if(epoch<=0||sequence==0||target<=0||
           messageLength is <1 or >FriendMessageMaxMessageBytes||
           frame.Length!=FriendMessageCommandHeaderBytes+messageLength)
            return false;
        try
        {
            string message=StrictUtf8.GetString(
                frame.Slice(FriendMessageCommandHeaderBytes,messageLength));
            if(string.IsNullOrEmpty(message))return false;
            value=new(epoch,sequence,target,message);
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodeWhisperResult(WhisperResultWire value)
    {
        byte[] name;
        try { name=StrictUtf8.GetBytes(value.TargetName??string.Empty); }
        catch(EncoderFallbackException)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if(!ValidWhisperResult(value,name))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.WhisperResult,
            WhisperResultHeaderBytes+name.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(16,4),value.ChatSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(21,8),value.TargetActorId);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(29,8),value.TargetConnectionEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(37,2),(ushort)name.Length);
        name.AsSpan().CopyTo(frame.AsSpan(WhisperResultHeaderBytes));
        return frame;
    }

    public static bool TryDecodeWhisperResult(
        ReadOnlySpan<byte> frame,out WhisperResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.WhisperResult||
           frame.Length<WhisperResultHeaderBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var status=(WhisperResultStatus)frame[20];
        long target=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8));
        int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(37,2));
        if(nameLength>ChatMaxSenderNameBytes||
           frame.Length!=WhisperResultHeaderBytes+nameLength)
            return false;
        try
        {
            string name=nameLength==0?string.Empty:
                StrictUtf8.GetString(frame.Slice(WhisperResultHeaderBytes,nameLength));
            value=new(epoch,sequence,status,target,connection,name);
            return ValidWhisperResult(value,
                nameLength==0?Array.Empty<byte>():
                    StrictUtf8.GetBytes(name));
        }
        catch(DecoderFallbackException){return false;}
        catch(EncoderFallbackException){return false;}
    }

    private static bool ValidWhisperResult(
        WhisperResultWire value,byte[] targetName)
    {
        if(value.MapEpoch<=0||value.ChatSequence==0||
           (byte)value.Status>(byte)WhisperResultStatus.QueuedOffline||
           value.TargetActorId<0||
           targetName.Length>ChatMaxSenderNameBytes)
            return false;
        if(value.Status==WhisperResultStatus.Delivered)
            return value.TargetActorId>0&&value.TargetConnectionEpoch>0&&
                targetName.Length>0&&!string.IsNullOrWhiteSpace(value.TargetName);
        if(value.Status==WhisperResultStatus.QueuedOffline)
            return value.TargetActorId>0&&value.TargetConnectionEpoch==0&&
                targetName.Length>0&&!string.IsNullOrWhiteSpace(value.TargetName);
        if(value.Status==WhisperResultStatus.SelfTarget)
            return value.TargetActorId>0&&value.TargetConnectionEpoch==0&&
                targetName.Length==0&&string.IsNullOrEmpty(value.TargetName);
        return value.TargetConnectionEpoch==0&&targetName.Length==0&&
            string.IsNullOrEmpty(value.TargetName);
    }

    public static byte[] EncodePartyCommand(PartyCommandWire value)
    {
        string targetNameText=value.TargetName??string.Empty;
        byte[] targetName;
        try{targetName=StrictUtf8.GetBytes(targetNameText);}
        catch(EncoderFallbackException)
        { throw new ArgumentOutOfRangeException(nameof(value)); }
        if(value.MapEpoch<=0||value.PartySequence==0||
           value.Action is < PartyCommandAction.Invite or > PartyCommandAction.Create||
           value.PartyId<0||value.TargetActorId<0||
           targetName.Length>PlayerIdentityMaxNameBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        bool nameTarget=targetName.Length>0;
        if(nameTarget&&
           (targetNameText!=targetNameText.Normalize(NormalizationForm.FormC).Trim()||
            string.IsNullOrWhiteSpace(targetNameText)||
            targetNameText.Any(char.IsControl)))
            throw new ArgumentOutOfRangeException(nameof(value));
        bool invite=value.Action==PartyCommandAction.Invite;
        bool memberTarget=value.Action is PartyCommandAction.Kick or PartyCommandAction.Promote;
        bool inviteResponse=value.Action is PartyCommandAction.Accept or PartyCommandAction.Reject;
        if(invite)
        {
            if((value.TargetActorId>0)==nameTarget)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if(nameTarget||
                (memberTarget&&(value.TargetActorId<=0||value.PartyId<=0))||
                (inviteResponse&&(value.PartyId<=0||value.TargetActorId<=0))||
                (value.Action==PartyCommandAction.Leave&&
                    (value.PartyId<=0||value.TargetActorId!=0))||
                (value.Action==PartyCommandAction.Create&&
                    (value.PartyId!=0||value.TargetActorId!=0))||
                (!memberTarget&&!inviteResponse&&
                    value.Action!=PartyCommandAction.Leave&&
                    value.Action!=PartyCommandAction.Create))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PartyCommand,
            PartyCommandFrameBytes+targetName.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PartySequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.PartyId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetActorId);
        targetName.AsSpan().CopyTo(frame.AsSpan(PartyCommandFrameBytes));
        return frame;
    }

    public static bool TryDecodePartyCommand(
        ReadOnlySpan<byte> frame,out PartyCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PartyCommand||
           frame.Length<PartyCommandFrameBytes||
           frame.Length>PartyCommandMaximumFrameBytes)return false;
        try
        {
            string targetName=frame.Length==PartyCommandFrameBytes
                ? string.Empty
                : StrictUtf8.GetString(frame.Slice(PartyCommandFrameBytes));
            value=new(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
                BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
                (PartyCommandAction)frame[20],
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
                targetName);
            _=EncodePartyCommand(value);
            return true;
        }
        catch(DecoderFallbackException){value=default;return false;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePartyResult(PartyResultWire value)
    {
        if(value.MapEpoch<=0||value.PartySequence==0||
           value.Action is < PartyCommandAction.Invite or > PartyCommandAction.Create||
           value.Status is < PartyResultStatus.Success or >
                PartyResultStatus.TargetHasPendingInvite||
           value.PartyId<0||value.TargetActorId<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PartyResult,PartyResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PartySequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.PartyId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(30,8),value.TargetActorId);
        return frame;
    }

    public static bool TryDecodePartyResult(
        ReadOnlySpan<byte> frame,out PartyResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PartyResult||
           frame.Length!=PartyResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PartyCommandAction)frame[20],(PartyResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(30,8)));
        if(value.MapEpoch<=0||value.PartySequence==0||
           value.Action is < PartyCommandAction.Invite or > PartyCommandAction.Create||
           value.Status is < PartyResultStatus.Success or >
                PartyResultStatus.TargetHasPendingInvite||
           value.PartyId<0||value.TargetActorId<0)
        { value=default; return false; }
        return true;
    }

    public static byte[] EncodePartyInvite(PartyInviteWire value)
    {
        byte[] name;
        try{name=StrictUtf8.GetBytes(value.InviterName??string.Empty);}
        catch(EncoderFallbackException)
        { throw new ArgumentOutOfRangeException(nameof(value)); }
        if(value.ReceiverMapEpoch<=0||value.PartyId<=0||value.Revision==0||
           value.InviterActorId<=0||value.InviterConnectionEpoch<=0||
           name.Length is <1 or >PlayerIdentityMaxNameBytes||
           string.IsNullOrWhiteSpace(value.InviterName))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PartyInvite,
            PartyInviteHeaderBytes+name.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.PartyId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.InviterActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(36,8),value.InviterConnectionEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(44,2),(ushort)name.Length);
        name.AsSpan().CopyTo(frame.AsSpan(PartyInviteHeaderBytes));
        return frame;
    }

    public static bool TryDecodePartyInvite(
        ReadOnlySpan<byte> frame,out PartyInviteWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PartyInvite||
           frame.Length<PartyInviteHeaderBytes+1)return false;
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(44,2));
        if(count is <1 or >PlayerIdentityMaxNameBytes||
           frame.Length!=PartyInviteHeaderBytes+count)return false;
        try
        {
            string name=StrictUtf8.GetString(frame.Slice(PartyInviteHeaderBytes,count));
            value=new(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
                BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(36,8)),name);
            if(value.ReceiverMapEpoch<=0||value.PartyId<=0||value.Revision==0||
               value.InviterActorId<=0||value.InviterConnectionEpoch<=0||
               string.IsNullOrWhiteSpace(name))
            { value=default; return false; }
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodePartySnapshot(PartySnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.ReceiverMapEpoch<=0||value.PartyId<=0||value.Revision==0||
           value.Members is null||value.Members.Count>PartyMaxMembers)
            throw new ArgumentOutOfRangeException(nameof(value));
        bool cleared=value.Members.Count==0;
        if(cleared!=(value.LeaderActorId==0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var seen=new HashSet<long>();
        var encoded=new List<(PartyMemberWire Member,byte[] Name)>();
        foreach(var member in value.Members)
        {
            byte[] name;
            try{name=StrictUtf8.GetBytes(member.Name??string.Empty);}
            catch(EncoderFallbackException)
            { throw new ArgumentOutOfRangeException(nameof(value)); }
            if(member.ActorId<=0||member.ConnectionEpoch<=0||
               name.Length is <1 or >PlayerIdentityMaxNameBytes||
               string.IsNullOrWhiteSpace(member.Name)||!seen.Add(member.ActorId))
                throw new ArgumentOutOfRangeException(nameof(value));
            encoded.Add((member,name));
        }
        if(!cleared&&!seen.Contains(value.LeaderActorId))
            throw new ArgumentOutOfRangeException(nameof(value));
        int bytes=PartySnapshotHeaderBytes+
            encoded.Sum(x=>PartyMemberHeaderBytes+x.Name.Length);
        var frame=NewFrame(WireOpcode.PartySnapshot,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.PartyId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.LeaderActorId);
        frame[36]=checked((byte)encoded.Count);
        int offset=PartySnapshotHeaderBytes;
        foreach(var item in encoded)
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.Member.ActorId);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+8,8),item.Member.ConnectionEpoch);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset+16,2),
                checked((ushort)item.Name.Length));
            item.Name.AsSpan().CopyTo(frame.AsSpan(offset+PartyMemberHeaderBytes));
            offset+=PartyMemberHeaderBytes+item.Name.Length;
        }
        return frame;
    }

    public static bool TryDecodePartySnapshot(
        ReadOnlySpan<byte> frame,out PartySnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PartySnapshot||
           frame.Length<PartySnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long partyId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        uint revision=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4));
        long leader=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8));
        int count=frame[36];
        if(epoch<=0||partyId<=0||revision==0||count>PartyMaxMembers)
            return false;
        int offset=PartySnapshotHeaderBytes;
        var members=new List<PartyMemberWire>(count);
        var seen=new HashSet<long>();
        try
        {
            for(int i=0;i<count;i++)
            {
                if(offset+PartyMemberHeaderBytes>frame.Length)return false;
                long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8));
                long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+8,8));
                int nameCount=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset+16,2));
                if(actor<=0||connection<=0||nameCount is <1 or >PlayerIdentityMaxNameBytes||
                   offset+PartyMemberHeaderBytes+nameCount>frame.Length||!seen.Add(actor))
                    return false;
                string name=StrictUtf8.GetString(
                    frame.Slice(offset+PartyMemberHeaderBytes,nameCount));
                if(string.IsNullOrWhiteSpace(name))return false;
                members.Add(new(actor,connection,name));
                offset+=PartyMemberHeaderBytes+nameCount;
            }
        }
        catch(DecoderFallbackException){return false;}
        if(offset!=frame.Length)return false;
        if((count==0)!=(leader==0)||count>0&&!seen.Contains(leader))return false;
        value=new(epoch,partyId,revision,leader,members);
        return true;
    }

    public static byte[] EncodeChatDelivery(ChatDeliveryWire value)
    {
        byte[] name,message;
        try
        {
            name=StrictUtf8.GetBytes(value.SenderName??string.Empty);
            message=StrictUtf8.GetBytes(value.Message??string.Empty);
        }
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.ReceiverMapEpoch<=0||value.SenderActorId<=0||
           value.SenderConnectionEpoch<=0||value.ChatSequence==0||
           value.Channel==ChatChannel.Emotion||
           (byte)value.Channel>(byte)ChatChannel.FriendMessage||
           value.BubbleType>5||
           (value.Channel==ChatChannel.FriendMessage&&value.BubbleType!=0)||
           name.Length is <1 or >ChatMaxSenderNameBytes||
           message.Length<1||
           message.Length>(value.Channel==ChatChannel.FriendMessage
               ?FriendMessageMaxMessageBytes:ChatMaxMessageBytes)||
           string.IsNullOrWhiteSpace(value.SenderName)||string.IsNullOrWhiteSpace(value.Message))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ChatDelivery,
            ChatDeliveryHeaderBytes+name.Length+message.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.SenderActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.SenderConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(32,4),value.ChatSequence);
        frame[36]=(byte)value.Channel; frame[37]=value.BubbleType;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(38,2),(ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(40,2),(ushort)message.Length);
        name.AsSpan().CopyTo(frame.AsSpan(ChatDeliveryHeaderBytes));
        message.AsSpan().CopyTo(frame.AsSpan(ChatDeliveryHeaderBytes+name.Length));
        return frame;
    }

    public static bool TryDecodeChatDelivery(ReadOnlySpan<byte> frame,out ChatDeliveryWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ChatDelivery||
           frame.Length<ChatDeliveryHeaderBytes+2)return false;
        long receiver=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(32,4));
        var channel=(ChatChannel)frame[36]; byte bubble=frame[37];
        int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(38,2));
        int messageLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(40,2));
        if(receiver<=0||actor<=0||connection<=0||sequence==0||
           channel==ChatChannel.Emotion||
           (byte)channel>(byte)ChatChannel.FriendMessage||bubble>5||
           (channel==ChatChannel.FriendMessage&&bubble!=0)||
           nameLength is <1 or >ChatMaxSenderNameBytes||messageLength<1||
           messageLength>(channel==ChatChannel.FriendMessage
               ?FriendMessageMaxMessageBytes:ChatMaxMessageBytes)||
           frame.Length!=ChatDeliveryHeaderBytes+nameLength+messageLength)return false;
        try
        {
            string name=StrictUtf8.GetString(frame.Slice(ChatDeliveryHeaderBytes,nameLength));
            string message=StrictUtf8.GetString(frame.Slice(ChatDeliveryHeaderBytes+nameLength,messageLength));
            if(string.IsNullOrWhiteSpace(name)||
               (channel==ChatChannel.FriendMessage
                   ?string.IsNullOrEmpty(message)
                   :string.IsNullOrWhiteSpace(message)))return false;
            value=new(receiver,actor,connection,sequence,channel,bubble,name,message);
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodePlayerIdentity(PlayerIdentityWire value)
    {
        byte[] name;
        try { name=StrictUtf8.GetBytes(value.Name??string.Empty); }
        catch(EncoderFallbackException)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if(value.ReceiverMapEpoch<=0||value.ActorId<=0||
           value.ConnectionEpoch<=0||value.EntityMapEpoch<=0||
           value.Mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite)||
           value.ServerUnixMilliseconds<=0||value.PinkUntilUnixMilliseconds<0||
           value.PvpUpdatedUnixMilliseconds<0||
           name.Length is <1 or >PlayerIdentityMaxNameBytes||
           string.IsNullOrWhiteSpace(value.Name))
            throw new ArgumentOutOfRangeException(nameof(value));
        int extensionBytes=value.Appearance is null ? 0 : 1+CharacterAppearanceBytes;
        int bytes=PlayerIdentityHeaderBytes+name.Length+extensionBytes;
        var frame=NewFrame(WireOpcode.PlayerIdentity,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.EntityMapEpoch);
        frame[40]=(byte)value.Mode;
        frame[41]=value.RedMan?(byte)1:(byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(42,8),value.ServerUnixMilliseconds);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(50,8),value.PinkUntilUnixMilliseconds);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(58,8),value.PvpUpdatedUnixMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(66,2),(ushort)name.Length);
        name.AsSpan().CopyTo(frame.AsSpan(PlayerIdentityHeaderBytes));
        if(value.Appearance is { } appearance)
        {
            int offset=PlayerIdentityHeaderBytes+name.Length;
            frame[offset++]=1; // Optional authoritative appearance extension.
            WriteAppearance(frame.AsSpan(offset,CharacterAppearanceBytes),appearance);
        }
        return frame;
    }

    public static bool TryDecodePlayerIdentity(
        ReadOnlySpan<byte> frame,out PlayerIdentityWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerIdentity||
           frame.Length<PlayerIdentityHeaderBytes+1)
            return false;
        long receiver=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8));
        long entityMap=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8));
        var mode=(PvpMode)frame[40];
        byte red=frame[41];
        long serverUnix=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(42,8));
        long pinkUntil=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(50,8));
        long updated=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(58,8));
        int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(66,2));
        int baseBytes=PlayerIdentityHeaderBytes+nameLength;
        int remaining=frame.Length-baseBytes;
        if(receiver<=0||actor<=0||connection<=0||entityMap<=0||
           mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite)||
           red>1||serverUnix<=0||pinkUntil<0||updated<0||
           nameLength is <1 or >PlayerIdentityMaxNameBytes||
           (remaining!=0&&remaining!=1+BodyLegCharacterAppearanceBytes&&
            remaining!=1+CharacterAppearanceBytes))
            return false;
        try
        {
            string name=StrictUtf8.GetString(
                frame.Slice(PlayerIdentityHeaderBytes,nameLength));
            if(string.IsNullOrWhiteSpace(name))return false;
            CharacterAppearance? appearance=null;
            if(remaining>0)
            {
                int offset=baseBytes;
                int appearanceBytes=remaining-1;
                if(frame[offset++]!=1||
                   !ReadAppearance(frame.Slice(offset,appearanceBytes),out var decoded))
                    return false;
                appearance=decoded;
            }
            value=new(receiver,actor,connection,entityMap,mode,red==1,
                serverUnix,pinkUntil,updated,name,appearance);
            return true;
        }
        catch(DecoderFallbackException)
        {
            return false;
        }
    }
}
