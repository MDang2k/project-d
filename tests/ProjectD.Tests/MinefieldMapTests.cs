using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class MinefieldMapTests
{
	private static MapLayout Layout()
	{
		var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
		while (!File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent!;
		return MapLayout.Parse(File.ReadAllText(Path.Combine(dir.FullName, "data/maps/border_strip.layout.json")));
	}

	[Fact]
	public void BorderStripGivesTwoFieldsAndTwoLanesInOrder()
	{
		var map = MinefieldMap.From(Layout());
		Assert.Equal(new Godot.Vector2(300f, 400f), map.MapSize);
		Assert.Equal(2, map.Minefields.Count);
		Assert.Equal(new[] { "north", "south" }, map.Lanes.Select(l => l.Name).OrderBy(n => n));
		var north = map.Lanes.First(l => l.Name == "north");
		Assert.Equal(4, north.Points.Count);
		Assert.True(north.Points.Zip(north.Points.Skip(1), (a, b) => b.Y > a.Y).All(x => x)); // runs north to south
	}

	[Fact]
	public void EveryLanePointLiesInsideOrJustOutsideAMinefieldRow()
	{
		var map = MinefieldMap.From(Layout());
		var field = map.Minefields[0];
		Assert.Equal(151f - 19f, field.Position.X);
		Assert.Equal(70f - 40f, field.Position.Y);
		Assert.Equal(38f, field.Size.X);
		Assert.Equal(80f, field.Size.Y);
	}

	[Fact]
	public void KnowledgeUnlocksTheLaneMapEffectAndTheBookOpensIt()
	{
		var book = new KnowledgeBook();
		book.Register(new("minefield_map", KnowledgeKind.Lore, "t", "x", "minefield_map", 0, new[] { MinefieldMap.EffectId("border_strip") }));
		Assert.False(book.Opens("minefield.border_strip"));
		book.UnlockFromItem("minefield_map");
		Assert.True(book.Opens("minefield.border_strip"));
	}
}
