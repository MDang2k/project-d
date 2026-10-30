using Xunit;

namespace ProjectD.Tests;

public class PerceptionEnvironmentTests
{
	[Fact]
	public void ThreeBushesBreakSightAndNoneLeavesItWhole()
	{
		Assert.Equal(1f, PerceptionEnvironment.FoliageVisibility(new float[0]));
		Assert.Equal(0.65f, PerceptionEnvironment.FoliageVisibility(new[] { 0.35f }), 3);
		Assert.Equal(0f, PerceptionEnvironment.FoliageVisibility(new[] { 0.35f, 0.35f, 0.35f }), 3);
		Assert.Equal(0f, PerceptionEnvironment.FoliageVisibility(new[] { 1f, 1f }));
	}

	[Fact]
	public void DarkerZonesAndNightLowerTheSightFactor()
	{
		Assert.True(PerceptionEnvironment.LightFactor(LightLevel.Dark) < PerceptionEnvironment.LightFactor(LightLevel.Dim));
		Assert.True(PerceptionEnvironment.LightFactor(LightLevel.Dim) < PerceptionEnvironment.LightFactor(LightLevel.Lit));
		Assert.Equal(1f, PerceptionEnvironment.LightFactor(LightLevel.Lit));
		Assert.True(PerceptionEnvironment.LightFactor(LightLevel.Lit, 0.3f) < 0.5f);
	}

	[Fact]
	public void DarkZoneShortensTheDistanceABotCanSeeYou()
	{
		var t = PerceptionTuning.Default;
		var target = new TargetProfile(Stance.Stand, 0f);
		var lit = PerceptionModel.VisionStrength(t, 40f, 0f, target, 1f, PerceptionEnvironment.LightFactor(LightLevel.Lit));
		var dark = PerceptionModel.VisionStrength(t, 40f, 0f, target, 1f, PerceptionEnvironment.LightFactor(LightLevel.Dark));
		Assert.True(lit > 0f);
		Assert.True(dark < lit);
	}

	[Theory]
	[InlineData("concrete", 18f)]
	[InlineData("brick", 15f)]
	[InlineData("wood", 8f)]
	[InlineData("drywall", 6f)]
	[InlineData("glass", 3f)]
	[InlineData("metal_sheet", 10f)]
	[InlineData("foliage", 1f)]
	[InlineData(null, 18f)]
	[InlineData("mystery", 18f)]
	public void WallLossFollowsTheMaterialTable(string? material, float db)
		=> Assert.Equal(db, PerceptionEnvironment.WallLossDb(material));

	[Fact]
	public void EachConcreteWallCostsAboutAFactorOfEightInRadius()
	{
		Assert.Equal(100f, PerceptionEnvironment.EffectiveRadius(100f, new string?[0]));
		Assert.Equal(100f * 0.1259f, PerceptionEnvironment.EffectiveRadius(100f, new string?[] { "concrete" }), 1);
		Assert.True(PerceptionEnvironment.EffectiveRadius(100f, new string?[] { "drywall" })
			> PerceptionEnvironment.EffectiveRadius(100f, new string?[] { "brick" }));
		Assert.True(PerceptionEnvironment.EffectiveRadius(100f, new string?[] { "wood", "wood" })
			< PerceptionEnvironment.EffectiveRadius(100f, new string?[] { "wood" }));
	}

	[Fact]
	public void BoldBotsOverreadCuesAndCarefulOnesUnderreadThem()
	{
		foreach (var kind in new[] { "dry_click", "reload_empty", "surgery_start", "heal" })
		{
			Assert.True(PerceptionEnvironment.CueMultiplier(kind, 1f) > 1f, kind);
			Assert.True(PerceptionEnvironment.CueMultiplier(kind, 0f) < 1f, kind);
			Assert.True(PerceptionEnvironment.IsCue(kind));
			Assert.True(PerceptionEnvironment.CueRadius(kind) > 0f);
		}
		Assert.Equal(1f, PerceptionEnvironment.CueMultiplier("gunshot", 0.9f));
		Assert.False(PerceptionEnvironment.IsCue("gunshot"));
	}

	[Fact]
	public void DryClickMattersMoreThanAHeal()
		=> Assert.True(PerceptionEnvironment.CueMultiplier("dry_click", 1f) > PerceptionEnvironment.CueMultiplier("heal", 1f));
}
