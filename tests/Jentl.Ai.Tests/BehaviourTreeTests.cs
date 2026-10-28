namespace Jentl.Ai.Tests;

public class BehaviourTreeTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);

    private sealed class Goblin
    {
        public int Health { get; set; } = 10;

        public bool SeesPlayer { get; set; }

        public List<string> Did { get; } = [];
    }

    [Fact]
    public void ASelector_TakesTheFirstPlanThatWorks_AndASequenceItsSteps()
    {
        var tree = new BehaviourTree<Goblin>(Bt.Selector(
            Bt.Sequence(Bt.If<Goblin>(g => g.SeesPlayer), Bt.Do<Goblin>(g => g.Did.Add("attack"))),
            Bt.Do<Goblin>(g => g.Did.Add("patrol"))));
        var goblin = new Goblin();

        tree.Tick(goblin, Frame).Should().Be(Status.Success);
        goblin.SeesPlayer = true;
        tree.Tick(goblin, Frame);

        goblin.Did.Should().Equal("patrol", "attack");
    }

    [Fact]
    public void ARunningSequence_PicksUpWhereItWas()
    {
        var steps = 0;
        var tree = new BehaviourTree<Goblin>(Bt.Sequence(
            Bt.Do<Goblin>(_ => steps++),
            Bt.Wait<Goblin>(TimeSpan.FromMilliseconds(250)),
            Bt.Do<Goblin>(g => g.Did.Add("done"))));
        var goblin = new Goblin();

        tree.Tick(goblin, Frame).Should().Be(Status.Running);
        tree.Tick(goblin, Frame).Should().Be(Status.Running);
        tree.Tick(goblin, Frame).Should().Be(Status.Success);

        steps.Should().Be(1, "the first step is not done again while waiting");
        goblin.Did.Should().Equal("done");
    }

    [Fact]
    public void AHigherPriority_TakesOverFromARunningBranch()
    {
        var goblin = new Goblin();
        var patrolled = 0;
        var tree = new BehaviourTree<Goblin>(Bt.Selector(
            Bt.Sequence(Bt.If<Goblin>(g => g.SeesPlayer), Bt.Do<Goblin>(g => g.Did.Add("attack"))),
            Bt.Do<Goblin>((_, _) =>
            {
                patrolled++;
                return Status.Running;
            })));

        tree.Tick(goblin, Frame).Should().Be(Status.Running);
        tree.Tick(goblin, Frame);
        goblin.SeesPlayer = true;
        tree.Tick(goblin, Frame).Should().Be(Status.Success);

        patrolled.Should().Be(2);
        goblin.Did.Should().Equal("attack");
    }

    [Fact]
    public void While_DropsARunningPlan_WhenItsConditionFails_AndItStartsAfreshNextTime()
    {
        var goblin = new Goblin { SeesPlayer = true };
        var tree = new BehaviourTree<Goblin>(Bt.Selector(
            Bt.While<Goblin>(g => g.SeesPlayer, Bt.Sequence(Bt.Wait<Goblin>(TimeSpan.FromMilliseconds(300)), Bt.Do<Goblin>(g => g.Did.Add("pounce")))),
            Bt.Do<Goblin>(g => g.Did.Add("idle"))));

        tree.Tick(goblin, Frame);
        tree.Tick(goblin, Frame);
        goblin.SeesPlayer = false;
        tree.Tick(goblin, Frame);
        goblin.SeesPlayer = true;
        tree.Tick(goblin, Frame);
        tree.Tick(goblin, Frame);

        goblin.Did.Should().Equal("idle");
        tree.Tick(goblin, Frame);
        goblin.Did.Should().Equal("idle", "pounce");
    }

    [Fact]
    public void Best_RunsTheHighestScore_AndNothingAtZero()
    {
        var goblin = new Goblin();
        var tree = new BehaviourTree<Goblin>(Bt.Best<Goblin>(
            (g => g.SeesPlayer ? 0.6f : 0, Bt.Do<Goblin>(g => g.Did.Add("fight"))),
            (g => (10 - g.Health) / 10f, Bt.Do<Goblin>(g => g.Did.Add("flee")))));

        tree.Tick(goblin, Frame).Should().Be(Status.Failure, "nothing scores");
        goblin.SeesPlayer = true;
        tree.Tick(goblin, Frame);
        goblin.Health = 2;
        tree.Tick(goblin, Frame);

        goblin.Did.Should().Equal("fight", "flee");
    }

    [Fact]
    public void Best_StaysWithARunningChoice_UnlessAnotherIsClearlyBetter()
    {
        var goblin = new Goblin { SeesPlayer = true, Health = 5 };
        var tree = new BehaviourTree<Goblin>(Bt.Best<Goblin>(
            0.2f,
            (g => 0.5f, Bt.Sequence(Bt.Do<Goblin>(g => g.Did.Add("eat")), Bt.Wait<Goblin>(TimeSpan.FromSeconds(1)))),
            (g => (10 - g.Health) / 10f, Bt.Sequence(Bt.Do<Goblin>(g => g.Did.Add("rest")), Bt.Wait<Goblin>(TimeSpan.FromSeconds(1))))));

        tree.Tick(goblin, Frame);
        goblin.Health = 4;
        tree.Tick(goblin, Frame);
        goblin.Did.Should().Equal(["eat"], "0.6 is not 0.2 better than 0.5");

        goblin.Health = 2;
        tree.Tick(goblin, Frame);
        goblin.Did.Should().Equal("eat", "rest");
    }

    [Fact]
    public void Cooldown_RunsOut_EvenWhileItsBranchIsNotReached()
    {
        var goblin = new Goblin();
        var tree = new BehaviourTree<Goblin>(Bt.Selector(
            Bt.Cooldown(TimeSpan.FromMilliseconds(500), Bt.Sequence(Bt.If<Goblin>(g => g.SeesPlayer), Bt.Do<Goblin>(g => g.Did.Add("fireball")))),
            Bt.Do<Goblin>(g => g.Did.Add("swipe"))));

        goblin.SeesPlayer = true;
        tree.Tick(goblin, Frame);
        tree.Tick(goblin, Frame);
        for (var i = 0; i < 6; i++)
        {
            tree.Tick(goblin, Frame);
        }

        goblin.Did.Take(3).Should().Equal("fireball", "swipe", "swipe");
        goblin.Did.Count(d => d == "fireball").Should().Be(2, "ready again after 500 ms");
    }

    [Fact]
    public void Decorators()
    {
        var goblin = new Goblin();
        var count = 0;

        new BehaviourTree<Goblin>(Bt.Not(Bt.If<Goblin>(_ => true))).Tick(goblin, Frame).Should().Be(Status.Failure);
        new BehaviourTree<Goblin>(Bt.Try(Bt.If<Goblin>(_ => false))).Tick(goblin, Frame).Should().Be(Status.Success);
        new BehaviourTree<Goblin>(Bt.Repeat(3, Bt.Do<Goblin>(_ => count++))).Tick(goblin, Frame).Should().Be(Status.Success);
        count.Should().Be(3);
        new BehaviourTree<Goblin>(Bt.Do<Goblin>((_, elapsed) => elapsed == Frame ? Status.Running : Status.Failure)).Tick(goblin, Frame).Should().Be(Status.Running);
    }
}
