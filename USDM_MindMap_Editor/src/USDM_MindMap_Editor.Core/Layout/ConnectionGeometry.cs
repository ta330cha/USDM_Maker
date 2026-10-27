namespace USDM_MindMap_Editor.Core.Layout;

public static class ConnectionGeometry
{
    public static LayoutEdge Create(string parentKey, LayoutRect parent, string childKey, LayoutRect child, LayoutSettings settings)
    {
        var start = parent.BottomCenter;
        var end = child.LeftCenter;
        var gap = child.X - parent.Right;
        if (gap <= 0) throw new ArgumentException("接続線には親子間の正の階層間隔が必要です。");
        var clearance = Math.Min(settings.ConnectionClearance, gap / 4);
        var incoming = Math.Clamp(gap * settings.ConnectionBendRatio, clearance, gap - clearance);
        var outgoing = Math.Max(clearance, gap * settings.ConnectionBendRatio);
        var edge = new LayoutEdge(parentKey, childKey, start, end,
            new(start.X, start.Y + outgoing), new(end.X - incoming, end.Y));

        // X is monotonic. Until the curve clears the parent's right side, keep Y
        // below its bottom. For an upward child, solve the required first control
        // distance analytically at that exit time instead of using a fixed bend.
        var low = 0.0;
        var high = 1.0;
        var exitX = parent.Right + clearance;
        for (var i = 0; i < 48; i++)
        {
            var t = (low + high) / 2;
            if (edge.PointAt(t).X < exitX) low = t;
            else high = t;
        }
        var exitTime = high;
        var u = 1 - exitTime;
        var rise = Math.Max(0, start.Y - end.Y);
        outgoing = Math.Max(outgoing,
            (clearance + rise * exitTime * exitTime * (3 - 2 * exitTime)) / (3 * exitTime * u * u));
        return edge with { ControlPoint1 = new(start.X, start.Y + outgoing) };
    }
}
