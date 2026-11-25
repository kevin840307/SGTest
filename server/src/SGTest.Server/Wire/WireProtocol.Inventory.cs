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

/// <summary>Inventory snapshots, skill books, recovery, item discard and pickup wire V1 operations.</summary>
public static partial class WireProtocol
{
    /// <summary>Server-only immutable inventory baseline; chunks must be
    /// consecutive and a final chunk is mandatory even for zero inventory.</summary>
    public static byte[] EncodeInventorySnapshot(InventorySnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.CharacterId<=0||value.StartIndex<0||
            value.StartIndex>InventorySnapshotMaxItems ||
            value.Items is null || value.Items.Count>InventorySnapshotMaxItemsPerFrame ||
            value.StartIndex+value.Items.Count>InventorySnapshotMaxItems)
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<long>();
        foreach(var item in value.Items)
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000 ||
               item.Quantity is <1 or >1_000_000 || item.SourceDropId<0 ||
               item.EnhancementLevel is <0 or >255 || item.DurabilityRaw < -1 ||
               !ids.Add(item.ItemUid))
                throw new ArgumentOutOfRangeException(nameof(value));
        int count=value.Items.Count;
        var frame=NewFrame(WireOpcode.InventorySnapshot,
            InventorySnapshotHeaderBytes+count*InventoryItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4),value.StartIndex);
        frame[20]=(byte)(value.Complete?1:0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(21,2),checked((ushort)count));
        for(int i=0;i<count;i++)
        {
            var item=value.Items[i];
            int offset=InventorySnapshotHeaderBytes+i*InventoryItemBytes;
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
            frame[offset+24]=checked((byte)item.EnhancementLevel);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+25,4),item.DurabilityRaw);
        }
        return frame;
    }

    public static bool TryDecodeInventorySnapshot(
        ReadOnlySpan<byte> frame,out InventorySnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||
            header.Opcode!=WireOpcode.InventorySnapshot) return false;
        long character=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4));
        byte complete=frame[20];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(21,2));
        if(character<=0||start<0||complete>1||
            count>InventorySnapshotMaxItemsPerFrame||
            start+count>InventorySnapshotMaxItems ||
            frame.Length!=InventorySnapshotHeaderBytes+count*InventoryItemBytes)
            return false;
        var items=new List<InventoryItemWire>(count);
        var ids=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=InventorySnapshotHeaderBytes+i*InventoryItemBytes;
            var item=new InventoryItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)),
                frame[offset+24],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+25,4)));
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000 ||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw < -1||
               !ids.Add(item.ItemUid)) return false;
            items.Add(item);
        }
        value=new(character,start,complete==1,items);
        return true;
    }

    /// <summary>Server-only learned-skill baseline. Skill level is owned by
    /// durable Server state and is never accepted from a gameplay command.</summary>
    public static byte[] EncodeSkillBookSnapshot(SkillBookSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.CharacterId<=0||value.StartIndex<0||
           value.StartIndex>SkillBookSnapshotMaxSkills||
           value.Skills is null||
           value.Skills.Count>SkillBookSnapshotMaxSkillsPerFrame||
           value.StartIndex+value.Skills.Count>SkillBookSnapshotMaxSkills)
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<int>();
        foreach(var skill in value.Skills)
            if(skill.SkillId is <1 or >65535||skill.Level is <1 or >255||
               !ids.Add(skill.SkillId))
                throw new ArgumentOutOfRangeException(nameof(value));
        int count=value.Skills.Count;
        var frame=NewFrame(WireOpcode.SkillBookSnapshot,
            SkillBookSnapshotHeaderBytes+count*SkillBookEntryBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4),value.StartIndex);
        frame[20]=(byte)(value.Complete?1:0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(21,2),checked((ushort)count));
        for(int i=0;i<count;i++)
        {
            int offset=SkillBookSnapshotHeaderBytes+i*SkillBookEntryBytes;
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(offset,4),value.Skills[i].SkillId);
            frame[offset+4]=checked((byte)value.Skills[i].Level);
        }
        return frame;
    }

    public static bool TryDecodeSkillBookSnapshot(
        ReadOnlySpan<byte> frame,out SkillBookSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||
           header.Opcode!=WireOpcode.SkillBookSnapshot)return false;
        long character=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4));
        byte complete=frame[20];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(21,2));
        if(character<=0||start<0||complete>1||
           count>SkillBookSnapshotMaxSkillsPerFrame||
           start+count>SkillBookSnapshotMaxSkills||
           frame.Length!=SkillBookSnapshotHeaderBytes+count*SkillBookEntryBytes)
            return false;
        var skills=new List<SkillBookEntryWire>(count);
        var ids=new HashSet<int>();
        for(int i=0;i<count;i++)
        {
            int offset=SkillBookSnapshotHeaderBytes+i*SkillBookEntryBytes;
            var skill=new SkillBookEntryWire(
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset,4)),
                frame[offset+4]);
            if(skill.SkillId is <1 or >65535||skill.Level is <1 or >255||
               !ids.Add(skill.SkillId))
                return false;
            skills.Add(skill);
        }
        value=new(character,start,complete==1,skills);
        return true;
    }

    public static byte[] EncodeRecoveryUse(RecoveryUseWire value)
    {
        if(value.MapEpoch<=0||value.UseSequence==0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RecoveryUse,RecoveryUseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.UseSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ItemUid);
        return frame;
    }
    public static bool TryDecodeRecoveryUse(ReadOnlySpan<byte> frame,
        out RecoveryUseWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.RecoveryUse)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0 && value.UseSequence>0 && value.ItemUid>0;
    }
    public static byte[] EncodeRecoveryUseResult(RecoveryUseResultWire value)
    {
        if(!ValidRecoveryUseResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RecoveryUseResult,RecoveryUseResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.UseSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.RemainingQuantity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.MaxHp);
        return frame;
    }
    public static bool TryDecodeRecoveryUseResult(ReadOnlySpan<byte> frame,
        out RecoveryUseResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.RecoveryUseResult)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (RecoveryUseStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)));
        return ValidRecoveryUseResult(value);
    }
    private static bool ValidRecoveryUseResult(RecoveryUseResultWire value)
    {
        if(value.MapEpoch<=0||value.UseSequence==0||value.ItemUid<=0||
            (byte)value.Status>(byte)RecoveryUseStatus.CombatDiverged)
            return false;
        if(value.Status is RecoveryUseStatus.Granted or RecoveryUseStatus.AlreadyGranted)
            return value.ItemDefinitionId is >0 and <=100_000_000 &&
                value.RemainingQuantity is >=0 and <=1_000_000 &&
                value.CurrentHp>0 && value.MaxHp>=value.CurrentHp;
        return value.ItemDefinitionId==0 && value.RemainingQuantity==0 &&
            value.CurrentHp==0 && value.MaxHp==0;
    }

    public static byte[] EncodeInventoryDiscard(InventoryDiscardWire value)
    {
        if(value.MapEpoch<=0||value.DiscardSequence==0||value.ItemUid<=0||
            value.Quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.InventoryDiscard,InventoryDiscardFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.DiscardSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeInventoryDiscard(ReadOnlySpan<byte> frame,
        out InventoryDiscardWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.InventoryDiscard)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        return value.MapEpoch>0&&value.DiscardSequence>0&&value.ItemUid>0&&
            value.Quantity is >0 and <=1_000_000;
    }

    public static byte[] EncodeInventoryDiscardResult(InventoryDiscardResultWire value)
    {
        if(!ValidInventoryDiscardResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.InventoryDiscardResult,
            InventoryDiscardResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.DiscardSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.DiscardedQuantity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.RemainingQuantity);
        return frame;
    }

    public static bool TryDecodeInventoryDiscardResult(ReadOnlySpan<byte> frame,
        out InventoryDiscardResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.InventoryDiscardResult)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (InventoryDiscardStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)));
        return ValidInventoryDiscardResult(value);
    }

    private static bool ValidInventoryDiscardResult(InventoryDiscardResultWire value)
    {
        if(value.MapEpoch<=0||value.DiscardSequence==0||value.ItemUid<=0||
            (byte)value.Status>(byte)InventoryDiscardStatus.StaleGeneration)
            return false;
        if(value.Status is InventoryDiscardStatus.Applied or
            InventoryDiscardStatus.AlreadyApplied)
            return value.ItemDefinitionId is >0 and <=100_000_000 &&
                value.DiscardedQuantity is >0 and <=1_000_000 &&
                value.RemainingQuantity is >=0 and <=1_000_000;
        return value.ItemDefinitionId==0&&value.DiscardedQuantity==0&&
            value.RemainingQuantity==0;
    }

    public static byte[] EncodeGroundPickup(GroundPickupWire value)
    {
        if (value.MapEpoch<=0||value.PickupSequence==0||value.DropId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GroundPickup,GroundPickupFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PickupSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.DropId);
        return frame;
    }
    public static bool TryDecodeGroundPickup(ReadOnlySpan<byte> frame,out GroundPickupWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GroundPickup)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0&&value.PickupSequence>0&&value.DropId>0;
    }
    public static byte[] EncodeGroundPickupResult(GroundPickupResultWire value)
    {
        if (!ValidPickupResult(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GroundPickupResult,GroundPickupResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PickupSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.DropId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Quantity);
        return frame;
    }
    public static bool TryDecodeGroundPickupResult(
        ReadOnlySpan<byte> frame,out GroundPickupResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GroundPickupResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PickupStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)));
        return ValidPickupResult(value);
    }
    private static bool ValidPickupResult(GroundPickupResultWire value)
    {
        if (value.MapEpoch<=0 || value.PickupSequence==0 || value.DropId<=0 ||
            (byte)value.Status>(byte)PickupStatus.InventoryFull ||
            value.ItemUid<0 || value.ItemDefinitionId<0 || value.Quantity<0)
            return false;
        return value.Status is PickupStatus.Granted or PickupStatus.AlreadyGranted
            ? value.ItemUid>0 && value.ItemDefinitionId is >0 and <=100_000_000 &&
              value.Quantity is >0 and <=1_000_000
            : value.ItemUid==0 && value.ItemDefinitionId==0 && value.Quantity==0;
    }



}
