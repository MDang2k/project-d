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

public class ActorPhase4Tests
{
	private static ActorCatalog Real()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return ActorCatalog.Parse(File.ReadAllText(Path.Combine(dir!, "data/actors/actors.json")));
	}

	[Fact]
	public void BossHpSetsMatchTheBossesSheetColumns()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		var lines = File.ReadAllLines(Path.Combine(dir!, "docs/design/catalog/bosses.csv"));
		var header = lines[0].Split(',');
		var catalog = Real();
		var checkedRows = 0;
		foreach (var line in lines.Skip(1))
		{
			// the notes columns hold commas inside quotes; the HP columns are the last seven plain numbers
			var cells = System.Text.RegularExpressions.Regex.Matches(line, "(\"([^\"]|\"\")*\"|[^,]*)(,|$)")
				.Select(m => m.Value.TrimEnd(',').Trim('"')).ToArray();
			var id = cells[0];
			if (id == "" || !catalog.SetNames.Contains("boss_hp_" + id))
				continue;
			var hp = new StatSet();
			hp.AddRange(catalog.Set("boss_hp_" + id));
			var n = header.Length;
			Assert.Equal(float.Parse(cells[n - 6], System.Globalization.CultureInfo.InvariantCulture), hp.Get(Stats.Health.Head));
			Assert.Equal(float.Parse(cells[n - 5], System.Globalization.CultureInfo.InvariantCulture), hp.Get(Stats.Health.Thorax));
			Assert.Equal(float.Parse(cells[n - 4], System.Globalization.CultureInfo.InvariantCulture), hp.Get(Stats.Health.Stomach));
			Assert.Equal(float.Parse(cells[n - 3], System.Globalization.CultureInfo.InvariantCulture), hp.Get(Stats.Health.Arm));
			Assert.Equal(float.Parse(cells[n - 2], System.Globalization.CultureInfo.InvariantCulture), hp.Get(Stats.Health.Leg));
			checkedRows++;
		}
		Assert.True(checkedRows >= 3);
	}

	[Fact]
	public void TierAndActSetsAreAddedOnlyWhenTheSheetDefinesThem()
	{
		var json = """{"actors":[{"id":"a","parent":"","name":"a","kind":"actor","faction":"","tags":[],"sets":[],"props":{},"stats":{},"links":{},"notes":""}],"modifier_sets":{"tier_2":[{"stat":"aim.accuracy","op":"Add","value":0.1}]}}""";
		var c = ActorCatalog.Parse(json);
		Assert.Equal(0.6f, c.Create("a", 2, 5).Stats.Get(Stats.Aim.Accuracy), 3);
		Assert.Equal(0.5f, c.Create("a", 1, null).Stats.Get(Stats.Aim.Accuracy), 3);
	}

	[Fact]
	public void InspectorShowsFinalValueBaseAndSource()
	{
		var inst = Real().Create("bandit", extraSets: new[] { "veteran" });
		var text = string.Join("\n", ActorInspectorText.Lines(inst));
		Assert.Contains("bandit", text);
		Assert.Contains("[perception]", text);
		Assert.Contains("veteran", text);
		var only = ActorInspectorText.Lines(inst, onlyChanged: true);
		Assert.All(only.Where(l => l.StartsWith("  ")), l => Assert.Contains("veteran", l));
	}

	[Fact]
	public void StatsChangedFiresWhenAModifierSetIsAddedLive()
	{
		var inst = Real().Create("looter");
		var seen = 0;
		inst.Stats.Changed += _ => seen++;
		var before = inst.Stats.Get(Stats.Awareness.NoticeS);
		inst.Stats.AddRange(Real().Set("rookie"));
		Assert.Equal(1, seen);
		Assert.True(inst.Stats.Get(Stats.Awareness.NoticeS) > before);
		inst.Stats.Remove("rookie");
		Assert.Equal(before, inst.Stats.Get(Stats.Awareness.NoticeS));
	}
}
