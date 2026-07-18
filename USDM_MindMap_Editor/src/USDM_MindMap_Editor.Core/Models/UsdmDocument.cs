namespace USDM_MindMap_Editor.Core.Models;

public sealed class UsdmDocument
{
    public string Version { get; set; } = AppVersion.Current;
    public string? Title { get; set; }
    public List<BackgroundItem> Backgrounds { get; set; } = [];
    public List<RequirementNode> Requirements { get; set; } = [];
    public List<SpecificationNode> Specifications { get; set; } = [];
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
