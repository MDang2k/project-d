using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectD.Tests;

public class KnowledgeTests
{
	private static KnowledgeSpec Code => new("code", KnowledgeKind.DoorCode, "t", "x", "code_item", 0, new[] { "lock.side_room" });
	private static KnowledgeSpec Stash => new("stash", KnowledgeKind.StashLocation, "t", "x", "stash_item", 0, new[] { "stash.tower" });
	private static KnowledgeSpec Note => new("note", KnowledgeKind.LabNote, "t", "x", "note_item", 2, new[] { "quest.delivery" });
	private static KnowledgeSpec Note2 => new("note2", KnowledgeKind.LabNote, "t", "x", "note2_item", 1, Array.Empty<string>());
	private static KnowledgeSpec Lore => new("lore", KnowledgeKind.Lore, "t", "x", "lore_item", 0, Array.Empty<string>());

	private static KnowledgeBook Book()
	{
		var b = new KnowledgeBook();
		foreach (var s in new[] { Code, Stash, Note, Note2, Lore })
			b.Register(s);
		return b;
	}

	// ---- unlocking

	[Fact]
	public void UnlockingGivesUsableKnowledgeForEveryKindExceptALabNote()
	{
		var b = Book();
		Assert.True(b.Unlock("code"));
		Assert.True(b.IsUnlocked("code"));
		Assert.True(b.IsActive("code"));
		Assert.True(b.Opens("lock.side_room"));
		Assert.False(b.Opens("stash.tower"));
	}

	[Fact]
	public void UnlockingTwiceChangesNothingAndUnknownIdsAreIgnored()
	{
		var b = Book();
		var seen = 0;
		b.Unlocked += _ => seen++;
		Assert.True(b.Unlock("lore"));
		Assert.False(b.Unlock("lore"));
		Assert.False(b.Unlock("nothing"));
		Assert.Equal(1, seen);
	}

	[Fact]
	public void ALabNoteIsKnownButWithheldUntilTranslated()
	{
		var b = Book();
		b.Unlock("note");
		Assert.True(b.IsUnlocked("note"));
		Assert.False(b.IsActive("note"));
		Assert.False(b.Opens("quest.delivery")); // its effects wait for the translation
	}

	[Fact]
	public void ItemsMapToTheirKnowledgeAndUnlockIt()
	{
		var b = Book();
		Assert.Equal("code", b.ForItem("code_item")!.Id);
		Assert.Null(b.ForItem("bandage"));
		Assert.Equal("stash", b.UnlockFromItem("stash_item")!.Id);
		Assert.Null(b.UnlockFromItem("stash_item")); // already known
		Assert.Null(b.UnlockFromItem("bandage"));
	}

	[Fact]
	public void KnownItemsStopSpawningOthersAlwaysMay()
	{
		var b = Book();
		Assert.True(b.ShouldSpawn("code_item"));
		b.Unlock("code");
		Assert.False(b.ShouldSpawn("code_item"));
		Assert.True(b.ShouldSpawn("bandage"));
		b.Unlock("note"); // an untranslated note counts: you have read it
		Assert.False(b.ShouldSpawn("note_item"));
	}

	// ---- translation

	[Fact]
	public void ATranslationRunsOneAtATimeAndOnlyForUnlockedLabNotes()
	{
		var b = Book();
		Assert.False(b.StartTranslation("note"));   // not unlocked
		b.Unlock("note");
		b.Unlock("note2");
		b.Unlock("code");
		Assert.False(b.StartTranslation("code"));   // needs none
		Assert.True(b.StartTranslation("note"));
		Assert.False(b.StartTranslation("note2"));  // one at a time
		Assert.False(b.StartTranslation("note"));   // already running
		Assert.Equal("note", b.ActiveTranslation);
		Assert.Equal(2, b.RaidsLeft("note"));
	}

	[Fact]
	public void EveryRaidEndCountsWhateverTheOutcomeAndTheLastOneFinishesIt()
	{
		var b = Book();
		b.Unlock("note");
		b.StartTranslation("note");
		var done = new List<string>();
		b.Translated += s => done.Add(s.Id);
		Assert.Null(b.OnRaidEnded());
		Assert.Equal(1, b.RaidsLeft("note"));
		Assert.False(b.IsActive("note"));
		Assert.Equal("note", b.OnRaidEnded()!.Id);
		Assert.True(b.IsActive("note"));
		Assert.True(b.Opens("quest.delivery"));
		Assert.Null(b.ActiveTranslation);
		Assert.Equal(new[] { "note" }, done);
		Assert.Null(b.OnRaidEnded()); // nothing left to count
	}

	[Fact]
	public void TheNextNoteCanStartOnceTheFirstIsDone()
	{
		var b = Book();
		b.Unlock("note");
		b.Unlock("note2");
		b.StartTranslation("note");
		b.OnRaidEnded();
		b.OnRaidEnded();
		Assert.True(b.StartTranslation("note2"));
		Assert.False(b.StartTranslation("note")); // already translated
	}

	[Fact]
	public void RaidEndsWithNothingRunningDoNothing()
	{
		var b = Book();
		b.Unlock("note");
		Assert.Null(b.OnRaidEnded());
		Assert.Equal(0, b.RaidsLeft("note"));
	}

	// ---- the read action

	[Fact]
	public void AReadTakesFourSecondsThenFinishes()
	{
		var r = new ReadTimer();
		string? done = null;
		r.Finished += id => done = id;
		Assert.True(r.Start("code_item"));
		r.Update(3.9f);
		Assert.Null(done);
		Assert.Equal(0.975f, r.Progress, 3);
		r.Update(0.2f);
		Assert.Equal("code_item", done);
		Assert.False(r.IsReading);
	}

	[Fact]
	public void DamageCancelsTheReadAndUnlocksNothing()
	{
		var r = new ReadTimer();
		string? done = null;
		var cancelled = 0;
		r.Finished += id => done = id;
		r.Cancelled += () => cancelled++;
		r.Start("code_item");
		r.Update(2f);
		r.Cancel();
		r.Update(10f);
		Assert.Null(done);
		Assert.Equal(1, cancelled);
		r.Cancel(); // nothing to cancel
		Assert.Equal(1, cancelled);
		Assert.True(r.Start("code_item")); // can start again from zero
		Assert.Equal(0f, r.Progress);
	}

	[Fact]
	public void OnlyOneReadAtATime()
	{
		var r = new ReadTimer();
		Assert.True(r.Start("a"));
		Assert.False(r.Start("b"));
	}

	// ---- extraction

	private static ItemSpec ItemOf(string id) => new(id, id, ItemCategory.Knowledge, 1, 1, 0.01f, BaseValue: 1000, Insurable: false);

	[Fact]
	public void ExtractingWithUnreadKnowledgeItemsUnlocksThemWhereverTheyAre()
	{
		var b = Book();
		var character = new CharacterInventory();
		character.TryAutoStore(ItemInstance.Create(ItemOf("code_item")));
		var pouch = ItemInstance.Create(new ItemSpec("pouch", "pouch", ItemCategory.SecureContainer, 2, 2, 0.5f, Grids: new[] { new GridSize(2, 2) }, Insurable: false));
		character.TryEquip(pouch, EquipSlot.SecureContainer);
		pouch.Grids[0].TryPlaceAnywhere(ItemInstance.Create(ItemOf("stash_item")));
		character.TryAutoStore(ItemInstance.Create(new ItemSpec("bandage", "bandage", ItemCategory.Medical, 1, 1, 0.1f)));
		var unlocked = KnowledgeRules.OnExtracted(b, character);
		Assert.Equal(new[] { "code", "stash" }, unlocked.Select(k => k.Id).OrderBy(i => i));
		Assert.True(b.IsActive("code") && b.IsActive("stash"));
		Assert.Empty(KnowledgeRules.OnExtracted(b, character)); // nothing new the second time
	}

	[Fact]
	public void KnowledgeSurvivesTheItemBeingLost()
	{
		var profile = new GameProfile();
		foreach (var s in new[] { Code, Note })
			profile.Knowledge.Register(s);
		var character = new CharacterInventory();
		character.TryAutoStore(ItemInstance.Create(ItemOf("code_item")));
		KnowledgeRules.OnExtracted(profile.Knowledge, character);
		character.TakeEverythingExceptSecure(); // death takes the gear
		Assert.True(profile.Knowledge.IsActive("code"));
		Assert.True(profile.Knowledge.Opens("lock.side_room"));
	}

	// ---- quests

	private static QuestSpec Quest(params string[] knowledge) => new("q", "t", "d", "medic", 1, Array.Empty<string>(), null,
		new[] { new ObjectiveSpec(ObjectiveKind.Eliminate, 1) }, Array.Empty<RewardSpec>(), knowledge);

	[Fact]
	public void AQuestWaitsForUsableKnowledge()
	{
		var profile = new GameProfile { Side = PlayerSide.Allied };
		profile.Knowledge.Register(Note);
		var q = Quest("note");
		var catalog = new[] { q };
		Assert.False(QuestEngine.PrerequisitesMet(q, profile.Quests, profile, catalog));
		profile.Knowledge.Unlock("note");
		Assert.False(QuestEngine.PrerequisitesMet(q, profile.Quests, profile, catalog)); // unlocked but untranslated
		profile.Knowledge.StartTranslation("note");
		profile.Knowledge.OnRaidEnded();
		profile.Knowledge.OnRaidEnded();
		Assert.True(QuestEngine.PrerequisitesMet(q, profile.Quests, profile, catalog));
	}

	[Fact]
	public void QuestsWithoutKnowledgeRequirementsAreUnaffected()
	{
		var profile = new GameProfile { Side = PlayerSide.Allied };
		Assert.True(QuestEngine.PrerequisitesMet(Quest(), profile.Quests, profile, new[] { Quest() }));
	}

	// ---- trade

	[Fact]
	public void OnlyTheScientistBuysKnowledgeItems()
	{
		var money = new ItemSpec("banknotes", "banknotes", ItemCategory.Misc, 1, 1, 0f, 5000, BaseValue: 1);
		var specs = new[] { money, ItemOf("code_item") }.ToDictionary(s => s.Id);
		var profile = new GameProfile();
		var engine = new TradeEngine(profile, new CharacterInventory(), id => specs.GetValueOrDefault(id),
			(id, n) => specs.TryGetValue(id, out var s) ? ItemInstance.Create(s, n) : null, _ => 0);
		var scientist = new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory> { ItemCategory.Knowledge }, 1.2f, 0.8f, new List<BarterSpec>());
		var medic = new TraderLevelSpec(new List<ListingSpec>(), new HashSet<ItemCategory> { ItemCategory.Medical, ItemCategory.Misc }, 1.2f, 0.45f, new List<BarterSpec>());
		var item = ItemInstance.Create(specs["code_item"]);
		profile.Stash.TryPlaceAnywhere(item);
		Assert.Equal(TradeFailure.NotSellableHere, engine.CanSell(medic, item));
		Assert.Equal(TradeFailure.None, engine.CanSell(scientist, item));
		var result = engine.Sell(scientist, profile.Trader("scientist"), item);
		Assert.True(result.Ok);
		Assert.Equal(800, result.Money); // 1000 x 0.8
	}

	// ---- save

	[Fact]
	public void KnowledgeAndAnActiveTranslationSurviveASaveRoundTrip()
	{
		var profile = new GameProfile();
		foreach (var s in new[] { Code, Note, Note2 })
			profile.Knowledge.Register(s);
		profile.Knowledge.Unlock("code");
		profile.Knowledge.Unlock("note");
		profile.Knowledge.Unlock("note2");
		profile.Knowledge.StartTranslation("note");
		profile.Knowledge.OnRaidEnded();
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false));
		Assert.Contains("\"knowledge\"", json);

		var loaded = new GameProfile();
		foreach (var s in new[] { Code, Note, Note2 })
			loaded.Knowledge.Register(s);
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, _ => null, new List<string>());
		Assert.True(loaded.Knowledge.IsActive("code"));
		Assert.Equal("note", loaded.Knowledge.ActiveTranslation);
		Assert.Equal(1, loaded.Knowledge.RaidsLeft("note"));
		Assert.False(loaded.Knowledge.IsActive("note"));
		Assert.True(loaded.Knowledge.IsUnlocked("note2"));
		Assert.False(loaded.Knowledge.StartTranslation("note2")); // still one at a time
	}

	[Fact]
	public void AVersion9SaveLoadsWithNothingKnown()
	{
		const string v9 = """{"version":9,"raidInProgress":false,"counters":{"raids":2,"extractions":1,"deaths":1,"mia":0},"quickUse":[]}""";
		var file = SaveSerializer.Parse(v9);
		Assert.Equal(SaveFile.CurrentVersion, file.Version);
		Assert.True(SaveFile.CurrentVersion >= 10);
		Assert.NotNull(file.Knowledge);
		Assert.Empty(file.Knowledge!);
	}

	[Fact]
	public void ABadSaveWithTwoRunningTranslationsKeepsOne()
	{
		var b = Book();
		b.Load(new Dictionary<string, KnowledgeEntry>
		{
			["note"] = new(KnowledgeState.Translating, 2), ["note2"] = new(KnowledgeState.Translating, 1),
		});
		Assert.Equal(1, b.Snapshot().Values.Count(e => e.State == KnowledgeState.Translating));
		Assert.True(b.IsUnlocked("note2"));
	}

	[Fact]
	public void UnknownKnowledgeStatesInASaveFallBackToUnlocked()
	{
		var profile = new GameProfile();
		profile.Knowledge.Register(Code);
		profile.Knowledge.Unlock("code");
		var json = SaveSerializer.ToJson(SaveSerializer.ToFile(new RunProgress(), profile, null, false)).Replace("\"Translated\"", "\"Mystery\"");
		var loaded = new GameProfile();
		loaded.Knowledge.Register(Code);
		SaveSerializer.Apply(SaveSerializer.Parse(json), new RunProgress(), loaded, _ => null, new List<string>());
		Assert.True(loaded.Knowledge.IsUnlocked("code"));
	}

	// ---- shipped data

	private static string RepoFile(string rel)
	{
		var dir = AppContext.BaseDirectory;
		while (dir != null && !File.Exists(Path.Combine(dir, "Project D.csproj")))
			dir = Path.GetDirectoryName(dir);
		return Path.Combine(dir!, rel);
	}

	[Fact]
	public void TheSixSliceEntriesAreConsistentAcrossDataItemsTextsAndTheCatalog()
	{
		var documents = File.ReadAllLines(RepoFile("docs/design/catalog/documents.csv"));
		var texts = File.ReadAllText(RepoFile("scripts/core/GameText.cs"));
		Assert.Equal(8, KnowledgeCatalog.Order.Length); // seven documents plus the 8e quest-reward lore entry
		foreach (var id in KnowledgeCatalog.Order.Where(i => i != "first_delivery"))
		{
			var data = File.ReadAllText(RepoFile($"data/knowledge/{id}.tres"));
			string Field(string name) => Regex.Match(data, $"^{name} = (.+)$", RegexOptions.Multiline).Groups[1].Value.Trim('"');
			Assert.Equal(id, Field("Id"));
			Assert.Equal(id, Field("ItemId"));
			Assert.Equal($"knowledge.{id}.title", Field("TitleKey"));
			Assert.Contains($"[\"knowledge.{id}.title\"]", texts);
			Assert.Contains($"[\"knowledge.{id}.text\"]", texts);
			Assert.Contains($"[\"item.{id}\"]", texts);
			var item = File.ReadAllText(RepoFile($"data/items/defs/{id}.tres"));
			Assert.Contains("Category = 14", item);
			Assert.Contains("Insurable = false", item);
			Assert.Contains($"Id = \"{id}\"", item);
			// the catalog row (documents sheet): kind, translation raids, Slice
			var row = documents.First(l => l.StartsWith(id + ","));
			var kind = new[] { "Lore", "DoorCode", "StashLocation", "Frequency", "LabNote" }[int.Parse(Field("Kind"))];
			Assert.Contains("," + kind + ",", row);
			Assert.Contains(",Slice,", row);
			Assert.Contains("," + Field("TranslationRaids") + ",", row);
		}
	}

	[Fact]
	public void TheScientistBuysKnowledgeItemsInTheShippedData()
	{
		var data = File.ReadAllText(RepoFile("data/traders/scientist.tres"));
		Assert.Equal(4, Regex.Matches(data, "BuyCategories = PackedStringArray\\(\"Knowledge\"\\)").Count);
		foreach (var other in new[] { "allied_qm", "soviet_qm", "smuggler", "medic", "engineer" })
			Assert.DoesNotContain("Knowledge", File.ReadAllText(RepoFile($"data/traders/{other}.tres")));
	}
}
