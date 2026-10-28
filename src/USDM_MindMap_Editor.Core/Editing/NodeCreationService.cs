using USDM_MindMap_Editor.Core.Ids;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Editing;

/// <summary>Validates confirmed input before allocating an ID and changing the document.</summary>
public sealed class NodeCreationService(UsdmIdAllocator? allocator = null)
{
    private readonly UsdmIdAllocator _allocator = allocator ?? new();

    public string AddBackground(UsdmDocument document, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireText(text, "背景を入力してください。");
        var id = _allocator.NextBackgroundId(document);
        document.Backgrounds.Add(new BackgroundItem { Id = id, Text = text });
        return id;
    }

    public string AddTopLevelRequirement(UsdmDocument document, string backgroundId, string request, string? reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireText(request, "要求を入力してください。");
        if (!UsdmIdRules.IsValidId(backgroundId, 1) || !document.Backgrounds.Any(x => x.Id == backgroundId))
            throw new ArgumentException("追加先の背景が存在しません。");

        var id = _allocator.NextTopLevelRequirementId(document);
        document.Requirements.Add(new RequirementNode
        {
            Id = id,
            Request = request,
            Reason = reason,
            ParentBackgroundId = backgroundId
        });
        return id;
    }

    public string AddLowerLevelRequirement(UsdmDocument document, string topLevelId, string request, string? reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireText(request, "要求を入力してください。");
        if (!document.Requirements.Any(x => x.Id == topLevelId && IsTopLevelRequirement(x)))
            throw new ArgumentException("追加先の上位要求が存在しません。");

        var id = _allocator.NextLowerLevelRequirementId(document, topLevelId);
        document.Requirements.Add(new RequirementNode
        {
            Id = id,
            Request = request,
            Reason = reason,
            ParentRequirementId = topLevelId
        });
        return id;
    }

    public string AddSpecification(UsdmDocument document, string lowerLevelId, string specification,
        IReadOnlyCollection<string>? relatedRequirementIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireText(specification, "仕様を入力してください。");
        var lowerIds = document.Requirements.Where(IsLowerLevelRequirement).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (!lowerIds.Contains(lowerLevelId))
            throw new ArgumentException("追加先の下位要求が存在しません。");

        // Copy the dialog's selection so later UI changes cannot mutate a saved node.
        var relatedIds = relatedRequirementIds?.ToList() ?? [];
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relatedId in relatedIds)
        {
            if (!lowerIds.Contains(relatedId))
                throw new ArgumentException("関連先には存在する下位要求を指定してください。");
            if (relatedId == lowerLevelId)
                throw new ArgumentException("所属先の下位要求を関連先に指定することはできません。");
            if (!uniqueIds.Add(relatedId))
                throw new ArgumentException("関連する下位要求が重複しています。");
        }

        var id = _allocator.NextSpecificationId(document, lowerLevelId);
        document.Specifications.Add(new SpecificationNode
        {
            Id = id,
            Specification = specification,
            ParentRequirementId = lowerLevelId,
            RelatedRequirementIds = relatedIds
        });
        return id;
    }

    private static bool IsTopLevelRequirement(RequirementNode node) =>
        UsdmIdRules.IsValidId(node.Id, 1) && node.ParentBackgroundId is not null && node.ParentRequirementId is null;

    private static bool IsLowerLevelRequirement(RequirementNode node) =>
        UsdmIdRules.IsValidId(node.Id, 2) && node.ParentRequirementId is not null && node.ParentBackgroundId is null;

    private static void RequireText(string? text, string message)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(message);
    }
}
