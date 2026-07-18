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
    public void ThrowsAfter255Ids()
    {
        var document = new UsdmDocument
        {
            Backgrounds = Enumerable.Range(1, 255).Select(x => new BackgroundItem { Id = $"{x:000}", Text = "背景" }).ToList()
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => _allocator.NextBackgroundId(document));
    }
}
