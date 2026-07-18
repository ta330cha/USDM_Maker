using System.Text.RegularExpressions;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Validation;

public sealed partial class UsdmDocumentValidator
{
    public ValidationResult Validate(UsdmDocument document, bool requireCurrentVersion = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(document.Version)) result.Add("version.required", "version は必須です。");
        else if (requireCurrentVersion && document.Version != AppVersion.Current)
            result.Add("version.unsupported", $"version は {AppVersion.Current} である必要があります。");

        ValidateBackgrounds(document, result);
        ValidateRequirements(document, result);
        ValidateSpecifications(document, result);
        return result;
    }

    private static void ValidateBackgrounds(UsdmDocument document, ValidationResult result)
    {
        ValidateLimit(document.Backgrounds.Count, "背景", result);
        ValidateDuplicates(document.Backgrounds.Select(x => x.Id), "background.id.duplicate", result);
        foreach (var item in document.Backgrounds)
        {
            if (!ThreeDigitId().IsMatch(item.Id)) result.Add("background.id.invalid", "背景IDは001～255の3桁で指定してください。", item.Id);
            if (string.IsNullOrWhiteSpace(item.Text)) result.Add("background.text.required", "背景本文は必須です。", item.Id);
        }
    }

    private static void ValidateRequirements(UsdmDocument document, ValidationResult result)
    {
        var topLevel = document.Requirements.Where(x => x.ParentBackgroundId is not null).ToList();
        var lowerLevel = document.Requirements.Where(x => x.ParentRequirementId is not null).ToList();
        ValidateLimit(topLevel.Count, "上位要求", result);
        ValidateDuplicates(document.Requirements.Select(x => x.Id), "requirement.id.duplicate", result);
        var backgroundIds = document.Backgrounds.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var topIds = topLevel.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var item in document.Requirements)
        {
            if (string.IsNullOrWhiteSpace(item.Request)) result.Add("requirement.request.required", "要求本文は必須です。", item.Id);
            var hasBackground = item.ParentBackgroundId is not null;
            var hasRequirement = item.ParentRequirementId is not null;
            if (hasBackground == hasRequirement)
            {
                result.Add("requirement.parent.invalid", "要求には背景または上位要求のどちらか一方だけを親に指定してください。", item.Id);
                continue;
            }

            if (hasBackground)
            {
                if (!ThreeDigitId().IsMatch(item.Id)) result.Add("requirement.id.invalid", "上位要求IDは001～255の3桁で指定してください。", item.Id);
                if (!backgroundIds.Contains(item.ParentBackgroundId!)) result.Add("requirement.parent.missing", "親背景が存在しません。", item.Id);
            }
            else
            {
                if (!LowerRequirementId().IsMatch(item.Id)) result.Add("requirement.id.invalid", "下位要求IDの形式が不正です。", item.Id);
                if (!topIds.Contains(item.ParentRequirementId!)) result.Add("requirement.parent.missing", "親上位要求が存在しません。", item.Id);
                if (!item.Id.StartsWith(item.ParentRequirementId + "-", StringComparison.Ordinal)) result.Add("requirement.id.parent_mismatch", "下位要求IDと親IDが一致しません。", item.Id);
            }
        }

        foreach (var group in lowerLevel.GroupBy(x => x.ParentRequirementId, StringComparer.Ordinal))
            ValidateLimit(group.Count(), $"上位要求 {group.Key} 配下の下位要求", result);
    }

    private static void ValidateSpecifications(UsdmDocument document, ValidationResult result)
    {
        ValidateDuplicates(document.Specifications.Select(x => x.Id), "specification.id.duplicate", result);
        var lowerIds = document.Requirements.Where(x => x.ParentRequirementId is not null).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var item in document.Specifications)
        {
            if (string.IsNullOrWhiteSpace(item.Specification)) result.Add("specification.text.required", "仕様本文は必須です。", item.Id);
            if (!SpecificationId().IsMatch(item.Id)) result.Add("specification.id.invalid", "仕様IDの形式が不正です。", item.Id);
            if (!lowerIds.Contains(item.ParentRequirementId)) result.Add("specification.parent.missing", "親下位要求が存在しません。", item.Id);
            if (!item.Id.StartsWith(item.ParentRequirementId + "-", StringComparison.Ordinal)) result.Add("specification.id.parent_mismatch", "仕様IDと親IDが一致しません。", item.Id);
            if (item.RelatedRequirementIds.Count != item.RelatedRequirementIds.Distinct(StringComparer.Ordinal).Count())
                result.Add("specification.related.duplicate", "関連要求IDが重複しています。", item.Id);
            foreach (var relatedId in item.RelatedRequirementIds)
            {
                if (!lowerIds.Contains(relatedId)) result.Add("specification.related.missing", $"関連要求 {relatedId} が存在しません。", item.Id);
                if (relatedId == item.ParentRequirementId) result.Add("specification.related.parent", "所属先の下位要求は関連要求に指定できません。", item.Id);
            }
        }

        foreach (var group in document.Specifications.GroupBy(x => x.ParentRequirementId, StringComparer.Ordinal))
            ValidateLimit(group.Count(), $"下位要求 {group.Key} 配下の仕様", result);
    }

    private static void ValidateLimit(int count, string label, ValidationResult result)
    {
        if (count > 255) result.Add("count.limit", $"{label}は255件までです。");
    }

    private static void ValidateDuplicates(IEnumerable<string> ids, string code, ValidationResult result)
    {
        foreach (var duplicate in ids.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1))
            result.Add(code, $"ID {duplicate.Key} が重複しています。", duplicate.Key);
    }

    [GeneratedRegex("^(?!000)(?:00[1-9]|0[1-9]\\d|1\\d{2}|2[0-4]\\d|25[0-5])$", RegexOptions.CultureInvariant)]
    private static partial Regex ThreeDigitId();
    [GeneratedRegex("^(?!000)(?:00[1-9]|0[1-9]\\d|1\\d{2}|2[0-4]\\d|25[0-5])-(?!000)(?:00[1-9]|0[1-9]\\d|1\\d{2}|2[0-4]\\d|25[0-5])$", RegexOptions.CultureInvariant)]
    private static partial Regex LowerRequirementId();
    [GeneratedRegex("^(?!000)(?:00[1-9]|0[1-9]\\d|1\\d{2}|2[0-4]\\d|25[0-5])-(?!000)(?:00[1-9]|0[1-9]\\d|1\\d{2}|2[0-4]\\d|25[0-5])-(?!000)(?:00[1-9]|0[1-9]\\d|1\\d{2}|2[0-4]\\d|25[0-5])$", RegexOptions.CultureInvariant)]
    private static partial Regex SpecificationId();
}
