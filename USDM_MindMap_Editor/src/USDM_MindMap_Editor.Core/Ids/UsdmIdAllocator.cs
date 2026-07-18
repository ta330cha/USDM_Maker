using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Ids;

public sealed class UsdmIdAllocator
{
    public string NextBackgroundId(UsdmDocument document) => Next(document.Backgrounds.Select(x => x.Id), null);
    public string NextTopLevelRequirementId(UsdmDocument document) => Next(document.Requirements.Where(x => x.ParentBackgroundId is not null).Select(x => x.Id), null);
    public string NextLowerLevelRequirementId(UsdmDocument document, string parentId) => Next(document.Requirements.Where(x => x.ParentRequirementId == parentId).Select(x => x.Id), parentId);
    public string NextSpecificationId(UsdmDocument document, string parentId) => Next(document.Specifications.Where(x => x.ParentRequirementId == parentId).Select(x => x.Id), parentId);

    private static string Next(IEnumerable<string> existingIds, string? prefix)
    {
        var used = existingIds.Select(x => x.Split('-')[^1]).Select(x => int.TryParse(x, out var value) ? value : 0).Where(x => x is >= 1 and <= 255).ToHashSet();
        for (var candidate = 1; candidate <= 255; candidate++)
            if (!used.Contains(candidate)) return prefix is null ? $"{candidate:000}" : $"{prefix}-{candidate:000}";
        throw new InvalidOperationException("採番可能なIDがありません（上限255件）。");
    }
}
