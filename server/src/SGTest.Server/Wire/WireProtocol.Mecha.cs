using System.Buffers.Binary;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public const int MechaCommandFrameBytes=29;
    public const int MechaResultFrameBytes=38;
    public const int MechaSnapshotHeaderBytes=26;
    public const int MechaSnapshotEntryBytes=24;
    public const int MechaSnapshotMaximumMechas=64;
    public const int MechaSnapshotMaximumFrameBytes=
        MechaSnapshotHeaderBytes+MechaSnapshotMaximumMechas*MechaSnapshotEntryBytes;

    public static byte[] EncodeMechaCommand(MechaCommandWire value)
    {
        if(value.MapEpoch<=0||value.MechaSequence==0||
           value.Action is <MechaCommandAction.Mount or >MechaCommandAction.Refresh||
           ((value.Action==MechaCommandAction.Refresh)!=(value.MechaUid==0))||
           (value.Action!=MechaCommandAction.Refresh&&value.MechaUid<=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.MechaCommand,MechaCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.MechaSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.MechaUid);
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
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)));
        try{_=EncodeMechaCommand(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeMechaResult(MechaResultWire value)
    {
        if(value.MapEpoch<=0||value.MechaSequence==0||
           value.Action is <MechaCommandAction.Mount or >MechaCommandAction.Refresh||
           value.Status is <MechaResultStatus.Applied or >MechaResultStatus.SequenceConflict||
           value.MountedMechaUid<0||
           ((value.Action==MechaCommandAction.Refresh)!=(value.MechaUid==0))||
           (value.Action!=MechaCommandAction.Refresh&&value.MechaUid<=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.MechaResult,MechaResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.MechaSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.MechaUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(30,8),value.MountedMechaUid);
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
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(30,8)));
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
}
