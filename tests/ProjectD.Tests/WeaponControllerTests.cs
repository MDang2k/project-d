using Xunit;

namespace ProjectD.Tests;

public class WeaponControllerTests
{
	private static readonly AmmoType Ball = new("ball", 50f, 3, 700f);

	private static Magazine Mag(int rounds, int capacity = 30)
	{
		var m = new Magazine(capacity);
		for (var i = 0; i < rounds; i++)
			m.Push(Ball);
		return m;
	}

	private static WeaponController Make(int inMag = 10, bool chambered = true)
	{
		var state = new WeaponState(new FireControl(new[] { FireMode.Semi }, 600f));
		state.InsertMagazine(Mag(inMag));
		if (chambered)
			state.ChamberRound();
		return new WeaponController(state, ActionTuning.Default);
	}

	private static void Run(WeaponController c, float seconds)
	{
		for (var t = 0f; t < seconds; t += 0.01f)
			c.Update(0.01f);
	}

	[Theory]
	[InlineData(0, MagEstimate.Empty)]
	[InlineData(3, MagEstimate.AlmostEmpty)]
	[InlineData(15, MagEstimate.AboutHalf)]
	[InlineData(22, MagEstimate.AboutHalf)]
	[InlineData(23, MagEstimate.Full)]
	[InlineData(30, MagEstimate.Full)]
	public void EstimateThresholds(int rounds, MagEstimate expected)
		=> Assert.Equal(expected, WeaponController.Estimate(Mag(rounds), ActionTuning.Default));

	[Fact]
	public void TacticalReloadKeepsOldMagazine()
	{
		var c = Make(inMag: 5);
		c.Pouch.Add(Mag(30));
		Assert.True(c.StartReload(fast: false));
		Assert.True(c.IsBusy);
		Run(c, 3f);
		Assert.False(c.IsBusy);
		Assert.Equal(30, c.State.Magazine!.Count);
		Assert.Equal(1, c.Pouch.Count);
	}

	[Fact]
	public void FastReloadDropsOldMagazine()
	{
		var c = Make(inMag: 5);
		c.Pouch.Add(Mag(30));
		Magazine? dropped = null;
		c.MagazineDropped += m => dropped = m;
		Assert.True(c.StartReload(fast: true));
		Run(c, 2f);
		Assert.NotNull(dropped);
		Assert.Equal(0, c.Pouch.Count);
	}

	[Fact]
	public void FastReloadIsFasterThanTactical()
	{
		var tac = Make();
		tac.Pouch.Add(Mag(30));
		tac.StartReload(false);
		var fast = Make();
		fast.Pouch.Add(Mag(30));
		fast.StartReload(true);
		Run(tac, 2.0f);
		Run(fast, 2.0f);
		Assert.True(tac.IsBusy);
		Assert.False(fast.IsBusy);
	}

	[Fact]
	public void EmptyChamberAddsChargeTime()
	{
		var withRound = Make(inMag: 5, chambered: true);
		withRound.Pouch.Add(Mag(30));
		withRound.StartReload(true);
		var dry = Make(inMag: 0, chambered: false);
		dry.Pouch.Add(Mag(30));
		dry.StartReload(true);
		Run(withRound, 1.7f);
		Run(dry, 1.7f);
		Assert.False(withRound.IsBusy);
		Assert.True(dry.IsBusy);
		Run(dry, 1f);
		Assert.NotNull(dry.State.Chamber);
	}

	[Fact]
	public void ReloadPicksFullestSpare()
	{
		var c = Make();
		c.Pouch.Add(Mag(4));
		c.Pouch.Add(Mag(28));
		c.Pouch.Add(Mag(12));
		c.StartReload(false);
		Run(c, 3f);
		Assert.Equal(28, c.State.Magazine!.Count);
	}

	[Fact]
	public void NoReloadWithoutUsableSpare()
	{
		var c = Make();
		Assert.False(c.StartReload(false));
		c.Pouch.Add(Mag(0));
		Assert.False(c.StartReload(false));
		Assert.Equal(1, c.Pouch.Count);
	}

	[Fact]
	public void DoubleTapUpgradesOnlyBeforeEject()
	{
		var c = Make();
		c.Pouch.Add(Mag(30));
		c.StartReload(false);
		Run(c, 0.2f);
		Assert.True(c.UpgradeToFast());
		Assert.Equal(WeaponActionKind.FastReload, c.Action);

		var late = Make();
		late.Pouch.Add(Mag(30));
		late.StartReload(false);
		Run(late, 1.5f);
		Assert.False(late.UpgradeToFast());
	}

	[Fact]
	public void MagCheckReportsEstimateOrExact()
	{
		var c = Make(inMag: 15);
		MagCheckResult? r = null;
		c.MagChecked += x => r = x;
		Assert.True(c.StartMagCheck());
		Assert.False(c.StartMagCheck());
		Run(c, 1.5f);
		Assert.Equal(MagEstimate.AboutHalf, r!.Value.Estimate);
		Assert.Null(r.Value.ExactCount);

		c.ExactMagCheck = true;
		c.StartMagCheck();
		Run(c, 1.5f);
		Assert.Equal(14, r.Value.ExactCount); // 15 loaded, one now chambered
	}

	[Fact]
	public void MagCheckWithoutMagazine()
	{
		var state = new WeaponState(new FireControl(new[] { FireMode.Semi }, 600f));
		var c = new WeaponController(state, ActionTuning.Default);
		MagCheckResult? r = null;
		c.MagChecked += x => r = x;
		c.StartMagCheck();
		Run(c, 1.5f);
		Assert.Equal(MagEstimate.None, r!.Value.Estimate);
	}

	[Fact]
	public void ChamberCheckReportsRound()
	{
		var c = Make(chambered: true);
		bool? loaded = null;
		c.ChamberChecked += x => loaded = x;
		c.StartChamberCheck();
		Run(c, 1.2f);
		Assert.True(loaded);

		var empty = Make(inMag: 0, chambered: false);
		empty.ChamberChecked += x => loaded = x;
		empty.StartChamberCheck();
		Run(empty, 1.2f);
		Assert.False(loaded);
	}
}

public class WeaponPoseModelTests
{
	[Fact]
	public void EnvelopeRisesHoldsAndFalls()
	{
		Assert.Equal(0f, WeaponPoseModel.Envelope(0f), 3);
		Assert.Equal(1f, WeaponPoseModel.Envelope(0.5f), 3);
		Assert.Equal(0f, WeaponPoseModel.Envelope(1f), 3);
	}

	[Fact]
	public void NoPoseWhenIdle()
	{
		var (pos, rot) = WeaponPoseModel.Pose(WeaponActionKind.None, 0.5f);
		Assert.Equal(Godot.Vector3.Zero, pos);
		Assert.Equal(Godot.Vector3.Zero, rot);
	}
}
