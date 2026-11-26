using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectD;
using Xunit;

public class OutfitWardrobesTests
{
	private static string Root()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent;
		return dir!.FullName;
	}

	private static OutfitWardrobes Shipped()
		=> OutfitWardrobes.Parse(File.ReadAllText(Path.Combine(Root(), "data", "actors", "wardrobes.json")));

	private static Dictionary<string, int> Rolls(string actor, string archetype, int count = 4000)
	{
		var rng = new Random(11);
		var shipped = Shipped();
		return Enumerable.Range(0, count).Select(_ => shipped.Roll(actor, archetype, rng)!.Name)
			.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
	}

	[Theory]
	[InlineData("allied_pmc", "infiltrator", "assault")]
	[InlineData("allied_pmc", "rat", "flanker")]
	[InlineData("soviet_pmc", "infiltrator", "assault")]
	[InlineData("soviet_pmc", "rat", "flanker")]
	public void ArchetypePicksTheOutfit(string actor, string archetype, string outfit)
		=> Assert.Equal(new[] { outfit }, Rolls(actor, archetype, 200).Keys);

	[Theory]
	[InlineData("allied_pmc")]
	[InlineData("soviet_pmc")]
	public void OperatorsAreRiflemenAndSometimesMarksmen(string actor)
	{
		var rolls = Rolls(actor, "operator");
		var marksmen = rolls.Where(r => r.Key.StartsWith("marksman")).Sum(r => r.Value);
		Assert.InRange(marksmen / 4000f, 0.17f, 0.23f); // riflemen to marksmen 4 to 1
		Assert.Equal(4000 - marksmen, rolls["rifleman"]);
	}

	[Theory]
	[InlineData("allied_pmc", "jacket", "allied_boots_black")]
	[InlineData("soviet_pmc", "jacket", "soviet_boots_brown")]
	public void EveryOutfitHasAJacketAndTheSidesBoots(string actor, string slot, string boots)
	{
		foreach (var archetype in new[] { "operator", "infiltrator", "rat" })
			foreach (var outfit in Shipped().Options(actor, archetype))
			{
				Assert.Single(outfit.Pieces, p => p.Contains($"_{slot}_"));
				Assert.Contains(boots, outfit.Pieces);
			}
	}

	[Fact]
	public void EveryPieceHasAModelAndEveryTextureAFile()
	{
		var shipped = Shipped();
		foreach (var piece in shipped.AllPieces)
			Assert.True(File.Exists(Path.Combine(Root(), "assets", "models", "characters", $"outfit_{piece}.glb")), piece);
		Assert.NotEmpty(shipped.Textures);
		foreach (var texture in shipped.Textures.Values)
			Assert.True(File.Exists(Path.Combine(Root(), "assets", "textures", "characters", texture)), texture);
	}

	[Fact]
	public void GlovesHideTheBodyHandsAndBareHandsDoNot()
	{
		var shipped = Shipped();
		Assert.All(shipped.Options("allied_pmc", "operator"), o => Assert.True(shipped.HidesHands(o)));
		Assert.All(shipped.Options("soviet_pmc", "operator"), o => Assert.False(shipped.HidesHands(o)));
	}

	[Fact]
	public void UnknownActorHasNoWardrobeAndUnknownArchetypeDressesAsAnOperator()
	{
		var shipped = Shipped();
		Assert.Null(shipped.Roll("looter", "conscript", new Random(1)));
		Assert.Equal(shipped.Options("allied_pmc", "operator"), shipped.Options("allied_pmc", ""));
	}

	[Fact]
	public void TheSameSeedGivesTheSameOutfit()
	{
		var shipped = Shipped();
		Assert.Equal(shipped.Roll("soviet_pmc", "operator", new Random(5)), shipped.Roll("soviet_pmc", "operator", new Random(5)));
	}
}
