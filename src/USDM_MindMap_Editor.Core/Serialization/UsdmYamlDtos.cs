using USDM_MindMap_Editor.Core.Ids;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Validation;
using YamlDotNet.Serialization;

namespace USDM_MindMap_Editor.Core.Serialization;

// Format-specific roots keep newer view fields out of older readers. Ordering is
// explicit because the current writer also contains inherited DTO properties.
internal class DocumentDto
{
    [YamlMember(Order = 0)] public string Version { get; set; } = "";
    [YamlMember(Order = 1)] public int? FormatVersion { get; set; }
    [YamlMember(Order = 2)] public string? Title { get; set; }
    [YamlMember(Order = 3)] public List<BackgroundDto> Backgrounds { get; set; } = [];
    [YamlMember(Order = 4)] public List<RequirementDto> Requirements { get; set; } = [];
    [YamlMember(Order = 5)] public List<SpecificationDto> Specifications { get; set; } = [];

    public UsdmDocument ToModel(DocumentViewState view) => new()
    {
        Version = Version,
        FormatVersion = FormatVersion ?? DocumentFormat.Legacy,
        Title = Title,
        Backgrounds = Backgrounds.Select(x => new BackgroundItem { Id = x.Id, Text = x.Text }).ToList(),
        Requirements = Requirements.Select(x => new RequirementNode
        {
            Id = x.Id, Request = x.Request, Reason = x.Reason,
            ParentBackgroundId = x.ParentBackgroundId, ParentRequirementId = x.ParentRequirementId
        }).ToList(),
        Specifications = Specifications.Select(x => new SpecificationNode
        {
            Id = x.Id, Specification = x.Specification, ParentRequirementId = x.ParentRequirementId,
            RelatedRequirementIds = x.RelatedRequirementIds ?? []
        }).ToList(),
        View = view
    };
}

internal sealed class ViewDocumentDto : DocumentDto
{
    [YamlMember(Order = 6)] public ViewDto? View { get; set; }
}

internal sealed class PositionedDocumentDto : DocumentDto
{
    [YamlMember(Order = 6)] public PositionedViewDto? View { get; set; }

    public static PositionedDocumentDto From(UsdmDocument document) => new()
    {
        Version = AppVersion.Current,
        FormatVersion = DocumentFormat.Current,
        Title = string.IsNullOrWhiteSpace(document.Title) ? null : document.Title,
        Backgrounds = document.Backgrounds.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer)
            .Select(x => new BackgroundDto { Id = x.Id, Text = x.Text }).ToList(),
        Requirements = document.Requirements.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer)
            .Select(x => new RequirementDto
            {
                Id = x.Id, Request = x.Request, Reason = string.IsNullOrWhiteSpace(x.Reason) ? null : x.Reason,
                ParentBackgroundId = x.ParentBackgroundId, ParentRequirementId = x.ParentRequirementId
            }).ToList(),
        Specifications = document.Specifications.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer)
            .Select(x => new SpecificationDto
            {
                Id = x.Id, Specification = x.Specification, ParentRequirementId = x.ParentRequirementId,
                RelatedRequirementIds = x.RelatedRequirementIds.Count == 0 ? null
                    : x.RelatedRequirementIds.Order(UsdmIdRules.NumericSegmentComparer).ToList()
            }).ToList(),
        View = PositionedViewDto.From(document.View)
    };
}

internal class ViewDto
{
    [YamlMember(Order = 0)] public double Zoom { get; set; } = 1.0;
    [YamlMember(Order = 1)] public PanDto? Pan { get; set; } = new();
    [YamlMember(Order = 2)] public Dictionary<string, NodeViewDto>? Nodes { get; set; }

    public DocumentViewState ToModel() => new()
    {
        Zoom = Zoom,
        Pan = new PanState { X = Pan?.X ?? 0, Y = Pan?.Y ?? 0 },
        Nodes = (Nodes ?? new Dictionary<string, NodeViewDto>(StringComparer.Ordinal))
            .Where(x => x.Value is not null)
            .ToDictionary(x => x.Key, x => new NodeViewState { ReasonExpanded = x.Value.ReasonExpanded }, StringComparer.Ordinal)
    };
}

internal sealed class PositionedViewDto : ViewDto
{
    [YamlMember(Order = 3)] public Dictionary<string, CanvasPositionDto?>? BackgroundPositions { get; set; }

    public static PositionedViewDto From(DocumentViewState view)
    {
        var nodes = view.Nodes.Where(x => x.Value.ReasonExpanded)
            .OrderBy(x => x.Key, NodeKeyComparer.Instance)
            .ToDictionary(x => x.Key, _ => new NodeViewDto { ReasonExpanded = true }, StringComparer.Ordinal);
        return new PositionedViewDto
        {
            Zoom = view.Zoom,
            Pan = new PanDto { X = view.Pan?.X ?? 0, Y = view.Pan?.Y ?? 0 },
            Nodes = nodes.Count == 0 ? null : nodes,
            BackgroundPositions = view.BackgroundPositions.OrderBy(x => x.Key, UsdmIdRules.NumericSegmentComparer)
                .ToDictionary(x => x.Key, x => (CanvasPositionDto?)new CanvasPositionDto
                {
                    X = x.Value.X, Y = x.Value.Y
                }, StringComparer.Ordinal)
        };
    }

    public DocumentViewState ToPositionedModel()
    {
        var view = ToModel();
        foreach (var (id, position) in BackgroundPositions ?? [])
        {
            if (position?.X is not double x || position.Y is not double y)
                throw new UsdmValidationException([new ValidationError("view.background_position.invalid",
                    "背景位置には x と y の両方を指定してください。", id)]);
            view.BackgroundPositions.Add(id, new CanvasPosition { X = x, Y = y });
        }
        return view;
    }
}

internal sealed class CanvasPositionDto
{
    [YamlMember(Order = 0)] public double? X { get; set; }
    [YamlMember(Order = 1)] public double? Y { get; set; }
}

internal sealed class PanDto
{
    [YamlMember(Order = 0)] public double X { get; set; }
    [YamlMember(Order = 1)] public double Y { get; set; }
}

internal sealed class NodeViewDto
{
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool ReasonExpanded { get; set; }
}

internal sealed class BackgroundDto
{
    [YamlMember(Order = 0)] public string Id { get; set; } = "";
    [YamlMember(Order = 1)] public string Text { get; set; } = "";
}

internal sealed class RequirementDto
{
    [YamlMember(Order = 0)] public string Id { get; set; } = "";
    [YamlMember(Order = 1)] public string Request { get; set; } = "";
    [YamlMember(Order = 2)] public string? Reason { get; set; }
    [YamlMember(Order = 3)] public string? ParentBackgroundId { get; set; }
    [YamlMember(Order = 4)] public string? ParentRequirementId { get; set; }
}

internal sealed class SpecificationDto
{
    [YamlMember(Order = 0)] public string Id { get; set; } = "";
    [YamlMember(Order = 1)] public string Specification { get; set; } = "";
    [YamlMember(Order = 2)] public string ParentRequirementId { get; set; } = "";
    [YamlMember(Order = 3)] public List<string>? RelatedRequirementIds { get; set; }
}

internal sealed class NodeKeyComparer : IComparer<string>
{
    public static NodeKeyComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        var leftParts = left.Split(':', 2);
        var rightParts = right.Split(':', 2);
        var kindComparison = string.Compare(leftParts[0], rightParts[0], StringComparison.Ordinal);
        return kindComparison != 0 ? kindComparison
            : UsdmIdRules.NumericSegmentComparer.Compare(leftParts.ElementAtOrDefault(1), rightParts.ElementAtOrDefault(1));
    }
}
