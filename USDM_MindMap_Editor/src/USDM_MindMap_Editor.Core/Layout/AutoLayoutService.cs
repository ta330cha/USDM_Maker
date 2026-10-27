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
        BackgroundPlacementService.ValidateSettings(settings);
        var predictor = new NodeSizePredictor(settings);
        var previous = request.PreviousLayout;
        var canReuse = previous is not null && request.ChangedBackgroundIds is not null && previous.Settings == settings;
        var previousTrees = previous?.Trees.ToDictionary(tree => tree.BackgroundId, StringComparer.Ordinal)
            ?? new Dictionary<string, BackgroundTreeLayout>(StringComparer.Ordinal);
        var changed = canReuse ? request.Document.Backgrounds
            .Where(background => request.ChangedBackgroundIds!.Contains(background.Id) || !previousTrees.ContainsKey(background.Id))
            .Select(background => background.Id).ToHashSet(StringComparer.Ordinal) : null;
        var roots = BuildTree(request, predictor, changed);
        var trees = new List<BackgroundTreeLayout>(request.Document.Backgrounds.Count);
        foreach (var root in roots)
        {
            MeasureSubtree(root, settings);
            var nodes = new List<LayoutNode>();
            var edges = new List<LayoutEdge>();
            Place(root, 0, 0, nodes, edges);
            // Internal spacing keeps the established geometry. Tree occupancy also
            // includes the painted stroke, but not shadows or interaction overlays.
            var bounds = root.SubtreeBounds;
            foreach (var edge in edges) bounds = bounds.Union(edge.Bounds.Inflate(settings.ConnectionStrokeWidth / 2));
            trees.Add(new(root.Node.Id, new(0, 0), bounds, nodes, edges));
        }
        if (canReuse)
            trees.AddRange(request.Document.Backgrounds.Where(background => !changed!.Contains(background.Id))
                .Select(background => previousTrees[background.Id]));

        var positions = request.BackgroundPositions ?? request.Document.View.BackgroundPositions
            .ToDictionary(item => item.Key, item => new LayoutPoint(item.Value.X, item.Value.Y), StringComparer.Ordinal);
        return new BackgroundPlacementService().Place(trees, positions, request.AnchoredBackgroundId, settings);
    }

    private static List<TreeItem> BuildTree(LayoutRequest request, NodeSizePredictor predictor, IReadOnlySet<string>? backgroundIds)
    {
        var document = request.Document;
        var expanded = request.ExpandedReasonNodeIds ?? new HashSet<string>(StringComparer.Ordinal);
        var measured = request.MeasuredSizes ?? new Dictionary<string, NodeSize>(StringComparer.Ordinal);

        NodeSize Size(LayoutNodeKind kind, string id, string text, string? reason) =>
            (measured.TryGetValue(NodeKey(kind, id), out var keyedValue) && keyedValue.IsValid ? keyedValue :
             measured.TryGetValue(id, out var legacyValue) && legacyValue.IsValid ? legacyValue : default) is var value && value.IsValid
                ? value
                : predictor.Predict(kind, text, reason, IsExpanded(kind, id, expanded));

        var backgrounds = document.Backgrounds.Where(background => backgroundIds is null || backgroundIds.Contains(background.Id))
            .OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer).Select(background =>
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
            var occupied = (child.SubtreeBounds with
            {
                X = childX + child.SubtreeBounds.X,
                Y = childY + child.SubtreeBounds.Y
            }).Union(edge.Bounds);
            item.SubtreeBounds = item.SubtreeBounds.Union(occupied);
            childY = occupied.Bottom + settings.SiblingGap;
        }
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
