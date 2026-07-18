using USDM_MindMap_Editor.Core.Validation;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class UsdmDocumentValidatorTests
{
    private readonly UsdmDocumentValidator _validator = new();

    [TestMethod]
    public void ValidDocumentHasNoErrors() => Assert.IsTrue(_validator.Validate(TestDocumentFactory.Create()).IsValid);

    [TestMethod]
    public void DetectsMissingParent()
    {
        var document = TestDocumentFactory.Create();
        document.Requirements[1].ParentRequirementId = "255";
        var result = _validator.Validate(document);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "requirement.parent.missing"));
        Assert.IsTrue(result.Errors.Any(x => x.Code == "requirement.id.parent_mismatch"));
    }

    [TestMethod]
    public void DetectsDuplicateIds() 
    {
        var document = TestDocumentFactory.Create();
        document.Backgrounds.Add(new() { Id = "001", Text = "重複" });
        Assert.IsTrue(_validator.Validate(document).Errors.Any(x => x.Code == "background.id.duplicate"));
    }

    [TestMethod]
    public void RejectsParentAsRelatedRequirement()
    {
        var document = TestDocumentFactory.Create();
        document.Specifications[0].RelatedRequirementIds.Add("001-001");
        Assert.IsTrue(_validator.Validate(document).Errors.Any(x => x.Code == "specification.related.parent"));
    }
}
