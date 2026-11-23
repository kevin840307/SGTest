using System.Buffers.Binary;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public const int MechaCommandFrameBytes=37;
    public const int MechaResultFrameBytes=55;
    public const int MechaSnapshotHeaderBytes=26;
    public const int MechaSnapshotEntryBytes=24;
    public const int MechaSnapshotMaximumMechas=64;
    public const int MechaSnapshotMaximumFrameBytes=
        MechaSnapshotHeaderBytes+MechaSnapshotMaximumMechas*MechaSnapshotEntryBytes;
    public const int MechaEquipmentSnapshotHeaderBytes=18;
    public const int MechaEquipmentSnapshotEntryBytes=34;
    public const int MechaEquipmentSnapshotMaximumItems=384;
    public const int MechaEquipmentSnapshotMaximumFrameBytes=
        MechaEquipmentSnapshotHeaderBytes+
        MechaEquipmentSnapshotMaximumItems*MechaEquipmentSnapshotEntryBytes;
    public const int MechaPresentationFrameBytes=65;

    public static byte[] EncodeMechaCommand(MechaCommandWire value)
    {
        bool refresh=value.Action==MechaCommandAction.Refresh;
        bool equipment=value.Action is MechaCommandAction.Equip or MechaCommandAction.Unequip;
        if(value.MapEpoch<=0||value.MechaSequence==0||
           value.Action is <MechaCommandAction.Mount or >MechaCommandAction.Unequip||
           (refresh&&(value.MechaUid!=0||value.ItemUid!=0))||
           (!refresh&&value.MechaUid<=0)||
           (equipment!=(value.ItemUid>0)))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.MechaCommand,MechaCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.MechaSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.MechaUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.ItemUid);
        return frame;
    }

    public static bool TryDecodeMechaCommand(
        ReadOnlySpan<byte> frame,out MechaCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.MechaCommand||
           frame.Length!=MechaCommandFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (MechaCommandAction)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)));
        try{_=EncodeMechaCommand(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeMechaResult(MechaResultWire value)
    {
        bool refresh=value.Action==MechaCommandAction.Refresh;
        bool equipment=value.Action is MechaCommandAction.Equip or MechaCommandAction.Unequip;
        if(value.MapEpoch<=0||value.MechaSequence==0||
           value.Action is <MechaCommandAction.Mount or >MechaCommandAction.Unequip||
           value.Status is <MechaResultStatus.Applied or >MechaResultStatus.RulesUnavailable||
           value.MountedMechaUid<0||value.SlotId is <0 or >6||value.ReplacedItemUid<0||
           (refresh&&(value.MechaUid!=0||value.ItemUid!=0||
                     value.SlotId!=0||value.ReplacedItemUid!=0))||
           (!refresh&&value.MechaUid<=0)||
           (equipment!=(value.ItemUid>0))||
           (!equipment&&(value.SlotId!=0||value.ReplacedItemUid!=0)))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.MechaResult,MechaResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.MechaSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.MechaUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(30,8),value.MountedMechaUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(38,8),value.ItemUid);
        frame[46]=(byte)value.SlotId;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(47,8),value.ReplacedItemUid);
        return frame;
    }

    public static bool TryDecodeMechaResult(
        ReadOnlySpan<byte> frame,out MechaResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.MechaResult||
           frame.Length!=MechaResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (MechaCommandAction)frame[20],(MechaResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(30,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(38,8)),
            frame[46],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(47,8)));
        try{_=EncodeMechaResult(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeMechaSnapshot(MechaSnapshotWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.MountedMechaUid<0||value.Mechas is null||
           value.Mechas.Count>MechaSnapshotMaximumMechas)
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<long>();
        bool mountedFound=value.MountedMechaUid==0;
        foreach(var mecha in value.Mechas)
        {
            if(mecha.MechaUid<=0||mecha.DefinitionId is <=0 or >100000000||
               mecha.Level is <=0 or >1000000||
               mecha.Experience is <0 or >9000000000000000L||
               !ids.Add(mecha.MechaUid))
                throw new ArgumentOutOfRangeException(nameof(value));
            if(mecha.MechaUid==value.MountedMechaUid)mountedFound=true;
        }
        if(!mountedFound)throw new ArgumentOutOfRangeException(nameof(value));
        int bytes=checked(
            MechaSnapshotHeaderBytes+value.Mechas.Count*MechaSnapshotEntryBytes);
        var frame=NewFrame(WireOpcode.MechaSnapshot,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.MountedMechaUid);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24,2),(ushort)value.Mechas.Count);
        int offset=MechaSnapshotHeaderBytes;
        foreach(var mecha in value.Mechas)
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),mecha.MechaUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),mecha.DefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),mecha.Level);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),mecha.Experience);
            offset+=MechaSnapshotEntryBytes;
        }
        return frame;
    }

    public static bool TryDecodeMechaSnapshot(
        ReadOnlySpan<byte> frame,out MechaSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.MechaSnapshot||
           frame.Length<MechaSnapshotHeaderBytes||
           frame.Length>MechaSnapshotMaximumFrameBytes||
           (frame.Length-MechaSnapshotHeaderBytes)%MechaSnapshotEntryBytes!=0)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long mounted=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(24,2));
        if(epoch<=0||mounted<0||count>MechaSnapshotMaximumMechas||
           frame.Length!=MechaSnapshotHeaderBytes+count*MechaSnapshotEntryBytes)
            return false;
        var mechas=new List<MechaSnapshotEntryWire>(count);
        int offset=MechaSnapshotHeaderBytes;
        for(int i=0;i<count;i++)
        {
            mechas.Add(new(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8))));
            offset+=MechaSnapshotEntryBytes;
        }
        value=new(epoch,mounted,mechas);
        try{_=EncodeMechaSnapshot(value);return true;}
        catch(ArgumentOutOfRangeException){value=null;return false;}
    }

    public static byte[] EncodeMechaEquipmentSnapshot(MechaEquipmentSnapshotWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.Items is null||
           value.Items.Count>MechaEquipmentSnapshotMaximumItems)
            throw new ArgumentOutOfRangeException(nameof(value));
        var itemIds=new HashSet<long>();
        var slots=new HashSet<(long MechaUid,int SlotId)>();
        foreach(var item in value.Items)
        {
            if(item.MechaUid<=0||item.ItemUid<=0||item.SlotId is <1 or >6||
               item.ItemDefinitionId is <=0 or >100000000||item.SourceDropId<0||
               item.EnhancementLevel is <0 or >255||item.DurabilityRaw < -1||
               !itemIds.Add(item.ItemUid)||!slots.Add((item.MechaUid,item.SlotId)))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        int bytes=checked(MechaEquipmentSnapshotHeaderBytes+
            value.Items.Count*MechaEquipmentSnapshotEntryBytes);
        var frame=NewFrame(WireOpcode.MechaEquipmentSnapshot,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16,2),(ushort)value.Items.Count);
        int offset=MechaEquipmentSnapshotHeaderBytes;
        foreach(var item in value.Items)
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.MechaUid);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+8,8),item.ItemUid);
            frame[offset+16]=(byte)item.SlotId;
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+17,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+21,8),item.SourceDropId);
            frame[offset+29]=(byte)item.EnhancementLevel;
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+30,4),item.DurabilityRaw);
            offset+=MechaEquipmentSnapshotEntryBytes;
        }
        return frame;
    }

    public static bool TryDecodeMechaEquipmentSnapshot(
        ReadOnlySpan<byte> frame,out MechaEquipmentSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||
           header.Opcode!=WireOpcode.MechaEquipmentSnapshot||
           frame.Length<MechaEquipmentSnapshotHeaderBytes||
           frame.Length>MechaEquipmentSnapshotMaximumFrameBytes||
           (frame.Length-MechaEquipmentSnapshotHeaderBytes)%
               MechaEquipmentSnapshotEntryBytes!=0)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(16,2));
        if(epoch<=0||count>MechaEquipmentSnapshotMaximumItems||
           frame.Length!=MechaEquipmentSnapshotHeaderBytes+
               count*MechaEquipmentSnapshotEntryBytes)
            return false;
        var items=new List<MechaEquipmentEntryWire>(count);
        int offset=MechaEquipmentSnapshotHeaderBytes;
        for(int i=0;i<count;i++)
        {
            items.Add(new(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+8,8)),
                frame[offset+16],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+17,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+21,8)),
                frame[offset+29],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+30,4))));
            offset+=MechaEquipmentSnapshotEntryBytes;
        }
        value=new(epoch,items);
        try{_=EncodeMechaEquipmentSnapshot(value);return true;}
        catch(ArgumentOutOfRangeException){value=null;return false;}
    }

    public static byte[] EncodeMechaPresentation(MechaPresentationWire value)
    {
        if(!ValidMechaPresentation(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.MechaPresentation,MechaPresentationFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        frame[48]=value.Mounted?(byte)1:(byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(49,8),value.MechaUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(57,4),value.DefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(61,4),value.Level);
        return frame;
    }

    public static bool TryDecodeMechaPresentation(
        ReadOnlySpan<byte> frame,out MechaPresentationWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||
           header.Opcode!=WireOpcode.MechaPresentation||
           frame.Length!=MechaPresentationFrameBytes)
            return false;
        byte mounted=frame[48];
        if(mounted>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            mounted==1,
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(49,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(57,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(61,4)));
        return ValidMechaPresentation(value);
    }

    private static bool ValidMechaPresentation(MechaPresentationWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||value.OwnerActorId<=0||
           value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0)
            return false;
        return value.Mounted
            ?value.MechaUid>0&&value.DefinitionId is >0 and <=100000000&&
                value.Level is >0 and <=1000000
            :value.MechaUid==0&&value.DefinitionId==0&&value.Level==0;
    }
}
