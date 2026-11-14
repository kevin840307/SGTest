using System.Buffers.Binary;
using SGTest.Server.Domain;

namespace SGTest.Server.Transport;

public static class SnapshotCodec
{
    public const int Stride=28, RowsPerPacket=28;
    public static byte[] Encode(KeyValuePair<int,Actor>[] rows)
    {
        byte[] bytes=new byte[rows.Length*Stride];
        for(int i=0;i<rows.Length;i++)
        {
            var row=rows[i]; var a=row.Value; var span=bytes.AsSpan(i*Stride,Stride);
            BinaryPrimitives.WriteInt32LittleEndian(span,row.Key);
            BinaryPrimitives.WriteSingleLittleEndian(span[4..],a.Position.X);
            BinaryPrimitives.WriteSingleLittleEndian(span[8..],a.Position.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(span[12..],(ushort)a.Hp);
            span[14]=(byte)a.Facing; span[15]=(byte)a.Pose;
            BinaryPrimitives.WriteSingleLittleEndian(span[16..],(float)a.ActionTime);
            BinaryPrimitives.WriteUInt32LittleEndian(span[20..],a.Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(span[24..],a is Monster m ? m.Definition : 0);
        }
        return bytes;
    }
}
