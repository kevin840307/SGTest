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
    public const int PetEntitySpawnFrameBytes=64;
    public const int PetEntityDespawnFrameBytes=56;

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

    public static byte[] EncodePetEntitySpawn(PetEntitySpawnWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.OwnerActorId<=0||value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0||
           value.PetUid<=0||value.DefinitionId is <=0 or >100000000||
           value.Level is <=0 or >1000000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetEntitySpawn,PetEntitySpawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.PetUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(56,4),value.DefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60,4),value.Level);
        return frame;
    }

    public static bool TryDecodePetEntitySpawn(ReadOnlySpan<byte> frame,out PetEntitySpawnWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetEntitySpawn||
           frame.Length!=PetEntitySpawnFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(56,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60,4)));
        try{_=EncodePetEntitySpawn(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePetEntityDespawn(PetEntityDespawnWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.OwnerActorId<=0||value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0||
           value.PetUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetEntityDespawn,PetEntityDespawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.PetUid);
        return frame;
    }

    public static bool TryDecodePetEntityDespawn(ReadOnlySpan<byte> frame,out PetEntityDespawnWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetEntityDespawn||
           frame.Length!=PetEntityDespawnFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)));
        try{_=EncodePetEntityDespawn(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }
}
