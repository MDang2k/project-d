using System;
using System.IO;
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class BossTests
{
	private static string RepoFile(string rel)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return Path.Combine(dir!, rel);
	}

	private static ActorCatalog Catalog() => ActorCatalog.Parse(File.ReadAllText(RepoFile("data/actors/actors.json")));

	[Theory]
	[InlineData(0f, 0)]
	[InlineData(1f, 100)]
	[InlineData(1.5f, 100)]
	[InlineData(-1f, 0)]
	public void CertainAndImpossibleChancesAreExact(float chance, int expectedOutOf100)
		=> Assert.Equal(expectedOutOf100, Enumerable.Range(0, 100).Count(i => BossRoll.Rolls(chance, new Random(i))));

	[Fact]
	public void ThirtyPercentIsAboutThirtyPercent()
	{
		var rng = new Random(1);
		var hits = Enumerable.Range(0, 5000).Count(_ => BossRoll.Rolls(0.3f, rng));
		Assert.InRange(hits, 1300, 1700);
	}

	[Fact]
	public void TheSquadStartsAtTheRoutePointFarthestFromThePlayerAndWalksOnInOrder()
	{
		var route = new[] { new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(100, 0, 100), new Vector3(0, 0, 100) };
		var walk = BossRoll.FromFarthest(route, new Vector3(-10, 0, -10));
		Assert.Equal(new Vector3(100, 0, 100), walk[0]);
		Assert.Equal(new[] { route[2], route[3], route[0], route[1] }, walk);
		Assert.Empty(BossRoll.FromFarthest(Array.Empty<Vector3>(), Vector3.Zero));
	}

	[Fact]
	public void DesertersAreHostileToEveryoneElseAndFriendlyToTheirOwn()
	{
		foreach (var other in new[] { Faction.Looters, Faction.Bandits, Faction.AlliedPmc, Faction.SovietPmc })
		{
			Assert.True(FactionRelations.IsHostile(Faction.Deserters, other), other.ToString());
			Assert.True(FactionRelations.IsHostile(other, Faction.Deserters), other.ToString());
		}
		Assert.False(FactionRelations.IsHostile(Faction.Deserters, Faction.Deserters));
	}

	[Fact]
	public void HarkerAndHisSquadResolveFromTheSheet()
	{
		var c = Catalog();
		var boss = c.Create("harker");
		Assert.Equal("Deserters", boss.Def.Faction);
		Assert.Contains("boss", boss.Def.Tags);
		Assert.Equal(ActorTuning.ParseFaction("Deserters"), Faction.Deserters);
		// Bosses sheet: HP multiplier 1.3 gives 46 / 111 / 91 / 78 / 85
		Assert.Equal(46, boss.Stats.GetInt("health.head"));
		Assert.Equal(111, boss.Stats.GetInt("health.thorax"));
		Assert.Equal(91, boss.Stats.GetInt("health.stomach"));
		Assert.Equal(78, boss.Stats.GetInt("health.arm"));
		Assert.Equal(85, boss.Stats.GetInt("health.leg"));
		Assert.Equal("boss_deserter", boss.Def.Text("pocket_table"));

		var escort = c.Create("harker_squad");
		Assert.Equal(35, escort.Stats.GetInt("health.head"));
		Assert.Contains("escort", escort.Def.Tags);
		Assert.Equal(new[] { "ak_type", "fal_type" }, escort.Def.List("weapon_choices"));
	}

	[Fact]
	public void TheBossIsMoreAccurateThanAnEscortAndThanAPmc()
	{
		var c = Catalog();
		Assert.True(c.Create("harker").Stats.Get("aim.accuracy") > c.Create("harker_squad").Stats.Get("aim.accuracy"));
		Assert.True(c.Create("harker").Stats.Get("aim.accuracy") > c.Create("allied_pmc").Stats.Get("aim.accuracy"));
		Assert.Equal(0.85f, c.Create("harker").Stats.Get("aim.recoil_control"), 3); // AI combat v2 spec: bosses 0.85
	}

	[Fact]
	public void TheBossCarriesTheKeyThroughItsLootTable()
	{
		var table = File.ReadAllText(RepoFile("data/loot/boss_deserter.tres"));
		Assert.Contains("GuaranteedIds = PackedStringArray(\"radio_room_key\")", table);
		Assert.True(File.Exists(RepoFile("data/items/defs/radio_room_key.tres")));
		Assert.Contains("radio_room_key,", File.ReadAllText(RepoFile("docs/design/catalog/keys_special.csv")));
	}

	[Fact]
	public void TheBorderStripConfigNamesTheBossFromTheSheet()
	{
		var cfg = File.ReadAllText(RepoFile("data/raid/border_strip.tres"));
		Assert.Contains("BossActorId = \"harker\"", cfg);
		Assert.Contains("BossEscortId = \"harker_squad\"", cfg);
		var c = Catalog();
		Assert.True(c.Has("harker") && c.Has("harker_squad"));
		var sheet = File.ReadAllLines(RepoFile("docs/design/catalog/bosses.csv")).First(l => l.StartsWith("harker,"));
		Assert.Contains(",0.3,", sheet); // Spawn chance matches BossChance
		Assert.Contains("BossChance = 0.3", cfg);
	}

	[Fact]
	public void TheLayoutHasASixPointBossRoute()
	{
		var layout = MapLayout.Parse(File.ReadAllText(RepoFile("data/maps/border_strip.layout.json")));
		var route = layout.OfType("bossRoute").ToList();
		Assert.Equal(6, route.Count);
		Assert.All(route, m => Assert.Equal("deserters", m.Prop("squad")));
		Assert.Equal(Enumerable.Range(0, 6).Select(i => i.ToString()), route.Select(m => m.Prop("order")));
	}
}
