using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Layout;

public sealed class LayoutRequest
{
    public required UsdmDocument Document { get; init; }
    public LayoutSettings Settings { get; init; } = new();
    public IReadOnlyDictionary<string, NodeSize>? MeasuredSizes { get; init; }
    public IReadOnlySet<string>? ExpandedReasonNodeIds { get; init; }
}
