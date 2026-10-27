using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Ids;

namespace USDM_MindMap_Editor.Core.Layout;

public sealed class AutoLayoutService : ILayoutEngine
{
    private sealed record TreeItem(LayoutNode Node, List<TreeItem> Children)
    {
        public double Height { get; set; }
        public double NodeTop { get; set; }
        public double ChildrenTop { get; set; }
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

        foreach (var root in roots)
        {
            MeasureSubtree(root, settings);
            Place(root, settings.CanvasMargin, cursorY, settings, placed, edges);
            cursorY += root.Height + settings.SiblingGap;
        }

        var drawingBounds = placed.Select(x => x.Bounds).Concat(edges.Select(x => x.Bounds)).ToArray();
        var padding = Math.Max(settings.CanvasMargin, settings.ConnectionClearance);
        var maxRight = drawingBounds.Length == 0 ? settings.CanvasMargin : drawingBounds.Max(x => x.Right) + padding;
        var maxBottom = drawingBounds.Length == 0 ? settings.CanvasMargin : drawingBounds.Max(x => x.Bottom) + padding;
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
        var childrenHeight = item.Children.Sum(child => child.Height)
            + settings.SiblingGap * Math.Max(0, item.Children.Count - 1);
        var contentHeight = Math.Max(item.Node.Size.Height, childrenHeight);
        item.NodeTop = (contentHeight - item.Node.Size.Height) / 2;
        item.ChildrenTop = (contentHeight - childrenHeight) / 2;
        item.Height = contentHeight;
        var parent = new LayoutRect(0, item.NodeTop, item.Node.Size.Width, item.Node.Size.Height);
        var childY = item.ChildrenTop;
        foreach (var child in item.Children)
        {
            var childRect = new LayoutRect(parent.Right + settings.LevelGap, childY + child.NodeTop,
                child.Node.Size.Width, child.Node.Size.Height);
            var edge = ConnectionGeometry.Create(item.Node.Key, parent, child.Node.Key, childRect, settings);
            item.Connections.Add(edge);
            item.Height = Math.Max(item.Height, edge.Bounds.Bottom);
            childY += child.Height + settings.SiblingGap;
        }
    }

    private static void Place(TreeItem item, double x, double y, LayoutSettings settings,
        List<LayoutNode> placed, List<LayoutEdge> edges)
    {
        var nodeY = y + item.NodeTop;
        var node = item.Node with { Bounds = new LayoutRect(x, nodeY, item.Node.Size.Width, item.Node.Size.Height) };
        placed.Add(node);
        edges.AddRange(item.Connections.Select(edge => edge.Translate(x, y)));
        var childY = y + item.ChildrenTop;
        foreach (var child in item.Children)
        {
            Place(child, node.Bounds.Right + settings.LevelGap, childY, settings, placed, edges);
            childY += child.Height + settings.SiblingGap;
        }
    }
}
