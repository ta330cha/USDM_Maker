namespace USDM_MindMap_Editor.Core.Layout;

public readonly record struct NodeSize(double Width, double Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public readonly record struct LayoutPoint(double X, double Y);

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public LayoutPoint RightCenter => new(Right, Y + Height / 2);
    public LayoutPoint BottomCenter => new(X + Width / 2, Bottom);
    public LayoutPoint LeftCenter => new(X, Y + Height / 2);

    public bool Intersects(LayoutRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}

public sealed record LayoutEdge(string ParentId, string ChildId, LayoutPoint Start, LayoutPoint End, LayoutPoint ControlPoint1, LayoutPoint ControlPoint2)
{
    public LayoutPoint PointAt(double t)
    {
        var u = 1 - t;
        return new LayoutPoint(
            u * u * u * Start.X + 3 * u * u * t * ControlPoint1.X + 3 * u * t * t * ControlPoint2.X + t * t * t * End.X,
            u * u * u * Start.Y + 3 * u * u * t * ControlPoint1.Y + 3 * u * t * t * ControlPoint2.Y + t * t * t * End.Y);
    }

    // The controls can lie well outside the curve. Include actual derivative extrema,
    // not the control polygon, to avoid excessive whitespace in occupied ranges.
    public LayoutRect Bounds
    {
        get
        {
            var points = new List<LayoutPoint> { Start, End };
            AddExtrema(Start.X, ControlPoint1.X, ControlPoint2.X, End.X, points);
            AddExtrema(Start.Y, ControlPoint1.Y, ControlPoint2.Y, End.Y, points);
            var left = points.Min(p => p.X);
            var top = points.Min(p => p.Y);
            return new LayoutRect(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
        }
    }

    public LayoutEdge Translate(double x, double y) => this with
    {
        Start = new(Start.X + x, Start.Y + y), End = new(End.X + x, End.Y + y),
        ControlPoint1 = new(ControlPoint1.X + x, ControlPoint1.Y + y),
        ControlPoint2 = new(ControlPoint2.X + x, ControlPoint2.Y + y)
    };

    private void AddExtrema(double p0, double p1, double p2, double p3, List<LayoutPoint> points)
    {
        var a = -p0 + 3 * p1 - 3 * p2 + p3;
        var b = 2 * (p0 - 2 * p1 + p2);
        var c = p1 - p0;
        void Add(double t) { if (t > 0 && t < 1) points.Add(PointAt(t)); }
        if (Math.Abs(a) < 1e-10)
        {
            if (Math.Abs(b) >= 1e-10) Add(-c / b);
            return;
        }
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return;
        var root = Math.Sqrt(discriminant);
        Add((-b + root) / (2 * a));
        Add((-b - root) / (2 * a));
    }
}

public sealed record LayoutNode(
    string Id,
    LayoutNodeKind Kind,
    string Text,
    string? Reason,
    string? ParentId,
    NodeSize Size,
    LayoutRect Bounds)
{
    public string Key => Kind switch
    {
        LayoutNodeKind.Background => $"background:{Id}",
        LayoutNodeKind.TopLevelRequirement or LayoutNodeKind.LowerLevelRequirement => $"requirement:{Id}",
        LayoutNodeKind.Specification => $"specification:{Id}",
        _ => $"unknown:{Id}"
    };
}
