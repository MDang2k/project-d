using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

public class GrenadeDataTests
{
	private static readonly string[] Ids =
	{
		"frag_grenade", "defensive_frag_grenade", "offensive_frag_grenade", "egg_grenade", "stick_grenade", "concussion_grenade",
	};

	private static string Repo(string rel)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (!File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent!;
		return Path.Combine(dir.FullName, rel);
	}

	private static string Field(string text, string name)
		=> Regex.Match(text, $"^{name} = (.+)$", RegexOptions.Multiline).Groups[1].Value.Trim('"');

	private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

	[Fact]
	public void FuseWeightAndValueMatchTheCatalogRows()
	{
		var rows = File.ReadAllLines(Repo("docs/design/catalog/grenades.csv")).Skip(1).Select(l => l.Split(',')).ToDictionary(c => c[0]);
		foreach (var id in Ids)
		{
			var text = File.ReadAllText(Repo($"data/items/defs/{id}.tres"));
			var row = rows[id];
			Assert.Equal(F(row[5]), F(Field(text, "FuseS")));
			Assert.Equal(F(row[8]), F(Field(text, "WeightKg")), 3);
			Assert.Equal(int.Parse(row[9]), int.Parse(Field(text, "BaseValue")));
			Assert.Equal(id, Field(text, "Id"));
			Assert.Contains("item." + id, text);
		}
	}

	[Fact]
	public void BlastCoreIsFortyPercentOfTheCatalogRadiusAndConcussionSeventy()
	{
		var rows = File.ReadAllLines(Repo("docs/design/catalog/grenades.csv")).Skip(1).Select(l => l.Split(',')).ToDictionary(c => c[0]);
		foreach (var id in Ids)
		{
			var core = F(Field(File.ReadAllText(Repo($"data/items/defs/{id}.tres")), "BlastCoreM"));
			var share = id == "concussion_grenade" ? 0.7f : 0.4f;
			Assert.Equal(F(rows[id][6]) * share, core, 2);
		}
	}

	[Fact]
	public void FragmentTableMatchesTheSpecAndConcussionHasNone()
	{
		var expected = new (string Id, int Frags, float Dmg, int Tier, float Range)[]
		{
			("frag_grenade", 70, 28f, 2, 25f), ("defensive_frag_grenade", 80, 30f, 2, 30f), ("offensive_frag_grenade", 50, 22f, 1, 16f),
			("egg_grenade", 50, 25f, 2, 20f), ("stick_grenade", 25, 25f, 1, 15f), ("concussion_grenade", 0, 0f, 1, 0f),
		};
		foreach (var e in expected)
		{
			var text = File.ReadAllText(Repo($"data/items/defs/{e.Id}.tres"));
			Assert.Equal(e.Frags, int.Parse(Field(text, "Fragments")));
			Assert.Equal(e.Dmg, F(Field(text, "FragmentDamage")));
			Assert.Equal(e.Tier, int.Parse(Field(text, "FragmentTier")));
			Assert.Equal(e.Range, F(Field(text, "FragmentRangeM")));
		}
		Assert.Equal(1.25f, F(Field(File.ReadAllText(Repo("data/items/defs/stick_grenade.tres")), "ThrowSpeedMult")));
	}

	[Fact]
	public void QuartermastersSellTheirSideGrenades()
	{
		var allied = File.ReadAllText(Repo("data/traders/allied_qm.tres"));
		var soviet = File.ReadAllText(Repo("data/traders/soviet_qm.tres"));
		foreach (var id in new[] { "frag_grenade", "egg_grenade", "concussion_grenade" })
			Assert.Contains($"ItemId = \"{id}\"", allied);
		foreach (var id in new[] { "defensive_frag_grenade", "offensive_frag_grenade" })
			Assert.Contains($"ItemId = \"{id}\"", soviet);
		Assert.DoesNotContain("stick_grenade", allied + soviet);
	}

	[Fact]
	public void StrengthAddsTwoPercentThrowSpeedPerLevelAboveOne()
	{
		var table = new[] { 0f, 100f, 250f, 450f, 700f, 1000f, 1400f, 1900f, 2500f, 3200f };
		SkillEffects At(int level)
		{
			var book = new SkillBook();
			book.Register(new SkillSpec(SkillIds.Strength, table, new System.Collections.Generic.Dictionary<string, float> { ["throw"] = 4f }));
			book.Load(new System.Collections.Generic.Dictionary<string, float> { [SkillIds.Strength] = table[level - 1] });
			return new SkillEffects(book);
		}
		Assert.Equal(0f, At(1).ThrowSpeedBonus, 4);
		Assert.Equal(0.02f, At(2).ThrowSpeedBonus, 4);
		Assert.Equal(0.18f, At(10).ThrowSpeedBonus, 4);
	}

	[Fact]
	public void TheStrengthDataTrainsOnThrows()
	{
		var text = File.ReadAllText(Repo("data/skills/strength.tres"));
		Assert.Contains("\"throw\"", text);
	}
}
