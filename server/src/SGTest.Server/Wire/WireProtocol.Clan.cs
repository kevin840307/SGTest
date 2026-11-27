using System.Buffers.Binary;
using System.Text;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public static byte[] EncodeClanCommand(ClanCommandWire value)
    {
        byte[] payload;
        try{payload=StrictUtf8.GetBytes(value.Name??string.Empty);}
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.MapEpoch<=0||value.ClanSequence==0||
           value.Action is <ClanCommandAction.Create or >ClanCommandAction.RejectInvite||
           value.ClanId<0||value.TargetActorId<0)
            throw new ArgumentOutOfRangeException(nameof(value));

        switch(value.Action)
        {
            case ClanCommandAction.Create:
                if(value.ClanId!=0||value.TargetActorId!=0||
                   payload.Length is <1 or >ClanMaxNameBytes||
                   string.IsNullOrWhiteSpace(value.Name)||
                   value.Name!=value.Name.Normalize(NormalizationForm.FormC).Trim()||
                   value.Name.Length is <2 or >24||value.Name.Any(char.IsControl))
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case ClanCommandAction.Join:
                if(value.ClanId<=0||value.TargetActorId<=0||payload.Length!=0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case ClanCommandAction.Leave:
            case ClanCommandAction.Refresh:
                if(value.ClanId!=0||value.TargetActorId!=0||payload.Length!=0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case ClanCommandAction.SetAnnouncement:
                if(value.ClanId!=0||value.TargetActorId!=0||
                   payload.Length>ClanMaxAnnouncementBytes||
                   (value.Name??string.Empty)!=(value.Name??string.Empty).Normalize(NormalizationForm.FormC).Trim()||
                   (value.Name??string.Empty).Any(ch=>char.IsControl(ch)&&ch!='\n'))
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case ClanCommandAction.Kick:
            case ClanCommandAction.TransferLeadership:
            case ClanCommandAction.Invite:
                if(value.ClanId!=0||value.TargetActorId<=0||payload.Length!=0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            case ClanCommandAction.RejectInvite:
                if(value.ClanId<=0||value.TargetActorId<=0||payload.Length!=0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        var frame=NewFrame(WireOpcode.ClanCommand,ClanCommandHeaderBytes+payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ClanSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ClanId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetActorId);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(37,2),(ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(ClanCommandHeaderBytes));
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
        long targetActorId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8));
        int payloadLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(37,2));
        if(frame.Length!=ClanCommandHeaderBytes+payloadLength)
            return false;
        try
        {
            string payload=payloadLength==0?string.Empty:
                StrictUtf8.GetString(frame.Slice(ClanCommandHeaderBytes,payloadLength));
            value=new(epoch,sequence,action,clanId,payload,targetActorId);
            _=EncodeClanCommand(value);
            return true;
        }
        catch(DecoderFallbackException){value=default;return false;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodeClanResult(ClanResultWire value)
    {
        if(value.MapEpoch<=0||value.ClanSequence==0||
           value.Action is <ClanCommandAction.Create or >ClanCommandAction.RejectInvite||
           value.Status is <ClanResultStatus.Success or >ClanResultStatus.TargetHasPendingInvite||
           value.ClanId<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ClanResult,ClanResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ClanSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
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
               !string.IsNullOrEmpty(value.Name)||!string.IsNullOrEmpty(value.Announcement)||
               value.Members.Count!=0)
                throw new ArgumentOutOfRangeException(nameof(value));
            var empty=NewFrame(WireOpcode.ClanSnapshot,ClanSnapshotHeaderBytes);
            BinaryPrimitives.WriteInt64LittleEndian(empty.AsSpan(8,8),value.ReceiverMapEpoch);
            return empty;
        }

        byte[] clanName;
        byte[] announcement;
        try
        {
            clanName=StrictUtf8.GetBytes(value.Name??string.Empty);
            announcement=StrictUtf8.GetBytes(value.Announcement??string.Empty);
        }
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}

        if(value.Revision==0||value.LeaderCharacterId<=0||
           clanName.Length is <1 or >ClanMaxNameBytes||
           announcement.Length>ClanMaxAnnouncementBytes||
           (value.Name??string.Empty)!=(value.Name??string.Empty).Normalize(NormalizationForm.FormC).Trim()||
           (value.Announcement??string.Empty)!=(value.Announcement??string.Empty).Normalize(NormalizationForm.FormC).Trim()||
           (value.Name??string.Empty).Any(char.IsControl)||
           (value.Announcement??string.Empty).Any(ch=>char.IsControl(ch)&&ch!='\n'))
            throw new ArgumentOutOfRangeException(nameof(value));

        var encoded=new List<(ClanMemberWire Member,byte[] Name)>(value.Members.Count);
        int bytes=ClanSnapshotHeaderBytes+clanName.Length+announcement.Length;
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
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(38,2),(ushort)announcement.Length);
        frame[40]=(byte)encoded.Count;
        int offset=ClanSnapshotHeaderBytes;
        clanName.CopyTo(frame.AsSpan(offset));offset+=clanName.Length;
        announcement.CopyTo(frame.AsSpan(offset));offset+=announcement.Length;
        foreach(var item in encoded)
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.Member.CharacterId);
            frame[offset+8]=item.Member.Role;
            frame[offset+9]=item.Member.IsOnline?(byte)1:(byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset+10,2),(ushort)item.Name.Length);
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
        int announcementLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(38,2));
        int count=frame[40];

        if(epoch<=0||count>ClanMaximumMembers||
           clanNameLength>ClanMaxNameBytes||announcementLength>ClanMaxAnnouncementBytes)
            return false;

        if(clanId==0)
        {
            if(revision!=0||leader!=0||clanNameLength!=0||announcementLength!=0||count!=0||
               frame.Length!=ClanSnapshotHeaderBytes)return false;
            value=new(epoch,0,0,0,string.Empty,Array.Empty<ClanMemberWire>(),string.Empty);
            return true;
        }

        if(clanId<0||revision==0||leader<=0||clanNameLength<1||
           frame.Length<ClanSnapshotHeaderBytes+clanNameLength+announcementLength)
            return false;

        try
        {
            int offset=ClanSnapshotHeaderBytes;
            string name=StrictUtf8.GetString(frame.Slice(offset,clanNameLength));
            offset+=clanNameLength;
            string announcement=announcementLength==0?string.Empty:
                StrictUtf8.GetString(frame.Slice(offset,announcementLength));
            offset+=announcementLength;
            var members=new List<ClanMemberWire>(count);
            var ids=new HashSet<long>();
            for(int i=0;i<count;i++)
            {
                if(offset+ClanMemberHeaderBytes>frame.Length)return false;
                long character=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8));
                byte role=frame[offset+8];
                byte online=frame[offset+9];
                int len=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset+10,2));
                if(character<=0||role>1||online>1||len is <1 or >PlayerIdentityMaxNameBytes||
                   offset+ClanMemberHeaderBytes+len>frame.Length||!ids.Add(character))
                    return false;
                string memberName=StrictUtf8.GetString(
                    frame.Slice(offset+ClanMemberHeaderBytes,len));
                members.Add(new(character,role,online!=0,memberName));
                offset+=ClanMemberHeaderBytes+len;
            }
            if(offset!=frame.Length||!ids.Contains(leader)||
               members.Count(x=>x.Role==1)!=1)
                return false;
            value=new(epoch,clanId,revision,leader,name,members,announcement);
            _=EncodeClanSnapshot(value);
            return true;
        }
        catch(DecoderFallbackException){value=null;return false;}
        catch(ArgumentOutOfRangeException){value=null;return false;}
    }

    public static byte[] EncodeClanInvite(ClanInviteWire value)
    {
        byte[] inviterName,clanName;
        try
        {
            inviterName=StrictUtf8.GetBytes(value.InviterName??string.Empty);
            clanName=StrictUtf8.GetBytes(value.ClanName??string.Empty);
        }
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.ReceiverMapEpoch<=0||value.ClanId<=0||value.Revision==0||
           value.InviterActorId<=0||value.InviterConnectionEpoch<=0||
           inviterName.Length is <1 or >PlayerIdentityMaxNameBytes||
           clanName.Length is <1 or >ClanMaxNameBytes||
           string.IsNullOrWhiteSpace(value.InviterName)||
           string.IsNullOrWhiteSpace(value.ClanName))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ClanInvite,
            ClanInviteHeaderBytes+inviterName.Length+clanName.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ClanId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.InviterActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(36,8),value.InviterConnectionEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(44,2),(ushort)inviterName.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(46,2),(ushort)clanName.Length);
        inviterName.CopyTo(frame.AsSpan(ClanInviteHeaderBytes));
        clanName.CopyTo(frame.AsSpan(ClanInviteHeaderBytes+inviterName.Length));
        return frame;
    }

    public static bool TryDecodeClanInvite(ReadOnlySpan<byte> frame,out ClanInviteWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ClanInvite||
           frame.Length<ClanInviteHeaderBytes+2||frame.Length>ClanInviteMaximumFrameBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long clanId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        uint revision=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4));
        long inviter=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(36,8));
        int inviterLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(44,2));
        int clanLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(46,2));
        if(inviterLength is <1 or >PlayerIdentityMaxNameBytes||
           clanLength is <1 or >ClanMaxNameBytes||
           frame.Length!=ClanInviteHeaderBytes+inviterLength+clanLength)
            return false;
        try
        {
            string inviterName=StrictUtf8.GetString(frame.Slice(ClanInviteHeaderBytes,inviterLength));
            string clanName=StrictUtf8.GetString(frame.Slice(ClanInviteHeaderBytes+inviterLength,clanLength));
            value=new(epoch,clanId,revision,inviter,connection,inviterName,clanName);
            _=EncodeClanInvite(value);
            return true;
        }
        catch(DecoderFallbackException){value=default;return false;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }
}
