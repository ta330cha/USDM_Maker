using USDM_MindMap_Editor.Core.Layout;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class AutoLayoutServiceTests
{
    [TestMethod]
    public void PlacesTreeFromLeftToRightWithConfiguredSpacing()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest { Document = TestDocumentFactory.Create() });
        var background = result.Find(LayoutNodeKind.Background, "001");
        var upper = result.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");

        Assert.IsLessThan(lower.Bounds.X, background.Bounds.X);
        Assert.IsLessThan(specification.Bounds.X, lower.Bounds.X);
        Assert.IsLessThan(upper.Bounds.X, background.Bounds.X);
        Assert.AreEqual(96, upper.Bounds.X - background.Bounds.Right, 0.001);
        Assert.AreEqual(96, lower.Bounds.X - upper.Bounds.Right, 0.001);
        Assert.AreEqual(96, specification.Bounds.X - lower.Bounds.Right, 0.001);
    }

    [TestMethod]
    public void UsesMeasuredSizeInPreferenceToPrediction()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest
        {
            Document = TestDocumentFactory.Create(),
            MeasuredSizes = new Dictionary<string, NodeSize> { ["001-001"] = new(320, 300) }
        });

        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");
        Assert.AreEqual(320, lower.Size.Width);
        Assert.AreEqual(300, lower.Size.Height);
        Assert.AreEqual(96, specification.Bounds.X - lower.Bounds.Right, 0.001);
    }

    [TestMethod]
    public void ProducesNonOverlappingNodeRectanglesAndParentEdges()
    {
        var result = new AutoLayoutService().Calculate(new LayoutRequest { Document = TestDocumentFactory.Create() });
        for (var i = 0; i < result.Nodes.Count; i++)
        for (var j = i + 1; j < result.Nodes.Count; j++)
            Assert.IsFalse(result.Nodes[i].Bounds.Intersects(result.Nodes[j].Bounds));

        var background = result.Find(LayoutNodeKind.Background, "001");
        var upper = result.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var lower = result.Find(LayoutNodeKind.LowerLevelRequirement, "001-001");
        var specification = result.Find(LayoutNodeKind.Specification, "001-001-001");
        Assert.IsTrue(result.Edges.Any(x => x.ParentId == background.Key && x.ChildId == upper.Key));
        var edge = result.Edges.Single(x => x.ParentId == lower.Key && x.ChildId == specification.Key);
        Assert.AreEqual(lower.Bounds.RightCenter, edge.Start);
        Assert.AreEqual(specification.Bounds.LeftCenter, edge.End);
    }
}
