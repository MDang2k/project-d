using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class SfxCatalogTests
{
	private static string Repo(string rel)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (!File.Exists(Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent!;
		return Path.Combine(dir.FullName, rel);
	}

	private static SfxCatalog Real() => SfxCatalog.Parse(File.ReadAllText(Repo("data/audio/sfx.json")));

	[Fact]
	public void EveryEventFileExistsAsAWav()
	{
		foreach (var e in Real().Events)
		{
			foreach (var f in e.Files)
				Assert.True(File.Exists(Repo($"assets/audio/sfx/{f}.wav")), $"{e.Id}: missing {f}.wav");
		}
	}

	[Fact]
	public void TheEventsTheGameAsksForAllExist()
	{
		var c = Real();
		foreach (var id in new[]
		{
			"shot_rifle", "shot_pistol", "dry_fire", "mag_out", "mag_in", "bolt_rack", "mag_check", "bullet_whiz", "impact_flesh",
			"impact_armor", "impact_concrete", "impact_dirt", "impact_metal", "footstep", "hurt", "pickup", "door_slide",
			"lock_click", "mine_blast", "flare_fire", "heli_land", "extract_tick", "extract_done", "ui_open", "ambient_wind",
		})
			Assert.NotNull(c.Event(id));
	}

	[Fact]
	public void BusesAreKnownAndUnknownEventsPickNothing()
	{
		var c = Real();
		Assert.All(c.Events, e => Assert.Contains(e.Bus, new[] { "SFX", "Ambient", "UI" }));
		Assert.Null(c.Pick("does_not_exist", new Random(1)));
	}

	[Fact]
	public void PickNeverRepeatsTheLastFileAndStaysInPitchRange()
	{
		var c = SfxCatalog.Parse("{\"events\":{\"a\":{\"files\":[\"x\",\"y\",\"z\"],\"pitch\":0.1}}}");
		var rng = new Random(5);
		var last = "";
		for (var i = 0; i < 200; i++)
		{
			var pick = c.Pick("a", rng)!.Value;
			Assert.NotEqual(last, pick.File);
			Assert.InRange(pick.PitchScale, 0.9f, 1.1f);
			last = pick.File;
		}
	}

	[Fact]
	public void SingleFileEventsAlwaysPickIt()
	{
		var c = SfxCatalog.Parse("{\"events\":{\"a\":{\"files\":[\"x\"]}}}");
		var rng = new Random(1);
		Assert.All(Enumerable.Range(0, 20), _ => Assert.Equal("x", c.Pick("a", rng)!.Value.File));
	}

	[Fact]
	public void ParseRejectsAnEventWithoutFiles()
		=> Assert.Throws<FormatException>(() => SfxCatalog.Parse("{\"events\":{\"a\":{\"files\":[]}}}"));
}
