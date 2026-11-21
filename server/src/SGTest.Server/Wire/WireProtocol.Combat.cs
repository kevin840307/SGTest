using System.Buffers.Binary;
using System.Text;
using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Wire;

/// <summary>Wire V1 combat, skill and player damage/respawn encoding.</summary>
public static partial class WireProtocol
{
    public static byte[] EncodeNormalAttack(NormalAttackWire value)
    {
        if (value.MapEpoch <= 0 || value.AttackSequence == 0 || value.TargetEntityId <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.NormalAttack, NormalAttackFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20, 8), value.TargetEntityId);
        return frame;
    }

    public static bool TryDecodeNormalAttack(ReadOnlySpan<byte> frame, out NormalAttackWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.NormalAttack)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20, 8)));
        return value.MapEpoch > 0 && value.AttackSequence != 0 && value.TargetEntityId > 0;
    }

    public static byte[] EncodeNormalAttackResult(NormalAttackResultWire value)
    {
        if (!ValidNormalAttackResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.NormalAttackResult, NormalAttackResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.AttackSequence);
        frame[20] = (byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29, 8), value.TargetEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37, 4), value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(45, 4), value.MaxHp);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(49, 8), value.TargetRevision);
        return frame;
    }

    public static bool TryDecodeNormalAttackResult(
        ReadOnlySpan<byte> frame, out NormalAttackResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.NormalAttackResult)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            (NormalAttackStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(45, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(49, 8)));
        return ValidNormalAttackResult(value);
    }

    public static byte[] EncodeSkillUse(SkillUseWire value)
    {
        if(value.MapEpoch<=0 || value.SkillSequence==0 ||
            value.SkillId is <1 or >100_000_000 || value.TargetEntityId<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.SkillUse,SkillUseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.TargetEntityId);
        return frame;
    }

    public static bool TryDecodeSkillUse(
        ReadOnlySpan<byte> frame,out SkillUseWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.SkillUse)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0 && value.SkillSequence>0 &&
            value.SkillId is >0 and <=100_000_000 && value.TargetEntityId>=0;
    }

    public static byte[] EncodeSkillUseResult(SkillUseResultWire value)
    {
        if(!ValidSkillUseResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.SkillUseResult,SkillUseResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ServerTick);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(33,8),value.TargetEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(45,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(49,4),value.MaxHp);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(53,8),value.TargetRevision);
        return frame;
    }

    public static bool TryDecodeSkillUseResult(
        ReadOnlySpan<byte> frame,out SkillUseResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.SkillUseResult)return false;
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
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(53,8)));
        return ValidSkillUseResult(value);
    }

    public static byte[] EncodePlayerSkillVisual(PlayerSkillVisualWire value)
    {
        if(!ValidPlayerSkillVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerSkillVisual,PlayerSkillVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.TargetEntityId);
        frame[56]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(57,4),value.Damage);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(61,8),value.TargetRevision);
        frame[69]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerSkillVisual(
        ReadOnlySpan<byte> frame,out PlayerSkillVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerSkillVisual)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)),
            (SkillUseStatus)frame[56],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(57,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(61,8)),
            frame[69]);
        return ValidPlayerSkillVisual(value);
    }

    public static byte[] EncodePlayerAttackVisual(PlayerAttackVisualWire value)
    {
        if(!ValidPlayerAttackVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerAttackVisual,PlayerAttackVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(44,8),value.TargetEntityId);
        frame[52]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(53,4),value.Damage);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(57,8),value.TargetRevision);
        frame[65]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerAttackVisual(
        ReadOnlySpan<byte> frame,out PlayerAttackVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerAttackVisual)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(44,8)),
            (NormalAttackStatus)frame[52],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(53,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(57,8)),
            frame[65]);
        return ValidPlayerAttackVisual(value);
    }

    public static byte[] EncodePlayerDamageVisual(PlayerDamageVisualWire value)
    {
        if(!ValidPlayerDamageVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerDamageVisual,PlayerDamageVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.VictimActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.VictimConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.DamageSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(44,8),value.AttackerMonsterEntityId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52,8),value.AttackerMonsterRevision);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60,4),value.Damage);
        frame[64]=value.Died?(byte)1:(byte)0;
        frame[65]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerDamageVisual(
        ReadOnlySpan<byte> frame,out PlayerDamageVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerDamageVisual)
            return false;
        byte died=frame[64];
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(44,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60,4)),
            died==1,frame[65]);
        return died<=1 && ValidPlayerDamageVisual(value);
    }

    public static byte[] EncodePlayerRespawnVisual(PlayerRespawnVisualWire value)
    {
        if(!ValidPlayerRespawnVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerRespawnVisual,PlayerRespawnVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.ConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.RespawnSequence);
        frame[44]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerRespawnVisual(
        ReadOnlySpan<byte> frame,out PlayerRespawnVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerRespawnVisual)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            frame[44]);
        return ValidPlayerRespawnVisual(value);
    }

    public static byte[] EncodePlayerCombatState(PlayerCombatStateWire value)
    {
        if (!ValidPlayerCombatState(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerCombatState, PlayerCombatStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), value.MaxHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24, 4), value.CurrentMp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28, 4), value.MaxMp);
        return frame;
    }

    public static bool TryDecodePlayerCombatState(
        ReadOnlySpan<byte> frame, out PlayerCombatStateWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerCombatState)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28, 4)));
        return ValidPlayerCombatState(value);
    }

    public static byte[] EncodePlayerDamage(PlayerDamageWire value)
    {
        if (!ValidPlayerDamage(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerDamage, PlayerDamageFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.DamageSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28, 8), value.AttackerMonsterEntityId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(36, 8), value.AttackerMonsterRevision);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44, 4), value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(52, 4), value.MaxHp);
        frame[56] = value.Died ? (byte)1 : (byte)0;
        return frame;
    }

    public static bool TryDecodePlayerDamage(
        ReadOnlySpan<byte> frame, out PlayerDamageWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerDamage)
            return false;
        byte died = frame[56];
        if (died > 1) return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(36, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(52, 4)),
            died != 0);
        return ValidPlayerDamage(value);
    }

    public static byte[] EncodePlayerRespawn(PlayerRespawnWire value)
    {
        if (value.MapEpoch <= 0 || value.RespawnSequence == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerRespawn, PlayerRespawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.RespawnSequence);
        return frame;
    }

    public static bool TryDecodePlayerRespawn(
        ReadOnlySpan<byte> frame, out PlayerRespawnWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerRespawn)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)));
        return value.MapEpoch > 0 && value.RespawnSequence != 0;
    }

    public static byte[] EncodePlayerRespawnResult(PlayerRespawnResultWire value)
    {
        if (!ValidPlayerRespawnResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerRespawnResult, PlayerRespawnResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.RespawnSequence);
        frame[20] = (byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(25, 4), value.MaxHp);
        return frame;
    }

    public static bool TryDecodePlayerRespawnResult(
        ReadOnlySpan<byte> frame, out PlayerRespawnResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerRespawnResult)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            (PlayerRespawnStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(25, 4)));
        return ValidPlayerRespawnResult(value);
    }

}
