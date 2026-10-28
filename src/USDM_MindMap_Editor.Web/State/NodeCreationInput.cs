namespace USDM_MindMap_Editor.Web.State;

public sealed record NodeCreationInput(string Text, string? Reason, IReadOnlyCollection<string> RelatedRequirementIds);
