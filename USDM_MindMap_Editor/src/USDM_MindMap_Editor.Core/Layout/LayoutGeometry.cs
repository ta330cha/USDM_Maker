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
    public LayoutPoint LeftCenter => new(X, Y + Height / 2);

    public bool Intersects(LayoutRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}

public sealed record LayoutEdge(string ParentId, string ChildId, LayoutPoint Start, LayoutPoint End, LayoutPoint ControlPoint1, LayoutPoint ControlPoint2);

public sealed record LayoutNode(
    string Id,
    LayoutNodeKind Kind,
    string Text,
    string? Reason,
    string? ParentId,
    NodeSize Size,
    LayoutRect Bounds)
{
    public string Key => $"{Kind}:{Id}";
}
