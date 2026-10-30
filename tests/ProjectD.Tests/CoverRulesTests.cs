using Xunit;

namespace ProjectD.Tests;

public class CoverRulesTests
{
	[Theory]
	[InlineData(2.4f, CoverClass.Standing)]
	[InlineData(1.8f, CoverClass.Standing)]
	[InlineData(1.5f, CoverClass.Crouch)] // between 1.3 and 1.8 counts as the lower class
	[InlineData(1.0f, CoverClass.Crouch)]
	[InlineData(0.8f, CoverClass.Crouch)]
	[InlineData(0.5f, CoverClass.Prone)]
	[InlineData(0.3f, CoverClass.Prone)]
	[InlineData(0.2f, CoverClass.None)]
	public void HeightDecidesTheClass(float height, CoverClass expected)
		=> Assert.Equal(expected, CoverRules.Classify(height));

	private static CoverCandidate C(CoverClass c = CoverClass.Standing, float d = 5f, float exposure = 0f, bool angle = true, float threat = 30f)
		=> new(c, d, exposure, angle, threat);

	[Fact]
	public void NearerCoverScoresBetter()
		=> Assert.True(CoverRules.Score(C(d: 3f), 30f, false) < CoverRules.Score(C(d: 9f), 30f, false));

	[Fact]
	public void ExposureOnTheWayCostsALot()
		=> Assert.True(CoverRules.Score(C(exposure: 1f), 30f, false) > CoverRules.Score(C(d: 12f), 30f, false));

	[Fact]
	public void CoverWithoutAFiringAngleLosesToOneWithIt()
		=> Assert.True(CoverRules.Score(C(angle: false), 30f, false) > CoverRules.Score(C(d: 8f), 30f, false));

	[Fact]
	public void LowerCoverIsLessAttractiveAtEqualDistance()
	{
		var stand = CoverRules.Score(C(CoverClass.Standing), 30f, false);
		var crouch = CoverRules.Score(C(CoverClass.Crouch), 30f, false);
		var prone = CoverRules.Score(C(CoverClass.Prone), 30f, false);
		Assert.True(stand < crouch && crouch < prone);
	}

	[Fact]
	public void RetreatPrefersSpotsFartherFromTheThreat()
		=> Assert.True(CoverRules.Score(C(threat: 50f), 30f, true) < CoverRules.Score(C(threat: 20f), 30f, true));

	[Fact]
	public void CoverThatLeadsTowardTheThreatCostsMore()
		=> Assert.True(CoverRules.Score(C(threat: 30f), 30f, false) < CoverRules.Score(C(threat: 20f), 30f, false));

	[Fact]
	public void PeekingRisesOneLevel()
	{
		Assert.Equal(1.0f, CoverRules.PeekEyeHeight(CoverClass.Prone));
		Assert.Equal(1.6f, CoverRules.PeekEyeHeight(CoverClass.Crouch));
		Assert.True(CoverRules.HiddenEyeHeight(CoverClass.Prone) < CoverRules.HiddenEyeHeight(CoverClass.Crouch));
	}
}
