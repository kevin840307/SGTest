using System.Buffers.Binary;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public const int PetCommandFrameBytes=29;
    public const int PetResultFrameBytes=38;
    public const int PetSnapshotHeaderBytes=26;
    public const int PetSnapshotEntryBytes=24;
    public const int PetSnapshotMaximumPets=64;
    public const int PetSnapshotMaximumFrameBytes=
        PetSnapshotHeaderBytes+PetSnapshotMaximumPets*PetSnapshotEntryBytes;

    public static byte[] EncodePetCommand(PetCommandWire value)
    {
        if(value.MapEpoch<=0||value.PetSequence==0||
           value.Action is <PetCommandAction.Summon or >PetCommandAction.Refresh||
           ((value.Action==PetCommandAction.Refresh)!=(value.PetUid==0))||
           (value.Action!=PetCommandAction.Refresh&&value.PetUid<=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetCommand,PetCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PetSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.PetUid);
        return frame;
    }

    public static bool TryDecodePetCommand(ReadOnlySpan<byte> frame,out PetCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetCommand||
           frame.Length!=PetCommandFrameBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var action=(PetCommandAction)frame[20];
        long uid=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8));
        value=new(epoch,sequence,action,uid);
        try{_=EncodePetCommand(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePetResult(PetResultWire value)
    {
        if(value.MapEpoch<=0||value.PetSequence==0||
           value.Action is <PetCommandAction.Summon or >PetCommandAction.Refresh||
           value.Status is <PetResultStatus.Applied or >PetResultStatus.SequenceConflict||
           value.ActivePetUid<0||
           ((value.Action==PetCommandAction.Refresh)!=(value.PetUid==0))||
           (value.Action!=PetCommandAction.Refresh&&value.PetUid<=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetResult,PetResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PetSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.PetUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(30,8),value.ActivePetUid);
        return frame;
    }

    public static bool TryDecodePetResult(ReadOnlySpan<byte> frame,out PetResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetResult||
           frame.Length!=PetResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PetCommandAction)frame[20],(PetResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(30,8)));
        try{_=EncodePetResult(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePetSnapshot(PetSnapshotWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ActivePetUid<0||value.Pets is null||
           value.Pets.Count>PetSnapshotMaximumPets)
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<long>();
        bool activeFound=value.ActivePetUid==0;
        foreach(var pet in value.Pets)
        {
            if(pet.PetUid<=0||pet.DefinitionId is <=0 or >100000000||
               pet.Level is <=0 or >1000000||
               pet.Experience is <0 or >9000000000000000L||
               !ids.Add(pet.PetUid))
                throw new ArgumentOutOfRangeException(nameof(value));
            if(pet.PetUid==value.ActivePetUid)activeFound=true;
        }
        if(!activeFound)throw new ArgumentOutOfRangeException(nameof(value));
        int bytes=checked(PetSnapshotHeaderBytes+value.Pets.Count*PetSnapshotEntryBytes);
        var frame=NewFrame(WireOpcode.PetSnapshot,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ActivePetUid);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(24,2),(ushort)value.Pets.Count);
        int offset=PetSnapshotHeaderBytes;
        foreach(var pet in value.Pets)
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),pet.PetUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),pet.DefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),pet.Level);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),pet.Experience);
            offset+=PetSnapshotEntryBytes;
        }
        return frame;
    }

    public static bool TryDecodePetSnapshot(ReadOnlySpan<byte> frame,out PetSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetSnapshot||
           frame.Length<PetSnapshotHeaderBytes||frame.Length>PetSnapshotMaximumFrameBytes||
           (frame.Length-PetSnapshotHeaderBytes)%PetSnapshotEntryBytes!=0)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long active=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(24,2));
        if(epoch<=0||active<0||count>PetSnapshotMaximumPets||
           frame.Length!=PetSnapshotHeaderBytes+count*PetSnapshotEntryBytes)
            return false;
        var pets=new List<PetSnapshotEntryWire>(count);
        int offset=PetSnapshotHeaderBytes;
        for(int i=0;i<count;i++)
        {
            pets.Add(new(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8))));
            offset+=PetSnapshotEntryBytes;
        }
        value=new(epoch,active,pets);
        try{_=EncodePetSnapshot(value);return true;}
        catch(ArgumentOutOfRangeException){value=null;return false;}
    }
}
