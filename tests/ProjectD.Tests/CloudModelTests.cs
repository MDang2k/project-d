using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class CloudModelTests
{
	[Fact]
	public void CloudGrowsFor4SecondsStaysThenThinsOutInTheLastFive()
	{
		Assert.Equal(0f, CloudModel.Radius(0f, 8f, 4f, 45f));
		Assert.Equal(4f, CloudModel.Radius(2f, 8f, 4f, 45f), 3);
		Assert.Equal(8f, CloudModel.Radius(4f, 8f, 4f, 45f), 3);
		Assert.Equal(8f, CloudModel.Radius(30f, 8f, 4f, 45f), 3);
		Assert.Equal(4f, CloudModel.Radius(42.5f, 8f, 4f, 45f), 3);
		Assert.Equal(0f, CloudModel.Radius(45f, 8f, 4f, 45f));
		Assert.False(CloudModel.IsAlive(45f, 45f));
		Assert.True(CloudModel.IsAlive(44.9f, 45f));
	}

	[Fact]
	public void OpacityIsOneAtTheCentreAndZeroAtTheEdge()
	{
		Assert.Equal(1f, CloudModel.Opacity(0f, 8f));
		Assert.Equal(0.5f, CloudModel.Opacity(4f, 8f), 3);
		Assert.Equal(0f, CloudModel.Opacity(8f, 8f));
		Assert.Equal(0f, CloudModel.Opacity(1f, 0f));
	}

	[Fact]
	public void SightLinesThroughTheCoreOrEndingInsideAreBlocked()
	{
		var c = new Vector3(0f, 1f, 0f);
		Assert.True(CloudModel.SegmentBlocked(new Vector3(-20f, 1f, 0f), new Vector3(20f, 1f, 0f), c, 8f)); // straight through
		Assert.True(CloudModel.SegmentBlocked(new Vector3(-20f, 1f, 0f), new Vector3(1f, 1f, 0f), c, 8f)); // target inside
		Assert.True(CloudModel.SegmentBlocked(c, new Vector3(30f, 1f, 0f), c, 8f)); // watcher inside
		Assert.False(CloudModel.SegmentBlocked(new Vector3(-20f, 1f, 7f), new Vector3(20f, 1f, 7f), c, 8f)); // grazes the thin edge
		Assert.False(CloudModel.SegmentBlocked(new Vector3(-20f, 1f, 0f), new Vector3(-12f, 1f, 0f), c, 8f)); // stops short
		Assert.False(CloudModel.SegmentBlocked(new Vector3(-20f, 1f, 0f), new Vector3(20f, 1f, 0f), c, 0f)); // no cloud yet
	}

	private static string Repo(string rel)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (!File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent!;
		return Path.Combine(dir.FullName, rel);
	}

	private static string Field(string text, string name) => Regex.Match(text, $"^{name} = (.+)$", RegexOptions.Multiline).Groups[1].Value.Trim('"');

	[Fact]
	public void SmokeItemsMatchTheCatalogAndTheSpec()
	{
		var rows = File.ReadAllLines(Repo("docs/design/catalog/grenades.csv")).Skip(1).Select(l => l.Split(',')).ToDictionary(c => c[0]);
		foreach (var id in new[] { "smoke_grenade_east", "smoke_grenade_west" })
		{
			var text = File.ReadAllText(Repo($"data/items/defs/{id}.tres"));
			Assert.Equal(float.Parse(rows[id][5]), float.Parse(Field(text, "FuseS"), System.Globalization.CultureInfo.InvariantCulture));
			Assert.Equal(float.Parse(rows[id][8], System.Globalization.CultureInfo.InvariantCulture), float.Parse(Field(text, "WeightKg"), System.Globalization.CultureInfo.InvariantCulture), 3);
			Assert.Equal(2500, int.Parse(Field(text, "BaseValue")));
			Assert.Equal("2", Field(text, "Kind"));
			Assert.Equal("8.0", Field(text, "CloudRadiusM"));
			Assert.Equal("45.0", Field(text, "CloudLifeS"));
		}
		Assert.Equal("true", Field(File.ReadAllText(Repo("data/items/defs/smoke_grenade_west.tres")), "RandomColor"));
		Assert.Equal("false", Field(File.ReadAllText(Repo("data/items/defs/smoke_grenade_east.tres")), "RandomColor"));
	}

	[Fact]
	public void BottleIsLouderThanRockAndNeitherSells()
	{
		var bottle = File.ReadAllText(Repo("data/items/defs/throwable_bottle.tres"));
		var rock = File.ReadAllText(Repo("data/items/defs/throwable_rock.tres"));
		Assert.Equal("20.0", Field(bottle, "ImpactNoiseM"));
		Assert.Equal("12.0", Field(rock, "ImpactNoiseM"));
		Assert.Equal("0", Field(bottle, "BaseValue"));
		Assert.Equal("0", Field(rock, "BaseValue"));
		Assert.Equal("3", Field(bottle, "Kind"));
	}

	[Fact]
	public void QuartermastersSellTheirSmoke()
	{
		Assert.Contains("smoke_grenade_west", File.ReadAllText(Repo("data/traders/allied_qm.tres")));
		Assert.Contains("smoke_grenade_east", File.ReadAllText(Repo("data/traders/soviet_qm.tres")));
	}
}
