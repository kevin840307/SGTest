using System.Buffers.Binary;
using System.Text;

namespace SGTest.Server.Transport;

// Bounded MessagePack subset: arrays, integers, float64, UTF8 strings, binary,
// booleans and nil. No typeless object activation or reflection deserialization.
public static class Wire
{
    public const int Version=1, MaxPacket=65536;
    public static byte[] Packet(int op,params object?[] body) => Encode(new object?[]{Version,op,body});
    public static byte[] Encode(object? value)
    {
        using var stream=new MemoryStream(); Write(stream,value); return stream.ToArray();
    }
    static void Number(Stream s,byte marker,ulong value,int size)
    {
        Span<byte> bytes=stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes,value);
        s.WriteByte(marker); s.Write(bytes[(8-size)..]);
    }
    static void Write(Stream s,object? value)
    {
        switch(value)
        {
            case null: s.WriteByte(0xc0); break;
            case bool b: s.WriteByte(b ? (byte)0xc3 : (byte)0xc2); break;
            case byte[] bytes: Number(s,0xc6,(ulong)bytes.Length,4); s.Write(bytes); break;
            case string text:
                var utf8=Encoding.UTF8.GetBytes(text); Number(s,0xdb,(ulong)utf8.Length,4); s.Write(utf8); break;
            case object?[] array:
                Number(s,0xdd,(ulong)array.Length,4); foreach(var item in array) Write(s,item); break;
            case float or double: Number(s,0xcb,(ulong)BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)),8); break;
            case byte or short or ushort or int or uint or long:
                Number(s,0xd3,unchecked((ulong)Convert.ToInt64(value)),8); break;
            default: throw new InvalidDataException("Unsupported wire type");
        }
    }
    public static object?[] Decode(byte[] bytes)
    {
        if (bytes.Length>MaxPacket) throw new InvalidDataException("Packet too large");
        int at=0; var result=Read(bytes,ref at,0) as object?[];
        if (at!=bytes.Length || result is not {Length:3} || Integer(result[0])!=Version || result[2] is not object?[])
            throw new InvalidDataException("Wrong protocol/version");
        return result;
    }
    static ulong Take(byte[] bytes,ref int at,int count)
    {
        if (at+count>bytes.Length) throw new InvalidDataException("Truncated MessagePack");
        ulong value=0; for(int i=0;i<count;i++) value=(value<<8)|bytes[at++]; return value;
    }
    static object? Read(byte[] bytes,ref int at,int depth)
    {
        if (depth>8) throw new InvalidDataException("Nested packet");
        byte marker=(byte)Take(bytes,ref at,1);
        if (marker<=0x7f) return (long)marker;
        if (marker>=0xe0) return (long)(sbyte)marker;
        if ((marker&0xf0)==0x90) return Array(bytes,ref at,marker&15,depth);
        if ((marker&0xe0)==0xa0) return Text(bytes,ref at,marker&31);
        return marker switch {
            0xc0=>null,0xc2=>false,0xc3=>true,
            0xcc=>(long)Take(bytes,ref at,1),0xcd=>(long)Take(bytes,ref at,2),0xce=>(long)Take(bytes,ref at,4),
            0xd0=>(long)(sbyte)Take(bytes,ref at,1),0xd1=>(long)(short)Take(bytes,ref at,2),
            0xd2=>(long)(int)Take(bytes,ref at,4),0xd3=>unchecked((long)Take(bytes,ref at,8)),
            0xca=>(double)BitConverter.Int32BitsToSingle((int)Take(bytes,ref at,4)),
            0xcb=>BitConverter.Int64BitsToDouble((long)Take(bytes,ref at,8)),
            0xd9=>Text(bytes,ref at,(int)Take(bytes,ref at,1)),
            0xda=>Text(bytes,ref at,(int)Take(bytes,ref at,2)),
            0xdb=>Text(bytes,ref at,checked((int)Take(bytes,ref at,4))),
            0xc4=>Binary(bytes,ref at,(int)Take(bytes,ref at,1)),
            0xc5=>Binary(bytes,ref at,(int)Take(bytes,ref at,2)),
            0xc6=>Binary(bytes,ref at,checked((int)Take(bytes,ref at,4))),
            0xdc=>Array(bytes,ref at,(int)Take(bytes,ref at,2),depth),
            0xdd=>Array(bytes,ref at,checked((int)Take(bytes,ref at,4)),depth),
            _=>throw new InvalidDataException("Unsupported MessagePack marker")
        };
    }
    static byte[] Binary(byte[] bytes,ref int at,int size)
    {
        if (size<0 || size>MaxPacket || at+size>bytes.Length) throw new InvalidDataException("Bad binary size");
        var value=bytes.AsSpan(at,size).ToArray(); at+=size; return value;
    }
    static string Text(byte[] bytes,ref int at,int size) => Encoding.UTF8.GetString(Binary(bytes,ref at,size));
    static object?[] Array(byte[] bytes,ref int at,int count,int depth)
    {
        if (count<0 || count>4096) throw new InvalidDataException("Bad array size");
        var result=new object?[count]; for(int i=0;i<count;i++) result[i]=Read(bytes,ref at,depth+1); return result;
    }
    public static long Integer(object? value) => value is long n ? n : throw new InvalidDataException("Expected integer");
    public static double Real(object? value) => value switch {long n=>n,double n when double.IsFinite(n)=>n,_=>throw new InvalidDataException("Expected finite number")};
}
