using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Nemesis creation in a raid (M8c spec s1): watches which AI hits the player, and when the raid ends in the player's
/// death it makes the killer a nemesis (or grows it) holding the gear that was lost, minus what insurance sends back.
/// MIA, and deaths with no attacker, make none. Thin node over <see cref="KillAttribution"/> and <see cref="NemesisRoster"/>.
/// </summary>
public partial class RaidNemesis : Node
{
	private const string NamesPath = "res://data/ai/nemesis_names.json";

	private sealed record Attacker(Faction Faction, string? NemesisId);

	private static NemesisNames? _names;

	private readonly KillAttribution _attribution = new();
	private readonly Dictionary<long, Attacker> _attackers = new();
	private Player _player = null!;
	private RaidManager _raid = null!;
	private string _mapId = "";
	private long? _killingBlow;
	private Vector3 _deathPosition;
	private RaidConfig _cfg = null!;
	private AiHuman? _live;

	/// <summary>The nemesis made or grown by this raid's death, for the result screen and the hub; null when there was none.</summary>
	public NemesisKillResult? Result { get; private set; }

	private static GameProfile Profile => GameState.Instance?.Profile ?? new GameProfile();

	public void Start(Player player, ProjectileManager projectiles, RaidManager raid, RaidConfig cfg)
	{
		_player = player;
		_raid = raid;
		_mapId = cfg.MapId;
		_cfg = cfg;
		projectiles.Hit += OnHit;
		projectiles.Killed += OnKilled;
		player.MeleeKilled += victim => OnNemesisDied(victim, "the player");
		SpawnFromRoster();
		player.Health.Died += _ => _deathPosition = player.GlobalPosition;
		if (OS.GetCmdlineUserArgs().Contains("--nemkill"))
			DevKill();
	}

	private float Now => _raid.Session.ElapsedS; // stays at the final time once the raid is over

	private void OnHit(HealthComponent victim, bool killed, Rid shooter)
	{
		if (!ReferenceEquals(victim, _player.Health) || FindAi(shooter) is not { } ai)
			return;
		var id = (long)ai.GetInstanceId();
		_attackers[id] = new Attacker(ai.Faction, ai.NemesisId);
		_attribution.RecordHit(id, Now);
		if (killed)
			_killingBlow = id;
	}

	private AiHuman? FindAi(Rid body) => GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().FirstOrDefault(a => a.GetRid() == body);

	/// <summary>
	/// The raid ended. On a death with a known killer the nemesis is made or grown and the roster changes; call it before the
	/// profile is saved. Returns the result, or null when the raid made no nemesis.
	/// </summary>
	public NemesisKillResult? OnRaidEnded(RaidOutcome outcome, IReadOnlyList<ItemInstance> lost, InsuranceRaidResult insurance)
	{
		Result = null;
		if (Live is { } survivor && !survivor.Health.IsDead)
			survivor.SyncNemesisArmor(); // a nemesis that lived keeps the wear on its armor
		if (outcome != RaidOutcome.Killed || _attribution.Killer(_killingBlow, Now) is not { } killerId
			|| !_attackers.TryGetValue(killerId, out var killer))
			return null;
		var slots = _raid.LostSlots;
		var gear = NemesisGear.Salvage(lost.Select(i => new LostItem(i, slots.TryGetValue(i, out var s) ? s : null)), insurance.ReturnedOriginals);
		var side = Profile.Side?.ToFaction();
		var friendly = killer.Faction == side; // it was on the player's side: hostile to the player only
		var place = NemesisSummary.NearestLandmark(_deathPosition.X, _deathPosition.Z,
			GetTree().GetNodesInGroup("map_landmarks").OfType<MapLandmark>().Select(l => l.ToLandmark())) ?? "";
		var kill = new NemesisKill(RunState.Current.Raids, _mapId, _deathPosition.X, _deathPosition.Z, place);
		Result = Profile.Nemeses.RegisterKill(killer.NemesisId, killer.Faction, friendly, kill, gear, Names(), ArmorTier,
			new System.Random(System.Random.Shared.Next()));
		GD.Print($"NEMESIS {(Result.IsNew ? "created" : "grew")}: {Result.Nemesis.DisplayName} {Result.Nemesis.Faction} rank {Result.Nemesis.Rank} " +
			$"kills {Result.Nemesis.KillCount} carries {NemesisGear.Carried(Result.Nemesis.Gear).Count} items, retired {Result.Retired?.Name ?? "-"}, place '{kill.Place}'");
		return Result;
	}

	private static int ArmorTier(ItemSpec spec) => ItemDb.Data(spec.Id)?.Payload is ArmorData a ? a.Tier : 0;

	private static NemesisNames Names()
	{
		if (_names is not null)
			return _names;
		_names = FileAccess.FileExists(NamesPath) ? NemesisNames.Parse(FileAccess.GetFileAsString(NamesPath)) : NemesisNames.Parse("{}");
		return _names;
	}

	/// <summary>The AI that plays the nemesis this raid, if one spawned.</summary>
	public AiHuman? Live => _live is { } ai && IsInstanceValid(ai) ? ai : null;

	/// <summary>The nemesis was killed (by the player, by another AI or by the zone) and left the roster.</summary>
	public event System.Action<Nemesis, string>? NemesisKilled;

	/// <summary>The player killed a nemesis (the HUD draws a line); other deaths of a nemesis make no pulse.</summary>
	public static event System.Action<Nemesis>? PlayerKilledNemesis;

	private bool _handled;

	private static string ActorFor(Faction faction) => faction switch
	{
		Faction.AlliedPmc => "allied_pmc",
		Faction.SovietPmc => "soviet_pmc",
		Faction.Bandits => "bandit",
		Faction.Deserters => "harker_squad",
		_ => "looter",
	};

	/// <summary>
	/// At raid start each nemesis of this map rolls its spawn chance and at most one comes (M8c spec s3): at the navmesh
	/// point nearest where it killed you, patrolling a small area, in your gear and a little tougher per rank. Too close
	/// to your start and it stays away this raid. Dev: --nemspawn always brings the first one, --nemseed=faction makes one.
	/// </summary>
	private async void SpawnFromRoster()
	{
		var args = OS.GetCmdlineUserArgs();
		var rng = new System.Random(System.Random.Shared.Next());
		if (args.FirstOrDefault(a => a.StartsWith("--nemseed="))?["--nemseed=".Length..] is { } seed)
			DevSeed(seed);
		var nemesis = args.Contains("--nemspawn") ? Profile.Nemeses.ForMap(_mapId).FirstOrDefault() : Profile.Nemeses.RollSpawn(_mapId, rng);
		if (nemesis is null)
			return;
		var map = _player.GetWorld3D().NavigationMap;
		await AiSpawner.WaitForNavigation(GetTree(), map);
		var centre = AiSpawner.Snap(map, new Vector3(nemesis.X, 0f, nemesis.Z));
		if (centre.DistanceTo(_player.GlobalPosition) < _cfg.AiMinDistanceFromPlayer)
		{
			GD.Print($"NEMESIS {nemesis.DisplayName} is too close to the start and stays away this raid");
			return;
		}
		var tuning = Profile.Nemeses.Tuning;
		var patrol = WavePlanner.Loop(centre, tuning.PatrolRadiusM).Select(p => AiSpawner.Snap(map, p)).ToArray();
		_live = AiSpawner.Spawn(GetParent(), ActorFor(nemesis.Faction), $"nemesis_{nemesis.Id}", centre, patrol, false, ai =>
		{
			ai.Nemesis = nemesis;
			ai.NemesisId = nemesis.Id;
			ai.HostileToPlayer = nemesis.FriendlyOrigin;
			var bonus = tuning.ToughnessPerRank * nemesis.Rank; // accuracy and courage, per rank, over the faction preset
			ai.Actor!.Stats.AddRange(new[]
			{
				new StatModifier(Stats.Aim.Accuracy, ModOp.Pct, bonus, "nemesis"),
				new StatModifier(Stats.Tactics.Courage, ModOp.Pct, bonus, "nemesis"),
			});
		});
		if (_live is null)
			return;
		_live.Health.Died += _ => CallDeferred(nameof(OnLiveDied));
		GD.Print($"NEMESIS spawned: {nemesis.DisplayName} rank {nemesis.Rank} at {centre.X:0},{centre.Z:0}, armor pieces {_live.Health.Armor.Count} (tiers {string.Join("/", _live.Health.Armor.Select(x => x.EffectiveTier))}), weapon {(_live.Weapon?.Data.Id ?? "none")}, wears {string.Join(",", NemesisGear.Carried(nemesis.Gear).Select(i => i.Spec.Id).Take(6))}");
		if (args.Contains("--nemslay"))
			DevSlay();
	}

	/// <summary>Dev: --nemslay kills the nemesis as the player a few seconds after it spawns.</summary>
	private async void DevSlay()
	{
		await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
		if (Live is { } ai)
		{
			ai.Health.Body.TakeDamage(BodyPart.Head, 9999f);
			OnNemesisDied(ai.Health, "the player");
		}
	}

	private void DevSeed(string factionName)
	{
		if (!System.Enum.TryParse<Faction>(factionName, true, out var faction))
			faction = Faction.Bandits;
		var gear = new CharacterInventory();
		gear.TryEquip(ItemDb.Create("fal_type")!, EquipSlot.Primary);
		gear.TryEquip(ItemDb.Create("prototype_vest_t4")!, EquipSlot.Vest);
		gear.TryEquip(ItemDb.Create("steel_helmet_t2")!, EquipSlot.Head);
		gear.TryEquip(ItemDb.Create("field_pack")!, EquipSlot.Backpack);
		gear.TryAutoStore(ItemDb.Create("bandage", 3)!);
		Profile.Nemeses.RegisterKill(null, faction, false, new NemesisKill(1, _mapId, -30f, 30f),
			gear.EquippedItems.Select(kv => new LostItem(kv.Value, kv.Key)), Names(), ArmorTier, new System.Random(3));
	}

	private void OnKilled(HealthComponent victim, Rid shooter)
	{
		var by = shooter == _player.GetRid() ? "the player"
			: FindAi(shooter) is { } ai ? GameText.Get("faction." + ai.Faction.ToString().ToLowerInvariant())
			: "the zone";
		OnNemesisDied(victim, by);
	}

	private void OnLiveDied()
	{
		if (!_handled && Live is { } ai)
			OnNemesisDied(ai.Health, "the zone"); // a mine, a fall: nobody took credit in time
	}

	private void OnNemesisDied(HealthComponent victim, string by)
	{
		if (_handled || victim.GetParent() is not AiHuman { Nemesis: { } nemesis })
			return;
		_handled = true;
		Profile.Nemeses.Remove(nemesis.Id, by, RunState.Current.Raids);
		GD.Print($"NEMESIS {nemesis.DisplayName} was killed by {by}; its body holds {NemesisGear.Carried(nemesis.Gear).Count} items");
		NemesisKilled?.Invoke(nemesis, by);
		if (by == "the player")
			PlayerKilledNemesis?.Invoke(nemesis);
	}

	/// <summary>Dev: --nemkill makes the nearest AI the killer and kills the player a few seconds in, to test the whole chain.</summary>
	private async void DevKill()
	{
		await ToSignal(GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
		var ai = GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>()
			.OrderBy(a => a.GlobalPosition.DistanceTo(_player.GlobalPosition)).FirstOrDefault();
		if (ai is null)
			return;
		GD.Print($"NEMESIS dev: {ai.Name} ({ai.Faction}) kills the player");
		OnHit(_player.Health, true, ai.GetRid());
		_player.Health.Body.TakeDamage(BodyPart.Head, 9999f);
	}
}
