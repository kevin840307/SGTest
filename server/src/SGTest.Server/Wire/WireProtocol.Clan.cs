using System.Buffers.Binary;
using System.Text;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public static byte[] EncodeClanCommand(ClanCommandWire value)
    {
        byte[] name;
        try{name=StrictUtf8.GetBytes(value.Name??string.Empty);}
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.MapEpoch<=0||value.ClanSequence==0||
           value.Action is <ClanCommandAction.Create or >ClanCommandAction.Refresh||
           value.ClanId<0||name.Length>ClanMaxNameBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        bool create=value.Action==ClanCommandAction.Create;
        if(create)
        {
            if(value.ClanId!=0||name.Length<1||string.IsNullOrWhiteSpace(value.Name)||
               value.Name!=value.Name.Normalize(NormalizationForm.FormC).Trim()||
               value.Name.Any(char.IsControl))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if(name.Length!=0||
                (value.Action==ClanCommandAction.Join&&value.ClanId<=0)||
                (value.Action is ClanCommandAction.Leave or ClanCommandAction.Refresh && value.ClanId!=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ClanCommand,ClanCommandHeaderBytes+name.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ClanSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ClanId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(29,2),(ushort)name.Length);
        name.CopyTo(frame.AsSpan(ClanCommandHeaderBytes));
        return frame;
    }

    public static bool TryDecodeClanCommand(ReadOnlySpan<byte> frame,out ClanCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ClanCommand||
           frame.Length<ClanCommandHeaderBytes||frame.Length>ClanCommandMaximumFrameBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var action=(ClanCommandAction)frame[20];
        long clanId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8));
        int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(29,2));
        if(nameLength>ClanMaxNameBytes||frame.Length!=ClanCommandHeaderBytes+nameLength)
            return false;
        try
        {
            string name=nameLength==0?string.Empty:
                StrictUtf8.GetString(frame.Slice(ClanCommandHeaderBytes,nameLength));
            value=new(epoch,sequence,action,clanId,name);
            _=EncodeClanCommand(value);
            return true;
        }
        catch(DecoderFallbackException){value=default;return false;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeClanResult(ClanResultWire value)
    {
        if(value.MapEpoch<=0||value.ClanSequence==0||
           value.Action is <ClanCommandAction.Create or >ClanCommandAction.Refresh||
           value.Status is <ClanResultStatus.Success or >ClanResultStatus.Conflict||
           value.ClanId<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ClanResult,ClanResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ClanSequence);
        frame[20]=(byte)value.Action; frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.ClanId);
        return frame;
    }

    public static bool TryDecodeClanResult(ReadOnlySpan<byte> frame,out ClanResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ClanResult||
           frame.Length!=ClanResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (ClanCommandAction)frame[20],(ClanResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)));
        try{_=EncodeClanResult(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeClanSnapshot(ClanSnapshotWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ClanId<0||value.Members is null||
           value.Members.Count>ClanMaximumMembers)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.ClanId==0)
        {
            if(value.Revision!=0||value.LeaderCharacterId!=0||
               !string.IsNullOrEmpty(value.Name)||value.Members.Count!=0)
                throw new ArgumentOutOfRangeException(nameof(value));
            var empty=NewFrame(WireOpcode.ClanSnapshot,ClanSnapshotHeaderBytes);
            BinaryPrimitives.WriteInt64LittleEndian(empty.AsSpan(8,8),value.ReceiverMapEpoch);
            return empty;
        }
        byte[] clanName;
        try{clanName=StrictUtf8.GetBytes(value.Name??string.Empty);}
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.Revision==0||value.LeaderCharacterId<=0||
           clanName.Length is <1 or >ClanMaxNameBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        var encoded=new List<(ClanMemberWire Member,byte[] Name)>(value.Members.Count);
        int bytes=ClanSnapshotHeaderBytes+clanName.Length;
        var ids=new HashSet<long>();
        foreach(var member in value.Members)
        {
            byte[] name;
            try{name=StrictUtf8.GetBytes(member.Name??string.Empty);}
            catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
            if(member.CharacterId<=0||member.Role>1||
               name.Length is <1 or >PlayerIdentityMaxNameBytes||!ids.Add(member.CharacterId))
                throw new ArgumentOutOfRangeException(nameof(value));
            encoded.Add((member,name));
            bytes=checked(bytes+ClanMemberHeaderBytes+name.Length);
        }
        if(bytes>ClanSnapshotMaximumFrameBytes||
           !ids.Contains(value.LeaderCharacterId)||
           encoded.Count(x=>x.Member.Role==1)!=1)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ClanSnapshot,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ClanId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.LeaderCharacterId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(36,2),(ushort)clanName.Length);
        frame[38]=(byte)encoded.Count;
        int offset=ClanSnapshotHeaderBytes;
        clanName.CopyTo(frame.AsSpan(offset)); offset+=clanName.Length;
        foreach(var item in encoded)
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.Member.CharacterId);
            frame[offset+8]=item.Member.Role;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset+9,2),(ushort)item.Name.Length);
            item.Name.CopyTo(frame.AsSpan(offset+ClanMemberHeaderBytes));
            offset+=ClanMemberHeaderBytes+item.Name.Length;
        }
        return frame;
    }

    public static bool TryDecodeClanSnapshot(ReadOnlySpan<byte> frame,out ClanSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ClanSnapshot||
           frame.Length<ClanSnapshotHeaderBytes||frame.Length>ClanSnapshotMaximumFrameBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long clanId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        uint revision=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4));
        long leader=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8));
        int clanNameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(36,2));
        int count=frame[38];
        if(epoch<=0||count>ClanMaximumMembers||clanNameLength>ClanMaxNameBytes)
            return false;
        if(clanId==0)
        {
            if(revision!=0||leader!=0||clanNameLength!=0||count!=0||
               frame.Length!=ClanSnapshotHeaderBytes)return false;
            value=new(epoch,0,0,0,string.Empty,Array.Empty<ClanMemberWire>());
            return true;
        }
        if(clanId<0||revision==0||leader<=0||clanNameLength<1||
           frame.Length<ClanSnapshotHeaderBytes+clanNameLength)return false;
        try
        {
            int offset=ClanSnapshotHeaderBytes;
            string name=StrictUtf8.GetString(frame.Slice(offset,clanNameLength));
            offset+=clanNameLength;
            var members=new List<ClanMemberWire>(count);
            for(int i=0;i<count;i++)
            {
                if(offset+ClanMemberHeaderBytes>frame.Length)return false;
                long character=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8));
                byte role=frame[offset+8];
                int len=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset+9,2));
                if(character<=0||role>1||len is <1 or >PlayerIdentityMaxNameBytes||
                   offset+ClanMemberHeaderBytes+len>frame.Length)return false;
                string memberName=StrictUtf8.GetString(frame.Slice(offset+ClanMemberHeaderBytes,len));
                members.Add(new(character,role,memberName));
                offset+=ClanMemberHeaderBytes+len;
            }
            if(offset!=frame.Length)return false;
            value=new(epoch,clanId,revision,leader,name,members);
            _=EncodeClanSnapshot(value);
            return true;
        }
        catch(DecoderFallbackException){value=null;return false;}
        catch(ArgumentOutOfRangeException){value=null;return false;}
    }
}
