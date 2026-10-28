using USDM_MindMap_Editor.Core.Ids;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Validation;

public sealed class UsdmDocumentValidator
{
    public ValidationResult Validate(UsdmDocument document, bool requireCurrentVersion = true, bool requireBackgroundPositions = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(document.Version)) result.Add("version.required", "version は必須です。");
        else if (requireCurrentVersion && document.Version != AppVersion.Current)
            result.Add("version.unsupported", $"version は {AppVersion.Current} である必要があります。");
        else if (!Version.TryParse(document.Version, out _) || document.Version.Count(x => x == '.') != 2)
            result.Add("version.invalid", "version は SemVer 形式で指定してください。");

        if (document.FormatVersion is < DocumentFormat.Legacy or > DocumentFormat.Current)
            result.Add("format_version.unsupported", $"format_version は {DocumentFormat.Legacy}〜{DocumentFormat.Current} に対応しています。");

        ValidateBackgrounds(document, result);
        ValidateRequirements(document, result);
        ValidateSpecifications(document, result);
        ValidateView(document, result, requireBackgroundPositions);
        return result;
    }

    private static void ValidateView(UsdmDocument document, ValidationResult result, bool requireBackgroundPositions)
    {
        var view = document.View ?? new DocumentViewState();
        var pan = view.Pan ?? new PanState();
        if (double.IsNaN(view.Zoom) || double.IsInfinity(view.Zoom) || view.Zoom is < 0.25 or > 2.0)
            result.Add("view.zoom.invalid", "view.zoom は0.25〜2.0の有限値で指定してください。");
        if (double.IsNaN(pan.X) || double.IsInfinity(pan.X) || double.IsNaN(pan.Y) || double.IsInfinity(pan.Y))
            result.Add("view.pan.invalid", "view.pan.x と view.pan.y は有限値で指定してください。");

        var backgroundIds = document.Backgrounds.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var requirementIds = document.Requirements.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var specificationIds = document.Specifications.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, position) in view.BackgroundPositions)
        {
            if (!backgroundIds.Contains(id))
                result.Add("view.background_position.missing", "view.background_positions が存在しない背景を参照しています。", id);
            if (position is null || !double.IsFinite(position.X) || !double.IsFinite(position.Y))
                result.Add("view.background_position.invalid", "背景位置の x と y は有限値で指定してください。", id);
        }
        if (requireBackgroundPositions)
        {
            foreach (var id in backgroundIds)
                if (!view.BackgroundPositions.ContainsKey(id))
                    result.Add("view.background_position.required", "保存前に背景の配置位置を確定してください。", id);
        }
        foreach (var (key, state) in view.Nodes)
        {
            var separator = key.IndexOf(':');
            if (separator <= 0 || separator == key.Length - 1)
            {
                result.Add("view.node.invalid", "view.nodes のキーは node-kind:id 形式で指定してください.", key);
                continue;
            }

            var kind = key[..separator];
            var id = key[(separator + 1)..];
            var exists = kind switch
            {
                "background" => backgroundIds.Contains(id),
                "requirement" => requirementIds.Contains(id),
                "specification" => specificationIds.Contains(id),
                _ => false
            };
            if (!exists) result.Add("view.node.missing", "view.nodes が存在しないノードを参照しています。", key);
            if (state.ReasonExpanded && kind != "requirement")
                result.Add("view.node.reason_invalid", "reason_expanded は要求ノードだけに設定できます。", key);
        }
    }

    private static void ValidateBackgrounds(UsdmDocument document, ValidationResult result)
    {
        ValidateLimit(document.Backgrounds.Count, "背景", result);
        ValidateDuplicates(document.Backgrounds.Select(x => x.Id), "background.id.duplicate", result);
        foreach (var item in document.Backgrounds)
        {
            if (!UsdmIdRules.IsValidId(item.Id, 1)) result.Add("background.id.invalid", "背景IDは001～65535で指定してください。", item.Id);
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
                if (!UsdmIdRules.IsValidId(item.Id, 1)) result.Add("requirement.id.invalid", "上位要求IDは001～65535で指定してください。", item.Id);
                if (!backgroundIds.Contains(item.ParentBackgroundId!)) result.Add("requirement.parent.missing", "親背景が存在しません。", item.Id);
            }
            else
            {
                if (!UsdmIdRules.IsValidId(item.Id, 2)) result.Add("requirement.id.invalid", "下位要求IDの形式が不正です。", item.Id);
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
            if (!UsdmIdRules.IsValidId(item.Id, 3)) result.Add("specification.id.invalid", "仕様IDの形式が不正です。", item.Id);
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
        if (count > UsdmIdRules.MaximumSegment) result.Add("count.limit", $"{label}は{UsdmIdRules.MaximumSegment}件までです。");
    }

    private static void ValidateDuplicates(IEnumerable<string> ids, string code, ValidationResult result)
    {
        foreach (var duplicate in ids.GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1))
            result.Add(code, $"ID {duplicate.Key} が重複しています。", duplicate.Key);
    }

}
