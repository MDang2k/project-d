using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class NemesisSummaryTests
{
	private static string Text(string key) => key switch
	{
		"map.border_strip" => "Border Strip",
		"map.map1" => "Bunker Entrance",
		"landmark.tower" => "the tower",
		"nemesis.where.near" => "near {0} ({1})",
		"nemesis.where.open" => "in the open ({0})",
		"nemesis.raid" => "Raid {0}:",
		"nemesis.killed_by" => "Killed by {0} \"{1}\".",
		"nemesis.killed" => "Nemesis {0} killed",
		"nemesis.fallen.player" => "{0} is dead: you killed them.",
		"nemesis.fallen.other" => "{0} was killed by {1}.",
		_ => key,
	};

	private static readonly Landmark[] Marks = { new("landmark.tower", 0f, 0f), new("landmark.gate", 100f, 0f) };

	[Fact]
	public void TheNearestLandmarkNamesThePlace()
	{
		Assert.Equal("landmark.tower", NemesisSummary.NearestLandmark(10f, 10f, Marks));
		Assert.Equal("landmark.gate", NemesisSummary.NearestLandmark(80f, 0f, Marks));
	}

	[Fact]
	public void OpenGroundBeyondReachHasNoPlace()
	{
		Assert.Null(NemesisSummary.NearestLandmark(500f, 500f, Marks));
		Assert.Null(NemesisSummary.NearestLandmark(0f, 0f, new List<Landmark>()));
		Assert.Equal("landmark.tower", NemesisSummary.NearestLandmark(-NemesisSummary.LandmarkReachM, 0f, Marks)); // the reach is inclusive
	}

	[Fact]
	public void LocationReadsNearTheLandmarkOrJustTheMap()
	{
		Assert.Equal("near the tower (Border Strip)", NemesisSummary.Location(new NemesisKill(1, "border_strip", 0, 0, "landmark.tower"), Text));
		Assert.Equal("in the open (Bunker Entrance)", NemesisSummary.Location(new NemesisKill(1, "map1", 0, 0), Text));
	}

	private static Nemesis Make()
	{
		var gear = new CharacterInventory();
		gear.TryEquip(ItemInstance.Create(new ItemSpec("ak", "ak", ItemCategory.Weapon, 5, 2, 3.6f, WeaponClass: WeaponClass.Primary)), EquipSlot.Primary);
		gear.TryAutoStore(ItemInstance.Create(new ItemSpec("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, 5), 3));
		var n = new Nemesis("n1", "Jack Cole", "Ghost", Faction.AlliedPmc, "border_strip", 0, 0, 2, false, gear);
		n.Kills.Add(new NemesisKill(2, "border_strip", 0, 0, "landmark.tower"));
		n.Kills.Add(new NemesisKill(5, "map1", 50, 50));
		return n;
	}

	[Fact]
	public void TheKillLogListsEveryKillOldestFirst()
		=> Assert.Equal(new[] { "Raid 2: near the tower (Border Strip)", "Raid 5: in the open (Bunker Entrance)" }, NemesisSummary.KillLog(Make(), Text));

	[Fact]
	public void CarriesGroupsAndCountsItemsMostNumerousFirst()
		=> Assert.Equal(new[] { "BANDAGE x3", "AK" }, NemesisSummary.Carries(Make(), id => id.ToUpperInvariant()));

	[Fact]
	public void CarriesIsTrimmedWithAMoreLine()
	{
		var n = Make();
		var lines = NemesisSummary.Carries(n, id => id, max: 1);
		Assert.Equal(2, lines.Count);
		Assert.Equal("... and 1 more kinds", lines[1]);
	}

	[Fact]
	public void TheDeathLineNamesTheKillerAndTheKillLineTheNemesis()
	{
		Assert.Equal("Killed by Jack Cole \"Ghost\".", NemesisSummary.KilledBy(Make(), Text));
		Assert.Equal("Nemesis Jack Cole killed", NemesisSummary.KilledLine(Make(), Text));
	}

	[Fact]
	public void NewsOfADeadNemesisDependsOnWhoKilledIt()
	{
		Assert.Equal("Jack Cole is dead: you killed them.", NemesisSummary.FallenLine(new FallenNemesis("Jack Cole", "Ghost", "the player", 3), Text));
		Assert.Equal("Jack Cole was killed by Soviet PMCs.", NemesisSummary.FallenLine(new FallenNemesis("Jack Cole", "Ghost", "Soviet PMCs", 3), Text));
	}

	[Fact]
	public void TheKillPlaceSurvivesTheSave()
	{
		var profile = new GameProfile();
		profile.Nemeses.Add(Make());
		var json = SaveSerializer.ToJson(new RunProgress(), profile, null, false);
		var loaded = SaveSerializer.Parse(json, id => null).Profile;
		Assert.Equal(new[] { "landmark.tower", "" }, loaded.Nemeses.All.Single().Kills.Select(k => k.Place));
	}

	[Fact]
	public void EveryTextKeyTheScreensUseExists()
	{
		var dir = System.AppContext.BaseDirectory;
		while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "Project D.csproj")))
			dir = System.IO.Path.GetDirectoryName(dir);
		var text = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!, "scripts/core/GameText.cs"));
		foreach (var key in new[] { "nemesis.title", "nemesis.none", "nemesis.hint", "nemesis.rank", "nemesis.kills", "nemesis.carries", "nemesis.raid",
			"nemesis.where.near", "nemesis.where.open", "nemesis.killed_by", "nemesis.killed", "nemesis.fallen.player", "nemesis.fallen.other",
			"nemesis.friendly", "map.border_strip", "map.map1", "landmark.canopy", "landmark.water_tower", "landmark.portal", "landmark.gate" })
			Assert.Contains($"[\"{key}\"]", text);
		// every landmark the Border Strip layout names has a display text
		var layout = MapLayout.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir!, "data/maps/border_strip.layout.json")));
		foreach (var m in layout.OfType("landmark"))
			Assert.Contains($"[\"{m.Prop("name")}\"]", text);
	}
}
