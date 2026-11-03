using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

/// <summary>The shipped Border Strip layout (data/maps/border_strip.layout.json) against the raid map standard.</summary>
public class BorderStripLayoutTests
{
	private static string RepoFile(string rel)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return Path.Combine(dir!, rel);
	}

	private static MapLayout Layout() => MapLayout.Parse(File.ReadAllText(RepoFile("data/maps/border_strip.layout.json")));

	[Fact]
	public void TheLayoutParsesAndPassesTheValidator()
	{
		var report = MapValidator.Validate(Layout());
		Assert.True(report.Ok, string.Join("\n", report.Issues.Select(i => $"{i.Rule}: {i.Message}")));
	}

	[Fact]
	public void MatchesTheSpecNumbers()
	{
		var l = Layout();
		Assert.Equal(new[] { 300f, 400f }, l.Size);
		Assert.Equal(30, l.RaidMinutes);
		Assert.Equal(6, l.OfType("playerSpawn").Count());
		Assert.Equal(3, l.OfType("playerSpawn").Count(m => m.Prop("side") == "west"));
		Assert.Equal(8, l.OfType("aiSpawn").Count(m => m.Prop("preset") == "looter"));
		Assert.Equal(2, l.OfType("waveSpawn").Count(m => m.Prop("wave") == "bandit" && m.Prop("atMin") == "8,16,24"));
		Assert.All(l.OfType("waveSpawn").Where(m => m.Prop("wave") == "patrol"), m => Assert.Equal("8", m.Prop("atMin")));
		Assert.Equal(3, l.OfType("extract").Count());
		var tunnel = l.OfType("tunnel").ToList();
		Assert.Equal(2, tunnel.Count);
		Assert.All(tunnel, t => Assert.Equal(t.Prop("pair") == "", false));
		Assert.Equal(tunnel[0].Id, tunnel[1].Prop("pair"));
		Assert.Equal(tunnel[1].Id, tunnel[0].Prop("pair"));
		Assert.Equal(2, l.OfType("transit").Count());
		Assert.Equal(2, l.OfType("arrival").Count());
	}

	[Fact]
	public void AiPresetsAndItemsExistInTheCatalogs()
	{
		var l = Layout();
		var actors = ActorCatalog.Parse(File.ReadAllText(RepoFile("data/actors/actors.json")));
		foreach (var m in l.OfType("aiSpawn").Concat(l.OfType("waveSpawn")))
			Assert.True(actors.Has(m.Prop("preset")), $"{m.Id}: unknown actor '{m.Prop("preset")}'");
		var keys = File.ReadAllText(RepoFile("docs/design/catalog/keys_special.csv"));
		foreach (var item in l.Markers.SelectMany(m => new[] { m.Prop("item"), m.Prop("key"), m.Prop("gives") }).Where(i => i != ""))
			Assert.Contains(item + ",", keys);
	}

	[Fact]
	public void TheGrayboxBuildsWithUniqueNames()
	{
		var g = GrayboxBuilder.Build(Layout());
		Assert.Equal(g.Boxes.Count, g.Boxes.Select(b => b.Name).Distinct().Count());
		Assert.Equal(g.Markers.Count, g.Markers.Select(m => m.Name).Distinct().Count());
	}
}
