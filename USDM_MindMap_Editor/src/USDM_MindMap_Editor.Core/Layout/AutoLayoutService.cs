using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Ids;

namespace USDM_MindMap_Editor.Core.Layout;

public sealed class AutoLayoutService : ILayoutEngine
{
    private sealed record TreeItem(LayoutNode Node, List<TreeItem> Children)
    {
        public LayoutRect SubtreeBounds { get; set; }
        public LayoutPoint OffsetFromParent { get; set; }
        public List<LayoutEdge> Connections { get; } = [];
    }

    public LayoutResult Calculate(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = request.Settings;
        var predictor = new NodeSizePredictor(settings);
        var roots = BuildTree(request, predictor);
        var placed = new List<LayoutNode>(roots.Count);
        var edges = new List<LayoutEdge>();
        var cursorY = settings.CanvasMargin;
        var maxRight = settings.CanvasMargin;
        var maxBottom = settings.CanvasMargin;
        var padding = Math.Max(settings.CanvasMargin, settings.ConnectionClearance);

        foreach (var root in roots)
        {
            MeasureSubtree(root, settings);
            Place(root, settings.CanvasMargin, cursorY, placed, edges);
            maxRight = Math.Max(maxRight, settings.CanvasMargin + root.SubtreeBounds.Right + padding);
            maxBottom = Math.Max(maxBottom, cursorY + root.SubtreeBounds.Bottom + padding);
            cursorY += root.SubtreeBounds.Bottom + settings.SiblingGap;
        }

        return new LayoutResult
        {
            Nodes = placed,
            Edges = edges,
            CanvasBounds = new LayoutRect(0, 0, maxRight, maxBottom)
        };
    }

    private static List<TreeItem> BuildTree(LayoutRequest request, NodeSizePredictor predictor)
    {
        var document = request.Document;
        var expanded = request.ExpandedReasonNodeIds ?? new HashSet<string>(StringComparer.Ordinal);
        var measured = request.MeasuredSizes ?? new Dictionary<string, NodeSize>(StringComparer.Ordinal);

        NodeSize Size(LayoutNodeKind kind, string id, string text, string? reason) =>
            (measured.TryGetValue(NodeKey(kind, id), out var keyedValue) && keyedValue.IsValid ? keyedValue :
             measured.TryGetValue(id, out var legacyValue) && legacyValue.IsValid ? legacyValue : default) is var value && value.IsValid
                ? value
                : predictor.Predict(kind, text, reason, IsExpanded(kind, id, expanded));

        var backgrounds = document.Backgrounds.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer).Select(background =>
        {
            var upper = document.Requirements
                .Where(x => x.ParentBackgroundId == background.Id)
                .OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer)
                .Select(requirement => BuildRequirement(document, requirement, Size, expanded))
                .ToList();
            var node = new LayoutNode(background.Id, LayoutNodeKind.Background, background.Text, null, null,
                Size(LayoutNodeKind.Background, background.Id, background.Text, null), default);
            return new TreeItem(node, upper);
        }).ToList();

        return backgrounds;
    }

    private static TreeItem BuildRequirement(UsdmDocument document, RequirementNode requirement,
        Func<LayoutNodeKind, string, string, string?, NodeSize> size, IReadOnlySet<string> expanded)
    {
        var lower = document.Requirements
            .Where(x => x.ParentRequirementId == requirement.Id)
            .OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer)
            .Select(x => BuildLowerRequirement(document, x, size, expanded))
            .ToList();
        var kind = requirement.ParentBackgroundId is not null ? LayoutNodeKind.TopLevelRequirement : LayoutNodeKind.LowerLevelRequirement;
        var node = new LayoutNode(requirement.Id, kind, requirement.Request, requirement.Reason,
            requirement.ParentBackgroundId ?? requirement.ParentRequirementId,
            size(kind, requirement.Id, requirement.Request, requirement.Reason), default);
        return new TreeItem(node, lower);
    }

    private static TreeItem BuildLowerRequirement(UsdmDocument document, RequirementNode requirement,
        Func<LayoutNodeKind, string, string, string?, NodeSize> size, IReadOnlySet<string> expanded)
    {
        var specifications = document.Specifications
            .Where(x => x.ParentRequirementId == requirement.Id)
            .OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer)
            .Select(x => new TreeItem(
                new LayoutNode(x.Id, LayoutNodeKind.Specification, x.Specification, null, x.ParentRequirementId,
                    size(LayoutNodeKind.Specification, x.Id, x.Specification, null), default), []))
            .ToList();
        var node = new LayoutNode(requirement.Id, LayoutNodeKind.LowerLevelRequirement, requirement.Request, requirement.Reason,
            requirement.ParentRequirementId, size(LayoutNodeKind.LowerLevelRequirement, requirement.Id, requirement.Request, requirement.Reason), default);
        return new TreeItem(node, specifications);
    }

    private static bool IsExpanded(LayoutNodeKind kind, string id, IReadOnlySet<string> expanded) =>
        expanded.Contains(id) || expanded.Contains(NodeKey(kind, id));

    private static string NodeKey(LayoutNodeKind kind, string id) => kind switch
    {
        LayoutNodeKind.Background => $"background:{id}",
        LayoutNodeKind.TopLevelRequirement or LayoutNodeKind.LowerLevelRequirement => $"requirement:{id}",
        LayoutNodeKind.Specification => $"specification:{id}",
        _ => $"unknown:{id}"
    };

    private static void MeasureSubtree(TreeItem item, LayoutSettings settings)
    {
        foreach (var child in item.Children) MeasureSubtree(child, settings);
        var parent = new LayoutRect(0, 0, item.Node.Size.Width, item.Node.Size.Height);
        item.SubtreeBounds = parent;
        var childX = parent.Width * 3 / 4;
        var childY = parent.Bottom + settings.ParentChildVerticalGap;
        foreach (var child in item.Children)
        {
            child.OffsetFromParent = new(childX, childY);
            var childRect = new LayoutRect(childX, childY, child.Node.Size.Width, child.Node.Size.Height);
            var edge = ConnectionGeometry.Create(item.Node.Key, parent, child.Node.Key, childRect, settings);
            item.Connections.Add(edge);
            // Reuse the measured child range; do not walk its descendants again.
            // Include the incoming connection in this sibling's occupied range.
            var occupied = Union(child.SubtreeBounds with
            {
                X = childX + child.SubtreeBounds.X,
                Y = childY + child.SubtreeBounds.Y
            }, edge.Bounds);
            item.SubtreeBounds = Union(item.SubtreeBounds, occupied);
            childY = occupied.Bottom + settings.SiblingGap;
        }
    }

    private static LayoutRect Union(LayoutRect first, LayoutRect second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        return new(left, top, Math.Max(first.Right, second.Right) - left, Math.Max(first.Bottom, second.Bottom) - top);
    }

    private static void Place(TreeItem item, double x, double y,
        List<LayoutNode> placed, List<LayoutEdge> edges)
    {
        var node = item.Node with { Bounds = new LayoutRect(x, y, item.Node.Size.Width, item.Node.Size.Height) };
        placed.Add(node);
        edges.AddRange(item.Connections.Select(edge => edge.Translate(x, y)));
        foreach (var child in item.Children)
        {
            Place(child, x + child.OffsetFromParent.X, y + child.OffsetFromParent.Y, placed, edges);
        }
    }
}
