namespace USDM_MindMap_Editor.Core.Layout;

public sealed class LayoutResult
{
    public required IReadOnlyList<LayoutNode> Nodes { get; init; }
    public required IReadOnlyList<LayoutEdge> Edges { get; init; }
    public required LayoutRect CanvasBounds { get; init; }
    public IReadOnlyList<BackgroundTreeLayout> Trees { get; init; } = [];
    public IReadOnlyDictionary<string, LayoutPoint> BackgroundPositions { get; init; } =
        new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);
    public IReadOnlyList<string> MovedBackgroundIds { get; init; } = [];
    public IReadOnlyList<string> InitializedBackgroundIds { get; init; } = [];
    public LayoutSettings Settings { get; init; } = new();

    public IReadOnlyDictionary<string, LayoutNode> NodesByKey =>
        Nodes.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public LayoutNode Find(LayoutNodeKind kind, string id) =>
        Nodes.First(x => x.Kind == kind && x.Id == id);
}

// Absolute logical geometry. Subtract Position to obtain background-local geometry.
public sealed record BackgroundTreeLayout(
    string BackgroundId,
    LayoutPoint Position,
    LayoutRect Bounds,
    IReadOnlyList<LayoutNode> Nodes,
    IReadOnlyList<LayoutEdge> Edges);

public sealed record BackgroundMoveResult(
    bool IsValid,
    bool HasMoved,
    LayoutResult Layout,
    IReadOnlyList<string> CollidingBackgroundIds);
