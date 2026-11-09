using System;
using System.Linq;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // static structure compile.
    public sealed class TimelineProgramTests
    {
        static int Node(TimelineProgram p, int item) => p.NodeOfItem(item);

        static void AssertRef(TimelineProgram p, int node, TimelineRefKind kind, int target = -1)
        {
            Assert.That(p.NodeRefKind(node), Is.EqualTo(kind), "ref kind");
            Assert.That(p.NodeRefTarget(node), Is.EqualTo(target), "ref target");
        }

        static bool HasIssue(TimelineProgram p, TimelineIssueCode code, int item)
            => p.Issues.ToArray().Any(i => i.Code == code && i.Item == item);

        static int[] Members(TimelineProgram p, int scope) => p.ScopeMembers(scope).ToArray();

        static int[] Dependents(TimelineProgram p, int evt) => p.Dependents(evt).ToArray();

        [Test]
        public void WithPrevious_ChainsOnStart_FirstOnScopeStart()
        {
            var p = Compile(Fx(), Fx(delay: 0.2f));
            AssertRef(p, Node(p, 0), TimelineRefKind.ScopeStart);
            AssertRef(p, Node(p, 1), TimelineRefKind.StartOf, Node(p, 0));
            Assert.That(p.NodeDelay(Node(p, 1)), Is.EqualTo(0.2f));
            Assert.That(Dependents(p, p.ScopeStartEvent(TimelineProgram.RootScope)), Is.EqualTo(new[] { Node(p, 0) }));
            Assert.That(Dependents(p, TimelineProgram.StartEvent(Node(p, 0))), Is.EqualTo(new[] { Node(p, 1) }));
            Assert.That(p.Issues.Length, Is.Zero);
        }

        [Test]
        public void AfterPrevious_WaitsOnEnd()
        {
            var p = Compile(Fx(), Fx(StartRule.AfterPrevious));
            AssertRef(p, Node(p, 1), TimelineRefKind.EndOf, Node(p, 0));
            Assert.That(Dependents(p, TimelineProgram.EndEvent(Node(p, 0))), Is.EqualTo(new[] { Node(p, 1) }));
        }

        [Test]
        public void AfterAll_WaitsOnEveryPriorEnd()
        {
            var p = Compile(Fx(), Fx(), Fx(StartRule.AfterAll));
            int c = Node(p, 2);
            AssertRef(p, c, TimelineRefKind.AllPrior);
            Assert.That(p.NodeWaitCount(c), Is.EqualTo(2));
            Assert.That(Dependents(p, TimelineProgram.EndEvent(Node(p, 0))), Does.Contain(c));
            Assert.That(Dependents(p, TimelineProgram.EndEvent(Node(p, 1))), Does.Contain(c));
        }

        [Test]
        public void AfterAll_WithNothingBefore_IsScopeStart()
        {
            var p = Compile(Fx(StartRule.AfterAll));
            AssertRef(p, Node(p, 0), TimelineRefKind.ScopeStart);
            Assert.That(p.NodeWaitCount(Node(p, 0)), Is.EqualTo(1));
        }

        [Test]
        public void R2_WithPreviousAfterWait_StartsAtWaitEnd()
        {
            var p = Compile(Fx(), Wait(0.5f), Fx());
            AssertRef(p, Node(p, 2), TimelineRefKind.EndOf, Node(p, 1));

            var s = Compile(Fx(), Signal(2f), Fx());
            AssertRef(s, Node(s, 2), TimelineRefKind.EndOf, Node(s, 1));
        }

        [Test]
        public void R2_RepeatedWait_IsStillAWait()
        {
            var p = Compile(Repeat(Wait(0.5f), 1), Fx());
            int block = p.NodeBlock(p.ScopeMembers(TimelineProgram.RootScope)[0]);
            AssertRef(p, Node(p, 1), TimelineRefKind.EndOf, p.BlockNode(block));
        }

        [Test]
        public void R4_Hold_TurnsNextIntoAfterAllAndAddsDelay()
        {
            var p = Compile(Fx(), Fx(), Hold(0.2f), Fx(delay: 0.1f));
            int next = Node(p, 3);
            Assert.That(p.NodeRule(next), Is.EqualTo(StartRule.AfterAll));
            Assert.That(p.NodeDelay(next), Is.EqualTo(0.1f), "own delay");
            Assert.That(p.NodeHoldDelay(next), Is.EqualTo(0.2f), "Hold delay kept apart");
            AssertRef(p, next, TimelineRefKind.AllPrior);
            Assert.That(p.NodeWaitCount(next), Is.EqualTo(2));
            Assert.That(p.NodeOfItem(2), Is.EqualTo(-1), "Hold takes no place");
        }

        [Test]
        public void R4_ConsecutiveHolds_AddDelays()
        {
            var p = Compile(Fx(), Hold(0.1f), Hold(0.2f), Fx());
            Assert.That(p.NodeRule(Node(p, 3)), Is.EqualTo(StartRule.AfterAll));
            Assert.That(p.NodeDelay(Node(p, 3)), Is.Zero);
            Assert.That(p.NodeHoldDelay(Node(p, 3)), Is.EqualTo(0.3f).Within(1e-6f));
            Assert.That(p.Issues.Length, Is.Zero);
        }

        [Test]
        public void R4_HoldWithoutNext_IsReported()
        {
            var last = Compile(Fx(), Hold());
            Assert.That(HasIssue(last, TimelineIssueCode.HoldWithoutNext, 1));

            var beforeLoopEnd = Compile(LoopStart(), Fx(), Hold(), LoopEnd(), Fx());
            Assert.That(HasIssue(beforeLoopEnd, TimelineIssueCode.HoldWithoutNext, 2));
            Assert.That(beforeLoopEnd.NodeRule(Node(beforeLoopEnd, 4)), Is.EqualTo(StartRule.WithPrevious), "Hold does not leak out of the block");
        }

        [Test]
        public void R4_HoldBeforeLoop_AppliesToTheBlock()
        {
            var p = Compile(Fx(), Hold(0.2f), LoopStart(delay: 0.1f), Fx(), LoopEnd());
            int blockNode = p.BlockNode(0);
            Assert.That(p.NodeRule(blockNode), Is.EqualTo(StartRule.AfterAll));
            Assert.That(p.NodeDelay(blockNode), Is.EqualTo(0.1f));
            Assert.That(p.NodeHoldDelay(blockNode), Is.EqualTo(0.2f));
            Assert.That(p.NodeScaleItem(blockNode), Is.EqualTo(2), "Loop Start supplies the delay scale");
        }

        [Test]
        public void R6_ItemRepeat_IsAOneItemBlock()
        {
            var p = Compile(Fx(), Repeat(Fx(StartRule.AfterPrevious, delay: 0.3f, duration: 0.2f), 2, gap: 0.5f));
            Assert.That(p.BlockCount, Is.EqualTo(1));
            Assert.That(p.BlockIsSynthetic(0), Is.True);
            Assert.That(p.BlockIterations(0), Is.EqualTo(3));
            Assert.That(p.BlockGap(0), Is.EqualTo(0.5f));

            int blockNode = p.BlockNode(0);
            Assert.That(p.NodeRule(blockNode), Is.EqualTo(StartRule.AfterPrevious));
            Assert.That(p.NodeDelay(blockNode), Is.EqualTo(0.3f));
            AssertRef(p, blockNode, TimelineRefKind.EndOf, Node(p, 0));

            int inner = Node(p, 1);
            Assert.That(p.NodeScope(inner), Is.EqualTo(TimelineProgram.BlockScope(0)));
            Assert.That(p.NodeDelay(inner), Is.Zero);
            AssertRef(p, inner, TimelineRefKind.ScopeStart);
            Assert.That(Members(p, TimelineProgram.RootScope), Is.EqualTo(new[] { Node(p, 0), blockNode }));
        }

        [Test]
        public void R6_InfiniteRepeat_IsAnInfiniteBlock()
        {
            var p = Compile(Repeat(Fx(), -1));
            Assert.That(p.BlockIterations(0), Is.Zero);
            Assert.That(p.IsInfinite, Is.True);
            Assert.That(p.IsSeekable, Is.False);
        }

        [Test]
        public void R6_ReferenceToRepeatedItem_IsTheBlock()
        {
            var p = Compile(Repeat(Fx(), 1), Fx(StartRule.AfterPrevious), Fx());
            int blockNode = p.BlockNode(0);
            AssertRef(p, Node(p, 1), TimelineRefKind.EndOf, blockNode);

            var w = Compile(Repeat(Fx(), 1), Fx());
            AssertRef(w, Node(w, 1), TimelineRefKind.StartOf, w.BlockNode(0));
        }

        [Test]
        public void R5_LoopBlock_ScopesItsItems()
        {
            var p = Compile(LoopStart(StartRule.AfterPrevious, 0.1f), Fx(), Fx(), LoopEnd(3, 0.25f));
            Assert.That(p.BlockCount, Is.EqualTo(1));
            Assert.That(p.BlockIsSynthetic(0), Is.False);
            Assert.That(p.BlockIterations(0), Is.EqualTo(3));
            Assert.That(p.BlockGap(0), Is.EqualTo(0.25f));
            Assert.That(p.BlockDepth(0), Is.EqualTo(1));
            int blockNode = p.BlockNode(0);
            Assert.That(p.NodeRule(blockNode), Is.EqualTo(StartRule.AfterPrevious));
            Assert.That(p.NodeDelay(blockNode), Is.EqualTo(0.1f));
            Assert.That(Members(p, TimelineProgram.BlockScope(0)), Is.EqualTo(new[] { Node(p, 1), Node(p, 2) }));
            Assert.That(p.Issues.Length, Is.Zero);
        }

        [Test]
        public void R5_NestingDepthFour_IsKept()
        {
            var p = Compile(
                LoopStart(), LoopStart(), LoopStart(), LoopStart(), Fx(), LoopEnd(), LoopEnd(), LoopEnd(), LoopEnd());
            Assert.That(p.BlockCount, Is.EqualTo(4));
            Assert.That(p.NodeScope(Node(p, 4)), Is.EqualTo(TimelineProgram.BlockScope(3)));
            Assert.That(p.BlockDepth(3), Is.EqualTo(4));
            Assert.That(p.Issues.Length, Is.Zero);
        }

        [Test]
        public void R5_NestingDepthFive_DropsTheInnermost()
        {
            var p = Compile(
                LoopStart(), LoopStart(), LoopStart(), LoopStart(), LoopStart(), Fx(),
                LoopEnd(), LoopEnd(), LoopEnd(), LoopEnd(), LoopEnd());
            Assert.That(p.BlockCount, Is.EqualTo(4));
            Assert.That(HasIssue(p, TimelineIssueCode.LoopTooDeep, 4));
            Assert.That(p.NodeScope(Node(p, 5)), Is.EqualTo(TimelineProgram.BlockScope(3)), "items stay in the deepest kept block");
        }

        [Test]
        public void R5_UnpairedLoopStart_IsIgnored()
        {
            var p = Compile(Fx(), LoopStart(), Fx());
            Assert.That(p.BlockCount, Is.Zero);
            Assert.That(HasIssue(p, TimelineIssueCode.UnpairedLoopStart, 1));
            AssertRef(p, Node(p, 2), TimelineRefKind.StartOf, Node(p, 0));
        }

        [Test]
        public void R5_UnpairedLoopEnd_WrapsFromScopeStart()
        {
            var p = Compile(Fx(), Fx(), LoopEnd(2), Fx());
            Assert.That(p.BlockCount, Is.EqualTo(1));
            Assert.That(HasIssue(p, TimelineIssueCode.UnpairedLoopEnd, 2));
            int blockNode = p.BlockNode(0);
            Assert.That(Members(p, TimelineProgram.RootScope), Is.EqualTo(new[] { blockNode, Node(p, 3) }));
            Assert.That(Members(p, TimelineProgram.BlockScope(0)), Is.EqualTo(new[] { Node(p, 0), Node(p, 1) }));
            AssertRef(p, blockNode, TimelineRefKind.ScopeStart);
        }

        [Test]
        public void R5_TwoUnpairedLoopEnds_Nest()
        {
            var p = Compile(Fx(), LoopEnd(2), Fx(), LoopEnd(3));
            Assert.That(p.BlockCount, Is.EqualTo(2));
            int outer = p.BlockNode(0);
            Assert.That(p.BlockIterations(0), Is.EqualTo(3));
            Assert.That(Members(p, TimelineProgram.RootScope), Is.EqualTo(new[] { outer }));
            Assert.That(Members(p, TimelineProgram.BlockScope(0)), Is.EqualTo(new[] { p.BlockNode(1), Node(p, 2) }));
        }

        [Test]
        public void R1_PreviousDoesNotCrossBlockBoundaries()
        {
            var p = Compile(Fx(), LoopStart(), Fx(), LoopEnd(), Fx(), Fx(StartRule.AfterPrevious));
            int blockNode = p.BlockNode(0);
            AssertRef(p, blockNode, TimelineRefKind.StartOf, Node(p, 0));
            AssertRef(p, Node(p, 2), TimelineRefKind.ScopeStart);
            AssertRef(p, Node(p, 4), TimelineRefKind.StartOf, blockNode);
            AssertRef(p, Node(p, 5), TimelineRefKind.EndOf, Node(p, 4));
            Assert.That(Dependents(p, p.ScopeStartEvent(TimelineProgram.BlockScope(0))), Is.EqualTo(new[] { Node(p, 2) }));

            var after = Compile(LoopStart(), Fx(), LoopEnd(), Fx(StartRule.AfterPrevious));
            AssertRef(after, Node(after, 3), TimelineRefKind.EndOf, after.BlockNode(0));
        }

        [Test]
        public void R3_AfterAllSeesOnlyItsScope()
        {
            var p = Compile(Fx(), LoopStart(), Fx(), Fx(StartRule.AfterAll), LoopEnd());
            int c = Node(p, 3);
            AssertRef(p, c, TimelineRefKind.AllPrior);
            Assert.That(p.NodeWaitCount(c), Is.EqualTo(1));
            Assert.That(Dependents(p, TimelineProgram.EndEvent(Node(p, 0))), Does.Not.Contain(c));
        }

        [Test]
        public void R7_BackgroundIsSkippedByAfterPreviousAndAfterAll()
        {
            var p = Compile(Fx(), Background(Fx(duration: float.PositiveInfinity)), Fx(StartRule.AfterPrevious), Fx(StartRule.AfterAll));
            Assert.That(p.NodeBackground(Node(p, 1)), Is.True);
            AssertRef(p, Node(p, 2), TimelineRefKind.EndOf, Node(p, 0));
            Assert.That(p.NodeWaitCount(Node(p, 3)), Is.EqualTo(2));
            Assert.That(Dependents(p, TimelineProgram.EndEvent(Node(p, 1))), Is.Empty);
            Assert.That(p.IsInfinite, Is.False, "background infinity does not count");
            Assert.That(p.IsSeekable, Is.True);
        }

        [Test]
        public void R7_WithPreviousMayStartWithBackground()
        {
            var p = Compile(Fx(), Background(Fx()), Fx());
            AssertRef(p, Node(p, 2), TimelineRefKind.StartOf, Node(p, 1));
        }

        [Test]
        public void R7_BackgroundInLoop_IsForeground()
        {
            var p = Compile(Fx(), LoopStart(), Background(Fx()), LoopEnd());
            Assert.That(p.NodeBackground(Node(p, 2)), Is.False);
            Assert.That(HasIssue(p, TimelineIssueCode.BackgroundInLoop, 2));
        }

        [Test]
        public void R7_BackgroundOnFlowItem_IsIgnored()
        {
            var p = Compile(Fx(), Background(Wait(1f)));
            Assert.That(p.NodeBackground(Node(p, 1)), Is.False);
            Assert.That(HasIssue(p, TimelineIssueCode.BackgroundOnFlowItem, 1));
        }

        [Test]
        public void R7_WithoutForeground_BackgroundIsIgnored()
        {
            var p = Compile(Background(Fx()), Background(Fx()));
            Assert.That(p.NodeBackground(Node(p, 0)), Is.False);
            Assert.That(p.NodeBackground(Node(p, 1)), Is.False);
            Assert.That(HasIssue(p, TimelineIssueCode.BackgroundWithoutForeground, 0));
            Assert.That(HasIssue(p, TimelineIssueCode.BackgroundWithoutForeground, 1));
        }

        [Test]
        public void R7_RepeatedBackground_TheBlockCarriesTheMark()
        {
            var p = Compile(Fx(), Background(Repeat(Fx(), -1)));
            int blockNode = p.BlockNode(0);
            Assert.That(p.NodeBackground(blockNode), Is.True);
            Assert.That(p.NodeBackground(Node(p, 1)), Is.False);
            Assert.That(p.Issues.Length, Is.Zero);
            Assert.That(p.IsInfinite, Is.False, "an infinitely repeated background is not an infinite foreground");
        }

        [Test]
        public void R8_InfiniteForeground_MakesSuccessorsUnreachable()
        {
            var p = Compile(Fx(duration: float.PositiveInfinity), Fx(StartRule.AfterPrevious), Fx(), Fx(StartRule.AfterAll));
            Assert.That(p.IsInfinite, Is.True);
            Assert.That(p.IsSeekable, Is.False);
            Assert.That(HasIssue(p, TimelineIssueCode.UnreachableAfterInfinite, 1));
            Assert.That(HasIssue(p, TimelineIssueCode.UnreachableAfterInfinite, 2), Is.False, "follows an unreachable start, not the infinity itself");
            Assert.That(HasIssue(p, TimelineIssueCode.UnreachableAfterInfinite, 3));
        }

        [Test]
        public void R8_InfiniteLoop_IsInfinite()
        {
            var p = Compile(LoopStart(), Fx(), LoopEnd(0), Fx(StartRule.AfterPrevious));
            Assert.That(p.IsInfinite, Is.True);
            Assert.That(HasIssue(p, TimelineIssueCode.UnreachableAfterInfinite, 3));
        }

        [Test]
        public void R8_OpenSignalWait_IsInfiniteAndNotSeekable()
        {
            var open = Compile(Fx(), Signal());
            Assert.That(open.HasOpenItem, Is.True);
            Assert.That(open.IsInfinite, Is.True);
            Assert.That(open.IsSeekable, Is.False);
            Assert.That(open.ItemDuration(1), Is.EqualTo(float.PositiveInfinity));

            var timed = Compile(Fx(), Signal(2f));
            Assert.That(timed.HasOpenItem, Is.False);
            Assert.That(timed.IsInfinite, Is.False);
            Assert.That(timed.IsSeekable, Is.True);
            Assert.That(timed.ItemDuration(1), Is.EqualTo(2f));
        }

        [Test]
        public void Instant_HasNoDuration()
        {
            var p = Compile(Instant());
            Assert.That(p.ItemInstant(0), Is.True);
            Assert.That(p.ItemDuration(0), Is.Zero);
        }

        [Test]
        public void Summary_ClockMask_IncludesPlayerFollowAndGapClocks()
        {
            var p = Compile(
                Fx(),
                Clock(Fx(), TimelineClock.Player),
                LoopStart(), Fx(), Clock(LoopEnd(2, 0.1f), TimelineClock.Unscaled));
            Assert.That(p.ClockMask, Is.EqualTo((1 << (int)TimelineClock.Scaled) | (1 << (int)TimelineClock.Player) | (1 << (int)TimelineClock.Unscaled)));
            Assert.That(p.BlockClock(0), Is.EqualTo(TimelineClock.Unscaled));
        }

        [Test]
        public void EmptyProgram_Compiles()
        {
            var p = Compile();
            Assert.That(p.NodeCount, Is.Zero);
            Assert.That(p.IsInfinite, Is.False);
            Assert.That(p.IsSeekable, Is.True);
            Assert.That(Members(p, TimelineProgram.RootScope), Is.Empty);
        }
    }
}
