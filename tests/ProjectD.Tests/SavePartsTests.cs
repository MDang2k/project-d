using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ProjectD.Tests;

/// <summary>Save parts (docs/superpowers/specs/architecture/save-parts-design.md s4).</summary>
public class SavePartsTests
{
	private static readonly Dictionary<string, ItemSpec> Specs = new[]
	{
		ModItems.Rifle,
		new ItemSpec("ammo", "ammo", ItemCategory.Ammo, 1, 1, 0.01f, MaxStack: 60),
		new ItemSpec("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: 30),
		new ItemSpec("vest", "vest", ItemCategory.Armor, 3, 3, 4f, MaxDurability: 60f),
		new ItemSpec("rig", "rig", ItemCategory.Rig, 3, 2, 1f, Grids: new[] { new GridSize(4, 3) }),
		new ItemSpec("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f, MaxStack: 4),
	}.ToDictionary(s => s.Id);

	private static ItemSpec? Lookup(string id) => Specs.GetValueOrDefault(id) ?? ModItems.Part(id)?.Spec;

	/// <summary>A version-10 save written before the save parts refactor, with every part filled.</summary>
	private static string Fixture()
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return File.ReadAllText(Path.Combine(dir!, "tests", "ProjectD.Tests", "fixtures", "save_v10.json"));
	}

	private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

	private static string Write(LoadedSave save)
		=> SaveSerializer.ToJson(save.Run, save.Profile, save.Run.Inventory, save.RaidInProgress);

	private static IEnumerable<ItemInstance> Gear(LoadedSave save)
		=> save.Profile.Nemeses.All.Single().Gear.EquippedItems.Select(kv => kv.Value);

	/// <summary>
	/// The fixture's one misfit part (a scope in the muzzle slot of the nemesis rifle) moves to the stash on load; every
	/// other key and value comes back as written.
	/// </summary>
	[Fact]
	public void TheVersion10FixtureReadsAndWritesBackTheSame()
	{
		var loaded = SaveSerializer.Parse(Fixture(), Lookup);

		var expected = Json(Fixture());
		var parts = (JsonObject)expected["nemeses"]![0]!["gear"]!["slots"]!["Primary"]!["parts"]!;
		var scope = parts["muzzle"]!;
		parts.Remove("muzzle");
		var written = Json(Write(loaded));
		var stashed = (JsonArray)written["stash"]!["items"]!;
		var moved = stashed.Single(i => (string?)i!["item"]!["id"] == "scope")!;
		Assert.True(JsonNode.DeepEquals(scope, moved["item"]));
		stashed.Remove(moved);

		Assert.True(JsonNode.DeepEquals(expected, written), written.ToJsonString());
		Assert.True(loaded.RaidInProgress);
		Assert.Single(loaded.Warnings, w => w.Contains("no longer fits"));
	}

	[Fact]
	public void EveryPartWritesBackWhatItRead()
	{
		var source = SaveSerializer.Parse(Fixture(), Lookup);
		var count = SaveSerializer.Parts(source.Run, source.Profile, source.Run.Inventory).Count;
		for (var i = 0; i < count; i++)
		{
			var part = SaveSerializer.Parts(source.Run, source.Profile, source.Run.Inventory)[i];
			var first = new JsonObject();
			part.Write(first);
			Assert.True(first.Count > 0, $"{part.GetType().Name} wrote nothing");

			var run = new RunProgress();
			var profile = new GameProfile();
			var ctx = new LoadContext(Lookup);
			SaveSerializer.Parts(run, profile, null)[i].Read(first, ctx);
			var second = new JsonObject();
			SaveSerializer.Parts(run, profile, run.Inventory)[i].Write(second);

			Assert.True(JsonNode.DeepEquals(first, second), $"{part.GetType().Name}: {second.ToJsonString()}");
			Assert.Empty(ctx.Warnings);
		}
	}

	[Fact]
	public void PartsOwnEveryKeyExactlyOnce()
	{
		var source = SaveSerializer.Parse(Fixture(), Lookup);
		var keys = SaveSerializer.Parts(source.Run, source.Profile, source.Run.Inventory).SelectMany(p =>
		{
			var root = new JsonObject();
			p.Write(root);
			return root.Select(kv => kv.Key).ToList();
		}).ToList();
		var expected = Json(Fixture()).Select(kv => kv.Key).Except(new[] { "version", "raidInProgress" });
		Assert.Equal(expected.OrderBy(k => k), keys.OrderBy(k => k));
	}

	/// <summary>The old static detached list kept nemesis parts and dropped them into the stash on the next load.</summary>
	[Fact]
	public void ADetachedNemesisPartReachesTheStashInTheSameLoadOnly()
	{
		var first = SaveSerializer.Parse(Fixture(), Lookup);
		Assert.Single(first.Profile.Stash.Items, i => i.Spec.Id == "scope");
		Assert.DoesNotContain("muzzle", Gear(first).Single().Parts.Keys);

		var clean = SaveSerializer.Parse(
			SaveSerializer.ToJson(new RunProgress(), new GameProfile(), null, false), Lookup);
		Assert.Empty(clean.Profile.Stash.Items);
		Assert.Empty(clean.Warnings);
	}

	[Fact]
	public void ADetachedInsuranceReturnPartReachesTheStash()
	{
		const string json = """
			{"version":10,"raidInProgress":false,"counters":{"raids":0,"extractions":0,"deaths":0,"mia":0},"quickUse":[],
			 "insuranceReturns":[{"item":{"id":"rifle","parts":{"muzzle":{"id":"scope"}}},"raidsRemaining":1}]}
			""";
		var loaded = SaveSerializer.Parse(json, Lookup);
		Assert.Single(loaded.Profile.Stash.Items, i => i.Spec.Id == "scope");
		Assert.Single(loaded.Profile.InsuranceReturns.Entries);
	}

	/// <summary>One broken part late in the file: the whole file is unreadable and the backup loads instead.</summary>
	[Fact]
	public void ACorruptPartFallsBackToTheBackupWithoutTouchingTheLiveProfile()
	{
		var broken = Json(Fixture());
		broken["knowledge"] = "not a map";
		Assert.Throws<JsonException>(() => SaveSerializer.Parse(broken.ToJsonString(), Lookup));

		var dir = Directory.CreateTempSubdirectory("pd-saveparts").FullName;
		try
		{
			var store = new SaveFileStore(Path.Combine(dir, "save.json"));
			store.Write(Fixture());
			store.Write(broken.ToJsonString()); // the good file becomes the backup
			var live = new GameProfile();
			var quarantined = new List<string>();

			var (loaded, source) = store.Load(json => SaveSerializer.Parse(json, Lookup), (file, _) => quarantined.Add(file));

			Assert.Equal(SaveSource.Backup, source);
			Assert.Single(quarantined);
			Assert.Equal(PlayerSide.Soviet, loaded!.Profile.Side);
			Assert.Null(live.Side);
			Assert.Equal(0, live.Stash.ItemCount);
		}
		finally
		{
			Directory.Delete(dir, true);
		}
	}

	[Fact]
	public void TheLoadFillsTheFreshProfileItIsGiven()
	{
		var fresh = new GameProfile();
		var loaded = SaveSerializer.Parse(Fixture(), Lookup, fresh);
		Assert.Same(fresh, loaded.Profile);
		Assert.Equal(260f, fresh.Skills.Xp("endurance"));
	}
}
