using System.Buffers.Binary;
using System.Text;
using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Wire;

/// <summary>NPC shop browsing, purchases and sale result wire V1 operations.</summary>
public static partial class WireProtocol
{
    public static byte[] EncodeShopOpen(ShopOpenWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopOpen,ShopOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeShopOpen(ReadOnlySpan<byte> frame,out ShopOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.ShopSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeShopSnapshot(ShopSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0||
           value.Money<0||value.Money>SqliteIdentityStore.MaximumMoney||
           value.Items is null||value.Items.Count>ShopSnapshotMaxItems||
           (byte)value.Status>(byte)ShopOpenStatus.PlayerDead)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Status==ShopOpenStatus.Opened && value.Items.Count==0)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Status!=ShopOpenStatus.Opened &&
           (value.Money!=0||value.Items.Count!=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<int>();
        foreach(var item in value.Items)
            if(item.ItemDefinitionId is <1 or >100_000_000 ||
               item.BuyPrice is <1 or >1_000_000_000_000L ||
               item.SellPrice<0||item.SellPrice>item.BuyPrice||
               !ids.Add(item.ItemDefinitionId))
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopSnapshot,
            ShopSnapshotHeaderBytes+value.Items.Count*ShopItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.Money);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(33,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=ShopSnapshotHeaderBytes+i*ShopItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(offset,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset+4,8),item.BuyPrice);
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset+12,8),item.SellPrice);
        }
        return frame;
    }

    public static bool TryDecodeShopSnapshot(
        ReadOnlySpan<byte> frame,out ShopSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopSnapshot||
           frame.Length<ShopSnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint seq=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        byte raw=frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        long money=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8));
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(33,2));
        if(epoch<=0||seq==0||npc<=0||raw>(byte)ShopOpenStatus.PlayerDead||
           money<0||money>SqliteIdentityStore.MaximumMoney||
           count>ShopSnapshotMaxItems||
           frame.Length!=ShopSnapshotHeaderBytes+count*ShopItemBytes)
            return false;
        var status=(ShopOpenStatus)raw;
        if(status==ShopOpenStatus.Opened && count==0)return false;
        if(status!=ShopOpenStatus.Opened && (money!=0||count!=0))return false;
        var items=new List<ShopItemWire>(count);
        var ids=new HashSet<int>();
        for(int i=0;i<count;i++)
        {
            int offset=ShopSnapshotHeaderBytes+i*ShopItemBytes;
            var item=new ShopItemWire(
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+4,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+12,8)));
            if(item.ItemDefinitionId is <1 or >100_000_000 ||
               item.BuyPrice is <1 or >1_000_000_000_000L||
               item.SellPrice<0||item.SellPrice>item.BuyPrice||
               !ids.Add(item.ItemDefinitionId))return false;
            items.Add(item);
        }
        value=new(epoch,seq,status,npc,money,items);
        return true;
    }

    public static byte[] EncodeShopBuy(ShopBuyWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0||
           value.ItemDefinitionId is <1 or >100_000_000||
           value.Quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopBuy,ShopBuyFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeShopBuy(ReadOnlySpan<byte> frame,out ShopBuyWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopBuy)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        return value.MapEpoch>0&&value.ShopSequence>0&&value.NpcSpawnId>0&&
            value.ItemDefinitionId is >0 and <=100_000_000&&
            value.Quantity is >0 and <=1_000_000;
    }

    public static byte[] EncodeShopSell(ShopSellWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0||
           value.ItemUid<=0||value.ItemDefinitionId is <1 or >100_000_000||
           value.Quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopSell,ShopSellFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(36,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeShopSell(ReadOnlySpan<byte> frame,out ShopSellWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopSell)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(32,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(36,4)));
        return value.MapEpoch>0&&value.ShopSequence>0&&value.NpcSpawnId>0&&
            value.ItemUid>0&&value.ItemDefinitionId is >0 and <=100_000_000&&
            value.Quantity is >0 and <=1_000_000;
    }

    public static byte[] EncodeShopTransactionResult(ShopTransactionResultWire value)
    {
        if(!ValidShopTransactionResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopTransactionResult,
            ShopTransactionResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(22,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(26,8),value.Money);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(34,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(42,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(46,4),value.Quantity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(50,4),value.RemainingQuantity);
        return frame;
    }

    public static bool TryDecodeShopTransactionResult(
        ReadOnlySpan<byte> frame,out ShopTransactionResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.ShopTransactionResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (ShopTransactionKind)frame[20],
            (ShopTransactionStatus)frame[21],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(22,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(26,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(34,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(42,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(46,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(50,4)));
        return ValidShopTransactionResult(value);
    }

    private static bool ValidShopTransactionResult(ShopTransactionResultWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||
           value.Kind is not (ShopTransactionKind.Buy or ShopTransactionKind.Sell)||
           (byte)value.Status>(byte)ShopTransactionStatus.NotSellable||
           value.NpcSpawnId<=0||value.Money<0||
           value.Money>SqliteIdentityStore.MaximumMoney||
           value.ItemUid<0||value.ItemDefinitionId is <1 or >100_000_000||
           value.Quantity is <1 or >1_000_000||
           value.RemainingQuantity is <0 or >1_000_000)
            return false;
        if(value.Status is ShopTransactionStatus.Applied or
            ShopTransactionStatus.AlreadyApplied)
            return value.ItemUid>0;
        return true;
    }

}
