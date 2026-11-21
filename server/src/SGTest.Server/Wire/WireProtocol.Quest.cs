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

/// <summary>Quest command and view codec.</summary>
public static partial class WireProtocol
{

    public static byte[] EncodeQuestOpen(QuestOpenWire value)
    {
        if(value.MapEpoch<=0||value.QuestSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.QuestOpen,QuestOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.QuestSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeQuestOpen(
        ReadOnlySpan<byte> frame,out QuestOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.QuestOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.QuestSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeQuestChoose(QuestChooseWire value)
    {
        if(value.MapEpoch<=0||value.QuestSequence==0||value.NpcSpawnId<=0||
           value.QuestId is <=0 or >100000000||
           value.ChoiceId is <=0 or >100000000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.QuestChoose,QuestChooseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.QuestSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24,4),value.QuestId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.ChoiceId);
        return frame;
    }

    public static bool TryDecodeQuestChoose(
        ReadOnlySpan<byte> frame,out QuestChooseWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.QuestChoose)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        return value.MapEpoch>0&&value.QuestSequence>0&&value.NpcSpawnId>0&&
            value.QuestId is >0 and <=100000000&&
            value.ChoiceId is >0 and <=100000000;
    }

    public static byte[] EncodeQuestView(QuestViewWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var encoded=EncodeQuestViewStrings(value);
        int frameBytes=checked(QuestViewHeaderBytes+
            encoded.Title.Length+encoded.Text.Length+
            encoded.Choices.Sum(x=>6+x.Label.Length));
        if(frameBytes>QuestViewMaximumFrameBytes||
           !ValidQuestView(value,encoded.Title,encoded.Text,encoded.Choices))
            throw new ArgumentOutOfRangeException(nameof(value));

        var frame=NewFrame(WireOpcode.QuestView,frameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.QuestSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(25,4),value.QuestId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.RuleRevision);
        frame[33]=(byte)value.State;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(34,4),value.Stage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(38,4),value.ProgressRevision);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(42,2),(ushort)encoded.Title.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(44,2),(ushort)encoded.Text.Length);
        frame[46]=(byte)encoded.Choices.Count;
        int offset=QuestViewHeaderBytes;
        encoded.Title.AsSpan().CopyTo(frame.AsSpan(offset)); offset+=encoded.Title.Length;
        encoded.Text.AsSpan().CopyTo(frame.AsSpan(offset)); offset+=encoded.Text.Length;
        foreach(var choice in encoded.Choices)
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset,4),choice.Id);
            BinaryPrimitives.WriteUInt16LittleEndian(
                frame.AsSpan(offset+4,2),(ushort)choice.Label.Length);
            offset+=6;
            choice.Label.AsSpan().CopyTo(frame.AsSpan(offset));
            offset+=choice.Label.Length;
        }
        return frame;
    }

    public static bool TryDecodeQuestView(
        ReadOnlySpan<byte> frame,out QuestViewWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.QuestView||
           frame.Length<QuestViewHeaderBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var status=(QuestViewWireStatus)frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        int quest=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(25,4));
        int rule=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4));
        var state=(QuestProgressState)frame[33];
        int stage=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(34,4));
        int progress=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(38,4));
        int titleLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(42,2));
        int textLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(44,2));
        int count=frame[46];
        if(titleLength>QuestViewMaxTitleBytes||textLength>QuestViewMaxTextBytes||
           count>QuestViewMaxChoices)
            return false;
        int offset=QuestViewHeaderBytes;
        if(offset+titleLength+textLength>frame.Length)return false;
        try
        {
            string title=StrictUtf8.GetString(frame.Slice(offset,titleLength));
            offset+=titleLength;
            string text=StrictUtf8.GetString(frame.Slice(offset,textLength));
            offset+=textLength;
            var choices=new List<QuestViewChoiceWire>(count);
            var seen=new HashSet<int>();
            for(int i=0;i<count;i++)
            {
                if(offset+6>frame.Length)return false;
                int id=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset,4));
                int labelLength=BinaryPrimitives.ReadUInt16LittleEndian(
                    frame.Slice(offset+4,2));
                offset+=6;
                if(id<=0||labelLength<1||labelLength>QuestViewMaxLabelBytes||
                   offset+labelLength>frame.Length||!seen.Add(id))
                    return false;
                string label=StrictUtf8.GetString(frame.Slice(offset,labelLength));
                offset+=labelLength;
                choices.Add(new(id,label));
            }
            if(offset!=frame.Length)return false;
            var candidate=new QuestViewWire(
                epoch,sequence,status,npc,quest,rule,state,stage,progress,
                title,text,choices);
            var encoded=EncodeQuestViewStrings(candidate);
            if(!ValidQuestView(candidate,encoded.Title,encoded.Text,encoded.Choices))
                return false;
            value=candidate;
            return true;
        }
        catch(DecoderFallbackException)
        {
            return false;
        }
    }

    private static (
        byte[] Title,byte[] Text,
        IReadOnlyList<(int Id,byte[] Label)> Choices)
        EncodeQuestViewStrings(QuestViewWire value)
    {
        byte[] title=StrictUtf8.GetBytes(value.Title??string.Empty);
        byte[] text=StrictUtf8.GetBytes(value.Text??string.Empty);
        var choices=new List<(int Id,byte[] Label)>(value.Choices?.Count??0);
        if(value.Choices is not null)
            foreach(var choice in value.Choices)
                choices.Add((choice.ChoiceId,
                    StrictUtf8.GetBytes(choice.Label??string.Empty)));
        return(title,text,choices);
    }

    private static bool ValidQuestView(
        QuestViewWire value,byte[] title,byte[] text,
        IReadOnlyList<(int Id,byte[] Label)> choices)
    {
        if(value.MapEpoch<=0||value.QuestSequence==0||value.NpcSpawnId<=0||
           (byte)value.Status>(byte)QuestViewWireStatus.RuleRevisionMismatch)
            return false;
        bool hasView=value.Status is
            QuestViewWireStatus.Ready or QuestViewWireStatus.Applied or
            QuestViewWireStatus.AlreadyApplied or QuestViewWireStatus.InvalidChoice or
            QuestViewWireStatus.AlreadyCompleted;
        if(!hasView)
            return value.QuestId==0&&value.RuleRevision==0&&
                value.State==QuestProgressState.NotStarted&&value.Stage==0&&
                value.ProgressRevision==0&&title.Length==0&&text.Length==0&&
                choices.Count==0;
        if(value.QuestId is <=0 or >100000000||value.RuleRevision<=0||
           value.Stage is <0 or >1000000||value.ProgressRevision<0||
           (byte)value.State>(byte)QuestProgressState.Completed||
           title.Length is <1 or >QuestViewMaxTitleBytes||
           text.Length is <1 or >QuestViewMaxTextBytes||
           choices.Count>QuestViewMaxChoices)
            return false;
        if(value.State==QuestProgressState.NotStarted&&value.ProgressRevision!=0)
            return false;
        if(value.State!=QuestProgressState.NotStarted&&value.ProgressRevision<=0)
            return false;
        if(value.State==QuestProgressState.Completed&&choices.Count!=0)
            return false;
        var seen=new HashSet<int>();
        foreach(var choice in choices)
            if(choice.Id is <=0 or >100000000||
               choice.Label.Length is <1 or >QuestViewMaxLabelBytes||
               !seen.Add(choice.Id))
                return false;
        return true;
    }

    private static float ReadFloat(ReadOnlySpan<byte> source) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));
}
