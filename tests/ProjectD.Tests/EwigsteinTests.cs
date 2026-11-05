using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class EwigsteinTests
{
	private static readonly ItemSpec ShardSpec = new("ewigstein_shard", "s", ItemCategory.Ewigstein, 1, 1, 0.5f);
	private static readonly ItemSpec SmallCanister = new("small_can", "c", ItemCategory.Canister, 2, 2, 6f, Grids: new[] { new GridSize(1, 1) });
	private static readonly ItemSpec LargeCanister = new("large_can", "c", ItemCategory.Canister, 2, 3, 12f, Grids: new[] { new GridSize(1, 3) });
	private static readonly ItemSpec Pouch = new("pouch", "p", ItemCategory.SecureContainer, 2, 2, 0.3f, Grids: new[] { new GridSize(2, 2) });
	private static readonly ItemSpec Pack = new("pack", "b", ItemCategory.Backpack, 4, 4, 1f, Grids: new[] { new GridSize(6, 6) });
	private static readonly ItemSpec Brick = new("brick", "x", ItemCategory.Misc, 1, 1, 1f);

	private static ItemInstance Shard() => ItemInstance.Create(ShardSpec);

	private static CharacterInventory Character()
	{
		var c = new CharacterInventory();
		var pack = ItemInstance.Create(Pack);
		Assert.True(c.TryEquip(pack, EquipSlot.Backpack));
		return c;
	}

	// ---- canister and secure rules

	[Fact]
	public void CanisterTakesShardsOnlyAndAsManyAsItsCells()
	{
		var can = ItemInstance.Create(LargeCanister);
		var grid = can.Grids[0];
		Assert.False(grid.TryPlaceAnywhere(ItemInstance.Create(Brick)));
		Assert.True(grid.TryPlaceAnywhere(Shard()));
		Assert.True(grid.TryPlaceAnywhere(Shard()));
		Assert.True(grid.TryPlaceAnywhere(Shard()));
		Assert.False(grid.TryPlaceAnywhere(Shard()));
		Assert.Single(ItemInstance.Create(SmallCanister).Grids);
		Assert.False(ItemInstance.Create(SmallCanister).Grids[0].TryPlaceAnywhere(ItemInstance.Create(Brick)));
	}

	[Fact]
	public void SecureContainerRejectsShardsCanistersAndAnythingHoldingThem()
	{
		var secure = ItemInstance.Create(Pouch).Grids[0];
		Assert.False(secure.TryPlaceAnywhere(Shard()));
		Assert.False(secure.TryPlaceAnywhere(ItemInstance.Create(SmallCanister)));
		var loaded = ItemInstance.Create(SmallCanister);
		loaded.Grids[0].TryPlaceAnywhere(Shard());
		Assert.False(secure.TryPlaceAnywhere(loaded));
		var pack = ItemInstance.Create(new ItemSpec("bag", "b", ItemCategory.Misc, 1, 1, 1f, Grids: new[] { new GridSize(2, 2) }));
		pack.Grids[0].TryPlaceAnywhere(Shard()); // a pouch with a shard inside is refused too
		Assert.False(secure.TryPlaceAnywhere(pack));
		Assert.True(secure.TryPlaceAnywhere(ItemInstance.Create(Brick)));
	}

	// ---- shielding

	[Fact]
	public void OnlyShardsOutsideACanisterExposeTheCarrier()
	{
		var c = Character();
		Assert.Equal(0, EwigsteinRules.UnshieldedShards(c));
		Assert.True(c.TryAutoStore(Shard()));
		Assert.Equal(1, EwigsteinRules.UnshieldedShards(c));
		var can = ItemInstance.Create(SmallCanister);
		can.Grids[0].TryPlaceAnywhere(Shard());
		Assert.True(c.TryAutoStore(can));
		Assert.Equal(1, EwigsteinRules.UnshieldedShards(c));
	}

	[Fact]
	public void HarvestedShardGoesToAFreeCanisterFirstElseUnshielded()
	{
		var c = Character();
		var can = ItemInstance.Create(SmallCanister);
		Assert.True(c.TryAutoStore(can));
		var first = Shard();
		Assert.True(EwigsteinRules.StoreShard(c, first));
		Assert.True(can.Grids[0].Contains(first));
		Assert.Equal(0, EwigsteinRules.UnshieldedShards(c));
		var second = Shard(); // the canister is full
		Assert.True(EwigsteinRules.StoreShard(c, second));
		Assert.False(can.Grids[0].Contains(second));
		Assert.Equal(1, EwigsteinRules.UnshieldedShards(c));
	}

	[Fact]
	public void StoreShardFailsWhenThereIsNoRoom()
	{
		var c = new CharacterInventory(new[] { new GridSize(1, 1) });
		Assert.True(EwigsteinRules.StoreShard(c, Shard()));
		Assert.False(EwigsteinRules.StoreShard(c, Shard()));
	}

	// ---- exposure

	private static readonly float[] None = System.Array.Empty<float>();

	[Fact]
	public void ShardsAndDepositsAddUpAndDecayRunsOnlyWithoutSources()
	{
		var m = new ExposureModel();
		m.Update(10f, 2, None); // 2 shards, 1.5/s each
		Assert.Equal(30f, m.Level, 3);
		m.Update(10f, 0, new[] { 0f }); // at a deposit: 3/s
		Assert.Equal(60f, m.Level, 3);
		m.Update(10f, 0, None);
		Assert.Equal(55f, m.Level, 3);
		m.Update(1000f, 0, None);
		Assert.Equal(0f, m.Level);
		m.Update(1000f, 5, None);
		Assert.Equal(100f, m.Level);
	}

	[Fact]
	public void DepositRateFallsOffLinearlyToNothingAtTwelveMetres()
	{
		var m = new ExposureModel();
		Assert.Equal(3f, m.DepositRate(0f), 3);
		Assert.Equal(1.5f, m.DepositRate(6f), 3);
		Assert.Equal(0f, m.DepositRate(12f));
		Assert.Equal(0f, m.DepositRate(40f));
	}

	[Fact]
	public void ThresholdsAndSwayCurve()
	{
		var m = new ExposureModel();
		m.Update(8f, 3, None); // 36
		Assert.True(m.Level > 25f);
		Assert.Equal(1f, new ExposureModel().SwayMultiplier);
		var t = new ExposureModel(new ExposureTuning(ShardRate: 1f));
		t.Update(24f, 1, None);
		Assert.Equal(1f, t.SwayMultiplier);
		t.Update(1f, 1, None); // 25
		Assert.Equal(1.2f, t.SwayMultiplier, 3);
		t.Update(75f, 1, None); // 100
		Assert.Equal(1.5f, t.SwayMultiplier, 3);
		Assert.True(t.Distorted);
		Assert.Equal(0.3f, t.HpLossPerS);
		var mid = new ExposureModel(new ExposureTuning(ShardRate: 1f));
		mid.Update(49f, 1, None);
		Assert.False(mid.Distorted);
		Assert.Equal(0f, mid.HpLossPerS);
		mid.Update(1f, 1, None);
		Assert.True(mid.Distorted);
		Assert.Equal(0f, mid.HpLossPerS);
		mid.Update(25f, 1, None); // 75
		Assert.Equal(0.3f, mid.HpLossPerS);
	}

	[Fact]
	public void ResetStartsARaidAtZero()
	{
		var m = new ExposureModel();
		m.Update(20f, 2, None);
		m.Reset();
		Assert.Equal(0f, m.Level);
		Assert.Equal(0f, m.SourceRate);
	}

	// ---- data

	[Fact]
	public void SliceItemsExistWithCatalogShapes()
	{
		string Def(string id) => System.IO.File.ReadAllText(RepoFile($"data/items/defs/{id}.tres"));
		Assert.Contains("Category = 15", Def("ewigstein_shard"));
		Assert.Contains("BaseValue = 0", Def("ewigstein_shard"));
		Assert.Contains("Vector2i(1, 1)", Def("small_ewigstein_canister"));
		Assert.Contains("Vector2i(1, 3)", Def("large_ewigstein_canister"));
		Assert.Contains("Category = 11", Def("secure_case"));
		Assert.Contains("Vector2i(3, 3)", Def("secure_case"));
		var sci = System.IO.File.ReadAllText(RepoFile("data/traders/scientist.tres"));
		Assert.Contains("small_ewigstein_canister", sci);
		Assert.Contains("large_ewigstein_canister", sci);
	}

	private static string RepoFile(string rel)
	{
		var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
		while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "project.godot")))
			dir = dir.Parent;
		return System.IO.Path.Combine(dir!.FullName, rel);
	}
}

public class FirstDeliveryTests
{
	private static readonly ItemSpec ShardSpec = new("ewigstein_shard", "s", ItemCategory.Ewigstein, 1, 1, 0.5f);
	private static readonly ItemSpec CanSpec = new("small_can", "c", ItemCategory.Canister, 2, 2, 6f, Grids: new[] { new GridSize(1, 1) });

	private static QuestSpec Quest() => new("q07", "t", "d", "side_qm", 1, System.Array.Empty<string>(), null,
		new[] { new ObjectiveSpec(ObjectiveKind.FindInRaid, 1, "ewigstein_shard") },
		new[]
		{
			new RewardSpec(RewardKind.Item, 1, "secure_case"),
			new RewardSpec(RewardKind.Reputation, 10, Target: "side_qm"),
			new RewardSpec(RewardKind.Reputation, 10, Target: QuestEngine.SideFaction),
			new RewardSpec(RewardKind.Knowledge, 0, "first_delivery"),
		},
		new[] { "note" });

	private static GameProfile Profile()
	{
		var p = new GameProfile { Side = PlayerSide.Allied };
		p.Knowledge.Register(new("note", KnowledgeKind.LabNote, "t", "x", "note_item", 1, System.Array.Empty<string>()));
		p.Knowledge.Register(new("first_delivery", KnowledgeKind.Lore, "t", "x", "", 0, System.Array.Empty<string>()));
		return p;
	}

	[Fact]
	public void QuestWaitsForTheTranslatedLabNote()
	{
		var p = Profile();
		var q = Quest();
		QuestEngine.Refresh(new[] { q }, p.Quests, p);
		Assert.Equal(QuestState.Locked, p.Quests.StateOf(q));
		p.Knowledge.Unlock("note");
		p.Knowledge.StartTranslation("note");
		p.Knowledge.OnRaidEnded();
		QuestEngine.Refresh(new[] { q }, p.Quests, p);
		Assert.Equal(QuestState.Available, p.Quests.StateOf(q));
	}

	[Fact]
	public void ShardInsideACanisterInTheStashCountsAndTurnInPaysEverything()
	{
		var p = Profile();
		var q = Quest();
		p.Knowledge.Unlock("note");
		p.Knowledge.StartTranslation("note");
		p.Knowledge.OnRaidEnded();
		QuestEngine.Refresh(new[] { q }, p.Quests, p);
		Assert.True(QuestEngine.Accept(p.Quests, q, p));
		var can = ItemInstance.Create(CanSpec);
		var shard = ItemInstance.Create(ShardSpec);
		shard.FoundInRaid = true;
		Assert.True(can.Grids[0].TryPlaceAnywhere(shard));
		Assert.True(p.Stash.TryPlaceAnywhere(can));
		var character = new CharacterInventory();
		QuestEngine.Recount(new[] { q }, p.Quests, p, character);
		Assert.Equal(QuestState.ReadyToTurnIn, p.Quests.StateOf(q));

		Assert.Equal(TurnInFailure.None, QuestEngine.TurnIn(new[] { q }, p.Quests, p, character, q));
		Assert.Empty(can.Grids[0].Items); // consumed from inside the canister
		Assert.Contains(p.Quests.Pending, r => r.ItemId == "secure_case");
		Assert.Equal(10, p.Quests.Reputation["allied_qm"]);
		Assert.Equal(10, p.Quests.Reputation[ReputationBook.FactionId(Faction.AlliedPmc)]);
		Assert.True(p.Knowledge.IsUnlocked("first_delivery"));
	}

	[Fact]
	public void ShardNotFoundInARaidDoesNotCount()
	{
		var p = Profile();
		Assert.True(p.Stash.TryPlaceAnywhere(ItemInstance.Create(ShardSpec)));
		Assert.Equal(0, QuestEngine.CountItems(p, new CharacterInventory(), "ewigstein_shard", true));
	}

	[Fact]
	public void DataFilesWire()
	{
		var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
		while (!System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "project.godot"))) dir = dir.Parent!;
		var quest = System.IO.File.ReadAllText(System.IO.Path.Combine(dir.FullName, "data/quests/q07_first_delivery.tres"));
		Assert.Contains("GiverTraderId = \"side_qm\"", quest);
		Assert.Contains("RequiresKnowledgeIds = PackedStringArray(\"german_lab_note_probe_7\")", quest);
		Assert.Contains("ItemId = \"secure_case\"", quest);
	}
}
