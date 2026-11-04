using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>Nemesis numbers (M8c spec s1 and s3). Placeholders to tune.</summary>
public sealed record NemesisTuning(
	int MaxRank = 3,
	/// <summary>Nemeses kept per map; a fourth retires the oldest.</summary>
	int CapPerMap = 3,
	/// <summary>Chance, per nemesis on the map, to be in a raid.</summary>
	float SpawnChance = 0.3f,
	/// <summary>Accuracy and courage bonus per rank over the faction preset.</summary>
	float ToughnessPerRank = 0.10f,
	/// <summary>Radius of the area it patrols round its kill position, metres.</summary>
	float PatrolRadiusM = 15f);

/// <summary>One time the nemesis killed the player; Place is the text key of the nearest map landmark, empty in open ground.</summary>
public sealed record NemesisKill(int Raid, string MapId, float X, float Z, string Place = "");

/// <summary>
/// An AI that killed the player and kept their gear (M8c). Lives in the profile and the save. The gear is a real
/// inventory of real items, so the body it dies with drops exactly what it carried.
/// </summary>
public sealed class Nemesis
{
	public Nemesis(string id, string name, string nickname, Faction faction, string mapId, float x, float z, int raidCreated,
		bool friendlyOrigin, CharacterInventory gear)
	{
		Id = id;
		Name = name;
		Nickname = nickname;
		Faction = faction;
		MapId = mapId;
		X = x;
		Z = z;
		RaidCreated = raidCreated;
		FriendlyOrigin = friendlyOrigin;
		Gear = gear;
	}

	public string Id { get; }
	public string Name { get; }
	public string Nickname { get; }
	public Faction Faction { get; }
	public string MapId { get; }
	/// <summary>Where it waits: the position of its latest kill.</summary>
	public float X { get; set; }
	public float Z { get; set; }
	public int Rank { get; set; } = 1;
	public int RaidCreated { get; }
	/// <summary>It was a friendly PMC when it killed the player (friendly fire or 8b hostility): hostile to the player only.</summary>
	public bool FriendlyOrigin { get; }
	public CharacterInventory Gear { get; }
	public List<NemesisKill> Kills { get; } = new();

	public int KillCount => Kills.Count;

	/// <summary>Multiplier on accuracy and courage over the faction preset.</summary>
	public float Toughness(NemesisTuning t) => 1f + t.ToughnessPerRank * Rank;

	public string DisplayName => $"{Name} \"{Nickname}\"";
}

/// <summary>What registering a kill did.</summary>
public sealed record NemesisKillResult(Nemesis Nemesis, bool IsNew, Nemesis? Retired, int GearWithoutRoom);

/// <summary>A nemesis that died, for the dossier until the next hub visit.</summary>
public sealed record FallenNemesis(string Name, string Nickname, string KilledBy, int Raid);

/// <summary>
/// The nemeses of the save (M8c spec s1 and s3): creates one from a kill or grows an existing one, caps them per map,
/// rolls which one is in a raid, and removes the dead. Pure logic.
/// </summary>
public sealed class NemesisRoster
{
	private readonly List<Nemesis> _all = new();
	private readonly List<FallenNemesis> _fallen = new();
	private int _nextId = 1;

	public NemesisRoster(NemesisTuning? tuning = null) => Tuning = tuning ?? new NemesisTuning();

	public NemesisTuning Tuning { get; }

	/// <summary>Oldest first.</summary>
	public IReadOnlyList<Nemesis> All => _all;

	public IReadOnlyList<FallenNemesis> Fallen => _fallen;

	public IEnumerable<Nemesis> ForMap(string mapId) => _all.Where(n => n.MapId == mapId);

	public Nemesis? Find(string id) => _all.FirstOrDefault(n => n.Id == id);

	/// <summary>Adds a nemesis loaded from a save, keeping its id and making later ids larger.</summary>
	public void Add(Nemesis nemesis)
	{
		_all.Add(nemesis);
		if (nemesis.Id.StartsWith('n') && int.TryParse(nemesis.Id.AsSpan(1), out var n))
			_nextId = Math.Max(_nextId, n + 1);
	}

	public void Clear()
	{
		_all.Clear();
		_fallen.Clear();
		_nextId = 1;
	}

	/// <summary>
	/// An AI killed the player. <paramref name="existingId"/> is the killer's nemesis id when it already is one: it grows
	/// (rank up to the cap, one more kill, better armor put on) and carries the new gear too. Otherwise a new nemesis is
	/// made, and a fourth on the map retires the oldest, whose gear is gone.
	/// </summary>
	public NemesisKillResult RegisterKill(string? existingId, Faction killer, bool friendlyOrigin, NemesisKill kill,
		IEnumerable<LostItem> lost, NemesisNames names, Func<ItemSpec, int> armorTier, Random rng)
	{
		var items = lost.ToList();
		if (existingId is not null && Find(existingId) is { } known)
		{
			var noRoom = NemesisGear.Equip(known.Gear, items, armorTier);
			known.Rank = Math.Min(Tuning.MaxRank, known.Rank + 1);
			known.Kills.Add(kill);
			known.X = kill.X;
			known.Z = kill.Z;
			return new NemesisKillResult(known, false, null, noRoom);
		}

		var (name, nickname) = names.Generate(killer, rng);
		var created = new Nemesis($"n{_nextId++}", name, nickname, killer, kill.MapId, kill.X, kill.Z, kill.Raid, friendlyOrigin,
			new CharacterInventory());
		var lacking = NemesisGear.Equip(created.Gear, items, armorTier);
		created.Kills.Add(kill);
		Nemesis? retired = null;
		if (ForMap(kill.MapId).Count() >= Tuning.CapPerMap)
		{
			retired = ForMap(kill.MapId).First();
			_all.Remove(retired);
		}
		_all.Add(created);
		return new NemesisKillResult(created, true, retired, lacking);
	}

	/// <summary>
	/// At raid start each nemesis on the map rolls its spawn chance; at most one of those that succeed is in the raid
	/// (picked at random). Null when none rolled in.
	/// </summary>
	public Nemesis? RollSpawn(string mapId, Random rng)
	{
		var rolled = ForMap(mapId).Where(_ => rng.NextDouble() < Tuning.SpawnChance).ToList();
		return rolled.Count == 0 ? null : rolled[rng.Next(rolled.Count)];
	}

	/// <summary>The nemesis died (to the player, or to another AI): it leaves the roster. The killer is shown in the dossier until the next hub visit.</summary>
	public bool Remove(string id, string killedBy, int raid)
	{
		if (Find(id) is not { } nemesis)
			return false;
		_all.Remove(nemesis);
		_fallen.Add(new FallenNemesis(nemesis.Name, nemesis.Nickname, killedBy, raid));
		return true;
	}

	/// <summary>The hub was visited: the news of dead nemeses has been seen.</summary>
	public void AcknowledgeFallen() => _fallen.Clear();
}
