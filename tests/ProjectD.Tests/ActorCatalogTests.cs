using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

public class ActorCatalogTests
{
	private static string RepoFile(string rel)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return Path.Combine(dir!, rel);
	}

	private static string Row(string id, string parent, string kind, string stats = "", string props = "", string sets = "", string tags = "")
		=> $$$"""{"id":"{{{id}}}","parent":"{{{parent}}}","name":"{{{id}}}","kind":"{{{kind}}}","faction":"","tags":[{{{tags}}}],"sets":[{{{sets}}}],"props":{ {{{props}}} },"stats":{ {{{stats}}} },"links":{},"notes":""}""";

	private static string Doc(string actors, string sets = "")
		=> $$$"""{"actors":[{{{actors}}}],"modifier_sets":{ {{{sets}}} }}""";

	[Fact]
	public void BlankCellsInheritFromTheParentAndChildCellsWin()
	{
		var json = Doc(string.Join(",",
			Row("base", "", "template", "\"perception.view_distance\":80,\"aim.accuracy\":0.5", "\"rig\":[\"light_rig\"]", tags: "\"human\""),
			Row("grunt", "base", "actor", "\"aim.accuracy\":0.7", tags: "\"grunt\"")));
		var grunt = ActorCatalog.Parse(json).Get("grunt");
		Assert.Equal(0.7f, grunt.Stats["aim.accuracy"]);
		Assert.Equal(80f, grunt.Stats["perception.view_distance"]);
		Assert.Equal("light_rig", grunt.Text("rig"));
		Assert.Equal(new[] { "human", "grunt" }, grunt.Tags);
	}

	[Fact]
	public void TemplatesAreNotActors()
	{
		var c = ActorCatalog.Parse(Doc(Row("base", "", "template") + "," + Row("a", "base", "actor")));
		Assert.False(c.Has("base"));
		Assert.True(c.Has("a"));
	}

	[Fact]
	public void BadDataIsRejected()
	{
		Assert.Throws<ArgumentException>(() => ActorCatalog.Parse(Doc(Row("a", "nope", "actor"))));
		Assert.Throws<ArgumentException>(() => ActorCatalog.Parse(Doc(Row("a", "b", "actor") + "," + Row("b", "a", "actor"))));
		Assert.Throws<ArgumentException>(() => ActorCatalog.Parse(Doc(Row("a", "", "actor", "\"not.a_stat\":1"))));
		Assert.Throws<ArgumentException>(() => ActorCatalog.Parse(Doc(Row("a", "", "actor", sets: "\"ghost\""))));
	}

	[Fact]
	public void ModifierSetsAreAppliedAndExplained()
	{
		var json = Doc(Row("a", "", "actor", "\"awareness.notice_s\":2", sets: "\"vet\""),
			"\"vet\":[{\"stat\":\"awareness.notice_s\",\"op\":\"Pct\",\"value\":-0.25}]");
		var inst = ActorCatalog.Parse(json).Create("a");
		Assert.Equal(1.5f, inst.Stats.Get("awareness.notice_s"), 3);
		Assert.Contains("vet", inst.Stats.Explain("awareness.notice_s").ToString());
	}

	[Fact]
	public void ExportedFileLoadsAndMatchesTheOldPresets()
	{
		var c = ActorCatalog.Parse(File.ReadAllText(RepoFile("data/actors/actors.json")));
		foreach (var id in new[] { "looter", "bandit", "allied_pmc", "soviet_pmc" })
			Assert.True(c.Has(id), id);
		var allied = c.Create("allied_pmc").Stats;
		Assert.Equal(110f, allied.Get("perception.view_distance"));
		Assert.Equal(0.75f, allied.Get("aim.accuracy"), 3);
		Assert.Equal(60f, c.Create("looter").Stats.Get("perception.view_distance"));
		Assert.Equal(35, c.Create("looter").Stats.GetInt("health.head"));
	}

	[Fact]
	public void ExportedItemReferencesExist()
	{
		var known = Directory.GetFiles(RepoFile("data"), "*.tres", SearchOption.AllDirectories)
			.SelectMany(f => new[] { Path.GetFileNameWithoutExtension(f) }
				.Concat(Regex.Matches(File.ReadAllText(f), "^Id = \"([^\"]+)\"", RegexOptions.Multiline).Select(m => m.Groups[1].Value)))
			.ToHashSet();
		var c = ActorCatalog.Parse(File.ReadAllText(RepoFile("data/actors/actors.json")));
		foreach (var a in c.Actors)
			foreach (var prop in new[] { "weapon", "weapon_choices", "armor", "heal_item", "rig", "backpack", "pocket_table" })
				foreach (var item in a.List(prop))
					Assert.True(known.Contains(item), $"{a.Id}.{prop}: {item} has no resource");
	}
}
