using USDM_MindMap_Editor.Core.Ids;

namespace USDM_MindMap_Editor.Core.Layout;

/// <summary>Pure placement of cached background trees; never mutates a document or its view.</summary>
public sealed class BackgroundPlacementService
{
    internal LayoutResult Place(IReadOnlyList<BackgroundTreeLayout> trees,
        IReadOnlyDictionary<string, LayoutPoint> positions, string? anchoredBackgroundId, LayoutSettings settings,
        IReadOnlyDictionary<string, LayoutRect> previousBounds)
    {
        ValidateSettings(settings);
        var ids = trees.Select(tree => tree.BackgroundId).ToHashSet(StringComparer.Ordinal);
        if (anchoredBackgroundId is not null && !ids.Contains(anchoredBackgroundId))
            throw new ArgumentException("固定する背景が存在しません。", nameof(anchoredBackgroundId));
        if (positions.Keys.Any(id => !ids.Contains(id)))
            throw new ArgumentException("配置位置が存在しない背景を参照しています。", nameof(positions));
        foreach (var position in positions.Values) ValidatePosition(position);
        foreach (var tree in trees) ValidateTree(tree);
        foreach (var bounds in previousBounds.Values) ValidateRect(bounds);

        var placed = new List<BackgroundTreeLayout>(trees.Count);
        // Saved positions precede missing positions. A changed/inserted source is
        // fixed first; otherwise current top and numeric ID determine priority.
        var positioned = trees.Where(tree => positions.ContainsKey(tree.BackgroundId))
            .Select(tree => TranslateTo(tree, positions[tree.BackgroundId]))
            .OrderBy(tree => tree.BackgroundId == anchoredBackgroundId ? 0 : 1)
            .ThenBy(tree => tree.Bounds.Y)
            .ThenBy(tree => tree.BackgroundId, UsdmIdRules.NumericSegmentComparer);
        foreach (var tree in positioned) placed.Add(ResolveCollisions(tree, placed, settings.SiblingGap, previousBounds));

        var initialized = new List<string>();
        foreach (var tree in trees.Where(tree => !positions.ContainsKey(tree.BackgroundId))
                     .OrderBy(tree => tree.BackgroundId, UsdmIdRules.NumericSegmentComparer))
        {
            var top = placed.Count == 0 ? settings.CanvasMargin
                : Math.Max(settings.CanvasMargin, placed.Max(other => other.Bounds.Bottom) + settings.SiblingGap);
            var localTop = tree.Bounds.Y - tree.Position.Y;
            var candidate = TranslateTo(tree, new(settings.CanvasMargin, top - localTop));
            placed.Add(ResolveCollisions(candidate, placed, settings.SiblingGap, previousBounds));
            initialized.Add(tree.BackgroundId);
        }

        var moved = placed.Where(tree => positions.TryGetValue(tree.BackgroundId, out var original) && original != tree.Position)
            .Select(tree => tree.BackgroundId).OrderBy(id => id, UsdmIdRules.NumericSegmentComparer).ToArray();
        return Compose(placed, settings, moved, initialized);
    }

    public BackgroundMoveResult TryMove(LayoutResult layout, string backgroundId, LayoutPoint candidate)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ValidatePosition(candidate);
        var original = layout.Trees.FirstOrDefault(tree => tree.BackgroundId == backgroundId)
            ?? throw new ArgumentException("移動する背景が存在しません。", nameof(backgroundId));
        if (candidate == original.Position) return new(true, false, layout, []);

        var moved = TranslateTo(original, candidate);
        var collisions = layout.Trees.Where(tree => tree.BackgroundId != backgroundId && tree.Bounds.Intersects(moved.Bounds))
            .Select(tree => tree.BackgroundId).OrderBy(id => id, UsdmIdRules.NumericSegmentComparer).ToArray();
        // Rejection returns the exact drag-start result; no other tree is pushed.
        if (collisions.Length > 0) return new(false, false, layout, collisions);
        var result = Compose(layout.Trees.Select(tree => tree.BackgroundId == backgroundId ? moved : tree).ToArray(),
            layout.Settings, [backgroundId], []);
        return new(true, true, result, []);
    }

    private static BackgroundTreeLayout ResolveCollisions(BackgroundTreeLayout tree,
        IReadOnlyList<BackgroundTreeLayout> placed, double gap, IReadOnlyDictionary<string, LayoutRect> previousBounds)
    {
        var bounds = tree.Bounds;
        var position = tree.Position;
        // Each positive-axis move passes one fixed obstacle's right or bottom.
        // Neither coordinate decreases, so a resolved pair cannot collide again:
        // at most placed.Count moves, with a final full recheck, are required.
        for (var pass = 0; pass <= placed.Count; pass++)
        {
            // Preserve placement priority when several obstacles intersect. A
            // move can encounter an earlier obstacle, so always restart the scan.
            var obstacle = placed.FirstOrDefault(other => bounds.Intersects(other.Bounds));
            if (obstacle is null) return TranslateTo(tree, position);
            var deltaX = obstacle.Bounds.Right + gap - bounds.X;
            var deltaY = obstacle.Bounds.Bottom + gap - bounds.Y;
            var pushRight = ShouldPushRight(obstacle.BackgroundId, tree.BackgroundId, previousBounds, deltaX, deltaY);
            // Only the selected axis needs to be representable. Overflow in an
            // unused alternative must not reject an otherwise valid translation.
            var selectedDelta = pushRight ? deltaX : deltaY;
            if (!double.IsFinite(selectedDelta) || selectedDelta <= 0)
                throw new ArgumentOutOfRangeException(nameof(tree), "背景の押し出し量が計算可能な有限座標の範囲を超えています。");
            var dx = pushRight ? deltaX : 0;
            var dy = pushRight ? 0 : deltaY;
            var nextPosition = new LayoutPoint(position.X + dx, position.Y + dy);
            var nextBounds = bounds.Translate(dx, dy);
            ValidatePosition(nextPosition);
            ValidateRect(nextBounds);
            // Reject overflow/precision loss instead of looping or accepting a
            // translation that did not actually clear the selected obstacle.
            if ((pushRight && (nextPosition.X <= position.X || nextBounds.X < obstacle.Bounds.Right))
                || (!pushRight && (nextPosition.Y <= position.Y || nextBounds.Y < obstacle.Bounds.Bottom))
                || (gap > 0 && (pushRight ? nextBounds.X <= obstacle.Bounds.Right : nextBounds.Y <= obstacle.Bounds.Bottom)))
                throw new ArgumentOutOfRangeException(nameof(tree), "背景を指定方向へ押し出せる座標精度がありません。");
            position = nextPosition;
            bounds = nextBounds;
        }
        throw new InvalidOperationException("背景の重複を解消できませんでした。");
    }

    private static bool ShouldPushRight(string obstacleId, string movingId,
        IReadOnlyDictionary<string, LayoutRect> previousBounds, double deltaX, double deltaY)
    {
        if (previousBounds.TryGetValue(obstacleId, out var beforeA)
            && previousBounds.TryGetValue(movingId, out var beforeB))
        {
            if (beforeB.X >= beforeA.Right && beforeB.Y < beforeA.Bottom && beforeB.Bottom > beforeA.Y)
                return true;
            if (beforeB.Y >= beforeA.Bottom && beforeB.X < beforeA.Right && beforeB.Right > beforeA.X)
                return false;
        }
        // Diagonal, invalid prior overlap, left/above, and missing history all use
        // the current pair's shorter displacement. Equal amounts prefer down.
        return deltaX < deltaY;
    }

    private static BackgroundTreeLayout TranslateTo(BackgroundTreeLayout tree, LayoutPoint position)
    {
        ValidatePosition(position);
        if (tree.Position == position) return tree;
        var dx = position.X - tree.Position.X;
        var dy = position.Y - tree.Position.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy))
            throw new ArgumentOutOfRangeException(nameof(position), "背景の移動量が計算可能な範囲を超えています。");
        var translated = tree with
        {
            Position = position,
            Bounds = tree.Bounds.Translate(dx, dy),
            Nodes = tree.Nodes.Select(node => node with { Bounds = node.Bounds.Translate(dx, dy) }).ToArray(),
            Edges = tree.Edges.Select(edge => edge.Translate(dx, dy)).ToArray()
        };
        ValidateTree(translated);
        return translated;
    }

    private static LayoutResult Compose(IEnumerable<BackgroundTreeLayout> trees, LayoutSettings settings,
        IReadOnlyList<string> moved, IReadOnlyList<string> initialized)
    {
        var ordered = trees.OrderBy(tree => tree.BackgroundId, UsdmIdRules.NumericSegmentComparer).ToArray();
        var padding = Math.Max(settings.CanvasMargin, settings.ConnectionClearance);
        var canvas = new LayoutRect(0, 0, settings.CanvasMargin, settings.CanvasMargin);
        if (ordered.Length > 0)
        {
            canvas = ordered[0].Bounds;
            foreach (var tree in ordered.Skip(1)) canvas = canvas.Union(tree.Bounds);
            canvas = canvas.Inflate(padding);
            ValidateRect(canvas);
        }
        return new LayoutResult
        {
            Trees = ordered,
            Nodes = ordered.SelectMany(tree => tree.Nodes).ToArray(),
            Edges = ordered.SelectMany(tree => tree.Edges).ToArray(),
            BackgroundPositions = ordered.ToDictionary(tree => tree.BackgroundId, tree => tree.Position, StringComparer.Ordinal),
            CanvasBounds = canvas,
            MovedBackgroundIds = moved,
            InitializedBackgroundIds = initialized,
            Settings = settings
        };
    }

    internal static void ValidateSettings(LayoutSettings settings)
    {
        foreach (var value in new[] { settings.ParentChildVerticalGap, settings.SiblingGap, settings.CanvasMargin,
                     settings.ConnectionClearance, settings.ConnectionStrokeWidth })
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(settings), "レイアウト間隔・余白・線幅は有限の非負値にしてください。");
    }

    private static void ValidatePosition(LayoutPoint position)
    {
        if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position), "背景位置は有限値にしてください。");
    }

    private static void ValidateRect(LayoutRect rect)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height)
            || rect.Width < 0 || rect.Height < 0 || !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom)
            || (rect.Width > 0 && rect.Right <= rect.X) || (rect.Height > 0 && rect.Bottom <= rect.Y))
            throw new ArgumentOutOfRangeException(nameof(rect), "描画範囲が計算可能な有限座標の範囲を超えています。");
    }

    internal static void ValidateTree(BackgroundTreeLayout tree)
    {
        ValidatePosition(tree.Position);
        ValidateRect(tree.Bounds);
        foreach (var node in tree.Nodes)
        {
            if (!node.Size.IsValid) throw new ArgumentOutOfRangeException(nameof(tree), "ノードサイズは有限の正値にしてください。");
            ValidateRect(node.Bounds);
        }
        foreach (var edge in tree.Edges)
        {
            ValidatePosition(edge.Start);
            ValidatePosition(edge.End);
            ValidatePosition(edge.ControlPoint1);
            ValidatePosition(edge.ControlPoint2);
            ValidateRect(edge.Bounds);
        }
    }
}
