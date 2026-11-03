using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class SkillTrainingTests
{
	// ---- units and tiers

	[Fact]
	public void UnitTimerCountsWholeUnitsAndKeepsTheRemainder()
	{
		var t = new UnitTimer(5f);
		Assert.Equal(0, t.Add(4.9f));
		Assert.Equal(1, t.Add(0.2f)); // 5.1 s in
		Assert.Equal(0, t.Add(4.8f)); // 4.9 into the next
		Assert.Equal(2, t.Add(5.2f)); // 10.1
	}

	[Fact]
	public void UnitTimerIgnoresNothingAndResets()
	{
		var t = new UnitTimer(5f);
		Assert.Equal(0, t.Add(0f));
		Assert.Equal(0, t.Add(-3f));
		t.Add(4f);
		t.Reset();
		Assert.Equal(0, t.Add(4f));
		Assert.Equal(2, new UnitTimer(5f).Add(10.5f));
	}

	[Theory]
	[InlineData(0f, null)]
	[InlineData(20f, null)]
	[InlineData(20.1f, "carry_light_5s")]
	[InlineData(29.9f, "carry_light_5s")]
	[InlineData(30f, "carry_heavy_5s")]
	[InlineData(39.9f, "carry_heavy_5s")]
	[InlineData(40f, "carry_extreme_5s")]
	[InlineData(70f, "carry_extreme_5s")]
	public void HeavierLoadsTrainStrengthInHigherTiers(float kg, string? action)
		=> Assert.Equal(action, SkillTraining.CarryAction(kg));

	// ---- the reporter, against real skill numbers

	private static SkillBook Book(bool inRaid = true)
	{
		var table = new float[] { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };
		var book = new SkillBook();
		SkillSpec Spec(string id, params (string, float)[] actions)
			=> new(id, table, actions.ToDictionary(a => a.Item1, a => a.Item2), Decay: 1f, DecayFloor: 1f); // no decay: exact sums
		book.Register(Spec(SkillIds.Endurance, ("sprint_5s", 4f), ("mantle", 6f)));
		book.Register(Spec(SkillIds.Strength, ("carry_light_5s", 3f), ("carry_heavy_5s", 5f), ("carry_extreme_5s", 8f)));
		book.Register(Spec(SkillIds.WeaponHandling, ("reload", 5f), ("mag_check", 2f), ("chamber_check", 2f), ("load_5_rounds", 1f)));
		book.Register(Spec(SkillIds.RecoilControl, ("shot", 0.5f)));
		book.Register(Spec(SkillIds.FirstAid, ("med_use", 15f)));
		book.Register(Spec(SkillIds.Searching, ("item_revealed", 4f)));
		book.Register(Spec(SkillIds.Vitality, ("damage_point", 0.5f)));
		if (inRaid)
			book.BeginRaid();
		return book;
	}

	[Fact]
	public void SprintingTrainsEnduranceEveryFiveSeconds()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		for (var i = 0; i < 101; i++)
			s.Update(0.1f, true, 5f, 10f); // a little over 10 s sprinting (float steps)
		Assert.Equal(8f, book.Xp(SkillIds.Endurance), 3);
		Assert.Equal(0f, book.Xp(SkillIds.Strength));
	}

	[Fact]
	public void StoppingSprintDropsTheBuiltUpPartUnit()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		s.Update(4.5f, true, 5f, 10f);
		s.Update(0.1f, false, 1f, 10f);
		s.Update(4.5f, true, 5f, 10f);
		Assert.Equal(0f, book.Xp(SkillIds.Endurance));
	}

	[Fact]
	public void MovingUnderLoadTrainsStrengthByTier()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		s.Update(5f, false, 3f, 25f);  // light
		s.Update(10f, false, 3f, 35f); // heavy, two units
		s.Update(5f, false, 3f, 45f);  // extreme
		Assert.Equal(3f + 2 * 5f + 8f, book.Xp(SkillIds.Strength), 3);
	}

	[Fact]
	public void StandingStillOrCarryingLightTrainsNoStrength()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		s.Update(20f, false, 0.1f, 50f); // heavy but not moving
		s.Update(20f, false, 3f, 15f);   // moving but light
		Assert.Equal(0f, book.Xp(SkillIds.Strength));
	}

	[Fact]
	public void ChangingTierRestartsTheUnit()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		s.Update(4f, false, 3f, 25f);
		s.Update(4f, false, 3f, 35f); // a different tier: nothing is owed from the first 4 s
		Assert.Equal(0f, book.Xp(SkillIds.Strength));
	}

	[Fact]
	public void EachActionReportsToItsSkill()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		s.Mantled();
		s.Shot();
		s.Shot();
		s.Reloaded();
		s.MagChecked();
		s.ChamberChecked();
		s.MedUsed();
		s.ItemRevealed();
		Assert.Equal(6f, book.Xp(SkillIds.Endurance));
		Assert.Equal(1f, book.Xp(SkillIds.RecoilControl));
		Assert.Equal(9f, book.Xp(SkillIds.WeaponHandling));
		Assert.Equal(15f, book.Xp(SkillIds.FirstAid));
		Assert.Equal(4f, book.Xp(SkillIds.Searching));
	}

	[Fact]
	public void EveryFifthRoundLoadedIsOneUnit()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		for (var i = 0; i < 4; i++)
			s.RoundLoaded();
		Assert.Equal(0f, book.Xp(SkillIds.WeaponHandling));
		s.RoundLoaded();
		Assert.Equal(1f, book.Xp(SkillIds.WeaponHandling));
		for (var i = 0; i < 10; i++)
			s.RoundLoaded();
		Assert.Equal(3f, book.Xp(SkillIds.WeaponHandling));
	}

	[Fact]
	public void HitsTrainVitalityByTheDamageThatGotThrough()
	{
		var book = Book();
		var s = new PlayerSkills(() => book);
		s.HitTaken(40f);
		s.HitTaken(0f);
		Assert.Equal(20f, book.Xp(SkillIds.Vitality), 3);
	}

	[Fact]
	public void NothingTrainsOutsideARaid()
	{
		var book = Book(inRaid: false);
		var s = new PlayerSkills(() => book);
		s.Update(20f, true, 5f, 50f);
		s.Mantled();
		s.Shot();
		s.HitTaken(30f);
		Assert.Empty(book.Snapshot());
	}

	[Fact]
	public void WithoutASkillBookNothingBreaks()
	{
		var s = new PlayerSkills(() => null);
		s.Update(10f, true, 5f, 50f);
		s.Shot();
		s.RoundLoaded();
		s.HitTaken(5f);
	}

	// ---- loader and health events that feed the reporter

	[Fact]
	public void TheMagazineLoaderReportsEachRoundLoaded()
	{
		var mag = ItemInstance.Create(new ItemSpec("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30, RoundWeightKg: 0.01f, Caliber: "7.62x39"));
		var ammo = ItemInstance.Create(new ItemSpec("ammo_ball", "ammo", ItemCategory.Ammo, 1, 1, 0.01f, 60, Caliber: "7.62x39", AmmoId: "ball"), 7);
		var loader = new MagazineLoader(0.1f);
		var loaded = 0;
		loader.RoundLoaded += () => loaded++;
		Assert.True(loader.StartLoad(mag, ammo));
		loader.Update(10f);
		Assert.Equal(7, loaded);
		Assert.Equal(7, mag.Rounds.Count);
	}
}
