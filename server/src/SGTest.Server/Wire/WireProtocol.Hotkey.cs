using System.Buffers.Binary;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public const int HotkeyCommandFrameBytes=31;
    public const int HotkeySnapshotHeaderBytes=22;
    public const int HotkeyEntryBytes=10;
    public const int HotkeyMaximumSlots=12;
    public const int HotkeySnapshotMaximumFrameBytes=
        HotkeySnapshotHeaderBytes+HotkeyMaximumSlots*HotkeyEntryBytes;

    public static byte[] EncodeHotkeyCommand(HotkeyCommandWire value)
    {
        if(value.MapEpoch<=0||value.HotkeySequence==0||
           value.Action is < HotkeyCommandAction.Save or > HotkeyCommandAction.Refresh||
           value.Slot>=HotkeyMaximumSlots)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Action==HotkeyCommandAction.Save)
        {
            if(value.DataType is < HotkeyDataType.Item or > HotkeyDataType.Skill||
               value.DataId<=0)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if(value.DataType!=HotkeyDataType.Item||value.DataId!=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.HotkeyCommand,HotkeyCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.HotkeySequence);
        frame[20]=(byte)value.Action;
        frame[21]=value.Slot;
        frame[22]=(byte)value.DataType;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(23,8),value.DataId);
        return frame;
    }

    public static bool TryDecodeHotkeyCommand(
        ReadOnlySpan<byte> frame,out HotkeyCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.HotkeyCommand||
           frame.Length!=HotkeyCommandFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (HotkeyCommandAction)frame[20],frame[21],
            (HotkeyDataType)frame[22],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(23,8)));
        try{_=EncodeHotkeyCommand(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeHotkeySnapshot(HotkeySnapshotWire value)
    {
        var entries=value.Entries??Array.Empty<HotkeyEntryWire>();
        if(value.ReceiverMapEpoch<=0||value.HotkeySequence==0||
           value.Status is < HotkeyResultStatus.Applied or > HotkeyResultStatus.InvalidBinding||
           entries.Count>HotkeyMaximumSlots)
            throw new ArgumentOutOfRangeException(nameof(value));
        var seen=new HashSet<byte>();
        foreach(var entry in entries)
            if(entry.Slot>=HotkeyMaximumSlots||!seen.Add(entry.Slot)||
               entry.DataType is < HotkeyDataType.Item or > HotkeyDataType.Skill||
               entry.DataId<=0)
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.HotkeySnapshot,
            HotkeySnapshotHeaderBytes+entries.Count*HotkeyEntryBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.HotkeySequence);
        frame[20]=(byte)value.Status;
        frame[21]=(byte)entries.Count;
        int offset=HotkeySnapshotHeaderBytes;
        foreach(var entry in entries)
        {
            frame[offset]=entry.Slot;
            frame[offset+1]=(byte)entry.DataType;
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+2,8),entry.DataId);
            offset+=HotkeyEntryBytes;
        }
        return frame;
    }

    public static bool TryDecodeHotkeySnapshot(
        ReadOnlySpan<byte> frame,out HotkeySnapshotWire? value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.HotkeySnapshot||
           frame.Length<HotkeySnapshotHeaderBytes||
           frame.Length>HotkeySnapshotMaximumFrameBytes||
           (frame.Length-HotkeySnapshotHeaderBytes)%HotkeyEntryBytes!=0)
            return false;
        int count=frame[21];
        if(count!=(frame.Length-HotkeySnapshotHeaderBytes)/HotkeyEntryBytes||
           count>HotkeyMaximumSlots)return false;
        var entries=new List<HotkeyEntryWire>(count);
        int offset=HotkeySnapshotHeaderBytes;
        for(int i=0;i<count;i++)
        {
            entries.Add(new(frame[offset],(HotkeyDataType)frame[offset+1],
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+2,8))));
            offset+=HotkeyEntryBytes;
        }
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (HotkeyResultStatus)frame[20],entries);
        try{_=EncodeHotkeySnapshot(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }
}
