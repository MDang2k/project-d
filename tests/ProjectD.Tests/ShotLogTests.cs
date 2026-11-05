using Xunit;

namespace ProjectD.Tests;

public class ShotLogTests
{
	[Fact]
	public void HitLineShowsTargetPartRangeDamageArmorHpAndEffects()
	{
		var line = ShotLog.Format(new ShotReport(42.46f, "bandit", true, BodyPart.Thorax, 34.2f, true, false, 1.5f, 120f, 86f, false, "bleed Thorax"));
		Assert.Equal("HIT  bandit Thorax  42.5 m  dmg 34.2  armor stopped (-1.5 dur)  hp 120>86  fx: bleed Thorax", line);
	}

	[Fact]
	public void KillAndPenetrationAndBareFlesh()
	{
		Assert.Contains("KILLED", ShotLog.Format(new ShotReport(10f, "looter", true, BodyPart.Head, 80f, false, true, 0f, 35f, 0f, true)));
		Assert.Contains("armor penetrated", ShotLog.Format(new ShotReport(10f, "x", true, BodyPart.Thorax, 20f, true, true, 2f, 100f, 80f, false)));
		Assert.Contains("no armor", ShotLog.Format(new ShotReport(10f, "x", true, BodyPart.Thorax, 20f, false, true, 0f, 100f, 80f, false)));
	}

	[Fact]
	public void MissNamesWhatItHitAndHowFar()
		=> Assert.Equal("MISS  Terrain  120.0 m", ShotLog.Format(new ShotReport(120f, "Terrain", false)));

	[Fact]
	public void LogKeepsTheLastLinesAndTheyExpire()
	{
		var log = new ShotLog(capacity: 2, lifetimeS: 10f);
		log.Add(new ShotReport(1f, "a", false));
		log.Add(new ShotReport(2f, "b", false));
		log.Add(new ShotReport(3f, "c", false));
		Assert.Equal(2, log.Lines.Count);
		Assert.Contains("b", log.Lines[0].Text);
		log.Update(9f);
		Assert.Equal(2, log.Lines.Count);
		Assert.True(log.Alpha(9f) < 0.5f);
		log.Update(2f);
		Assert.Empty(log.Lines);
	}

	[Fact]
	public void EffectsTextListsBleedsAndFractures()
	{
		var text = ShotLog.EffectsText(new[] { (BodyPart.LeftArm, BleedLevel.Heavy), (BodyPart.Head, BleedLevel.None) }, new[] { BodyPart.RightLeg });
		Assert.Equal("heavy bleed LeftArm, fracture RightLeg", text);
		Assert.Equal("", ShotLog.EffectsText(new (BodyPart, BleedLevel)[0], new BodyPart[0]));
	}
}
