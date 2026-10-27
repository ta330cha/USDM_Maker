namespace USDM_MindMap_Editor.Core.Models;

public sealed class UsdmDocument
{
    public string Version { get; set; } = AppVersion.Current;
    public int FormatVersion { get; set; } = DocumentFormat.Current;
    public string? Title { get; set; }
    public List<BackgroundItem> Backgrounds { get; set; } = [];
    public List<RequirementNode> Requirements { get; set; } = [];
    public List<SpecificationNode> Specifications { get; set; } = [];
    public DocumentViewState View { get; set; } = new();
}

public static class DocumentFormat
{
    public const int Legacy = 1;
    public const int ViewState = 2;
    public const int Current = 3;
}

public sealed class DocumentViewState
{
    public double Zoom { get; set; } = 1.0;
    public PanState Pan { get; set; } = new();
    public Dictionary<string, NodeViewState> Nodes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CanvasPosition> BackgroundPositions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CanvasPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class PanState
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class NodeViewState
{
    public bool ReasonExpanded { get; set; }
}

public sealed class BackgroundItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class RequirementNode
{
    public string Id { get; set; } = "";
    public string Request { get; set; } = "";
    public string? Reason { get; set; }
    public string? ParentBackgroundId { get; set; }
    public string? ParentRequirementId { get; set; }
}

public sealed class SpecificationNode
{
    public string Id { get; set; } = "";
    public string Specification { get; set; } = "";
    public string ParentRequirementId { get; set; } = "";
    public List<string> RelatedRequirementIds { get; set; } = [];
}
