using System.Buffers.Binary;
using SGTest.Server.Monster;
using SGTest.Server.Skill;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public const int PetCommandFrameBytes=29;
    public const int PetResultFrameBytes=38;
    public const int PetSnapshotHeaderBytes=26;
    public const int PetSnapshotEntryBytes=28;
    public const int PetSnapshotMaximumPets=64;
    public const int PetSnapshotMaximumFrameBytes=
        PetSnapshotHeaderBytes+PetSnapshotMaximumPets*PetSnapshotEntryBytes;
    public const int PetEntitySpawnFrameBytes=76;
    public const int PetEntityDespawnFrameBytes=56;
    public const int PetEntityDeltaFrameBytes=64;
    public const int PetAttackFrameBytes=36;
    public const int PetAttackResultFrameBytes=65;
    public const int PetAttackVisualFrameBytes=81;
    public const int PetSkillFrameBytes=40;
    public const int PetSkillResultFrameBytes=69;
    public const int PetSkillVisualFrameBytes=85;

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
               pet.EvolutionId is <0 or >65535||pet.Level is <=0 or >1000000||
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
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),pet.EvolutionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+16,4),pet.Level);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+20,8),pet.Experience);
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
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+16,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+20,8))));
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
           value.EvolutionId is <0 or >65535||value.Level is <=0 or >1000000||
           !float.IsFinite(value.X)||!float.IsFinite(value.Y)||
           value.X<0||value.Y<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetEntitySpawn,PetEntitySpawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.PetUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(56,4),value.DefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60,4),value.EvolutionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(64,4),value.Level);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(68,4),value.X);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(72,4),value.Y);
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
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(64,4)),
            BinaryPrimitives.ReadSingleLittleEndian(frame.Slice(68,4)),
            BinaryPrimitives.ReadSingleLittleEndian(frame.Slice(72,4)));
        try{_=EncodePetEntitySpawn(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePetEntityDelta(PetEntityDeltaWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.OwnerActorId<=0||value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0||
           value.PetUid<=0||!float.IsFinite(value.X)||!float.IsFinite(value.Y)||
           value.X<0||value.Y<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetEntityDelta,PetEntityDeltaFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.PetUid);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(56,4),value.X);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(60,4),value.Y);
        return frame;
    }

    public static bool TryDecodePetEntityDelta(ReadOnlySpan<byte> frame,out PetEntityDeltaWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetEntityDelta||
           frame.Length!=PetEntityDeltaFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)),
            BinaryPrimitives.ReadSingleLittleEndian(frame.Slice(56,4)),
            BinaryPrimitives.ReadSingleLittleEndian(frame.Slice(60,4)));
        try{_=EncodePetEntityDelta(value);return true;}
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

    public static byte[] EncodePetAttack(PetAttackWire value)
    {
        if(value.MapEpoch<=0||value.AttackSequence==0||
           value.PetUid<=0||value.TargetEntityId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetAttack,PetAttackFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.PetUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.TargetEntityId);
        return frame;
    }

    public static bool TryDecodePetAttack(ReadOnlySpan<byte> frame,out PetAttackWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetAttack||
           frame.Length!=PetAttackFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8)));
        try{_=EncodePetAttack(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePetAttackResult(PetAttackResultWire value)
    {
        if(!ValidPetAttackResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetAttackResult,PetAttackResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.AttackSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.PetUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(45,4),value.MaxHp);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(49,8),value.TargetRevision);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(57,8),value.ServerTick);
        return frame;
    }

    public static bool TryDecodePetAttackResult(
        ReadOnlySpan<byte> frame,out PetAttackResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetAttackResult||
           frame.Length!=PetAttackResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (NormalAttackStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(45,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(49,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(57,8)));
        return ValidPetAttackResult(value);
    }

    public static byte[] EncodePetAttackVisual(PetAttackVisualWire value)
    {
        if(!ValidPetAttackVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetAttackVisual,PetAttackVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52,8),value.PetUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(60,8),value.TargetEntityId);
        frame[68]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(69,4),value.Damage);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(73,8),value.TargetRevision);
        return frame;
    }

    public static bool TryDecodePetAttackVisual(
        ReadOnlySpan<byte> frame,out PetAttackVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetAttackVisual||
           frame.Length!=PetAttackVisualFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(48,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(60,8)),
            (NormalAttackStatus)frame[68],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(69,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(73,8)));
        return ValidPetAttackVisual(value);
    }

    public static byte[] EncodePetSkill(PetSkillWire value)
    {
        if(value.MapEpoch<=0||value.SkillSequence==0||value.PetUid<=0||
           value.SkillId<=0||value.TargetEntityId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetSkill,PetSkillFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.PetUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.TargetEntityId);
        return frame;
    }

    public static bool TryDecodePetSkill(ReadOnlySpan<byte> frame,out PetSkillWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetSkill||
           frame.Length!=PetSkillFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)));
        try{_=EncodePetSkill(value);return true;}
        catch(ArgumentOutOfRangeException){value=default;return false;}
    }

    public static byte[] EncodePetSkillResult(PetSkillResultWire value)
    {
        if(!ValidPetSkillResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetSkillResult,PetSkillResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.PetUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(33,8),value.TargetEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(45,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(49,4),value.MaxHp);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(53,8),value.TargetRevision);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(61,8),value.ServerTick);
        return frame;
    }

    public static bool TryDecodePetSkillResult(
        ReadOnlySpan<byte> frame,out PetSkillResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetSkillResult||
           frame.Length!=PetSkillResultFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (SkillUseStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(33,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(45,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(49,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(53,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(61,8)));
        return ValidPetSkillResult(value);
    }

    public static byte[] EncodePetSkillVisual(PetSkillVisualWire value)
    {
        if(!ValidPetSkillVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PetSkillVisual,PetSkillVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48,4),value.SkillSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52,8),value.PetUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(64,8),value.TargetEntityId);
        frame[72]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(73,4),value.Damage);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(77,8),value.TargetRevision);
        return frame;
    }

    public static bool TryDecodePetSkillVisual(
        ReadOnlySpan<byte> frame,out PetSkillVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||header.Opcode!=WireOpcode.PetSkillVisual||
           frame.Length!=PetSkillVisualFrameBytes)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(48,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(64,8)),
            (SkillUseStatus)frame[72],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(73,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(77,8)));
        return ValidPetSkillVisual(value);
    }

    private static bool ValidPetSkillVisual(PetSkillVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||value.OwnerActorId<=0||
           value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0||
           value.SkillSequence==0||value.PetUid<=0||value.SkillId<=0||
           value.TargetEntityId<=0||value.TargetRevision<=0||
           value.Status is not (SkillUseStatus.Applied or SkillUseStatus.Miss))
            return false;
        return value.Status==SkillUseStatus.Applied?value.Damage>0:value.Damage==0;
    }

    private static bool ValidPetSkillResult(PetSkillResultWire value)
    {
        if(value.MapEpoch<=0||value.SkillSequence==0||value.PetUid<=0||
           value.SkillId<=0||value.TargetEntityId<=0||value.ServerTick<0||
           (byte)value.Status>(byte)SkillUseStatus.Backpressured||
           value.Damage<0||value.CurrentHp<0||value.MaxHp<0||value.TargetRevision<0)
            return false;
        if(value.MaxHp>0&&value.CurrentHp>value.MaxHp)return false;
        if(value.Status==SkillUseStatus.Applied)
            return value.Damage>0&&value.MaxHp>0&&value.TargetRevision>0;
        if(value.Status==SkillUseStatus.Miss)
            return value.Damage==0&&value.MaxHp>0&&value.TargetRevision>0;
        return value.Damage==0;
    }

    private static bool ValidPetAttackVisual(PetAttackVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||value.OwnerActorId<=0||
           value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0||
           value.AttackSequence==0||value.PetUid<=0||value.TargetEntityId<=0||
           value.TargetRevision<=0||
           value.Status is not (NormalAttackStatus.Applied or NormalAttackStatus.Miss))
            return false;
        return value.Status==NormalAttackStatus.Applied?value.Damage>0:value.Damage==0;
    }

    private static bool ValidPetAttackResult(PetAttackResultWire value)
    {
        if(value.MapEpoch<=0||value.AttackSequence==0||
           (byte)value.Status>(byte)NormalAttackStatus.ReplayedSequence||
           value.PetUid<=0||value.TargetEntityId<=0||value.ServerTick<0||
           value.Damage<0||value.CurrentHp<0||value.MaxHp<0||value.TargetRevision<0)
            return false;
        if(value.MaxHp>0&&value.CurrentHp>value.MaxHp)return false;
        if(value.Status==NormalAttackStatus.Applied)
            return value.Damage>0&&value.MaxHp>0&&value.TargetRevision>0;
        if(value.Status==NormalAttackStatus.Miss)
            return value.Damage==0&&value.MaxHp>0&&value.TargetRevision>0;
        return value.Damage==0;
    }
}
