using USDM_MindMap_Editor.Core.Validation;
using USDM_MindMap_Editor.Core.Models;

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

    [TestMethod]
    public void AcceptsFiveDigitIdsUpTo65535()
    {
        var document = new UsdmDocument
        {
            Backgrounds = [new() { Id = "65535", Text = "背景" }],
            Requirements =
            [
                new() { Id = "65535", Request = "上位要求", ParentBackgroundId = "65535" },
                new() { Id = "65535-65535", Request = "下位要求", ParentRequirementId = "65535" }
            ],
            Specifications = [new() { Id = "65535-65535-65535", Specification = "仕様", ParentRequirementId = "65535-65535" }]
        };
        Assert.IsTrue(_validator.Validate(document).IsValid);
    }

    [TestMethod]
    public void RejectsIdsAbove65535()
    {
        var document = TestDocumentFactory.Create();
        document.Backgrounds[0].Id = "65536";
        Assert.IsTrue(_validator.Validate(document).Errors.Any(x => x.Code == "background.id.invalid"));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void AcceptsAllThreeSupportedFormatsAndMissingPositionsDuringStructuralValidation(int format)
    {
        var document = TestDocumentFactory.Create();
        document.FormatVersion = format;
        Assert.IsTrue(_validator.Validate(document).IsValid);
        Assert.IsTrue(_validator.Validate(document, requireBackgroundPositions: true).Errors
            .Any(x => x.Code == "view.background_position.required"));
    }

    [TestMethod]
    public void AcceptsNegativeFractionalAndZeroBackgroundCoordinates()
    {
        var document = TestDocumentFactory.Create();
        document.View.BackgroundPositions["001"] = new() { X = -123.75, Y = 0 };
        Assert.IsTrue(_validator.Validate(document, requireBackgroundPositions: true).IsValid);
    }

    [TestMethod]
    public void DetectsPositionForAnUnknownBackground()
    {
        var document = TestDocumentFactory.Create();
        document.View.BackgroundPositions["requirement:001"] = new() { X = 96, Y = 96 };
        var result = _validator.Validate(document);
        Assert.IsTrue(result.Errors.Any(x => x.Code == "view.background_position.missing"));
    }

    [TestMethod]
    [DataRow(double.NaN, 0d)]
    [DataRow(double.PositiveInfinity, 0d)]
    [DataRow(double.NegativeInfinity, 0d)]
    [DataRow(0d, double.NaN)]
    [DataRow(0d, double.PositiveInfinity)]
    [DataRow(0d, double.NegativeInfinity)]
    public void RejectsNonFiniteBackgroundCoordinates(double x, double y)
    {
        var document = TestDocumentFactory.Create();
        document.View.BackgroundPositions["001"] = new() { X = x, Y = y };
        Assert.IsTrue(_validator.Validate(document).Errors.Any(error => error.Code == "view.background_position.invalid"));
    }

    [TestMethod]
    public void RejectsAnUnspecifiedPositionValue()
    {
        var document = TestDocumentFactory.Create();
        document.View.BackgroundPositions["001"] = null!;
        Assert.IsTrue(_validator.Validate(document).Errors.Any(x => x.Code == "view.background_position.invalid"));
    }
}
