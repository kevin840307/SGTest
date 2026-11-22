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

/// <summary>Equipment, storage, bank, repairs and enhancement codec.</summary>
public static partial class WireProtocol
{

    public static byte[] EncodeStorageOpen(StorageOpenWire value)
    {
        if(value.MapEpoch<=0||value.StorageSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.StorageOpen,StorageOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeStorageOpen(
        ReadOnlySpan<byte> frame,out StorageOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.StorageOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.StorageSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeStorageSnapshot(StorageSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.MapEpoch<=0||value.StorageSequence==0||value.NpcSpawnId<=0||
           value.StartIndex<0||value.StartIndex>StorageSnapshotMaxItems||
           value.Items is null||value.Items.Count>StorageSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>StorageSnapshotMaxItems||
           (byte)value.Status>(byte)StorageOpenStatus.PlayerDead)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Status!=StorageOpenStatus.Opened &&
           (value.StartIndex!=0||!value.Complete||value.Items.Count!=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var seen=new HashSet<long>();
        foreach(var item in value.Items)
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw < -1||
               !seen.Add(item.ItemUid))
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.StorageSnapshot,
            StorageSnapshotHeaderBytes+value.Items.Count*StorageItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(25,4),value.StartIndex);
        frame[29]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(30,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=StorageSnapshotHeaderBytes+i*StorageItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
            frame[offset+24]=checked((byte)item.EnhancementLevel);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+25,4),item.DurabilityRaw);
        }
        return frame;
    }

    public static bool TryDecodeStorageSnapshot(
        ReadOnlySpan<byte> frame,out StorageSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.StorageSnapshot||
           frame.Length<StorageSnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint seq=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        byte raw=frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(25,4));
        byte completeRaw=frame[29];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(30,2));
        if(epoch<=0||seq==0||npc<=0||raw>(byte)StorageOpenStatus.PlayerDead||
           start<0||start>StorageSnapshotMaxItems||completeRaw>1||
           count>StorageSnapshotMaxItemsPerFrame||
           start+count>StorageSnapshotMaxItems||
           frame.Length!=StorageSnapshotHeaderBytes+count*StorageItemBytes)
            return false;
        var status=(StorageOpenStatus)raw;
        bool complete=completeRaw==1;
        if(status!=StorageOpenStatus.Opened &&
           (start!=0||!complete||count!=0))return false;
        var items=new List<StorageItemWire>(count);
        var seen=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=StorageSnapshotHeaderBytes+i*StorageItemBytes;
            var item=new StorageItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)),
                frame[offset+24],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+25,4)));
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw < -1||
               !seen.Add(item.ItemUid))return false;
            items.Add(item);
        }
        value=new(epoch,seq,status,npc,start,complete,items);
        return true;
    }

    private static byte[] EncodeStorageMove(
        WireOpcode opcode,StorageMoveWire value)
    {
        if(opcode is not (WireOpcode.StorageDeposit or WireOpcode.StorageWithdraw)||
           value.MapEpoch<=0||value.StorageSequence==0||
           value.NpcSpawnId<=0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(opcode,StorageMoveFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        return frame;
    }

    public static byte[] EncodeStorageDeposit(StorageMoveWire value)=>
        EncodeStorageMove(WireOpcode.StorageDeposit,value);
    public static byte[] EncodeStorageWithdraw(StorageMoveWire value)=>
        EncodeStorageMove(WireOpcode.StorageWithdraw,value);

    private static bool TryDecodeStorageMove(
        ReadOnlySpan<byte> frame,WireOpcode opcode,out StorageMoveWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=opcode)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.StorageSequence>0&&
            value.NpcSpawnId>0&&value.ItemUid>0;
    }

    public static bool TryDecodeStorageDeposit(
        ReadOnlySpan<byte> frame,out StorageMoveWire value)=>
        TryDecodeStorageMove(frame,WireOpcode.StorageDeposit,out value);
    public static bool TryDecodeStorageWithdraw(
        ReadOnlySpan<byte> frame,out StorageMoveWire value)=>
        TryDecodeStorageMove(frame,WireOpcode.StorageWithdraw,out value);

    public static byte[] EncodeStorageTransferResult(
        StorageTransferResultWire value)
    {
        if(!ValidStorageTransferResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.StorageTransferResult,
            StorageTransferResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(22,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(26,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(34,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(38,4),value.Quantity);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(42,8),value.SourceDropId);
        return frame;
    }

    public static bool TryDecodeStorageTransferResult(
        ReadOnlySpan<byte> frame,out StorageTransferResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.StorageTransferResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (StorageTransferKind)frame[20],
            (StorageTransferStatus)frame[21],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(22,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(26,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(34,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(38,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(42,8)));
        return ValidStorageTransferResult(value);
    }

    private static bool ValidStorageTransferResult(StorageTransferResultWire value)
    {
        if(value.MapEpoch<=0||value.StorageSequence==0||
           value.Kind is not (StorageTransferKind.Deposit or StorageTransferKind.Withdraw)||
           (byte)value.Status>(byte)StorageTransferStatus.PlayerDead||
           value.NpcSpawnId<=0||value.ItemUid<=0||
           value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.Quantity<0||value.Quantity>1_000_000||value.SourceDropId<0)
            return false;
        if(value.Status is StorageTransferStatus.Applied or
            StorageTransferStatus.AlreadyApplied)
            return value.ItemDefinitionId>0&&value.Quantity>0;
        return value.ItemDefinitionId==0&&value.Quantity==0&&value.SourceDropId==0;
    }

    public static byte[] EncodeEquipmentSnapshot(EquipmentSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.CharacterId<=0||value.StartIndex<0||value.StartIndex>EquipmentSnapshotMaxItems||
           value.Items is null||value.Items.Count>EquipmentSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>EquipmentSnapshotMaxItems)
            throw new ArgumentOutOfRangeException(nameof(value));
        var uids=new HashSet<long>();
        var slots=new HashSet<int>();
        foreach(var item in value.Items)
            if(item.ItemUid<=0||item.SlotId is <1 or >255||
               item.ItemDefinitionId is <1 or >100_000_000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw < -1||
               !uids.Add(item.ItemUid)||!slots.Add(item.SlotId))
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EquipmentSnapshot,
            EquipmentSnapshotHeaderBytes+value.Items.Count*EquipmentItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4),value.StartIndex);
        frame[20]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(21,2),checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=EquipmentSnapshotHeaderBytes+i*EquipmentItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.SlotId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
            frame[offset+24]=checked((byte)item.EnhancementLevel);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+25,4),item.DurabilityRaw);
        }
        return frame;
    }

    public static bool TryDecodeEquipmentSnapshot(ReadOnlySpan<byte> frame,out EquipmentSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.EquipmentSnapshot||
           frame.Length<EquipmentSnapshotHeaderBytes)return false;
        long characterId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4));
        byte completeRaw=frame[20];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(21,2));
        if(characterId<=0||start<0||start>EquipmentSnapshotMaxItems||completeRaw>1||
           count>EquipmentSnapshotMaxItemsPerFrame||start+count>EquipmentSnapshotMaxItems||
           frame.Length!=EquipmentSnapshotHeaderBytes+count*EquipmentItemBytes)return false;
        var items=new List<EquipmentItemWire>(count);
        var uids=new HashSet<long>();
        var slots=new HashSet<int>();
        for(int i=0;i<count;i++)
        {
            int offset=EquipmentSnapshotHeaderBytes+i*EquipmentItemBytes;
            var item=new EquipmentItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)),
                frame[offset+24],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+25,4)));
            if(item.ItemUid<=0||item.SlotId is <1 or >255||
               item.ItemDefinitionId is <1 or >100_000_000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw < -1||
               !uids.Add(item.ItemUid)||!slots.Add(item.SlotId))return false;
            items.Add(item);
        }
        value=new(characterId,start,completeRaw==1,items);
        return true;
    }
    private static byte[] EncodeEquipmentMove(WireOpcode opcode,EquipmentMoveWire value)
    {
        if(opcode is not (WireOpcode.EquipmentEquip or WireOpcode.EquipmentUnequip)||
           value.MapEpoch<=0||value.EquipmentSequence==0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(opcode,EquipmentMoveFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.EquipmentSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ItemUid);
        return frame;
    }

    public static byte[] EncodeEquipmentEquip(EquipmentMoveWire value)=>
        EncodeEquipmentMove(WireOpcode.EquipmentEquip,value);
    public static byte[] EncodeEquipmentUnequip(EquipmentMoveWire value)=>
        EncodeEquipmentMove(WireOpcode.EquipmentUnequip,value);

    private static bool TryDecodeEquipmentMove(ReadOnlySpan<byte> frame,WireOpcode opcode,out EquipmentMoveWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=opcode)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0&&value.EquipmentSequence>0&&value.ItemUid>0;
    }

    public static bool TryDecodeEquipmentEquip(ReadOnlySpan<byte> frame,out EquipmentMoveWire value)=>
        TryDecodeEquipmentMove(frame,WireOpcode.EquipmentEquip,out value);
    public static bool TryDecodeEquipmentUnequip(ReadOnlySpan<byte> frame,out EquipmentMoveWire value)=>
        TryDecodeEquipmentMove(frame,WireOpcode.EquipmentUnequip,out value);

    public static byte[] EncodeEquipmentTransferResult(EquipmentTransferResultWire value)
    {
        if(!ValidEquipmentTransferResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EquipmentTransferResult,EquipmentTransferResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.EquipmentSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(30,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(34,4),value.SlotId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(38,8),value.SourceDropId);
        return frame;
    }

    public static bool TryDecodeEquipmentTransferResult(ReadOnlySpan<byte> frame,out EquipmentTransferResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.EquipmentTransferResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (EquipmentTransferKind)frame[20],
            (EquipmentTransferStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(30,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(34,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(38,8)));
        return ValidEquipmentTransferResult(value);
    }

    private static bool ValidEquipmentTransferResult(EquipmentTransferResultWire value)
    {
        if(value.MapEpoch<=0||value.EquipmentSequence==0||
           value.Kind is not (EquipmentTransferKind.Equip or EquipmentTransferKind.Unequip)||
           (byte)value.Status>(byte)EquipmentTransferStatus.StaleGeneration||
           value.ItemUid<=0||value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.SlotId<0||value.SlotId>255||value.SourceDropId<0)return false;
        if(value.Status is EquipmentTransferStatus.Applied or EquipmentTransferStatus.AlreadyApplied)
            return value.ItemDefinitionId>0&&value.SlotId>0;
        return value.ItemDefinitionId==0&&value.SlotId==0&&value.SourceDropId==0;
    }
    public static byte[] EncodeBankOpen(BankOpenWire value)
    {
        if(value.MapEpoch<=0||value.BankSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.BankOpen,BankOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeBankOpen(ReadOnlySpan<byte> frame,out BankOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.BankOpen)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.BankSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeBankSnapshot(BankSnapshotWire value)
    {
        if(!ValidBankSnapshot(value))throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.BankSnapshot,BankSnapshotFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        frame[25]=(byte)value.Scope;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(26,8),value.WalletMoney);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(34,8),value.BankMoney);
        return frame;
    }

    public static bool TryDecodeBankSnapshot(ReadOnlySpan<byte> frame,out BankSnapshotWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.BankSnapshot)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (BankOpenStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4)),
            (BankBalanceScope)frame[25],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(26,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(34,8)));
        return ValidBankSnapshot(value);
    }

    private static bool ValidBankSnapshot(BankSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.BankSequence==0||value.NpcSpawnId<=0||
           (byte)value.Status>(byte)BankOpenStatus.PlayerDead||
           value.WalletMoney<0||value.BankMoney<0||
           value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.BankMoney>SqliteIdentityStore.MaximumMoney)return false;
        if(value.Status==BankOpenStatus.Opened)
            return value.Scope is BankBalanceScope.Character or BankBalanceScope.Account;
        return value.Scope==BankBalanceScope.None&&value.WalletMoney==0&&value.BankMoney==0;
    }

    private static byte[] EncodeBankMove(WireOpcode opcode,BankMoveWire value)
    {
        if(opcode is not (WireOpcode.BankDeposit or WireOpcode.BankWithdraw)||
           value.MapEpoch<=0||value.BankSequence==0||value.NpcSpawnId<=0||
           value.Amount<=0||value.Amount>SqliteIdentityStore.MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(opcode,BankMoveFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.Amount);
        return frame;
    }

    public static byte[] EncodeBankDeposit(BankMoveWire value)=>
        EncodeBankMove(WireOpcode.BankDeposit,value);
    public static byte[] EncodeBankWithdraw(BankMoveWire value)=>
        EncodeBankMove(WireOpcode.BankWithdraw,value);

    private static bool TryDecodeBankMove(
        ReadOnlySpan<byte> frame,WireOpcode opcode,out BankMoveWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=opcode)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.BankSequence>0&&value.NpcSpawnId>0&&
            value.Amount>0&&value.Amount<=SqliteIdentityStore.MaximumMoney;
    }

    public static bool TryDecodeBankDeposit(ReadOnlySpan<byte> frame,out BankMoveWire value)=>
        TryDecodeBankMove(frame,WireOpcode.BankDeposit,out value);
    public static bool TryDecodeBankWithdraw(ReadOnlySpan<byte> frame,out BankMoveWire value)=>
        TryDecodeBankMove(frame,WireOpcode.BankWithdraw,out value);

    public static byte[] EncodeBankTransactionResult(BankTransactionResultWire value)
    {
        if(!ValidBankTransactionResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.BankTransactionResult,BankTransactionResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(22,4),value.NpcSpawnId);
        frame[26]=(byte)value.Scope;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(27,8),value.Amount);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(35,8),value.WalletMoney);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(43,8),value.BankMoney);
        return frame;
    }

    public static bool TryDecodeBankTransactionResult(
        ReadOnlySpan<byte> frame,out BankTransactionResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.BankTransactionResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (BankTransactionKind)frame[20],
            (BankTransactionStatus)frame[21],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(22,4)),
            (BankBalanceScope)frame[26],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(27,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(35,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(43,8)));
        return ValidBankTransactionResult(value);
    }

    private static bool ValidBankTransactionResult(BankTransactionResultWire value)
    {
        if(value.MapEpoch<=0||value.BankSequence==0||
           value.Kind is not (BankTransactionKind.Deposit or BankTransactionKind.Withdraw)||
           (byte)value.Status>(byte)BankTransactionStatus.PlayerDead||value.NpcSpawnId<=0||
           value.Amount<=0||value.Amount>SqliteIdentityStore.MaximumMoney||
           value.WalletMoney<0||value.BankMoney<0||
           value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.BankMoney>SqliteIdentityStore.MaximumMoney)return false;
        bool scoped=value.Scope is BankBalanceScope.Character or BankBalanceScope.Account;
        if(value.Status is BankTransactionStatus.Applied or
            BankTransactionStatus.AlreadyApplied or
            BankTransactionStatus.InsufficientWallet or
            BankTransactionStatus.InsufficientBank or
            BankTransactionStatus.WalletLimit or
            BankTransactionStatus.BankLimit or
            BankTransactionStatus.StaleSequence)
            return scoped;
        return value.Scope==BankBalanceScope.None&&
            value.WalletMoney==0&&value.BankMoney==0;
    }
    public static byte[] EncodeRepairOpen(RepairOpenWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairOpen,RepairOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeRepairOpen(
        ReadOnlySpan<byte> frame,out RepairOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.RepairSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeRepairSnapshot(RepairSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!ValidRepairSnapshot(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairSnapshot,
            RepairSnapshotHeaderBytes+value.Items.Count*RepairPreviewItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.WalletMoney);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.StartIndex);
        frame[37]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(38,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=RepairSnapshotHeaderBytes+i*RepairPreviewItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            frame[offset+12]=(byte)item.Location;
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+13,4),item.CurrentDurability);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+17,4),item.MaxDurability);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+21,8),item.Cost);
        }
        return frame;
    }

    public static bool TryDecodeRepairSnapshot(
        ReadOnlySpan<byte> frame,out RepairSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairSnapshot||
           frame.Length<RepairSnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var status=(RepairOpenStatus)frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        long wallet=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4));
        byte completeRaw=frame[37];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(38,2));
        if(epoch<=0||sequence==0||npc<=0||
           (byte)status>(byte)RepairOpenStatus.PlayerDead||
           wallet<0||wallet>SqliteIdentityStore.MaximumMoney||
           start<0||start>RepairSnapshotMaxItems||completeRaw>1||
           count>RepairSnapshotMaxItemsPerFrame||
           start+count>RepairSnapshotMaxItems||
           frame.Length!=RepairSnapshotHeaderBytes+count*RepairPreviewItemBytes)
            return false;
        var items=new List<RepairPreviewItemWire>(count);
        var uids=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=RepairSnapshotHeaderBytes+i*RepairPreviewItemBytes;
            var item=new RepairPreviewItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                (RepairItemLocation)frame[offset+12],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+13,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+17,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+21,8)));
            if(!ValidRepairPreviewItem(item)||!uids.Add(item.ItemUid))
                return false;
            items.Add(item);
        }
        value=new(epoch,sequence,status,npc,wallet,start,completeRaw==1,items);
        return ValidRepairSnapshot(value);
    }

    private static bool ValidRepairSnapshot(RepairSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||value.NpcSpawnId<=0||
           (byte)value.Status>(byte)RepairOpenStatus.PlayerDead||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.StartIndex<0||value.StartIndex>RepairSnapshotMaxItems||
           value.Items is null||
           value.Items.Count>RepairSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>RepairSnapshotMaxItems)
            return false;
        if(value.Status!=RepairOpenStatus.Opened)
            return value.WalletMoney==0&&value.StartIndex==0&&value.Complete&&
                value.Items.Count==0;
        var uids=new HashSet<long>();
        return value.Items.All(item=>ValidRepairPreviewItem(item)&&uids.Add(item.ItemUid));
    }

    private static bool ValidRepairPreviewItem(RepairPreviewItemWire item)=>
        item.ItemUid>0&&
        item.ItemDefinitionId is >=1 and <=100_000_000&&
        item.Location is RepairItemLocation.Inventory or RepairItemLocation.Equipped&&
        item.MaxDurability>0&&item.CurrentDurability>=0&&
        item.CurrentDurability<=item.MaxDurability&&
        item.Cost>=0&&item.Cost<=SqliteIdentityStore.MaximumMoney;

    public static byte[] EncodeRepairItem(RepairItemWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||
           value.NpcSpawnId<=0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairItem,RepairItemFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        return frame;
    }

    public static bool TryDecodeRepairItem(
        ReadOnlySpan<byte> frame,out RepairItemWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairItem)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.RepairSequence>0&&
            value.NpcSpawnId>0&&value.ItemUid>0;
    }

    public static byte[] EncodeRepairResult(RepairResultWire value)
    {
        if(!ValidRepairResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairResult,RepairResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.ItemDefinitionId);
        frame[37]=(byte)value.Location;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(38,4),value.CurrentDurability);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(42,4),value.MaxDurability);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(46,8),value.Cost);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(54,8),value.WalletMoney);
        return frame;
    }

    public static bool TryDecodeRepairResult(
        ReadOnlySpan<byte> frame,out RepairResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (RepairStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            (RepairItemLocation)frame[37],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(38,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(42,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(46,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(54,8)));
        return ValidRepairResult(value);
    }

    private static bool ValidRepairResult(RepairResultWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||
           (byte)value.Status>(byte)RepairStatus.InvalidDurability||
           value.NpcSpawnId<=0||value.ItemUid<=0||
           value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.CurrentDurability<0||value.MaxDurability<0||
           value.Cost<0||value.Cost>SqliteIdentityStore.MaximumMoney||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney)
            return false;
        bool knownItem=value.ItemDefinitionId>0&&
            value.Location is RepairItemLocation.Inventory or RepairItemLocation.Equipped;
        return value.Status switch
        {
            RepairStatus.Applied or RepairStatus.AlreadyApplied =>
                knownItem&&value.MaxDurability>0&&
                value.CurrentDurability==value.MaxDurability&&value.Cost>0,
            RepairStatus.AlreadyFull =>
                knownItem&&value.MaxDurability>0&&
                value.CurrentDurability==value.MaxDurability&&value.Cost==0,
            RepairStatus.InsufficientMoney =>
                knownItem&&value.MaxDurability>0&&
                value.CurrentDurability<=value.MaxDurability&&value.Cost>0,
            RepairStatus.UnknownItem =>
                knownItem&&value.CurrentDurability==0&&
                value.MaxDurability==0&&value.Cost==0,
            RepairStatus.InvalidDurability =>
                knownItem&&value.CurrentDurability==0&&
                value.MaxDurability>0&&value.Cost==0,
            _ =>
                value.ItemDefinitionId==0&&value.Location==RepairItemLocation.None&&
                value.CurrentDurability==0&&value.MaxDurability==0&&value.Cost==0
        };
    }

    public static byte[] EncodeEnhancementOpen(EnhancementOpenWire value)
    {
        if(value.MapEpoch<=0||value.EnhancementSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EnhancementOpen,EnhancementOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(16,4),value.EnhancementSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeEnhancementOpen(
        ReadOnlySpan<byte> frame,out EnhancementOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.EnhancementOpen||
           frame.Length!=EnhancementOpenFrameBytes)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.EnhancementSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeEnhancementSnapshot(EnhancementSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!ValidEnhancementSnapshot(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EnhancementSnapshot,
            EnhancementSnapshotHeaderBytes+
            value.Items.Count*EnhancementPreviewItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(16,4),value.EnhancementSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.WalletMoney);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.StartIndex);
        frame[37]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(38,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=EnhancementSnapshotHeaderBytes+
                i*EnhancementPreviewItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            frame[offset+12]=(byte)item.Location;
            frame[offset+13]=checked((byte)item.CurrentLevel);
            frame[offset+14]=checked((byte)item.TargetLevel);
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(offset+15,4),item.SuccessPpm);
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset+19,8),item.MoneyCost);
            frame[offset+27]=item.ConsumeMoneyOnFailure?(byte)1:(byte)0;
        }
        return frame;
    }

    public static bool TryDecodeEnhancementSnapshot(
        ReadOnlySpan<byte> frame,out EnhancementSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.EnhancementSnapshot||
           frame.Length<EnhancementSnapshotHeaderBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var status=(EnhancementOpenStatus)frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        long wallet=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4));
        byte completeRaw=frame[37];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(38,2));
        if(epoch<=0||sequence==0||npc<=0||
           (byte)status>(byte)EnhancementOpenStatus.PlayerDead||
           wallet<0||wallet>SqliteIdentityStore.MaximumMoney||
           start<0||start>EnhancementSnapshotMaxItems||completeRaw>1||
           count>EnhancementSnapshotMaxItemsPerFrame||
           start+count>EnhancementSnapshotMaxItems||
           frame.Length!=EnhancementSnapshotHeaderBytes+
               count*EnhancementPreviewItemBytes)
            return false;
        var items=new List<EnhancementPreviewItemWire>(count);
        var uids=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=EnhancementSnapshotHeaderBytes+
                i*EnhancementPreviewItemBytes;
            byte consumeRaw=frame[offset+27];
            if(consumeRaw>1)return false;
            var item=new EnhancementPreviewItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                (EnhancementItemLocation)frame[offset+12],
                frame[offset+13],frame[offset+14],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+15,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+19,8)),
                consumeRaw==1);
            if(!ValidEnhancementPreviewItem(item)||!uids.Add(item.ItemUid))
                return false;
            items.Add(item);
        }
        value=new(epoch,sequence,status,npc,wallet,start,completeRaw==1,items);
        return ValidEnhancementSnapshot(value);
    }

    private static bool ValidEnhancementSnapshot(EnhancementSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.EnhancementSequence==0||
           value.NpcSpawnId<=0||
           (byte)value.Status>(byte)EnhancementOpenStatus.PlayerDead||
           value.WalletMoney<0||
           value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.StartIndex<0||
           value.StartIndex>EnhancementSnapshotMaxItems||
           value.Items is null||
           value.Items.Count>EnhancementSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>EnhancementSnapshotMaxItems)
            return false;
        if(value.Status!=EnhancementOpenStatus.Opened)
            return value.WalletMoney==0&&value.StartIndex==0&&
                value.Complete&&value.Items.Count==0;
        var uids=new HashSet<long>();
        return value.Items.All(
            item=>ValidEnhancementPreviewItem(item)&&uids.Add(item.ItemUid));
    }

    private static bool ValidEnhancementPreviewItem(
        EnhancementPreviewItemWire item)=>
        item.ItemUid>0&&
        item.ItemDefinitionId is >=1 and <=100_000_000&&
        item.Location is EnhancementItemLocation.Inventory or
            EnhancementItemLocation.Equipped&&
        item.CurrentLevel is >=0 and <=254&&
        item.TargetLevel is >=1 and <=255&&
        item.TargetLevel>item.CurrentLevel&&
        item.SuccessPpm is >=0 and <=EquipmentEnhancementCatalog.ProbabilityScale&&
        item.MoneyCost>=0&&item.MoneyCost<=SqliteIdentityStore.MaximumMoney;

    public static byte[] EncodeEnhancementItem(EnhancementItemWire value)
    {
        if(value.MapEpoch<=0||value.EnhancementSequence==0||
           value.NpcSpawnId<=0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EnhancementItem,EnhancementItemFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(16,4),value.EnhancementSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        return frame;
    }

    public static bool TryDecodeEnhancementItem(
        ReadOnlySpan<byte> frame,out EnhancementItemWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.EnhancementItem||
           frame.Length!=EnhancementItemFrameBytes)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.EnhancementSequence>0&&
            value.NpcSpawnId>0&&value.ItemUid>0;
    }

    public static byte[] EncodeEnhancementResult(EnhancementResultWire value)
    {
        if(!ValidEnhancementResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EnhancementResult,EnhancementResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(16,4),value.EnhancementSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(33,4),value.ItemDefinitionId);
        frame[37]=(byte)value.Location;
        frame[38]=checked((byte)value.PreviousLevel);
        frame[39]=checked((byte)value.TargetLevel);
        frame[40]=checked((byte)value.CurrentLevel);
        frame[41]=value.Succeeded?(byte)1:(byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(42,8),value.MoneyCost);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(50,8),value.MoneyCharged);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(58,8),value.WalletMoney);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(66,4),value.RollPpm);
        return frame;
    }

    public static bool TryDecodeEnhancementResult(
        ReadOnlySpan<byte> frame,out EnhancementResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.EnhancementResult||
           frame.Length!=EnhancementResultFrameBytes)
            return false;
        byte succeededRaw=frame[41];
        if(succeededRaw>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (EnhancementStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            (EnhancementItemLocation)frame[37],
            frame[38],frame[39],frame[40],succeededRaw==1,
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(42,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(50,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(58,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(66,4)));
        return ValidEnhancementResult(value);
    }

    private static bool ValidEnhancementResult(EnhancementResultWire value)
    {
        if(value.MapEpoch<=0||value.EnhancementSequence==0||
           (byte)value.Status>(byte)EnhancementStatus.PlayerDead||
           value.NpcSpawnId<=0||value.ItemUid<=0||
           value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.PreviousLevel is <0 or >255||
           value.TargetLevel is <0 or >255||
           value.CurrentLevel is <0 or >255||
           value.MoneyCost<0||value.MoneyCost>SqliteIdentityStore.MaximumMoney||
           value.MoneyCharged<0||value.MoneyCharged>value.MoneyCost||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.RollPpm<0||
           value.RollPpm>=EquipmentEnhancementCatalog.ProbabilityScale)
            return false;
        bool known=value.ItemDefinitionId>0&&
            value.Location is EnhancementItemLocation.Inventory or
                EnhancementItemLocation.Equipped;
        if(value.Status is EnhancementStatus.Applied or
            EnhancementStatus.AlreadyApplied)
            return known&&value.TargetLevel>value.PreviousLevel&&
                (value.Succeeded
                    ? value.CurrentLevel==value.TargetLevel&&
                      value.MoneyCharged==value.MoneyCost
                    : value.CurrentLevel==value.PreviousLevel);
        if(value.Status==EnhancementStatus.InsufficientMoney)
            return known&&!value.Succeeded&&
                value.TargetLevel>value.PreviousLevel&&
                value.CurrentLevel==value.PreviousLevel&&
                value.MoneyCost>0&&value.MoneyCharged==0&&value.RollPpm==0;
        if(value.Status==EnhancementStatus.NoRule||
           value.Status==EnhancementStatus.MaxLevel)
            return known&&!value.Succeeded&&value.TargetLevel==0&&
                value.CurrentLevel==value.PreviousLevel&&
                value.MoneyCost==0&&value.MoneyCharged==0&&value.RollPpm==0;
        return value.ItemDefinitionId==0&&
            value.Location==EnhancementItemLocation.None&&
            value.PreviousLevel==0&&value.TargetLevel==0&&
            value.CurrentLevel==0&&!value.Succeeded&&value.MoneyCost==0&&
            value.MoneyCharged==0&&value.RollPpm==0;
    }
}
