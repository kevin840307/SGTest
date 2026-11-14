using System.Buffers.Binary;
using System.Numerics;

namespace SGTest.Server.Domain;

public sealed class CollisionGrid(int width, int height, byte[] cells)
{
    public const float Cell = 16;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public Vector2 Bounds => new(Width * Cell, Height * Cell);

    public static CollisionGrid Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 8) throw new InvalidDataException("Short collision header");
        int w = BinaryPrimitives.ReadInt32LittleEndian(raw), h = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4));
        if (w <= 0 || h <= 0 || (long)w*h > 16_000_000 || raw.Length != 8+(long)w*h)
            throw new InvalidDataException("Invalid collision dimensions");
        return new(w,h,raw[8..]);
    }

    bool CellOpen(int x,int y) => x >= 0 && y >= 0 && x < Width && y < Height && (cells[y*Width+x]&7) == 0;
    static (int X,int Y) Index(Vector2 p) => ((int)MathF.Floor(p.X/Cell),(int)MathF.Floor(p.Y/Cell));
    public bool Walkable(Vector2 p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) return false;
        var (x,y) = Index(p); return CellOpen(x,y);
    }
    public bool Allows(Vector2 from,Vector2 to)
    {
        if (!Walkable(from) || !Walkable(to)) return false;
        int steps = Math.Max(1,(int)MathF.Ceiling(Vector2.Distance(from,to)/(Cell*.5f)));
        var previous = Index(from);
        for (int i=1;i<=steps;i++)
        {
            var current = Index(Vector2.Lerp(from,to,(float)i/steps));
            if (!CellOpen(current.X,current.Y)) return false;
            if (current.X != previous.X && current.Y != previous.Y &&
                (!CellOpen(current.X,previous.Y) || !CellOpen(previous.X,current.Y))) return false;
            previous = current;
        }
        return true;
    }
    public Vector2 Slide(Vector2 from,Vector2 to)
    {
        if (Allows(from,to)) return to;
        var x = new Vector2(to.X,from.Y);
        if (Allows(from,x)) return x;
        var y = new Vector2(from.X,to.Y);
        return Allows(from,y) ? y : from;
    }
    public Vector2 Nearest(Vector2 p)
    {
        if (Walkable(p)) return p;
        var origin = Index(p);
        for (int ring=1;ring<=64;ring++)
            for (int y=-ring;y<=ring;y++)
                for (int x=-ring;x<=ring;x++)
                    if ((Math.Abs(x)==ring || Math.Abs(y)==ring) && CellOpen(origin.X+x,origin.Y+y))
                        return new((origin.X+x+.5f)*Cell,(origin.Y+y+.5f)*Cell);
        throw new InvalidDataException("No legal spawn near requested position");
    }
}
