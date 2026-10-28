using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Layout;

public sealed class LayoutRequest
{
    public required UsdmDocument Document { get; init; }
    public LayoutSettings Settings { get; init; } = new();
    public IReadOnlyDictionary<string, NodeSize>? MeasuredSizes { get; init; }
    public IReadOnlySet<string>? ExpandedReasonNodeIds { get; init; }
    // Null uses the persisted view positions; an explicit map is a complete override.
    public IReadOnlyDictionary<string, LayoutPoint>? BackgroundPositions { get; init; }
    public string? AnchoredBackgroundId { get; init; }
    public LayoutResult? PreviousLayout { get; init; }
    // With a previous result, only these backgrounds need their internal geometry rebuilt.
    public IReadOnlySet<string>? ChangedBackgroundIds { get; init; }
}
