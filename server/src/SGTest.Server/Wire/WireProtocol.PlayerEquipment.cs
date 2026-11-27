using System.Buffers.Binary;

namespace SGTest.Server.Wire;

public static partial class WireProtocol
{
    public const int PlayerEquipmentPresentationFrameBytes=89;

    public static byte[] EncodePlayerEquipmentPresentation(
        PlayerEquipmentPresentationWire value)
    {
        if(!ValidPlayerEquipmentPresentation(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(
            WireOpcode.PlayerEquipmentPresentation,
            PlayerEquipmentPresentationFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.OwnerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.OwnerConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.OwnerMapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48,4),value.Revision);
        frame[52]=value.Present?(byte)1:(byte)0;
        Span<int> definitions=stackalloc int[]
        {
            value.HeadDefinitionId,value.LeftHandDefinitionId,value.RightHandDefinitionId,
            value.BodyDefinitionId,value.LegDefinitionId,value.WeaponDefinitionId,
            value.ShieldDefinitionId,value.BackDefinitionId,value.CharmDefinitionId
        };
        int offset=53;
        foreach(int definitionId in definitions)
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset,4),definitionId);
            offset+=4;
        }
        return frame;
    }

    public static bool TryDecodePlayerEquipmentPresentation(
        ReadOnlySpan<byte> frame,out PlayerEquipmentPresentationWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||
           header.Opcode!=WireOpcode.PlayerEquipmentPresentation||
           frame.Length!=PlayerEquipmentPresentationFrameBytes)
            return false;
        byte present=frame[52];
        if(present>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(48,4)),
            present==1,
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(53,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(57,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(61,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(65,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(69,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(73,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(77,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(81,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(85,4)));
        return ValidPlayerEquipmentPresentation(value);
    }

    private static bool ValidPlayerEquipmentPresentation(
        PlayerEquipmentPresentationWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||value.OwnerActorId<=0||
           value.OwnerConnectionEpoch<=0||value.OwnerMapEpoch<=0)
            return false;
        Span<int> definitions=stackalloc int[]
        {
            value.HeadDefinitionId,value.LeftHandDefinitionId,value.RightHandDefinitionId,
            value.BodyDefinitionId,value.LegDefinitionId,value.WeaponDefinitionId,
            value.ShieldDefinitionId,value.BackDefinitionId,value.CharmDefinitionId
        };
        foreach(int definitionId in definitions)
        {
            if(definitionId is <0 or >100000000)return false;
            if(!value.Present&&definitionId!=0)return false;
        }
        return true;
    }
}
