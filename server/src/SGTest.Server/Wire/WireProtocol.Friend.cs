using System.Buffers.Binary;
using System.Text;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public static byte[] EncodeFriendCommand(FriendCommandWire value)
    {
        byte[] name;
        try{name=StrictUtf8.GetBytes(value.TargetName??string.Empty);}
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.MapEpoch<=0||value.FriendSequence==0||
           value.Action is <FriendCommandAction.Add or >FriendCommandAction.Refresh||
           value.TargetActorId<0||name.Length>PlayerIdentityMaxNameBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        switch(value.Action)
        {
            case FriendCommandAction.Add:
                if((value.TargetActorId>0)==(name.Length>0))
                    throw new ArgumentOutOfRangeException(nameof(value));
                if(name.Length>0&&
                   ((value.TargetName??string.Empty)!=(value.TargetName??string.Empty)
                       .Normalize(NormalizationForm.FormC).Trim()||
                    (value.TargetName??string.Empty).Any(char.IsControl)))
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case FriendCommandAction.Remove:
                if(value.TargetActorId<=0||name.Length!=0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case FriendCommandAction.Refresh:
                if(value.TargetActorId!=0||name.Length!=0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
        }
        var frame=NewFrame(WireOpcode.FriendCommand,FriendCommandHeaderBytes+name.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.FriendSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.TargetActorId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(29,2),(ushort)name.Length);
        name.CopyTo(frame.AsSpan(FriendCommandHeaderBytes));
        return frame;
    }

    public static bool TryDecodeFriendCommand(ReadOnlySpan<byte> frame,out FriendCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.FriendCommand||
           frame.Length<FriendCommandHeaderBytes||frame.Length>FriendCommandMaximumFrameBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var action=(FriendCommandAction)frame[20];
        long target=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8));
        int length=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(29,2));
        if(frame.Length!=FriendCommandHeaderBytes+length)return false;
        try
        {
            string name=length==0?string.Empty:
                StrictUtf8.GetString(frame.Slice(FriendCommandHeaderBytes,length));
            value=new(epoch,sequence,action,target,name);
            _=EncodeFriendCommand(value);
            return true;
        }
        catch(DecoderFallbackException){value=default;return false;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeFriendResult(FriendResultWire value)
    {
        if(value.MapEpoch<=0||value.FriendSequence==0||
           value.Action is <FriendCommandAction.Add or >FriendCommandAction.Refresh||
           value.Status is <FriendResultStatus.Success or >FriendResultStatus.Conflict||
           value.TargetActorId<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.FriendResult,FriendResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.FriendSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.TargetActorId);
        return frame;
    }

    public static bool TryDecodeFriendResult(ReadOnlySpan<byte> frame,out FriendResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.FriendResult||
           frame.Length!=FriendResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (FriendCommandAction)frame[20],(FriendResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)));
        try{_=EncodeFriendResult(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeFriendSnapshot(FriendSnapshotWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.Friends is null||
           value.Friends.Count>ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        var encoded=new List<(FriendEntryWire Entry,byte[] Name)>(value.Friends.Count);
        var ids=new HashSet<long>();
        int bytes=FriendSnapshotHeaderBytes;
        foreach(var entry in value.Friends)
        {
            byte[] name;
            try{name=StrictUtf8.GetBytes(entry.Name??string.Empty);}
            catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
            if(entry.CharacterId<=0||name.Length is <1 or >PlayerIdentityMaxNameBytes||
               !ids.Add(entry.CharacterId))
                throw new ArgumentOutOfRangeException(nameof(value));
            bytes=checked(bytes+FriendEntryHeaderBytes+name.Length);
            if(bytes>MaximumFrameBytes)
                throw new ArgumentOutOfRangeException(nameof(value));
            encoded.Add((entry,name));
        }
        var frame=NewFrame(WireOpcode.FriendSnapshot,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16,2),(ushort)encoded.Count);
        int offset=FriendSnapshotHeaderBytes;
        foreach(var item in encoded)
        {
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset,8),item.Entry.CharacterId);
            frame[offset+8]=item.Entry.IsOnline?(byte)1:(byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                frame.AsSpan(offset+9,2),(ushort)item.Name.Length);
            item.Name.CopyTo(frame.AsSpan(offset+FriendEntryHeaderBytes));
            offset+=FriendEntryHeaderBytes+item.Name.Length;
        }
        return frame;
    }

    public static bool TryDecodeFriendSnapshot(
        ReadOnlySpan<byte> frame,out FriendSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.FriendSnapshot||
           frame.Length<FriendSnapshotHeaderBytes||frame.Length>MaximumFrameBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(16,2));
        if(epoch<=0)return false;
        try
        {
            int offset=FriendSnapshotHeaderBytes;
            var friends=new List<FriendEntryWire>(count);
            var ids=new HashSet<long>();
            for(int i=0;i<count;i++)
            {
                if(offset+FriendEntryHeaderBytes>frame.Length)return false;
                long character=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8));
                byte online=frame[offset+8];
                int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(
                    frame.Slice(offset+9,2));
                if(character<=0||online>1||
                   nameLength is <1 or >PlayerIdentityMaxNameBytes||
                   offset+FriendEntryHeaderBytes+nameLength>frame.Length||
                   !ids.Add(character))
                    return false;
                string name=StrictUtf8.GetString(
                    frame.Slice(offset+FriendEntryHeaderBytes,nameLength));
                friends.Add(new(character,name,online==1));
                offset+=FriendEntryHeaderBytes+nameLength;
            }
            if(offset!=frame.Length)return false;
            value=new(epoch,friends);
            _=EncodeFriendSnapshot(value);
            return true;
        }
        catch(DecoderFallbackException){value=null;return false;}
        catch(ArgumentOutOfRangeException){value=null;return false;}
    }
}
