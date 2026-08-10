using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Layout;

public sealed class AutoLayoutService : ILayoutEngine
{
    private sealed record TreeItem(LayoutNode Node, List<TreeItem> Children);

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
            var requiredHeight = MeasureSubtree(root, settings);
            _ = Place(root, settings.CanvasMargin, cursorY, requiredHeight, settings, placed, edges);
            cursorY += requiredHeight + settings.SiblingGap;
        }

        var maxRight = placed.Count == 0 ? settings.CanvasMargin : placed.Max(x => x.Bounds.Right) + settings.CanvasMargin;
        var maxBottom = placed.Count == 0 ? settings.CanvasMargin : placed.Max(x => x.Bounds.Bottom) + settings.CanvasMargin;
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
            (measured.TryGetValue($"{kind}:{id}", out var keyedValue) && keyedValue.IsValid ? keyedValue :
             measured.TryGetValue(id, out var legacyValue) && legacyValue.IsValid ? legacyValue : default) is var value && value.IsValid
                ? value
                : predictor.Predict(kind, text, reason, expanded.Contains(id));

        var backgrounds = document.Backgrounds.OrderBy(x => x.Id, StringComparer.Ordinal).Select(background =>
        {
            var upper = document.Requirements
                .Where(x => x.ParentBackgroundId == background.Id)
                .OrderBy(x => x.Id, StringComparer.Ordinal)
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
            .OrderBy(x => x.Id, StringComparer.Ordinal)
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
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => new TreeItem(
                new LayoutNode(x.Id, LayoutNodeKind.Specification, x.Specification, null, x.ParentRequirementId,
                    size(LayoutNodeKind.Specification, x.Id, x.Specification, null), default), []))
            .ToList();
        var node = new LayoutNode(requirement.Id, LayoutNodeKind.LowerLevelRequirement, requirement.Request, requirement.Reason,
            requirement.ParentRequirementId, size(LayoutNodeKind.LowerLevelRequirement, requirement.Id, requirement.Request, requirement.Reason), default);
        return new TreeItem(node, specifications);
    }

    private static double MeasureSubtree(TreeItem item, LayoutSettings settings)
    {
        if (item.Children.Count == 0) return item.Node.Size.Height;
        var childrenHeight = item.Children.Sum(child => MeasureSubtree(child, settings));
        childrenHeight += settings.SiblingGap * (item.Children.Count - 1);
        return Math.Max(item.Node.Size.Height, childrenHeight);
    }

    private static LayoutNode Place(TreeItem item, double x, double y, double subtreeHeight, LayoutSettings settings,
        List<LayoutNode> placed, List<LayoutEdge> edges)
    {
        var nodeY = y + (subtreeHeight - item.Node.Size.Height) / 2;
        var node = item.Node with { Bounds = new LayoutRect(x, nodeY, item.Node.Size.Width, item.Node.Size.Height) };
        placed.Add(node);

        var childHeight = item.Children.Count == 0 ? 0 : item.Children.Sum(child => MeasureSubtree(child, settings)) + settings.SiblingGap * (item.Children.Count - 1);
        var childY = y + (subtreeHeight - childHeight) / 2;
        foreach (var child in item.Children)
        {
            var childSubtreeHeight = MeasureSubtree(child, settings);
            var placedChild = Place(child, node.Bounds.Right + settings.LevelGap, childY, childSubtreeHeight, settings, placed, edges);
            var start = node.Bounds.RightCenter;
            var end = placedChild.Bounds.LeftCenter;
            var controlX = (start.X + end.X) / 2;
            edges.Add(new LayoutEdge(node.Key, placedChild.Key, start, end,
                new LayoutPoint(controlX, start.Y), new LayoutPoint(controlX, end.Y)));
            childY += childSubtreeHeight + settings.SiblingGap;
        }
        return node;
    }
}
