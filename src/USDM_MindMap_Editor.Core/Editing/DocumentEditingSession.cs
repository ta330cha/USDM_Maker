using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Serialization;
using USDM_MindMap_Editor.Core.Validation;

namespace USDM_MindMap_Editor.Core.Editing;

/// <summary>
/// Stages document, view and layout changes together. A failed placement never
/// publishes a partially edited document. Pointer previews are not session state.
/// </summary>
public sealed class DocumentEditingSession
{
    private readonly ILayoutEngine _layoutEngine;
    private readonly NodeCreationService _creation;
    private readonly BackgroundPlacementService _placement = new();
    private Dictionary<string, NodeSize> _measuredSizes = new(StringComparer.Ordinal);
    private Dictionary<string, string> _backgroundIdsByNodeKey = new(StringComparer.Ordinal);
    private PendingMeasurementSource? _pendingMeasurementSource;

    private sealed record PendingMeasurementSource(string BackgroundId, string NodeKey);

    public UsdmDocument Document { get; private set; }
    public LayoutResult Layout { get; private set; }
    public LayoutSettings Settings { get; }
    public long Revision { get; private set; } = 1;
    public bool HasUnsavedChanges { get; private set; }
    public IReadOnlyList<string> AutomaticallyMovedBackgroundIds { get; private set; } = [];
    public IReadOnlyList<string> InitializedBackgroundIds { get; }

    public DocumentEditingSession(UsdmDocument document, LayoutSettings? settings = null,
        ILayoutEngine? layoutEngine = null, NodeCreationService? creation = null)
    {
        Settings = settings ?? new();
        _layoutEngine = layoutEngine ?? new AutoLayoutService();
        _creation = creation ?? new();
        var draft = CopyDocument(document);
        var validation = new UsdmDocumentValidator().Validate(draft, requireCurrentVersion: false);
        if (!validation.IsValid) throw new UsdmValidationException(validation.Errors);
        var layout = Calculate(draft, _measuredSizes);
        ApplyPositions(draft, layout);
        Document = draft;
        Layout = layout;
        IndexTreeOwnership(layout);
        InitializedBackgroundIds = layout.InitializedBackgroundIds;
        AutomaticallyMovedBackgroundIds = layout.MovedBackgroundIds;
        HasUnsavedChanges = InitializedBackgroundIds.Count > 0 || AutomaticallyMovedBackgroundIds.Count > 0;
    }

    public string AddNode(LayoutNodeKind kind, string? parentId, string text, string? reason = null,
        IReadOnlyCollection<string>? relatedRequirementIds = null, LayoutPoint? backgroundPosition = null)
    {
        var draft = CopyDocument(Document);
        var id = kind switch
        {
            LayoutNodeKind.Background => _creation.AddBackground(draft, text),
            LayoutNodeKind.TopLevelRequirement => _creation.AddTopLevelRequirement(draft, parentId!, text, reason),
            LayoutNodeKind.LowerLevelRequirement => _creation.AddLowerLevelRequirement(draft, parentId!, text, reason),
            LayoutNodeKind.Specification => _creation.AddSpecification(draft, parentId!, text, relatedRequirementIds),
            _ => throw new ArgumentException("追加するノードの種別が不正です。", nameof(kind))
        };
        var sourceId = kind == LayoutNodeKind.Background ? id : BackgroundForParent(kind, parentId!);
        if (kind == LayoutNodeKind.Background && backgroundPosition is { } position)
        {
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
                throw new ArgumentException("背景位置には有限の座標を指定してください。", nameof(backgroundPosition));
            draft.View.BackgroundPositions[id] = new CanvasPosition { X = position.X, Y = position.Y };
        }
        var changed = new HashSet<string>(StringComparer.Ordinal) { sourceId };
        var layout = Calculate(draft, _measuredSizes, changed,
            kind != LayoutNodeKind.Background || backgroundPosition.HasValue ? sourceId : null);
        Commit(draft, layout, _measuredSizes, markChanged: true);
        var nodeKey = kind switch
        {
            LayoutNodeKind.Background => $"background:{id}",
            LayoutNodeKind.Specification => $"specification:{id}",
            _ => $"requirement:{id}"
        };
        _pendingMeasurementSource = new(sourceId, nodeKey);
        return id;
    }

    public bool SetReasonExpanded(string nodeKey, bool expanded)
    {
        var node = Layout.NodesByKey.GetValueOrDefault(nodeKey);
        if (node is null || node.Kind is not (LayoutNodeKind.TopLevelRequirement or LayoutNodeKind.LowerLevelRequirement))
            return false;
        var current = Document.View.Nodes.TryGetValue(nodeKey, out var view) && view.ReasonExpanded;
        if (current == expanded) return false;
        var draft = CopyDocument(Document);
        if (expanded) draft.View.Nodes[nodeKey] = new NodeViewState { ReasonExpanded = true };
        else draft.View.Nodes.Remove(nodeKey);
        var measurements = new Dictionary<string, NodeSize>(_measuredSizes, StringComparer.Ordinal);
        measurements.Remove(nodeKey);
        var sourceId = BackgroundForNode(nodeKey);
        var layout = Calculate(draft, measurements, new HashSet<string>(StringComparer.Ordinal) { sourceId }, sourceId);
        // Reason-only unsaved prompts remain undecided (Issue #027 / D-06).
        // Persist the view state, but dirty only the resulting background moves.
        Commit(draft, layout, measurements, markChanged: layout.MovedBackgroundIds.Count > 0);
        _pendingMeasurementSource = new(sourceId, nodeKey);
        return true;
    }

    public bool ApplyMeasuredSizes(IReadOnlyDictionary<string, NodeSize> changes)
    {
        var measurements = new Dictionary<string, NodeSize>(_measuredSizes, StringComparer.Ordinal);
        var changedBackgrounds = new HashSet<string>(StringComparer.Ordinal);
        var nodes = Layout.NodesByKey;
        var measuresPendingSource = false;
        foreach (var (key, reported) in changes)
        {
            if (!nodes.ContainsKey(key) || !reported.IsValid) continue;
            measuresPendingSource |= key == _pendingMeasurementSource?.NodeKey;
            var size = reported with { Height = Math.Max(reported.Height, Settings.MinNodeHeight) };
            if (measurements.TryGetValue(key, out var oldSize) && oldSize == size) continue;
            measurements[key] = size;
            changedBackgrounds.Add(BackgroundForNode(key));
        }
        if (changedBackgrounds.Count == 0) return false;
        var draft = CopyDocument(Document);
        // An operation's first valid DOM size can share a batch with other roots.
        // Keep that operation's root fixed until this correction succeeds. Unrelated
        // batches do not consume the source or inherit its priority (the operation
        // may not change DOM size, so ResizeObserver need not report it at all).
        // Initial batches have no source and retain the load-time Y/ID precedence.
        var anchor = measuresPendingSource ? _pendingMeasurementSource!.BackgroundId
            : changedBackgrounds.Count == 1 ? changedBackgrounds.Single() : null;
        var layout = Calculate(draft, measurements, changedBackgrounds, anchor);
        Commit(draft, layout, measurements, markChanged: layout.MovedBackgroundIds.Count > 0);
        if (measuresPendingSource) _pendingMeasurementSource = null;
        return true;
    }

    public BackgroundMoveResult MoveBackground(string backgroundId, LayoutPoint candidate, long expectedRevision)
    {
        if (expectedRevision != Revision)
            return new BackgroundMoveResult(false, false, Layout, []);
        var result = _placement.TryMove(Layout, backgroundId, candidate);
        if (result.IsValid && result.HasMoved)
        {
            Commit(CopyDocument(Document), result.Layout, _measuredSizes, markChanged: true);
            AutomaticallyMovedBackgroundIds = [];
            _pendingMeasurementSource = null;
        }
        return result;
    }

    public void SetViewport(double zoom, double panX, double panY)
    {
        if (!double.IsFinite(zoom) || !double.IsFinite(panX) || !double.IsFinite(panY)) return;
        Document.View.Zoom = Math.Clamp(zoom, Settings.MinZoom, Settings.MaxZoom);
        Document.View.Pan = new PanState { X = panX, Y = panY };
        // Whether viewport-only changes require an unsaved prompt is still Issue #027.
    }

    public string Serialize(UsdmYamlSerializer? serializer = null) => (serializer ?? new()).Serialize(Document);
    public void MarkSaved() => HasUnsavedChanges = false;

    private LayoutResult Calculate(UsdmDocument draft, IReadOnlyDictionary<string, NodeSize> measurements,
        IReadOnlySet<string>? changed = null, string? anchor = null) => _layoutEngine.Calculate(new LayoutRequest
    {
        Document = draft,
        Settings = Settings,
        MeasuredSizes = measurements,
        ExpandedReasonNodeIds = draft.View.Nodes.Where(x => x.Value.ReasonExpanded).Select(x => x.Key).ToHashSet(StringComparer.Ordinal),
        PreviousLayout = changed is null ? null : Layout,
        ChangedBackgroundIds = changed,
        AnchoredBackgroundId = anchor
    });

    private void Commit(UsdmDocument draft, LayoutResult layout, Dictionary<string, NodeSize> measurements, bool markChanged)
    {
        ApplyPositions(draft, layout);
        Document = draft;
        Layout = layout;
        if (_backgroundIdsByNodeKey.Count != layout.Nodes.Count) IndexTreeOwnership(layout);
        _measuredSizes = measurements;
        AutomaticallyMovedBackgroundIds = layout.MovedBackgroundIds;
        HasUnsavedChanges |= markChanged;
        Revision++;
    }

    private string BackgroundForParent(LayoutNodeKind childKind, string parentId) => childKind switch
    {
        LayoutNodeKind.TopLevelRequirement => parentId,
        LayoutNodeKind.LowerLevelRequirement or LayoutNodeKind.Specification => BackgroundForNode($"requirement:{parentId}"),
        _ => throw new ArgumentException("追加先の背景を特定できません。")
    };

    private string BackgroundForNode(string key) => _backgroundIdsByNodeKey[key];

    private void IndexTreeOwnership(LayoutResult layout) => _backgroundIdsByNodeKey = layout.Trees
        .SelectMany(tree => tree.Nodes.Select(node => new KeyValuePair<string, string>(node.Key, tree.BackgroundId)))
        .ToDictionary(StringComparer.Ordinal);

    private static void ApplyPositions(UsdmDocument document, LayoutResult layout)
    {
        var positions = layout.BackgroundPositions.ToDictionary(x => x.Key,
            x => new CanvasPosition { X = x.Value.X, Y = x.Value.Y }, StringComparer.Ordinal);
        if (positions.Count != document.Backgrounds.Count || positions.Values.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new InvalidOperationException("背景配置を確定できませんでした。文書は変更していません。");
        document.View.BackgroundPositions = positions;
        document.FormatVersion = DocumentFormat.Current;
    }

    // Existing entities are not edited by these commands. Copy collections and all
    // mutable view objects so layout failure cannot affect the committed snapshot.
    private static UsdmDocument CopyDocument(UsdmDocument source)
    {
        var ids = source.Backgrounds.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return new UsdmDocument
        {
            Version = source.Version, FormatVersion = source.FormatVersion, Title = source.Title,
            Backgrounds = [.. source.Backgrounds], Requirements = [.. source.Requirements], Specifications = [.. source.Specifications],
            View = new DocumentViewState
            {
                Zoom = source.View.Zoom,
                Pan = new PanState { X = source.View.Pan.X, Y = source.View.Pan.Y },
                Nodes = source.View.Nodes.ToDictionary(x => x.Key, x => new NodeViewState { ReasonExpanded = x.Value.ReasonExpanded }, StringComparer.Ordinal),
                BackgroundPositions = source.View.BackgroundPositions.Where(x => ids.Contains(x.Key))
                    .ToDictionary(x => x.Key, x => new CanvasPosition { X = x.Value.X, Y = x.Value.Y }, StringComparer.Ordinal)
            }
        };
    }
}
