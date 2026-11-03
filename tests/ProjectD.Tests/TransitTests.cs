using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class TransitTests
{
	private static readonly Func<string, bool> NoItems = _ => false;

	private static TransitSpec Road(string dest = "kreuzdorf", int cost = 0, string? item = null, float seconds = 6f)
		=> new("road", TransitKind.Road, seconds, cost, item, Destination: dest);

	private static TransitSpec Tunnel(float seconds = 4f, int cost = 3000)
		=> new("tun_w", TransitKind.Tunnel, seconds, cost, PairId: "tun_e");

	// ---- gate

	[Fact]
	public void AFreeOpenRoadHasNoBlock()
		=> Assert.Equal(TransitBlock.None, TransitRules.Check(Road(), 0, NoItems, true));

	[Fact]
	public void ARoadToAClosedMapIsBlockedWhateverYouCarry()
		=> Assert.Equal(TransitBlock.Closed, TransitRules.Check(Road(cost: 10), 99999, _ => true, false));

	[Fact]
	public void ATunnelIgnoresTheMapRegistry()
		=> Assert.Equal(TransitBlock.None, TransitRules.Check(Tunnel(), 5000, NoItems, false));

	[Fact]
	public void PaidGatesNeedTheMoneyOnYou()
	{
		Assert.Equal(TransitBlock.CannotPay, TransitRules.Check(Tunnel(), 2999, NoItems, true));
		Assert.Equal(TransitBlock.None, TransitRules.Check(Tunnel(), 3000, NoItems, true));
	}

	[Fact]
	public void ItemGatesNeedTheItem()
	{
		var spec = Road(item: "officers_pass");
		Assert.Equal(TransitBlock.MissingItem, TransitRules.Check(spec, 0, NoItems, true));
		Assert.Equal(TransitBlock.None, TransitRules.Check(spec, 0, id => id == "officers_pass", true));
	}

	[Fact]
	public void ClosedComesBeforeMissingItemBeforePay()
	{
		var spec = Road(cost: 500, item: "pass");
		Assert.Equal(TransitBlock.Closed, TransitRules.Check(spec, 0, NoItems, false));
		Assert.Equal(TransitBlock.MissingItem, TransitRules.Check(spec, 0, NoItems, true));
		Assert.Equal(TransitBlock.CannotPay, TransitRules.Check(spec, 0, _ => true, true));
	}

	// ---- countdown

	[Fact]
	public void StandingInAnUnblockedZoneCompletesItOnceAfterItsTime()
	{
		var c = new TransitCountdown();
		var spec = Tunnel(seconds: 4f);
		Assert.Null(c.Update(1f, spec, TransitBlock.None));
		Assert.Equal(0.25f, c.Progress, 3);
		Assert.Equal(3f, c.Remaining, 3);
		Assert.Null(c.Update(2f, spec, TransitBlock.None));
		Assert.Same(spec, c.Update(1.5f, spec, TransitBlock.None));
		Assert.Null(c.Update(1f, spec, TransitBlock.None)); // still standing there: it does not fire again
	}

	[Fact]
	public void LeavingOrBeingBlockedResetsTheTimer()
	{
		var c = new TransitCountdown();
		var spec = Road();
		c.Update(3f, spec, TransitBlock.None);
		Assert.True(c.Progress > 0.4f);
		c.Update(0.1f, null, TransitBlock.None);
		Assert.Equal(0f, c.Progress);
		c.Update(3f, spec, TransitBlock.None);
		c.Update(0.1f, spec, TransitBlock.CannotPay);
		Assert.Equal(0f, c.Progress);
		Assert.Null(c.ZoneId);
	}

	[Fact]
	public void TheZoneWorksAgainAfterYouLeaveIt()
	{
		var c = new TransitCountdown();
		var spec = Tunnel(seconds: 1f);
		Assert.Same(spec, c.Update(1f, spec, TransitBlock.None));
		Assert.Null(c.Update(5f, spec, TransitBlock.None));
		c.Update(0.1f, null, TransitBlock.None);
		Assert.Same(spec, c.Update(1f, spec, TransitBlock.None));
	}

	[Fact]
	public void LandingInATunnelEndDoesNotSendYouStraightBack()
	{
		var c = new TransitCountdown();
		var east = new TransitSpec("tun_e", TransitKind.Tunnel, 1f, 3000, PairId: "tun_w");
		c.Landed("tun_e");
		Assert.Null(c.Update(10f, east, TransitBlock.None));
		c.Update(0.1f, null, TransitBlock.None);
		Assert.Same(east, c.Update(1f, east, TransitBlock.None));
	}

	[Fact]
	public void SteppingFromOneZoneToAnotherRestartsTheTimer()
	{
		var c = new TransitCountdown();
		var a = Tunnel(seconds: 4f);
		var b = Road(seconds: 4f);
		c.Update(3f, a, TransitBlock.None);
		c.Update(1f, b, TransitBlock.None);
		Assert.Equal(0.25f, c.Progress, 3);
		Assert.Equal("road", c.ZoneId);
	}

	[Fact]
	public void AZeroSecondZoneCompletesAtOnce()
		=> Assert.NotNull(new TransitCountdown().Update(0.016f, Tunnel(seconds: 0f), TransitBlock.None));

	// ---- registry

	private const string Json = """
	{ // roster
	  "maps": [
	    { "id": "border_strip", "name": "Border Strip", "scene": "res://scenes/maps/border_strip.tscn", "open": true },
	    { "id": "kreuzdorf", "name": "Kreuzdorf", "scene": "", "open": true },
	    { "id": "rail_yard", "name": "Rail Yard", "scene": "res://x.tscn", "open": false },
	  ]
	}
	""";

	[Fact]
	public void OnlyMapsWithASceneAndTheOpenFlagCanBeEntered()
	{
		var r = MapRegistry.Parse(Json);
		Assert.True(r.IsOpen("border_strip"));
		Assert.False(r.IsOpen("kreuzdorf")); // open but nothing built yet
		Assert.False(r.IsOpen("rail_yard")); // built but locked
		Assert.False(r.IsOpen("nowhere"));
		Assert.Equal("res://scenes/maps/border_strip.tscn", r.SceneOf("border_strip"));
		Assert.Null(r.SceneOf("kreuzdorf"));
		Assert.True(r.Has("rail_yard"));
	}

	[Fact]
	public void DuplicateMapIdsAreRejected()
		=> Assert.Throws<ArgumentException>(() => MapRegistry.Parse("{\"maps\":[{\"id\":\"a\"},{\"id\":\"a\"}]}"));

	[Fact]
	public void TheShippedRegistryKnowsTheBorderStripTransitDestinations()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		var r = MapRegistry.Parse(File.ReadAllText(Path.Combine(dir!, "data/maps/registry.json")));
		var layout = MapLayout.Parse(File.ReadAllText(Path.Combine(dir!, "data/maps/border_strip.layout.json")));
		Assert.True(layout.OfType("transit").Any());
		foreach (var m in layout.OfType("transit"))
			Assert.True(r.Has(m.Prop("to")), $"{m.Id} leads to unknown map '{m.Prop("to")}'");
		foreach (var map in r.Maps.Where(m => m.Scene != ""))
			Assert.True(File.Exists(Path.Combine(dir!, map.Scene.Replace("res://", ""))), map.Scene);
		Assert.True(r.IsOpen("border_strip"));
		Assert.False(r.IsOpen("kreuzdorf")); // road still blocked until the map exists
	}

	// ---- character carried by a transit

	[Fact]
	public void TheBodyAndInjuriesSurviveAMapChange()
	{
		var body = new BodyHealth(HealthTuning.Default);
		var fx = new StatusEffects(EffectsTuning.Default);
		var vitals = new VitalsModel(VitalsTuning.Default);
		body.TakeDamage(BodyPart.LeftLeg, 40f);
		body.TakeDamage(BodyPart.Thorax, 20f);
		body.TakeDamage(BodyPart.RightArm, 500f); // destroyed
		fx.Inflict(BodyPart.LeftLeg, true, BleedLevel.Heavy, body);
		fx.Morphine(30f);
		vitals.Set(55f, 20f);
		var snapshot = CharacterSnapshot.Capture(body, fx, vitals);

		var body2 = new BodyHealth(HealthTuning.Default);
		var fx2 = new StatusEffects(EffectsTuning.Default);
		var vitals2 = new VitalsModel(VitalsTuning.Default);
		snapshot.Apply(body2, fx2, vitals2);

		foreach (var p in BodyHealth.AllParts)
		{
			Assert.Equal(body.Hp(p), body2.Hp(p), 3);
			Assert.Equal(body.MaxHp(p), body2.MaxHp(p), 3);
		}
		Assert.True(body2.IsDestroyed(BodyPart.RightArm));
		Assert.True(fx2.IsFractured(BodyPart.LeftLeg));
		Assert.Equal(BleedLevel.Heavy, fx2.Bleed(BodyPart.LeftLeg));
		Assert.Equal(fx.Pain, fx2.Pain);
		Assert.True(fx2.PainSuppressed);
		Assert.Equal(55f, vitals2.Energy);
		Assert.Equal(20f, vitals2.Hydration);
	}

	[Fact]
	public void ASnapshotIsACopyNotAView()
	{
		var body = new BodyHealth(HealthTuning.Default);
		var fx = new StatusEffects(EffectsTuning.Default);
		var vitals = new VitalsModel(VitalsTuning.Default);
		fx.Inflict(BodyPart.LeftLeg, true, BleedLevel.Light, body);
		var snapshot = CharacterSnapshot.Capture(body, fx, vitals);
		fx.Splint(BodyPart.LeftLeg);
		body.TakeDamage(BodyPart.Head, 10f);
		Assert.Contains(BodyPart.LeftLeg, snapshot.Effects.Fractures);
		Assert.Equal(HealthTuning.Default.HeadHp, snapshot.Body.Parts[BodyPart.Head].Hp);
	}

	[Fact]
	public void ThePendingCarryIsClearedOnceTaken()
	{
		TransitCarry.Pending = CharacterSnapshot.Capture(new BodyHealth(HealthTuning.Default), new StatusEffects(EffectsTuning.Default), new VitalsModel(VitalsTuning.Default));
		TransitCarry.FromMap = "border_strip";
		TransitCarry.Clear();
		Assert.Null(TransitCarry.Pending);
		Assert.Equal("", TransitCarry.FromMap);
	}
}
