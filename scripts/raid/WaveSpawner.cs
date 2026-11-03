using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Timed waves of a map (Border Strip spec s6): reads the map's <see cref="WaveSpawnPoint"/>s, runs a
/// <see cref="WaveDirector"/> off the raid clock and spawns each due wave on the navmesh, patrolling. Thin node; the
/// timing, alternation and route rules are in <see cref="WaveDirector"/> and <see cref="WavePlanner"/>. A map with no
/// wave markers does nothing.
/// </summary>
public partial class WaveSpawner : Node
{
	private sealed class Pending
	{
		public WaveEntry Entry = null!;
		public WaveSpawnPoint Point = null!;
		public float WaitedS;
	}

	private WaveDirector? _director;
	private WaveTuning _tuning = new();
	private RaidManager _raid = null!;
	private Player _player = null!;
	private readonly List<Pending> _pending = new();
	private int _spawned;

	/// <summary>Dev: seconds added to the raid clock for the waves only (--waveclock=), to reach minute 8 quickly.</summary>
	public float ClockOffsetS { get; set; }
	public bool ShowDebug { get; set; }

	/// <summary>Raised a lead time before a wave arrives: the truck engines, radio chatter or shouting cue.</summary>
	public event System.Action<WaveEntry>? WaveTelegraphed;

	/// <summary>Raised when a wave has been placed on the map.</summary>
	public event System.Action<WaveEntry, int>? WaveArrived;

	public void Start(RaidManager raid, Player player, WaveTuning tuning)
	{
		_raid = raid;
		_player = player;
		_tuning = tuning;
		var points = GetTree().GetNodesInGroup("wave_spawns").OfType<WaveSpawnPoint>().ToList();
		var entries = points.SelectMany(p => WaveDirector.ParseMinutes(p.AtMinutes)
			.Select(s => new WaveEntry(p.Name, p.Wave, s, p.PresetId, p.Count, p.Side))).ToList();
		if (entries.Count == 0)
			return;
		_director = new WaveDirector(entries, tuning);
		_director.Telegraphed += e =>
		{
			GD.Print($"WAVE telegraph {e.Kind} {e.PointId} at {e.AtS / 60f:0.#} min");
			WaveTelegraphed?.Invoke(e);
		};
		_director.Arrived += e => _pending.Add(new Pending { Entry = e, Point = points.First(p => p.Name == e.PointId) });
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_director is null || !_raid.IsRunning)
			return;
		_director.Update(_raid.Session.ElapsedS + ClockOffsetS);
		for (var i = _pending.Count - 1; i >= 0; i--)
		{
			var p = _pending[i];
			p.WaitedS += (float)delta;
			var distance = p.Point.GlobalPosition.DistanceTo(_player.GlobalPosition);
			if (!WavePlanner.MayAppear(_tuning, distance, p.WaitedS))
				continue;
			_pending.RemoveAt(i);
			Place(p.Entry, p.Point);
		}
	}

	private void Place(WaveEntry entry, WaveSpawnPoint point)
	{
		var map = _player.GetWorld3D().NavigationMap;
		var route = RouteFor(entry, point, map);
		var count = 0;
		foreach (var offset in WavePlanner.Offsets(entry.Count))
		{
			var at = AiSpawner.Snap(map, point.GlobalPosition + offset);
			var ai = AiSpawner.Spawn(GetParent(), entry.Preset, $"{entry.Preset}_wave{_spawned + 1}_{++count}", at, route.ToArray(), ShowDebug);
			if (ai is null)
				break;
		}
		_spawned++;
		GD.Print($"WAVE arrived {entry.Kind} {entry.PointId} x{count} at {entry.AtS / 60f:0.#} min, route points {route.Count}");
		WaveArrived?.Invoke(entry, count);
	}

	/// <summary>
	/// Patrol waves walk the posts of their side (the map's AI spawn points of the same preset); bandit waves go for
	/// the mid and high loot nearest them. With nothing to walk to, a wave loops around its marker.
	/// </summary>
	private IReadOnlyList<Vector3> RouteFor(WaveEntry entry, WaveSpawnPoint point, Rid map)
	{
		var candidates = entry.Kind == "bandit"
			? GetTree().GetNodesInGroup("loot_spots").OfType<LootSpot>().Where(l => l.Tier >= LootTier.Mid).Select(l => l.GlobalPosition)
			: GetTree().GetNodesInGroup("ai_spawns").OfType<AiSpawnPoint>().Where(a => a.PresetId == entry.Preset).Select(a => a.GlobalPosition);
		var chain = WavePlanner.NearestChain(point.GlobalPosition, candidates, entry.Kind == "bandit" ? 6 : 8);
		if (chain.Count < 2)
			chain = WavePlanner.Loop(point.GlobalPosition, 15f);
		return chain.Select(p => AiSpawner.Snap(map, p)).ToList();
	}
}
