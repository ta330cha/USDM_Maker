namespace USDM_MindMap_Editor.Core.Layout;

public static class ConnectionGeometry
{
    public static LayoutEdge Create(string parentKey, LayoutRect parent, string childKey, LayoutRect child, LayoutSettings settings)
    {
        var start = parent.BottomCenter;
        var end = child.LeftCenter;
        var horizontalDistance = end.X - start.X;
        var verticalDistance = end.Y - start.Y;
        if (!double.IsFinite(horizontalDistance) || horizontalDistance <= 0 ||
            !double.IsFinite(verticalDistance) || verticalDistance <= 0 || child.Y < parent.Bottom)
            throw new ArgumentException("接続先は親の下側かつ下辺中央より右側に配置してください。");
        if (!double.IsFinite(settings.ConnectionBendRatio) || settings.ConnectionBendRatio <= 0 || settings.ConnectionBendRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(settings), "曲線の距離係数は0より大きく1以下にしてください。");

        // Ordered controls make both coordinates monotonic. The curve stays below
        // the parent and left of the child/sibling subtrees, even when their
        // horizontal projections overlap the parent rectangle.
        var outgoing = verticalDistance * settings.ConnectionBendRatio;
        var incoming = horizontalDistance * settings.ConnectionBendRatio;
        return new LayoutEdge(parentKey, childKey, start, end,
            new(start.X, start.Y + outgoing), new(end.X - incoming, end.Y));
    }
}
