using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Serialization;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class ConnectionGeometryTests
{
    private const double Tolerance = 0.00001;

    [TestMethod]
    [DataRow("single")]
    [DataRow("multiple")]
    [DataRow("deep-first-subtree")]
    [DataRow("four-level")]
    [DataRow("multiple-backgrounds")]
    [DataRow("long-text")]
    [DataRow("long-expanded")]
    public void ConnectionsStayOutsideEveryNodeAndInsideDrawingBounds(string scenario)
    {
        var result = new AutoLayoutService().Calculate(ConnectionScenarioFactory.CreateRequest(scenario));
        Assert.HasCount(result.Nodes.Count - result.Nodes.Count(x => x.Kind == LayoutNodeKind.Background), result.Edges);
        AssertLayout(result);
        foreach (var group in result.Edges.GroupBy(x => x.ParentId))
            Assert.AreEqual(1, group.Select(x => x.Start).Distinct().Count(), "Children must share the parent's bottom anchor.");
    }

    [TestMethod]
    public void ExpansionAndMeasuredCorrectionMoveBothIncomingAndOutgoingAnchors()
    {
        var document = ConnectionScenarioFactory.Create("long-text");
        var service = new AutoLayoutService();
        var collapsed = service.Calculate(new LayoutRequest { Document = document });
        var expandedKeys = new HashSet<string>(StringComparer.Ordinal) { "requirement:001" };
        var expanded = service.Calculate(new LayoutRequest { Document = document, ExpandedReasonNodeIds = expandedKeys });
        var corrected = service.Calculate(new LayoutRequest
        {
            Document = document,
            ExpandedReasonNodeIds = expandedKeys,
            MeasuredSizes = new Dictionary<string, NodeSize>
            {
                ["requirement:001"] = new(280, 2400),
                ["requirement:001-001"] = new(280, 1900)
            }
        });
        var foldedAgain = service.Calculate(new LayoutRequest { Document = document });

        Assert.IsGreaterThan(collapsed.Find(LayoutNodeKind.TopLevelRequirement, "001").Size.Height,
            expanded.Find(LayoutNodeKind.TopLevelRequirement, "001").Size.Height);
        Assert.AreEqual(2400, corrected.Find(LayoutNodeKind.TopLevelRequirement, "001").Size.Height);
        Assert.AreNotEqual(collapsed.Find(LayoutNodeKind.TopLevelRequirement, "001").Bounds.BottomCenter,
            expanded.Find(LayoutNodeKind.TopLevelRequirement, "001").Bounds.BottomCenter);
        CollectionAssert.AreEqual(collapsed.Edges.ToArray(), foldedAgain.Edges.ToArray());
        AssertLayout(collapsed);
        AssertLayout(expanded);
        AssertLayout(corrected);
        AssertLayout(foldedAgain);
    }

    [TestMethod]
    public void RandomMeasuredHeightsDoNotCauseConnectionsToCrossOtherSubtrees()
    {
        var document = ConnectionScenarioFactory.Create("multiple-backgrounds");
        var service = new AutoLayoutService();
        var unmeasured = service.Calculate(new LayoutRequest { Document = document });
        var random = new Random(27102026);
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var measured = unmeasured.Nodes.ToDictionary(x => x.Key,
                _ => new NodeSize(280, random.Next(110, 6000)), StringComparer.Ordinal);
            var result = service.Calculate(new LayoutRequest { Document = document, MeasuredSizes = measured });
            AssertLayout(result, samples: 300);
        }
    }

    [TestMethod]
    public void ExpandingLongReasonMovesFollowingSiblingDownAndCollapseRestoresItsPosition()
    {
        var document = ConnectionScenarioFactory.Create("deep-first-subtree");
        document.Requirements[0].Reason = string.Join('\n', Enumerable.Repeat("長い理由を展開した場合も、次の兄弟と子孫の配置を重ねない。", 12));
        var service = new AutoLayoutService();
        var collapsed = service.Calculate(new LayoutRequest { Document = document });
        var expanded = service.Calculate(new LayoutRequest
        {
            Document = document,
            ExpandedReasonNodeIds = new HashSet<string>(StringComparer.Ordinal) { "requirement:001" }
        });
        var foldedAgain = service.Calculate(new LayoutRequest { Document = document });
        var originalParent = collapsed.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var expandedParent = expanded.Find(LayoutNodeKind.TopLevelRequirement, "001");
        var increase = expandedParent.Size.Height - originalParent.Size.Height;
        Assert.IsGreaterThan(0, increase);
        Assert.AreEqual(originalParent.Bounds.Y, expandedParent.Bounds.Y, Tolerance);
        Assert.AreEqual(collapsed.Find(LayoutNodeKind.TopLevelRequirement, "002").Bounds.Y + increase,
            expanded.Find(LayoutNodeKind.TopLevelRequirement, "002").Bounds.Y, Tolerance);
        foreach (var node in collapsed.Nodes)
            Assert.AreEqual(node.Bounds.X, expanded.Nodes.Single(x => x.Key == node.Key).Bounds.X, Tolerance);
        CollectionAssert.AreEqual(collapsed.Nodes.ToArray(), foldedAgain.Nodes.ToArray());
        CollectionAssert.AreEqual(collapsed.Edges.ToArray(), foldedAgain.Edges.ToArray());
        AssertLayout(expanded);
    }

    [TestMethod]
    [DataRow(240.0, 0.0, 0.5)]
    [DataRow(280.0, 1.0, 0.5)]
    [DataRow(280.0, 96.0, 0.5)]
    [DataRow(320.0, 4000.0, 0.5)]
    [DataRow(280.0, 20000.0, 1.0)]
    [DataRow(280.0, 96.0, 0.1)]
    public void DirectCurveStaysBelowParentAndApproachesIndentedChildMonotonically(double parentWidth, double verticalGap, double bendRatio)
    {
        var parent = new LayoutRect(20, 30, parentWidth, 500);
        var child = new LayoutRect(parent.X + parent.Width * 0.75, parent.Bottom + verticalGap, 280, 300);
        var edge = ConnectionGeometry.Create("parent", parent, "child", child, new LayoutSettings { ConnectionBendRatio = bendRatio });
        AssertAnchors(edge, parent, child);
        Assert.AreEqual(edge.Start.Y + (edge.End.Y - edge.Start.Y) * bendRatio, edge.ControlPoint1.Y, Tolerance);
        Assert.AreEqual(edge.End.X - (edge.End.X - edge.Start.X) * bendRatio, edge.ControlPoint2.X, Tolerance);
        var previous = edge.Start;
        for (var index = 0; index <= 2000; index++)
        {
            var point = edge.PointAt(index / 2000.0);
            Assert.IsTrue(double.IsFinite(point.X) && double.IsFinite(point.Y));
            Assert.IsFalse(IsInterior(parent, point), $"Parent crossing at t={index / 2000.0}");
            Assert.IsFalse(IsInterior(child, point), $"Child crossing at t={index / 2000.0}");
            AssertContains(edge.Bounds, point);
            Assert.IsGreaterThanOrEqualTo(previous.X - Tolerance, point.X);
            Assert.IsGreaterThanOrEqualTo(previous.Y - Tolerance, point.Y);
            previous = point;
        }
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-0.1)]
    [DataRow(1.01)]
    [DataRow(double.NaN)]
    public void RejectsBendRatiosOutsideMonotonicRange(double bendRatio)
    {
        var parent = new LayoutRect(0, 0, 280, 110);
        var child = new LayoutRect(210, 206, 280, 110);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ConnectionGeometry.Create("parent", parent, "child", child, new LayoutSettings { ConnectionBendRatio = bendRatio }));
    }

    [TestMethod]
    public void CurveExtremaAreIncludedWithoutUsingOversizedControlPolygon()
    {
        var edge = new LayoutEdge("a", "b", new(0, 0), new(10, 0), new(0, 100), new(10, 100));
        Assert.AreEqual(75, edge.Bounds.Bottom, Tolerance);
        Assert.AreEqual(0, edge.Bounds.X, Tolerance);
        Assert.AreEqual(10, edge.Bounds.Right, Tolerance);
        Assert.AreEqual(new LayoutPoint(5, 75), edge.PointAt(0.5));
        var translated = edge.Translate(12, 34);
        Assert.AreEqual(109, translated.Bounds.Bottom, Tolerance);
        Assert.AreEqual(new LayoutPoint(17, 109), translated.PointAt(0.5));
    }

    [TestMethod]
    public void CanvasIncludesEveryCurveWithSmallCanvasMargin()
    {
        var settings = new LayoutSettings { CanvasMargin = 0 };
        var result = new AutoLayoutService().Calculate(new LayoutRequest
        {
            Document = ConnectionScenarioFactory.Create("single"),
            Settings = settings
        });
        Assert.IsGreaterThanOrEqualTo(result.Edges.Max(x => x.Bounds.Bottom) + settings.ConnectionClearance - Tolerance, result.CanvasBounds.Bottom);
        AssertLayout(result);
    }

    [TestMethod]
    public void LayoutDoesNotPersistGeometryOrChangeYamlFormat()
    {
        var document = ConnectionScenarioFactory.Create("long-expanded");
        var serializer = new UsdmYamlSerializer();
        var before = serializer.Serialize(document);
        var expanded = document.View.Nodes.Keys.ToHashSet(StringComparer.Ordinal);
        _ = new AutoLayoutService().Calculate(new LayoutRequest { Document = document, ExpandedReasonNodeIds = expanded });
        var after = serializer.Serialize(document);
        Assert.AreEqual(before, after);
        Assert.AreEqual(2, serializer.Deserialize(after).FormatVersion);
        Assert.IsFalse(after.Contains("anchor", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(after.Contains("control_point", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(after.Contains("bounds", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertLayout(LayoutResult result, int samples = 600)
    {
        var byKey = result.Nodes.ToDictionary(x => x.Key, StringComparer.Ordinal);
        for (var left = 0; left < result.Nodes.Count; left++)
        for (var right = left + 1; right < result.Nodes.Count; right++)
            Assert.IsFalse(result.Nodes[left].Bounds.Intersects(result.Nodes[right].Bounds),
                $"Node overlap: {result.Nodes[left].Key} / {result.Nodes[right].Key}");

        foreach (var edge in result.Edges)
        {
            var parent = byKey[edge.ParentId].Bounds;
            var child = byKey[edge.ChildId].Bounds;
            AssertAnchors(edge, parent, child);
            Assert.AreEqual(parent.X + parent.Width * 0.75, child.X, Tolerance);
            Assert.IsGreaterThanOrEqualTo(parent.Bottom, child.Y, "Children must be below their parents.");
            var curveBounds = edge.Bounds;
            AssertContains(result.CanvasBounds, new(curveBounds.X, curveBounds.Y));
            AssertContains(result.CanvasBounds, new(curveBounds.Right, curveBounds.Bottom));
            for (var index = 0; index <= samples; index++)
            {
                var point = edge.PointAt(index / (double)samples);
                AssertContains(curveBounds, point);
                AssertContains(result.CanvasBounds, point);
                foreach (var node in result.Nodes)
                    Assert.IsFalse(IsInterior(node.Bounds, point),
                        $"{edge.ParentId} -> {edge.ChildId} crosses {node.Key} at t={index / (double)samples}.");
            }
        }
    }

    private static void AssertAnchors(LayoutEdge edge, LayoutRect parent, LayoutRect child)
    {
        Assert.AreEqual(parent.BottomCenter.X, edge.Start.X, Tolerance);
        Assert.AreEqual(parent.BottomCenter.Y, edge.Start.Y, Tolerance);
        Assert.AreEqual(child.LeftCenter.X, edge.End.X, Tolerance);
        Assert.AreEqual(child.LeftCenter.Y, edge.End.Y, Tolerance);
        Assert.AreEqual(edge.Start.X, edge.ControlPoint1.X, Tolerance);
        Assert.IsGreaterThan(edge.Start.Y, edge.ControlPoint1.Y, "The departure tangent must point downwards.");
        Assert.AreEqual(edge.End.Y, edge.ControlPoint2.Y, Tolerance);
        Assert.IsLessThan(edge.End.X, edge.ControlPoint2.X, "The arrival tangent must point rightwards.");
    }

    private static bool IsInterior(LayoutRect bounds, LayoutPoint point) =>
        point.X > bounds.X + Tolerance && point.X < bounds.Right - Tolerance &&
        point.Y > bounds.Y + Tolerance && point.Y < bounds.Bottom - Tolerance;

    private static void AssertContains(LayoutRect bounds, LayoutPoint point) =>
        Assert.IsTrue(point.X >= bounds.X - Tolerance && point.X <= bounds.Right + Tolerance &&
            point.Y >= bounds.Y - Tolerance && point.Y <= bounds.Bottom + Tolerance,
            $"Point {point} is outside bounds {bounds}.");
}
