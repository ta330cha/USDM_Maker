using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class DirectionalBackgroundPlacementTests
{
    [TestMethod]
    public void ClearRightSideBeforeOverridesAShorterDownwardMove()
    {
        var before = Rectangles(new(0, 0, 100, 100), new(120, 0, 100, 100));
        var current = Rectangles(new(0, 0, 500, 100), before["002"]);
        var result = Calculate(current, before, "001");
        Assert.AreEqual(new LayoutPoint(532, 0), result.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(0, 0), result.BackgroundPositions["001"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void ClearBelowSideBeforeOverridesAShorterRightwardMove()
    {
        var before = Rectangles(new(0, 0, 100, 100), new(0, 120, 100, 100));
        var current = Rectangles(new(0, 0, 100, 500), before["002"]);
        var result = Calculate(current, before, "001");
        Assert.AreEqual(new LayoutPoint(0, 532), result.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(0, 0), result.BackgroundPositions["001"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ClearDirectionsPropagateThroughAThreeTreeChain(bool right)
    {
        var before = right
            ? Rectangles(new(0, 0, 100, 100), new(120, 0, 100, 100), new(240, 0, 100, 100))
            : Rectangles(new(0, 0, 100, 100), new(0, 120, 100, 100), new(0, 240, 100, 100));
        var current = new Dictionary<string, LayoutRect>(before)
        {
            ["001"] = right ? new(0, 0, 250, 100) : new(0, 0, 100, 250)
        };
        var result = Calculate(current, before, "001");
        Assert.AreEqual(right ? new LayoutPoint(282, 0) : new(0, 282), result.BackgroundPositions["002"]);
        Assert.AreEqual(right ? new LayoutPoint(414, 0) : new(0, 414), result.BackgroundPositions["003"]);
        CollectionAssert.AreEqual(new[] { "002", "003" }, result.MovedBackgroundIds.ToArray());
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void RightThenDownChainUsesCommittedBoundsNotTheMovedObstacle()
    {
        var before = Rectangles(new(0, 0, 100, 100), new(120, 0, 100, 100), new(200, 120, 200, 100));
        var current = Rectangles(new(0, 0, 250, 100), new(120, 0, 100, 500), before["003"]);
        var result = Calculate(current, before, "001");
        Assert.AreEqual(new LayoutPoint(282, 0), result.BackgroundPositions["002"]);
        // With the moved B as "before", C would appear left of B and fallback
        // would choose the shorter right move. Its true committed side is below.
        Assert.AreEqual(new LayoutPoint(200, 532), result.BackgroundPositions["003"]);
        Assert.AreEqual(new LayoutRect(120, 0, 100, 100), before["002"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void DownThenRightChainSelectsTheDirectionForEachPair()
    {
        var before = Rectangles(new(0, 0, 100, 100), new(0, 120, 100, 100), new(120, 200, 100, 200));
        var current = Rectangles(new(0, 0, 100, 250), new(0, 120, 500, 100), before["003"]);
        var result = Calculate(current, before, "001");
        Assert.AreEqual(new LayoutPoint(0, 282), result.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(532, 200), result.BackgroundPositions["003"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void SimultaneousCollisionsUseAnchorPriorityAndOneObstacleAtATime()
    {
        var current = Rectangles(new(100, 100, 100, 100), new(220, 0, 100, 100), new(150, 50, 100, 100));
        var result = Calculate(current, anchoredId: "001");
        Assert.AreEqual(new LayoutPoint(232, 132), result.BackgroundPositions["003"]);
        Assert.AreEqual(new LayoutPoint(100, 100), result.BackgroundPositions["001"]);
        Assert.AreEqual(new LayoutPoint(220, 0), result.BackgroundPositions["002"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void MovingRightRechecksAnEarlierObstacleAndCanThenMoveDown()
    {
        var current = Rectangles(new(200, 100, 100, 100), new(0, 0, 100, 300), new(50, 150, 100, 100));
        var result = Calculate(current, anchoredId: "001");
        Assert.AreEqual(new LayoutPoint(132, 232), result.BackgroundPositions["003"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    [DataRow(90, 0, 132, 0)]
    [DataRow(0, 90, 0, 132)]
    [DataRow(0, 0, 0, 132)]
    [DataRow(50, 50, 50, 132)]
    public void MissingPriorBoundsChooseShorterCurrentDisplacementAndDownOnTies(
        double x, double y, double expectedX, double expectedY)
    {
        var result = Calculate(Rectangles(new(0, 0, 100, 100), new(x, y, 100, 100)), anchoredId: "001");
        Assert.AreEqual(new LayoutPoint(expectedX, expectedY), result.BackgroundPositions["002"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void DiagonalPriorSeparationUsesTheCurrentShorterDisplacement()
    {
        var before = Rectangles(new(0, 0, 100, 100), new(120, 120, 100, 100));
        var result = Calculate(Rectangles(new(0, 0, 150, 300), before["002"]), before, "001");
        Assert.AreEqual(new LayoutPoint(182, 120), result.BackgroundPositions["002"]);
    }

    [TestMethod]
    public void PriorLeftAboveAndOverlappingPositionsUseFallback()
    {
        var left = Rectangles(new(0, 0, 100, 100), new(-120, 0, 100, 100));
        var leftResult = Calculate(Rectangles(left["001"], new(-120, 0, 200, 100)), left, "001");
        Assert.AreEqual(new LayoutPoint(-120, 132), leftResult.BackgroundPositions["002"]);
        var above = Rectangles(new(0, 0, 100, 100), new(0, -120, 100, 100));
        var aboveResult = Calculate(Rectangles(above["001"], new(0, -120, 100, 200)), above, "001");
        Assert.AreEqual(new LayoutPoint(132, -120), aboveResult.BackgroundPositions["002"]);
        var overlap = Rectangles(new(0, 0, 100, 100), new(50, 50, 100, 100));
        Assert.AreEqual(new LayoutPoint(50, 132), Calculate(overlap, overlap, "001").BackgroundPositions["002"]);
    }

    [TestMethod]
    public void MissingOneMembersPriorBoundsAlsoUsesFallback()
    {
        var prior = new Dictionary<string, LayoutRect> { ["001"] = new(0, 0, 100, 100) };
        var result = Calculate(Rectangles(prior["001"], new(90, 0, 100, 100)), prior, "001");
        Assert.AreEqual(new LayoutPoint(132, 0), result.BackgroundPositions["002"]);
    }

    [TestMethod]
    [DataRow(100, 0)]
    [DataRow(0, 100)]
    [DataRow(100, 100)]
    public void BoundaryContactDoesNotCreateAnAutomaticGap(double x, double y)
    {
        var result = Calculate(Rectangles(new(0, 0, 100, 100), new(x, y, 100, 100)), anchoredId: "001");
        Assert.AreEqual(new LayoutPoint(x, y), result.BackgroundPositions["002"]);
        Assert.IsEmpty(result.MovedBackgroundIds);
    }

    [TestMethod]
    public void NegativeFractionalCoordinatesAndConfiguredGapArePreserved()
    {
        var before = Rectangles(new(-500.25, -100.75, 100, 100), new(-380.25, -100.75, 100, 100));
        var result = Calculate(Rectangles(new(-500.25, -100.75, 250, 100), before["002"]), before, "001",
            new() { SiblingGap = 17.5 });
        Assert.AreEqual(new LayoutPoint(-232.75, -100.75), result.BackgroundPositions["002"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void StrokeOnlyOverlapUsesActualTreeBoundsForRightwardDisplacement()
    {
        var document = ConnectionScenarioFactory.Create("single");
        document.Backgrounds.Add(new() { Id = "002", Text = "右側" });
        var layout = new AutoLayoutService();
        var before = layout.Calculate(new()
        {
            Document = document,
            BackgroundPositions = new Dictionary<string, LayoutPoint> { ["001"] = new(0, 0), ["002"] = new(500, 0) }
        });
        var result = layout.Calculate(new()
        {
            Document = document, BackgroundPositions = before.BackgroundPositions,
            PreviousLayout = before, AnchoredBackgroundId = "001", Settings = new() { ConnectionStrokeWidth = 800 }
        });
        var source = result.Trees[0];
        Assert.IsFalse(source.Nodes.Any(node => node.Bounds.Intersects(before.Trees[1].Bounds)));
        Assert.IsTrue(source.Bounds.Intersects(before.Trees[1].Bounds));
        Assert.AreEqual(new LayoutPoint(source.Bounds.Right + result.Settings.SiblingGap, 0), result.BackgroundPositions["002"]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void RightwardPushTranslatesEveryFourLevelNodeAndBezierPointWithoutRebuildingTheTree()
    {
        var document = ConnectionScenarioFactory.Create("multiple-backgrounds");
        var layout = new AutoLayoutService();
        var initial = layout.Calculate(new() { Document = document });
        var before = layout.Calculate(new()
        {
            Document = document,
            BackgroundPositions = new Dictionary<string, LayoutPoint>
            {
                ["001"] = new(0, 0), ["002"] = new(initial.Trees[0].Bounds.Width + 10, 0), ["003"] = new(10000, 0)
            }
        });
        var result = layout.Calculate(new()
        {
            Document = document, PreviousLayout = before, BackgroundPositions = before.BackgroundPositions,
            AnchoredBackgroundId = "001", ChangedBackgroundIds = new HashSet<string> { "001" },
            MeasuredSizes = new Dictionary<string, NodeSize> { ["background:001"] = new(2000, 110) }
        });
        var original = before.Trees[1];
        var moved = result.Trees[1];
        var dx = moved.Position.X - original.Position.X;
        Assert.IsGreaterThan(0, dx);
        Assert.AreEqual(original.Position.Y, moved.Position.Y);
        for (var index = 0; index < original.Nodes.Count; index++)
            Assert.AreEqual(original.Nodes[index] with { Bounds = original.Nodes[index].Bounds.Translate(dx, 0) }, moved.Nodes[index]);
        for (var index = 0; index < original.Edges.Count; index++)
            Assert.AreEqual(original.Edges[index].Translate(dx, 0), moved.Edges[index]);
        Assert.AreSame(before.Trees[2], result.Trees[2]);
        AssertNonOverlapping(result);
    }

    [TestMethod]
    public void InputEnumerationDoesNotChangeNumericPriorityOrTheFinalPositions()
    {
        var rectangles = new Dictionary<string, LayoutRect>
        {
            ["1000"] = new(0, 0, 100, 100), ["999"] = new(0, 0, 100, 100), ["001"] = new(90, 50, 100, 100)
        };
        var first = Calculate(rectangles);
        var reversed = Calculate(rectangles.Reverse().ToDictionary(item => item.Key, item => item.Value));
        foreach (var (id, position) in first.BackgroundPositions) Assert.AreEqual(position, reversed.BackgroundPositions[id]);
        Assert.AreEqual(new LayoutPoint(0, 0), first.BackgroundPositions["999"]);
        AssertNonOverlapping(first);
    }

    [TestMethod]
    public void SeededMultiTreeGrowthConvergesMonotonicallyAndIndependentlyOfEnumeration()
    {
        foreach (var seed in new[] { 7, 23, 81, 2026, 1028, 65535 })
        {
            var random = new Random(seed);
            var before = new Dictionary<string, LayoutRect>();
            var current = new Dictionary<string, LayoutRect>();
            for (var index = 0; index < 50; index++)
            {
                var id = $"{index + 1:D3}";
                var rect = new LayoutRect(index % 10 * 140 - 700.25, index / 10 * 120 - 240.5,
                    random.Next(60, 101), random.Next(40, 81));
                before[id] = rect;
                current[id] = rect with { Width = rect.Width + random.Next(251), Height = rect.Height + random.Next(251) };
            }
            var sourceId = $"{random.Next(1, 51):D3}";
            var result = Calculate(current, before, sourceId);
            var reversed = Calculate(current.Reverse().ToDictionary(item => item.Key, item => item.Value),
                before.Reverse().ToDictionary(item => item.Key, item => item.Value), sourceId);

            Assert.AreEqual(new LayoutPoint(before[sourceId].X, before[sourceId].Y), result.BackgroundPositions[sourceId]);
            AssertNonOverlapping(result);
            foreach (var (id, position) in result.BackgroundPositions)
            {
                Assert.IsGreaterThanOrEqualTo(before[id].X, position.X);
                Assert.IsGreaterThanOrEqualTo(before[id].Y, position.Y);
                Assert.IsTrue(position.IsFinite);
                Assert.AreEqual(position, reversed.BackgroundPositions[id]);
            }
        }
    }

    [TestMethod]
    public void LargeFiniteCoordinatesRejectAnUnrepresentablePositiveGapButAllowBoundaryContact()
    {
        var current = Rectangles(new(1e18, 0, 1024, 10000), new(1e18 + 512, 0, 1024, 10000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Calculate(current, anchoredId: "001"));
        var touching = Calculate(current, anchoredId: "001", settings: new() { SiblingGap = 0 });
        Assert.AreEqual(current["001"].Right, touching.Trees[1].Bounds.X);
        AssertNonOverlapping(touching);
    }

    [TestMethod]
    public void OrdinaryFractionalRoundingDoesNotRejectARepresentableGap()
    {
        var current = Rectangles(new(-.1, -.2, .4, .4), new(.21, -.2, .3, .4));
        var result = Calculate(current, anchoredId: "001", settings: new() { SiblingGap = .1 });
        Assert.AreEqual(.4, result.BackgroundPositions["002"].X, 1e-12);
        Assert.AreEqual(-.2, result.BackgroundPositions["002"].Y);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.MaxValue)]
    public void InvalidPriorBoundsAreRejectedWithoutMutatingInputs(double coordinate)
    {
        var before = Rectangles(new LayoutRect(coordinate, 0, 100, 100));
        var current = Rectangles(new LayoutRect(0, 0, 100, 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Calculate(current, before));
        Assert.AreEqual(coordinate, before["001"].X);
        Assert.AreEqual(new LayoutRect(0, 0, 100, 100), current["001"]);
    }

    [TestMethod]
    public void OverflowingDisplacementFailsWithoutMutatingCurrentOrPriorPositions()
    {
        var before = Rectangles(new(0, 0, 100, 100), new(120, 0, 100, 100));
        var current = Rectangles(new(0, 0, 150, 100), before["002"]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Calculate(current, before, "001", new() { SiblingGap = double.MaxValue }));
        Assert.AreEqual(new LayoutRect(120, 0, 100, 100), before["002"]);
        Assert.AreEqual(before["002"], current["002"]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void OverflowOfTheUnusedAlternativeDoesNotRejectAFiniteSelectedMove(bool hasHistory)
    {
        var before = Rectangles(new(-1e308, 0, 1.7e308, 1e307), new(-9e307, 1.2e307, 1e307, 1e307));
        var current = Rectangles(new(-1e308, 0, 1.7e308, 1.5e307), before["002"]);
        var result = Calculate(current, hasHistory ? before : null, "001", new() { SiblingGap = 1e308 });
        Assert.AreEqual(-9e307, result.BackgroundPositions["002"].X);
        Assert.AreEqual(1.15e308, result.BackgroundPositions["002"].Y, 1e293);
        Assert.IsTrue(result.BackgroundPositions["002"].IsFinite);
        AssertNonOverlapping(result);
    }

    private static Dictionary<string, LayoutRect> Rectangles(params LayoutRect[] rectangles) => rectangles
        .Select((rect, index) => (Id: $"{index + 1:D3}", Rect: rect)).ToDictionary(item => item.Id, item => item.Rect);

    private static LayoutResult Calculate(IReadOnlyDictionary<string, LayoutRect> current,
        IReadOnlyDictionary<string, LayoutRect>? previous = null, string? anchoredId = null, LayoutSettings? settings = null)
    {
        settings ??= new();
        // These background-only fixtures isolate collision geometry from text
        // prediction. Previous bounds are immutable committed snapshots, including
        // deliberately invalid old overlaps for the documented fallback cases.
        var snapshot = previous is null ? null : new LayoutResult
        {
            Trees = previous.Select(item => new BackgroundTreeLayout(item.Key, new(item.Value.X, item.Value.Y), item.Value, [], [])).ToArray(),
            Nodes = [], Edges = [], CanvasBounds = default, Settings = settings
        };
        return new AutoLayoutService().Calculate(new()
        {
            Document = new() { Backgrounds = current.Keys.Select(id => new BackgroundItem { Id = id, Text = "背景" }).ToList() },
            Settings = settings, AnchoredBackgroundId = anchoredId, PreviousLayout = snapshot,
            ChangedBackgroundIds = current.Keys.ToHashSet(),
            BackgroundPositions = current.ToDictionary(item => item.Key, item => new LayoutPoint(item.Value.X, item.Value.Y)),
            MeasuredSizes = current.ToDictionary(item => $"background:{item.Key}", item => new NodeSize(item.Value.Width, item.Value.Height))
        });
    }

    private static void AssertNonOverlapping(LayoutResult result)
    {
        for (var i = 0; i < result.Trees.Count; i++)
            for (var j = i + 1; j < result.Trees.Count; j++)
                Assert.IsFalse(result.Trees[i].Bounds.Intersects(result.Trees[j].Bounds));
    }
}
