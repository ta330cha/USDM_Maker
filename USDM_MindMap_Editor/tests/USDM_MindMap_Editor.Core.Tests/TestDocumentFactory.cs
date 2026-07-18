using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

internal static class TestDocumentFactory
{
    public static UsdmDocument Create() => new()
    {
        Title = "テスト文書",
        Backgrounds = [new() { Id = "001", Text = "背景" }],
        Requirements =
        [
            new() { Id = "001", Request = "上位要求", Reason = "理由", ParentBackgroundId = "001" },
            new() { Id = "001-001", Request = "下位要求1", ParentRequirementId = "001" },
            new() { Id = "001-002", Request = "下位要求2", ParentRequirementId = "001" }
        ],
        Specifications =
        [
            new()
            {
                Id = "001-001-001", Specification = "仕様", ParentRequirementId = "001-001",
                RelatedRequirementIds = ["001-002"]
            }
        ]
    };
}
