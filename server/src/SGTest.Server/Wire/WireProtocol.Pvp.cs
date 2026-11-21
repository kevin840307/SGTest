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

/// <summary>PvP policy-facing binary messages, separate from player presentation colors.</summary>
public static partial class WireProtocol
{

    public static byte[] EncodePvpModeCommand(PvpModeCommandWire value)
    {
        if(value.MapEpoch<=0||value.PvpSequence==0||(byte)value.Action>2)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpModeCommand,PvpModeCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PvpSequence);
        frame[20]=(byte)value.Action;
        return frame;
    }

    public static bool TryDecodePvpModeCommand(ReadOnlySpan<byte> frame,out PvpModeCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpModeCommand)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PvpModeCommandAction)frame[20]);
        return value.MapEpoch>0&&value.PvpSequence>0&&(byte)value.Action<=2;
    }

    public static byte[] EncodePvpModeResult(PvpModeResultWire value)
    {
        if(value.MapEpoch<=0||value.PvpSequence==0||(byte)value.Status>6||
           (byte)value.Mode>1)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpModeResult,PvpModeResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PvpSequence);
        frame[20]=(byte)value.Status; frame[21]=(byte)value.Mode;
        frame[22]=value.MapAllowsPlayerPvp?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpModeResult(ReadOnlySpan<byte> frame,out PvpModeResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpModeResult)return false;
        byte allowed=frame[22];
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PvpModeResultStatus)frame[20],(PvpMode)frame[21],allowed==1);
        return value.MapEpoch>0&&value.PvpSequence>0&&allowed<=1&&
            (byte)value.Status<=6&&(byte)value.Mode<=1;
    }

    public static byte[] EncodePvpAttack(PvpAttackWire value)
    {
        if(value.MapEpoch<=0||value.AttackSequence==0||value.TargetActorId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpAttack,PvpAttackFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.TargetActorId);
        return frame;
    }

    public static bool TryDecodePvpAttack(ReadOnlySpan<byte> frame,out PvpAttackWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpAttack)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0&&value.AttackSequence>0&&value.TargetActorId>0;
    }

    public static byte[] EncodePvpAttackResult(PvpAttackResultWire value)
    {
        if(value.MapEpoch<=0||value.AttackSequence==0||(byte)value.Status>14||
           value.ServerTick<0||value.TargetActorId<=0||value.Damage<0||
           (value.Status==PvpAttackStatus.Applied?value.Damage<=0:value.Damage!=0||value.Died))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpAttackResult,PvpAttackResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.AttackSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetActorId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.Damage);
        frame[41]=value.Died?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpAttackResult(ReadOnlySpan<byte> frame,out PvpAttackResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpAttackResult)return false;
        byte died=frame[41];
        if(died>1)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PvpAttackStatus)frame[20],BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),died==1);
        return value.MapEpoch>0&&value.AttackSequence>0&&(byte)value.Status<=14&&
            value.ServerTick>=0&&value.TargetActorId>0&&value.Damage>=0&&
            (value.Status==PvpAttackStatus.Applied?value.Damage>0:value.Damage==0&&!value.Died);
    }

    public static byte[] EncodePvpDamage(PvpDamageWire value)
    {
        if(value.MapEpoch<=0||value.DamageSequence==0||value.ServerTick<0||
           value.AttackerActorId<=0||value.AttackerConnectionEpoch<=0||
           value.AttackerAttackSequence==0||value.Damage<=0||
           value.CurrentHp<0||value.MaxHp<=0||value.CurrentHp>value.MaxHp||
           (value.Died?value.CurrentHp!=0:value.CurrentHp<=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpDamage,PvpDamageFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.DamageSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(36,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(44,4),value.AttackerAttackSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48,4),value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(52,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(56,4),value.MaxHp);
        frame[60]=value.Died?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpDamage(ReadOnlySpan<byte> frame,out PvpDamageWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpDamage)return false;
        byte died=frame[60];
        if(died>1)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(36,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(52,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(56,4)),died==1);
        return value.MapEpoch>0&&value.DamageSequence>0&&value.ServerTick>=0&&
            value.AttackerActorId>0&&value.AttackerConnectionEpoch>0&&
            value.AttackerAttackSequence>0&&value.Damage>0&&
            value.CurrentHp>=0&&value.MaxHp>0&&value.CurrentHp<=value.MaxHp&&
            (value.Died?value.CurrentHp==0:value.CurrentHp>0);
    }

    public static byte[] EncodePvpCombatVisual(PvpCombatVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.AttackerActorId<=0||value.AttackerConnectionEpoch<=0||
           value.AttackSequence==0||value.VictimActorId<=0||
           value.VictimConnectionEpoch<=0||value.DamageSequence==0||
           value.Damage<=0||value.AttackerActorId==value.VictimActorId||
           value.AttackerFacing is <1 or >8||value.VictimFacing is <1 or >8)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpCombatVisual,PvpCombatVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(44,8),value.VictimActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52,8),value.VictimConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(60,4),value.DamageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(64,4),value.Damage);
        frame[68]=value.Died?(byte)1:(byte)0;
        frame[69]=value.AttackerFacing;
        frame[70]=value.VictimFacing;
        return frame;
    }

    public static bool TryDecodePvpCombatVisual(
        ReadOnlySpan<byte> frame,out PvpCombatVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpCombatVisual)
            return false;
        byte died=frame[68];
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(44,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(60,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(64,4)),
            died==1,frame[69],frame[70]);
        return died<=1&&value.ReceiverMapEpoch>0&&value.ServerTick>=0&&
            value.AttackerActorId>0&&value.AttackerConnectionEpoch>0&&
            value.AttackSequence>0&&value.VictimActorId>0&&
            value.VictimConnectionEpoch>0&&value.DamageSequence>0&&
            value.Damage>0&&value.AttackerActorId!=value.VictimActorId&&
            value.AttackerFacing is >=1 and <=8&&value.VictimFacing is >=1 and <=8;
    }

    public static byte[] EncodePvpSkill(PvpSkillWire value)
    {
        if(value.MapEpoch<=0||value.SkillSequence==0||
           value.SkillId is <1 or >100_000_000||value.TargetActorId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpSkill,PvpSkillFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.TargetActorId);
        return frame;
    }

    public static bool TryDecodePvpSkill(ReadOnlySpan<byte> frame,out PvpSkillWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpSkill)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.SkillSequence>0&&
            value.SkillId is >0 and <=100_000_000&&value.TargetActorId>0;
    }

    public static byte[] EncodePvpSkillResult(PvpSkillResultWire value)
    {
        if(value.MapEpoch<=0||value.SkillSequence==0||
           (byte)value.Status>(byte)SkillUseStatus.Backpressured||
           value.ServerTick<0||value.SkillId is <1 or >100_000_000||
           value.TargetActorId<=0||value.Damage<0||
           (value.Status==SkillUseStatus.Applied
                ? value.Damage<=0
                : value.Damage!=0||value.Died))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpSkillResult,PvpSkillResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ServerTick);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(33,8),value.TargetActorId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Damage);
        frame[45]=value.Died?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpSkillResult(
        ReadOnlySpan<byte> frame,out PvpSkillResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpSkillResult)return false;
        byte died=frame[45];
        if(died>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (SkillUseStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(33,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)),died==1);
        return value.MapEpoch>0&&value.SkillSequence>0&&
            (byte)value.Status<=(byte)SkillUseStatus.Backpressured&&
            value.ServerTick>=0&&value.SkillId is >0 and <=100_000_000&&
            value.TargetActorId>0&&value.Damage>=0&&
            (value.Status==SkillUseStatus.Applied
                ? value.Damage>0
                : value.Damage==0&&!value.Died);
    }

    public static byte[] EncodePvpSkillVisual(PvpSkillVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.AttackerActorId<=0||value.AttackerConnectionEpoch<=0||
           value.SkillSequence==0||value.SkillId is <1 or >100_000_000||
           value.VictimActorId<=0||value.VictimConnectionEpoch<=0||
           value.DamageSequence==0||value.Damage<=0||
           value.AttackerActorId==value.VictimActorId||
           value.AttackerFacing is <1 or >8||value.VictimFacing is <1 or >8)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpSkillVisual,PvpSkillVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.VictimActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(56,8),value.VictimConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(64,4),value.DamageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(68,4),value.Damage);
        frame[72]=value.Died?(byte)1:(byte)0;
        frame[73]=value.AttackerFacing;
        frame[74]=value.VictimFacing;
        return frame;
    }

    public static bool TryDecodePvpSkillVisual(
        ReadOnlySpan<byte> frame,out PvpSkillVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpSkillVisual)return false;
        byte died=frame[72];
        if(died>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(56,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(64,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(68,4)),
            died==1,frame[73],frame[74]);
        return value.ReceiverMapEpoch>0&&value.ServerTick>=0&&
            value.AttackerActorId>0&&value.AttackerConnectionEpoch>0&&
            value.SkillSequence>0&&value.SkillId is >0 and <=100_000_000&&
            value.VictimActorId>0&&value.VictimConnectionEpoch>0&&
            value.DamageSequence>0&&value.Damage>0&&
            value.AttackerActorId!=value.VictimActorId&&
            value.AttackerFacing is >=1 and <=8&&value.VictimFacing is >=1 and <=8;
    }

    public static byte[] EncodeGmCapability(GmCapabilityWire value)
    {
        var frame=NewFrame(WireOpcode.GmCapability,GmCapabilityFrameBytes);
        frame[8]=value.Authorized?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodeGmCapability(
        ReadOnlySpan<byte> frame,out GmCapabilityWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GmCapability||
           frame[8]>1)
            return false;
        value=new(frame[8]==1);
        return true;
    }

    public static byte[] EncodeGmPvpNameCommand(GmPvpNameCommandWire value)
    {
        if(value.MapEpoch<=0||value.GmSequence==0||value.TargetActorId<=0||
           (byte)value.State>4)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GmPvpNameCommand,GmPvpNameCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.GmSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.TargetActorId);
        frame[28]=(byte)value.State;
        return frame;
    }

    public static bool TryDecodeGmPvpNameCommand(
        ReadOnlySpan<byte> frame,out GmPvpNameCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GmPvpNameCommand)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            (PvpNameState)frame[28]);
        return value.MapEpoch>0&&value.GmSequence>0&&
            value.TargetActorId>0&&(byte)value.State<=4;
    }

    public static byte[] EncodeGmPvpNameResult(GmPvpNameResultWire value)
    {
        if(value.MapEpoch<=0||value.GmSequence==0||
           (byte)value.Status>5||value.TargetActorId<=0||(byte)value.State>4)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GmPvpNameResult,GmPvpNameResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.GmSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.TargetActorId);
        frame[29]=(byte)value.State;
        return frame;
    }

    public static bool TryDecodeGmPvpNameResult(
        ReadOnlySpan<byte> frame,out GmPvpNameResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GmPvpNameResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (GmPvpNameResultStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            (PvpNameState)frame[29]);
        return value.MapEpoch>0&&value.GmSequence>0&&
            (byte)value.Status<=5&&value.TargetActorId>0&&(byte)value.State<=4;
    }
}
