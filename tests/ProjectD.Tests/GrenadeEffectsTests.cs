using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

public class GrenadeEffectsTests
{
	// ---- stun

	[Fact]
	public void StunStrengthFallsWithDistanceAndHalvesWhenLookingAway()
	{
		Assert.Equal(1f, StunModel.Strength(0f, 5f, true), 3);
		Assert.Equal(0.5f, StunModel.Strength(2.5f, 5f, true), 3);
		Assert.Equal(0.25f, StunModel.Strength(2.5f, 5f, false), 3);
		Assert.Equal(0f, StunModel.Strength(5f, 5f, true));
		Assert.Equal(0.25f, StunModel.Strength(2.5f, 5f, true, 0.5f), 3); // the concussion's half-strength stun
	}

	[Fact]
	public void BlindLastsThreeToFiveSecondsAndDeafnessFour()
	{
		Assert.Equal(0f, StunModel.BlindS(0f));
		Assert.Equal(3.2f, StunModel.BlindS(0.1f), 3);
		Assert.Equal(5f, StunModel.BlindS(1f), 3);
		Assert.Equal(4f, StunModel.DeafnessS(0.5f));
		Assert.Equal(0f, StunModel.DeafnessS(0f));
	}

	// ---- burning

	[Fact]
	public void BurningDealsSixHpPerSecondSplitOverLegsAndTorsoForThreeSecondsAfter()
	{
		var b = new BurnStatus();
		Assert.False(b.IsBurning);
		Assert.Empty(b.Update(1f));
		b.Ignite();
		var hits = b.Update(1f);
		Assert.Equal(6f, hits.Sum(h => h.Damage), 3);
		Assert.Equal(1.5f, hits.First(h => h.Part == BodyPart.LeftLeg).Damage, 3);
		Assert.Contains(hits, h => h.Part == BodyPart.Thorax);
		Assert.True(b.IsBurning); // 2 s of the linger left
		var total = 6f;
		for (var i = 0; i < 4; i++)
			total += b.Update(1f).Sum(h => h.Damage);
		Assert.Equal(18f, total, 2); // 3 s of burn in all, then it stops
		Assert.False(b.IsBurning);
	}

	[Fact]
	public void StayingInFireKeepsRefreshingTheBurn()
	{
		var b = new BurnStatus();
		for (var i = 0; i < 10; i++)
		{
			b.Ignite();
			b.Update(0.5f);
		}
		Assert.True(b.IsBurning);
		b.Extinguish();
		Assert.False(b.IsBurning);
	}

	// ---- gas

	[Fact]
	public void GasSwayGoesFromOneToTwoAndAHalf()
	{
		Assert.Equal(1f, GasModel.SwayMultiplier(0f));
		Assert.Equal(2.5f, GasModel.SwayMultiplier(1f));
		Assert.Equal(1.75f, GasModel.SwayMultiplier(0.5f), 3);
	}

	// ---- thermite and locks

	[Fact]
	public void ThermiteBurnsThroughAfterTwentySecondsWithANoiseEveryFive()
	{
		var t = new ThermiteLock(false);
		var noises = 0;
		var through = false;
		t.Noise += () => noises++;
		t.BurnedThrough += () => through = true;
		t.Update(30f); // not started: nothing happens
		Assert.False(through);
		Assert.True(t.Start());
		Assert.False(t.Start());
		for (var i = 0; i < 19; i++)
			t.Update(1f);
		Assert.False(through);
		Assert.Equal(3, noises);
		t.Update(1.1f);
		Assert.True(through);
		Assert.True(t.Burnt);
		Assert.True(noises >= 4);
	}

	[Fact]
	public void ProofLocksCannotBeBurned()
	{
		var t = new ThermiteLock(true);
		Assert.False(t.Start());
		t.Update(100f);
		Assert.False(t.Burnt);
	}

	[Fact]
	public void ForceOpenOpensALockWithoutAKey()
	{
		var l = new LockModel("key");
		var opened = false;
		l.Opened += () => opened = true;
		Assert.Equal(UnlockResult.Opened, l.ForceOpen());
		Assert.True(opened);
		Assert.Equal(LockState.Open, l.State);
		Assert.Equal(UnlockResult.AlreadyOpen, l.ForceOpen());
	}

	[Fact]
	public void ImpactItemsHaveNoPin()
	{
		GrenadeSpec Of(GrenadeKind k) => new("x", 1f, 0f, 0, 0f, 1, 0f, 0.3f, 0f, 0f, 1, 0f, Kind: k);
		Assert.True(Of(GrenadeKind.Distraction).ImpactTriggered());
		Assert.True(Of(GrenadeKind.Molotov).ImpactTriggered());
		Assert.True(Of(GrenadeKind.Flare).ImpactTriggered());
		Assert.False(Of(GrenadeKind.Frag).ImpactTriggered());
		Assert.False(Of(GrenadeKind.Thermite).ImpactTriggered());
		Assert.False(Of(GrenadeKind.Stun).ImpactTriggered());
	}

	// ---- data

	private static string Repo(string rel)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (!File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent!;
		return Path.Combine(dir.FullName, rel);
	}

	private static string Field(string text, string name) => Regex.Match(text, $"^{name} = (.+)$", RegexOptions.Multiline).Groups[1].Value.Trim('"');

	[Fact]
	public void NewThrowablesMatchTheCatalogAndTheSpec()
	{
		var rows = File.ReadAllLines(Repo("docs/design/catalog/grenades.csv")).Skip(1).Select(l => l.Split(',')).ToDictionary(c => c[0]);
		var inv = System.Globalization.CultureInfo.InvariantCulture;
		foreach (var (id, kind, radius, life) in new[]
		{
			("stun_grenade_prototype", "4", "5.0", "0.0"), ("tear_gas_grenade", "5", "6.0", "30.0"),
			("molotov_cocktail", "6", "3.0", "15.0"), ("thermite_grenade", "7", "1.0", "20.0"),
		})
		{
			var text = File.ReadAllText(Repo($"data/items/defs/{id}.tres"));
			Assert.Equal(kind, Field(text, "Kind"));
			Assert.Equal(radius, Field(text, "CloudRadiusM"));
			Assert.Equal(life, Field(text, "CloudLifeS"));
			Assert.Equal(float.Parse(rows[id][8], inv), float.Parse(Field(text, "WeightKg"), inv), 3);
			Assert.Equal(int.Parse(rows[id][9]), int.Parse(Field(text, "BaseValue")));
			if (float.TryParse(rows[id][5], System.Globalization.NumberStyles.Float, inv, out var fuse))
				Assert.Equal(fuse, float.Parse(Field(text, "FuseS"), inv));
		}
		Assert.Equal("40.0", Field(File.ReadAllText(Repo("data/items/defs/molotov_cocktail.tres")), "ImpactNoiseM"));
		Assert.Equal("8", Field(File.ReadAllText(Repo("data/items/defs/signal_flare.tres")), "Kind"));
		Assert.Equal("60.0", Field(File.ReadAllText(Repo("data/items/defs/signal_flare.tres")), "CloudLifeS"));
		var conc = File.ReadAllText(Repo("data/items/defs/concussion_grenade.tres"));
		Assert.Equal("1", Field(conc, "Kind"));
		Assert.Equal("6.0", Field(conc, "CloudRadiusM"));
	}

	[Fact]
	public void SmugglerSellsTheOddOnesAndTheEngineerThermite()
	{
		var smuggler = File.ReadAllText(Repo("data/traders/smuggler.tres"));
		foreach (var id in new[] { "stun_grenade_prototype", "tear_gas_grenade", "molotov_cocktail" })
			Assert.Contains($"ItemId = \"{id}\"", smuggler);
		Assert.Contains("ItemId = \"thermite_grenade\"", File.ReadAllText(Repo("data/traders/engineer.tres")));
	}
}
