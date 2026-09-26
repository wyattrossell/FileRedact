namespace FileRedact.Core.Model;

/// <summary>
/// A rectangle in PDF points (1/72 inch) with a top-left origin, i.e. Y grows downwards, matching
/// how pages are rendered on screen. All geometry in the document model uses this convention.
/// </summary>
public readonly record struct RectPt(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterY => Y + Height / 2;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectPt FromEdges(double left, double top, double right, double bottom)
        => new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));

    public RectPt Union(RectPt o)
        => FromEdges(Math.Min(Left, o.Left), Math.Min(Top, o.Top), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));

    public RectPt Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public bool Intersects(RectPt o)
        => o.Left < Right && o.Right > Left && o.Top < Bottom && o.Bottom > Top;

    public double IntersectionArea(RectPt o)
    {
        var w = Math.Min(Right, o.Right) - Math.Max(Left, o.Left);
        var h = Math.Min(Bottom, o.Bottom) - Math.Max(Top, o.Top);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }

    public double Area => Width * Height;

    public RectPt Scale(double s) => new(X * s, Y * s, Width * s, Height * s);
}
