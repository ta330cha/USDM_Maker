using USDM_MindMap_Editor.Core.Ids;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class UsdmIdAllocatorTests
{
    private readonly UsdmIdAllocator _allocator = new();

    [TestMethod]
    public void EmptyDocumentStartsAt001()
    {
        var document = new UsdmDocument();
        Assert.AreEqual("001", _allocator.NextBackgroundId(document));
        Assert.AreEqual("001", _allocator.NextTopLevelRequirementId(document));
        Assert.AreEqual("001-001", _allocator.NextLowerLevelRequirementId(document, "001"));
        Assert.AreEqual("001-001-001", _allocator.NextSpecificationId(document, "001-001"));
    }

    [TestMethod]
    public void ReusesSmallestAvailableNumber()
    {
        var document = TestDocumentFactory.Create();
        document.Requirements.RemoveAll(x => x.Id == "001-001");
        Assert.AreEqual("001-001", _allocator.NextLowerLevelRequirementId(document, "001"));
    }

    [TestMethod]
    public void Allows65535IdsAndThrowsAfterLimit()
    {
        var document = new UsdmDocument
        {
            Backgrounds = Enumerable.Range(1, 65_535).Select(x => new BackgroundItem { Id = x <= 999 ? $"{x:000}" : x.ToString(), Text = "背景" }).ToList()
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => _allocator.NextBackgroundId(document));
    }

    [TestMethod]
    public void FormatsValuesAbove999WithoutLeadingZeroes()
    {
        var document = new UsdmDocument { Backgrounds = [new() { Id = "001", Text = "背景" }] };
        for (var value = 2; value <= 999; value++)
            document.Backgrounds.Add(new() { Id = $"{value:000}", Text = "背景" });

        Assert.AreEqual("1000", _allocator.NextBackgroundId(document));
    }
}
