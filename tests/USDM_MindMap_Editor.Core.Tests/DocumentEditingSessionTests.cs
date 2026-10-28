using USDM_MindMap_Editor.Core.Editing;
using USDM_MindMap_Editor.Core.Layout;
using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Serialization;

namespace USDM_MindMap_Editor.Core.Tests;

[TestClass]
public sealed class DocumentEditingSessionTests
{
    [TestMethod]
    public void FailedPlacementDoesNotPublishNodeIdViewOrPartialPositions()
    {
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(CreateRoots(), layoutEngine: engine);
        session.MarkSaved();
        var document = session.Document;
        var layout = session.Layout;
        var yaml = session.Serialize();
        var revision = session.Revision;
        engine.Fail = true;

        Assert.ThrowsExactly<InvalidOperationException>(() => session.AddNode(LayoutNodeKind.TopLevelRequirement, "001", "追加"));
        Assert.AreSame(document, session.Document);
        Assert.AreSame(layout, session.Layout);
        Assert.AreEqual(yaml, session.Serialize());
        Assert.AreEqual(revision, session.Revision);
        Assert.IsFalse(session.HasUnsavedChanges);
        engine.Fail = false;
        Assert.AreEqual("001", session.AddNode(LayoutNodeKind.TopLevelRequirement, "001", "追加"));
    }

    [TestMethod]
    public void FailedReasonOrMeasurementChangeLeavesCommittedStateUnchanged()
    {
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(TestDocumentFactory.Create(), layoutEngine: engine);
        session.MarkSaved();
        var yaml = session.Serialize();
        var layout = session.Layout;
        engine.Fail = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => session.SetReasonExpanded("requirement:001", true));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.ApplyMeasuredSizes(
            new Dictionary<string, NodeSize> { ["requirement:001"] = new(280, 400) }));
        Assert.AreSame(layout, session.Layout);
        Assert.AreEqual(yaml, session.Serialize());
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    [TestMethod]
    public void AddingChildKeepsSourceAndCommitsChainedPositionsWithDocument()
    {
        var session = new DocumentEditingSession(CreateRoots());
        var source = session.Layout.BackgroundPositions["001"];
        var unrelated = session.Layout.BackgroundPositions["004"];
        session.AddNode(LayoutNodeKind.TopLevelRequirement, "001", "追加した要求");
        Assert.AreEqual(source, session.Layout.BackgroundPositions["001"]);
        Assert.AreEqual(unrelated, session.Layout.BackgroundPositions["004"]);
        CollectionAssert.AreEqual(new[] { "002", "003" }, session.AutomaticallyMovedBackgroundIds.ToArray());
        Assert.IsTrue(session.HasUnsavedChanges);
        AssertNonOverlapping(session.Layout);

        var restored = new DocumentEditingSession(new UsdmYamlSerializer().Deserialize(session.Serialize()));
        Assert.AreEqual(session.Serialize(), restored.Serialize());
        foreach (var (id, position) in session.Layout.BackgroundPositions)
            Assert.AreEqual(position, restored.Layout.BackgroundPositions[id]);
    }

    [TestMethod]
    public void PositionedBackgroundUsesNegativeFractionalOriginAndToolbarUsesVacantSpace()
    {
        var session = new DocumentEditingSession(new UsdmDocument());
        var id = session.AddNode(LayoutNodeKind.Background, null, "任意位置", backgroundPosition: new(-250.125, -100.75));
        Assert.AreEqual(new LayoutPoint(-250.125, -100.75), session.Layout.BackgroundPositions[id]);
        var second = session.AddNode(LayoutNodeKind.Background, null, "ツールバー");
        Assert.AreEqual(session.Settings.CanvasMargin, session.Layout.BackgroundPositions[second].X);
        AssertNonOverlapping(session.Layout);
        var restored = new DocumentEditingSession(new UsdmYamlSerializer().Deserialize(session.Serialize()));
        Assert.AreEqual(session.Serialize(), restored.Serialize());
    }

    [TestMethod]
    public void InvalidInputAndUnrepresentablePositionDoNotAllocateOrDirtyDocument()
    {
        var session = new DocumentEditingSession(new UsdmDocument());
        session.MarkSaved();
        var before = session.Serialize();
        Assert.Throws<ArgumentException>(() => session.AddNode(LayoutNodeKind.Background, null, " ", backgroundPosition: new(20, 20)));
        Assert.Throws<ArgumentException>(() => session.AddNode(LayoutNodeKind.Background, null, "背景", backgroundPosition: new(double.MaxValue, 0)));
        Assert.AreEqual(before, session.Serialize());
        Assert.IsFalse(session.HasUnsavedChanges);
        Assert.AreEqual("001", session.AddNode(LayoutNodeKind.Background, null, "背景"));
    }

    [TestMethod]
    public void InvalidStaleAndNoOpMoveDoNotChangeSavedStateOrRevision()
    {
        var session = new DocumentEditingSession(CreateRoots());
        session.MarkSaved();
        var before = session.Serialize();
        var revision = session.Revision;
        var layout = session.Layout;
        Assert.IsFalse(session.MoveBackground("001", new(0, 150), revision).IsValid);
        Assert.IsFalse(session.MoveBackground("001", new(-1000, -50), revision - 1).IsValid);
        var noOp = session.MoveBackground("001", layout.BackgroundPositions["001"], revision);
        Assert.IsTrue(noOp.IsValid);
        Assert.IsFalse(noOp.HasMoved);
        Assert.AreSame(layout, session.Layout);
        Assert.AreEqual(before, session.Serialize());
        Assert.AreEqual(revision, session.Revision);
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    [TestMethod]
    public void ValidMoveNeverCallsLayoutEngineAndPreservesAllRelativeGeometry()
    {
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(TestDocumentFactory.Create(), layoutEngine: engine);
        session.MarkSaved();
        var before = session.Layout;
        var count = engine.Calls;
        engine.Fail = true;
        var candidate = new LayoutPoint(-123.5, -600.25);
        var result = session.MoveBackground("001", candidate, session.Revision);
        Assert.IsTrue(result.IsValid && result.HasMoved);
        Assert.AreEqual(count, engine.Calls);
        Assert.IsTrue(session.HasUnsavedChanges);
        var start = before.BackgroundPositions["001"];
        var dx = candidate.X - start.X;
        var dy = candidate.Y - start.Y;
        foreach (var node in before.Nodes)
            Assert.AreEqual(node.Bounds.Translate(dx, dy), session.Layout.NodesByKey[node.Key].Bounds);
        for (var i = 0; i < before.Edges.Count; i++)
            Assert.AreEqual(before.Edges[i].Translate(dx, dy), session.Layout.Edges[i]);
        var restored = new DocumentEditingSession(new UsdmYamlSerializer().Deserialize(session.Serialize()));
        Assert.AreEqual(candidate, restored.Layout.BackgroundPositions["001"]);
    }

    [TestMethod]
    public void ReasonExpansionAndMeasuredGrowthMoveOthersButShrinkDoesNotCompact()
    {
        var document = CreateRoots();
        document.Requirements.Add(new() { Id = "001", ParentBackgroundId = "001", Request = "要求", Reason = string.Join('\n', Enumerable.Repeat("長い理由", 20)) });
        document.View.BackgroundPositions["002"].Y = 350;
        document.View.BackgroundPositions["003"].Y = 492;
        var session = new DocumentEditingSession(document);
        session.SetReasonExpanded("requirement:001", true);
        var expanded = session.Layout.BackgroundPositions["002"];
        Assert.IsGreaterThan(350, expanded.Y);
        session.SetReasonExpanded("requirement:001", false);
        Assert.AreEqual(expanded, session.Layout.BackgroundPositions["002"]);
        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize> { ["requirement:001"] = new(280, 1500) });
        var measured = session.Layout.BackgroundPositions["002"];
        Assert.IsGreaterThan(expanded.Y, measured.Y);
        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize> { ["requirement:001"] = new(280, 110) });
        Assert.AreEqual(measured, session.Layout.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(0, 0), session.Layout.BackgroundPositions["001"]);
        AssertNonOverlapping(session.Layout);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReasonExpansionOnlyMarksDirtyWhenItMovesAnotherBackground(bool hasCollidingBackground)
    {
        var document = new UsdmDocument
        {
            Backgrounds = [new() { Id = "001", Text = "背景" }],
            Requirements = [new() { Id = "001", ParentBackgroundId = "001", Request = "要求", Reason = string.Join('\n', Enumerable.Repeat("理由の説明", 20)) }],
            View = new() { BackgroundPositions = new() { ["001"] = new() { X = 0, Y = 0 } } }
        };
        if (hasCollidingBackground)
        {
            document.Backgrounds.Add(new() { Id = "002", Text = "後続の背景" });
            document.View.BackgroundPositions["002"] = new() { X = 0, Y = 500 };
        }
        var session = new DocumentEditingSession(document);
        session.MarkSaved();

        Assert.IsTrue(session.SetReasonExpanded("requirement:001", true));

        Assert.AreEqual(hasCollidingBackground, session.HasUnsavedChanges);
        Assert.IsTrue(session.Document.View.Nodes["requirement:001"].ReasonExpanded);
        var restored = new UsdmYamlSerializer().Deserialize(session.Serialize());
        Assert.IsTrue(restored.View.Nodes["requirement:001"].ReasonExpanded);
        if (hasCollidingBackground) CollectionAssert.AreEqual(new[] { "002" }, session.AutomaticallyMovedBackgroundIds.ToArray());
        else Assert.IsEmpty(session.AutomaticallyMovedBackgroundIds);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MixedMeasurementBatchKeepsNodeOperationSourceFixed(bool changeReason)
    {
        var document = CreateRoots();
        if (changeReason)
            document.Requirements.Add(new() { Id = "001", ParentBackgroundId = "002", Request = "要求", Reason = "追加の説明" });
        var session = new DocumentEditingSession(document);
        if (changeReason) session.SetReasonExpanded("requirement:001", true);
        else session.AddNode(LayoutNodeKind.TopLevelRequirement, "002", "追加した要求");
        var source = session.Layout.BackgroundPositions["002"];

        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(280, 400),
            ["requirement:001"] = new(280, 500)
        });

        Assert.AreEqual(source, session.Layout.BackgroundPositions["002"]);
        // 001 was above the fixed source, so there is no explicit right/below
        // relationship. The directional fallback now chooses the shorter right
        // displacement instead of the former unconditional downward move.
        var sourceBounds = session.Layout.Trees.Single(tree => tree.BackgroundId == "002").Bounds;
        Assert.AreEqual(sourceBounds.Right + session.Settings.SiblingGap, session.Layout.BackgroundPositions["001"].X);
        Assert.AreEqual(0, session.Layout.BackgroundPositions["001"].Y);
        Assert.DoesNotContain("002", session.AutomaticallyMovedBackgroundIds);
        AssertNonOverlapping(session.Layout);
    }

    [TestMethod]
    public void InitialMixedMeasurementBatchStillUsesSavedYAndIdPrecedence()
    {
        var document = CreateRoots();
        document.View.BackgroundPositions["001"].Y = 142;
        document.View.BackgroundPositions["002"].Y = 0;
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(document, layoutEngine: engine);

        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(280, 500),
            ["background:002"] = new(280, 400)
        });

        Assert.IsNull(engine.LastAnchor);
        Assert.AreEqual(new LayoutPoint(0, 0), session.Layout.BackgroundPositions["002"]);
        Assert.AreEqual(new LayoutPoint(0, 432), session.Layout.BackgroundPositions["001"]);
        AssertNonOverlapping(session.Layout);
    }

    [TestMethod]
    public void PendingMeasurementSourceSurvivesUnrelatedBatchesAndFailureThenClearsOnSuccess()
    {
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(CreateRoots(), layoutEngine: engine);
        session.AddNode(LayoutNodeKind.TopLevelRequirement, "002", "追加した要求");
        var source = session.Layout.BackgroundPositions["002"];
        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize> { ["background:004"] = new(280, 400) });
        Assert.AreEqual("004", engine.LastAnchor);
        var changes = new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(280, 400), ["requirement:001"] = new(280, 500)
        };
        var before = session.Serialize();
        var revision = session.Revision;
        engine.Fail = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => session.ApplyMeasuredSizes(changes));
        Assert.AreEqual(before, session.Serialize());
        Assert.AreEqual(revision, session.Revision);

        engine.Fail = false;
        session.ApplyMeasuredSizes(changes);
        Assert.AreEqual("002", engine.LastAnchor);
        Assert.AreEqual(source, session.Layout.BackgroundPositions["002"]);

        // Once the operation's own measurement is committed, subsequent mixed
        // corrections are independent and must not inherit the old source.
        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(280, 410), ["requirement:001"] = new(280, 510)
        });
        Assert.IsNull(engine.LastAnchor);
        AssertNonOverlapping(session.Layout);
    }

    [TestMethod]
    public void FailedNewOperationDoesNotReplacePendingMeasurementSource()
    {
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(CreateRoots(), layoutEngine: engine);
        session.AddNode(LayoutNodeKind.TopLevelRequirement, "002", "追加した要求");
        engine.Fail = true;
        Assert.ThrowsExactly<InvalidOperationException>(() => session.AddNode(LayoutNodeKind.TopLevelRequirement, "004", "失敗する要求"));
        engine.Fail = false;

        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(280, 400), ["requirement:001"] = new(280, 500)
        });

        Assert.AreEqual("002", engine.LastAnchor);
    }

    [TestMethod]
    public void SuccessfulMoveEndsThePriorOperationsMeasurementPriority()
    {
        var engine = new ControlledLayoutEngine();
        var session = new DocumentEditingSession(CreateRoots(), layoutEngine: engine);
        session.AddNode(LayoutNodeKind.TopLevelRequirement, "002", "追加した要求");
        Assert.IsTrue(session.MoveBackground("002", new(-1000, -100), session.Revision).HasMoved);

        session.ApplyMeasuredSizes(new Dictionary<string, NodeSize>
        {
            ["background:001"] = new(280, 400), ["requirement:001"] = new(280, 500)
        });

        Assert.IsNull(engine.LastAnchor);
    }

    [TestMethod]
    public void LegacyViewAndMissingPositionsAreCompletedBeforeStrictVersionThreeSave()
    {
        const string yaml = """
            version: "0.1.0"
            format_version: 2
            backgrounds:
              - id: "001"
                text: 背景
            view:
              zoom: 0.25
              pan:
                x: -30.125
                y: 80.5
            """;
        var session = new DocumentEditingSession(new UsdmYamlSerializer().Deserialize(yaml));
        Assert.AreEqual(.25, session.Document.View.Zoom);
        Assert.AreEqual(-30.125, session.Document.View.Pan.X);
        Assert.AreEqual(80.5, session.Document.View.Pan.Y);
        CollectionAssert.AreEqual(new[] { "001" }, session.InitializedBackgroundIds.ToArray());
        StringAssert.Contains(session.Serialize(), "format_version: 3");
        Assert.AreEqual(session.Serialize(), new DocumentEditingSession(new UsdmYamlSerializer().Deserialize(session.Serialize())).Serialize());
    }

    [TestMethod]
    public void LoadingOverlapReportsCorrectionAndDoesNotMutateInputDocument()
    {
        var document = CreateRoots();
        document.View.BackgroundPositions["002"].Y = 20;
        var session = new DocumentEditingSession(document);
        CollectionAssert.AreEqual(new[] { "002" }, session.AutomaticallyMovedBackgroundIds.ToArray());
        Assert.AreEqual(20, document.View.BackgroundPositions["002"].Y);
        Assert.IsTrue(session.HasUnsavedChanges);
        AssertNonOverlapping(session.Layout);
    }

    [TestMethod]
    public void RemovedBackgroundPositionIsPrunedWithoutAddingDeleteUi()
    {
        var document = CreateRoots();
        document.Backgrounds.RemoveAll(x => x.Id == "003");
        var session = new DocumentEditingSession(document);
        Assert.DoesNotContain("003", session.Document.View.BackgroundPositions.Keys);
        Assert.DoesNotContain("003", new UsdmYamlSerializer().Deserialize(session.Serialize()).View.BackgroundPositions.Keys);
    }

    private static UsdmDocument CreateRoots() => new()
    {
        Backgrounds = Enumerable.Range(1, 4).Select(i => new BackgroundItem { Id = $"{i:D3}", Text = $"背景{i}" }).ToList(),
        View = new()
        {
            BackgroundPositions = new()
            {
                ["001"] = new() { X = 0, Y = 0 }, ["002"] = new() { X = 0, Y = 142 },
                ["003"] = new() { X = 0, Y = 284 }, ["004"] = new() { X = 2000, Y = 0 }
            }
        }
    };

    private static void AssertNonOverlapping(LayoutResult layout)
    {
        foreach (var tree in layout.Trees)
            foreach (var other in layout.Trees.Where(other => other.BackgroundId != tree.BackgroundId))
                Assert.IsFalse(tree.Bounds.Intersects(other.Bounds));
    }

    private sealed class ControlledLayoutEngine : ILayoutEngine
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public string? LastAnchor { get; private set; }
        public LayoutResult Calculate(LayoutRequest request)
        {
            Calls++;
            LastAnchor = request.AnchoredBackgroundId;
            if (Fail) throw new InvalidOperationException("配置計算失敗の注入");
            return new AutoLayoutService().Calculate(request);
        }
    }
}
