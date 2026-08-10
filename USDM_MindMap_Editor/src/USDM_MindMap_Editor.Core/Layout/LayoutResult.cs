namespace USDM_MindMap_Editor.Core.Layout;

public sealed class LayoutResult
{
    public required IReadOnlyList<LayoutNode> Nodes { get; init; }
    public required IReadOnlyList<LayoutEdge> Edges { get; init; }
    public required LayoutRect CanvasBounds { get; init; }

    public IReadOnlyDictionary<string, LayoutNode> NodesByKey =>
        Nodes.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public LayoutNode Find(LayoutNodeKind kind, string id) =>
        Nodes.First(x => x.Kind == kind && x.Id == id);
}
