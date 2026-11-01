# Map 1 Reinforcement Wave and Map Validator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a timed AI reinforcement wave that patrols the extract approaches, plus a marker validator that checks map 1 against the spec's spawn, extract and loot rules.

**Architecture:** Timing, marker choice, extract assignment and patrol-ring math are plain C# classes in `scripts/systems` with xUnit tests. A thin `WaveSpawner` node reads `RaidManager` time, plays a placeholder siren and instantiates `AiHuman` with patrol points (the existing brain already patrols; no new AI state). The validator is pure logic with injected path-length and line-of-sight functions; a runner node supplies navmesh and physics, run via a `--validate` dev arg.

**Tech Stack:** Godot 4.7.2 mono, C# .NET 8, xUnit.

**Spec:** `docs/superpowers/specs/maps/map1-requirements-design.md` (sections 3, 4, 5, 6, 7).

## Global Constraints

- Units: meters, seconds. Tuning numbers live in `RaidConfig` (`data/raid/map1.tres`), never hard-coded in nodes.
- Logic is plain C# (Godot math types allowed), no `GodotObject`; nodes are thin wrappers.
- Events past tense. Private fields `_camelCase`. File named after its class. Nullable enabled.
- `dotnet build "Project D.csproj"` must produce no warnings. `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj` must pass.
- No HUD element for the wave (telegraph is audio only). No ammo counter, no crosshair.
- Do not change `project.godot` input map or autoloads. No new addons.
- Wave defaults from spec: T-8 min (480 s remaining of 1200 s), 4-6 AI (default 5), telegraph ~20 s before, spawn >= 60 m from player.
- Validator defaults from spec: spawn-to-spawn >= 90 m, spawn-to-extract path >= 60 m, each spawn reaches >= 2 extracts, no line of sight spawn-to-spawn or spawn-to-high-tier-loot.

## Review Focus

- First `WaveDirector.Update` already past both thresholds (e.g. dev `--raidtime=40`): fires Telegraphed then Arrived once each, in order, in one call.
- `WaveDirector.Update` called again after Arrived or with a larger remaining value: no repeat events.
- Zero wave markers on the map: no crash, a single warning line, no wave.
- Every wave marker closer than `MinDistance` to the player: wave uses the farthest markers rather than spawning nothing.
- More AI requested than markers: markers cycle, AI do not stack on identical coordinates (small deterministic offset).
- `RaidLevel` `--raidtime=` override must not drop the new wave fields (it currently rebuilds `RaidConfig` by hand).
- Empty or unknown preset id in `WavePresetIds`: skipped with a warning, rest of the wave still spawns.
- Unreachable extract (path function returns null): validator reports it, does not throw.

## File Structure

| File | Responsibility |
|---|---|
| `scripts/systems/WaveDirector.cs` (create) | Phase machine: Waiting -> Telegraphed -> Arrived from remaining time. |
| `scripts/systems/WavePlanner.cs` (create) | Pick markers, assign presets and extract zones; patrol ring points. |
| `scripts/systems/MapValidator.cs` (create) | Pure rule checks over marker data. |
| `scripts/raid/WaveSpawner.cs` (create) | Node: drives director, plays siren, spawns `AiHuman`. |
| `scripts/raid/MapValidationRunner.cs` (create) | Collects markers from groups, supplies navmesh/LOS, prints report. |
| `scripts/raid/RaidConfig.cs` (modify) | Wave fields. |
| `scripts/raid/AiSpawnPoint.cs` (modify) | `IsWave` flag and `wave_spawns` group. |
| `scripts/raid/LootSpot.cs` (modify) | Join `loot_spots` group. |
| `scripts/raid/RaidLevel.cs` (modify) | Add `WaveSpawner`, `--wavetime=`, `--validate`, duplicate config on override. |
| `data/raid/map1.tres` (modify) | Wave tuning values. |
| `scenes/maps/map1.tscn` (modify) | `WaveSpawns` marker group. |
| `tests/ProjectD.Tests/WaveDirectorTests.cs`, `WavePlannerTests.cs`, `MapValidatorTests.cs` (create) | xUnit. |

Out of this plan (deliberately): the knowledge-gated depot hatch extract (needs the knowledge system, M8); the trench extract keeps its placeholder `bandage` requirement until a bolt-cutters item exists. Travel-time targets (90-150 s safe route) and "high-tier spot has >= 2 attack angles" are playtest checks, not automated.

---

### Task 1: WaveDirector

**Files:**
- Create: `scripts/systems/WaveDirector.cs`
- Test: `tests/ProjectD.Tests/WaveDirectorTests.cs`

**Interfaces:**
- Produces: `enum WavePhase { Waiting, Telegraphed, Arrived }`; `sealed record WaveTiming(float TriggerRemainingS, float TelegraphLeadS)`; `sealed class WaveDirector(WaveTiming timing)` with `WavePhase Phase`, `void Update(float remainingS)`, `event Action? Telegraphed`, `event Action? Arrived`. Telegraph fires when `remainingS <= TriggerRemainingS + TelegraphLeadS`; arrival when `remainingS <= TriggerRemainingS`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Xunit;

namespace ProjectD.Tests;

public class WaveDirectorTests
{
	private static (WaveDirector d, List<string> log) Make()
	{
		var d = new WaveDirector(new WaveTiming(480f, 20f));
		var log = new List<string>();
		d.Telegraphed += () => log.Add("tele");
		d.Arrived += () => log.Add("arrive");
		return (d, log);
	}

	[Fact]
	public void FiresTelegraphThenArrivalAsTimeRunsDown()
	{
		var (d, log) = Make();
		d.Update(600f);
		Assert.Equal(WavePhase.Waiting, d.Phase);
		d.Update(500f);
		Assert.Equal(WavePhase.Telegraphed, d.Phase);
		d.Update(481f);
		Assert.Equal(new[] { "tele" }, log);
		d.Update(479f);
		Assert.Equal(WavePhase.Arrived, d.Phase);
		Assert.Equal(new[] { "tele", "arrive" }, log);
	}

	[Fact]
	public void FirstUpdatePastBothThresholdsFiresBothInOrderOnce()
	{
		var (d, log) = Make();
		d.Update(40f);
		d.Update(39f);
		d.Update(500f);
		Assert.Equal(new[] { "tele", "arrive" }, log);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter WaveDirectorTests`
Expected: FAIL (compile error, `WaveDirector` not defined).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;

namespace ProjectD;

public enum WavePhase { Waiting, Telegraphed, Arrived }

/// <summary>When the wave warns and when it arrives, as raid time remaining, seconds.</summary>
public sealed record WaveTiming(float TriggerRemainingS, float TelegraphLeadS);

/// <summary>
/// Reinforcement wave clock (spec s6). Pure logic: feed it the raid's remaining time each tick;
/// it raises <see cref="Telegraphed"/> then <see cref="Arrived"/> once each, in order.
/// </summary>
public sealed class WaveDirector
{
	private readonly WaveTiming _t;

	public WaveDirector(WaveTiming timing) => _t = timing;

	public WavePhase Phase { get; private set; } = WavePhase.Waiting;

	public event Action? Telegraphed;
	public event Action? Arrived;

	public void Update(float remainingS)
	{
		if (Phase == WavePhase.Waiting && remainingS <= _t.TriggerRemainingS + _t.TelegraphLeadS)
		{
			Phase = WavePhase.Telegraphed;
			Telegraphed?.Invoke();
		}
		if (Phase == WavePhase.Telegraphed && remainingS <= _t.TriggerRemainingS)
		{
			Phase = WavePhase.Arrived;
			Arrived?.Invoke();
		}
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter WaveDirectorTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add scripts/systems/WaveDirector.cs tests/ProjectD.Tests/WaveDirectorTests.cs
git commit -m "feat(wave): WaveDirector clock for the reinforcement wave"
```

---

### Task 2: WavePlanner (marker choice, extract assignment, patrol ring)

**Files:**
- Create: `scripts/systems/WavePlanner.cs`
- Test: `tests/ProjectD.Tests/WavePlannerTests.cs`

**Interfaces:**
- Produces:
  - `sealed record WaveSpawn(Godot.Vector3 Position, string PresetId, string ExtractId)`
  - `sealed record ExtractPoint(string Id, Godot.Vector3 Position)`
  - `static class WavePlanner`:
    - `List<WaveSpawn> Plan(IReadOnlyList<Vector3> markers, Vector3 player, IReadOnlyList<ExtractPoint> extracts, IReadOnlyList<string> presetIds, int count, float minDistanceM)`
    - `Vector3[] PatrolRing(Vector3 centre, float radiusM, int points, float phaseRad = 0f)`
  - Rules: eligible = markers with horizontal distance >= `minDistanceM`; if none, use the markers sorted by distance descending (farthest first). Take `count` spawns by cycling through eligible markers in order of distance descending; the Nth use of the same marker is offset by `1.5 m * N` along +X. Preset = `presetIds[i % presetIds.Count]`, extract = `extracts[i % extracts.Count].Id`. Empty markers, extracts or presets -> empty list.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class WavePlannerTests
{
	private static readonly ExtractPoint[] Zones =
	{
		new("gate", new Vector3(0, 0, 76)), new("breach", new Vector3(-76, 0, 16)), new("ledge", new Vector3(66, 0, -76)),
	};

	[Fact]
	public void SkipsMarkersNearPlayerAndAssignsPresetsAndZonesRoundRobin()
	{
		var markers = new[] { new Vector3(10, 0, 0), new Vector3(80, 0, 0), new Vector3(-80, 0, 0) };
		var plan = WavePlanner.Plan(markers, Vector3.Zero, Zones, new[] { "bandit", "looter" }, 4, 60f);
		Assert.Equal(4, plan.Count);
		Assert.All(plan, s => Assert.True(new Vector3(s.Position.X, 0, s.Position.Z).DistanceTo(Vector3.Zero) >= 60f));
		Assert.Equal(new[] { "bandit", "looter", "bandit", "looter" }, plan.Select(s => s.PresetId));
		Assert.Equal(new[] { "gate", "breach", "ledge", "gate" }, plan.Select(s => s.ExtractId));
	}

	[Fact]
	public void FallsBackToFarthestMarkersWhenAllAreTooClose()
	{
		var markers = new[] { new Vector3(5, 0, 0), new Vector3(30, 0, 0) };
		var plan = WavePlanner.Plan(markers, Vector3.Zero, Zones, new[] { "bandit" }, 1, 60f);
		Assert.Single(plan);
		Assert.Equal(30f, plan[0].Position.X);
	}

	[Fact]
	public void ReusedMarkersAreOffsetSoAiDoNotStack()
	{
		var plan = WavePlanner.Plan(new[] { new Vector3(80, 0, 0) }, Vector3.Zero, Zones, new[] { "bandit" }, 3, 60f);
		Assert.Equal(3, plan.Select(s => s.Position).Distinct().Count());
	}

	[Fact]
	public void EmptyInputsGiveEmptyPlan()
	{
		Assert.Empty(WavePlanner.Plan(System.Array.Empty<Vector3>(), Vector3.Zero, Zones, new[] { "bandit" }, 3, 60f));
		Assert.Empty(WavePlanner.Plan(new[] { new Vector3(80, 0, 0) }, Vector3.Zero, System.Array.Empty<ExtractPoint>(), new[] { "bandit" }, 3, 60f));
		Assert.Empty(WavePlanner.Plan(new[] { new Vector3(80, 0, 0) }, Vector3.Zero, Zones, System.Array.Empty<string>(), 3, 60f));
	}

	[Fact]
	public void PatrolRingIsEvenlySpacedAtRadius()
	{
		var ring = WavePlanner.PatrolRing(new Vector3(10, 0, 20), 15f, 4);
		Assert.Equal(4, ring.Length);
		Assert.All(ring, p => Assert.Equal(15f, p.DistanceTo(new Vector3(10, 0, 20)), 3));
		Assert.Equal(15f * Mathf.Sqrt(2f), ring[0].DistanceTo(ring[1]), 3);
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter WavePlannerTests`
Expected: FAIL (compile error, `WavePlanner` not defined).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

public sealed record WaveSpawn(Vector3 Position, string PresetId, string ExtractId);

public sealed record ExtractPoint(string Id, Vector3 Position);

/// <summary>Chooses where the reinforcement wave appears and which extract each AI watches (spec s6).</summary>
public static class WavePlanner
{
	/// <summary>Spacing applied to the second and later AI spawned from the same marker, metres.</summary>
	private const float StackOffsetM = 1.5f;

	public static List<WaveSpawn> Plan(IReadOnlyList<Vector3> markers, Vector3 player,
		IReadOnlyList<ExtractPoint> extracts, IReadOnlyList<string> presetIds, int count, float minDistanceM)
	{
		var result = new List<WaveSpawn>();
		if (markers.Count == 0 || extracts.Count == 0 || presetIds.Count == 0 || count <= 0)
			return result;

		float Flat(Vector3 m) => new Vector3(m.X - player.X, 0f, m.Z - player.Z).Length();
		var far = markers.Where(m => Flat(m) >= minDistanceM).OrderByDescending(Flat).ToList();
		var pool = far.Count > 0 ? far : markers.OrderByDescending(Flat).ToList();

		for (var i = 0; i < count; i++)
		{
			var lap = i / pool.Count;
			var pos = pool[i % pool.Count] + new Vector3(StackOffsetM * lap, 0f, 0f);
			result.Add(new WaveSpawn(pos, presetIds[i % presetIds.Count], extracts[i % extracts.Count].Id));
		}
		return result;
	}

	/// <summary>Evenly spaced points on a horizontal circle; the patrol loop around an extract.</summary>
	public static Vector3[] PatrolRing(Vector3 centre, float radiusM, int points, float phaseRad = 0f)
	{
		var ring = new Vector3[points];
		for (var i = 0; i < points; i++)
		{
			var a = phaseRad + Mathf.Tau * i / points;
			ring[i] = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radiusM;
		}
		return ring;
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter WavePlannerTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add scripts/systems/WavePlanner.cs tests/ProjectD.Tests/WavePlannerTests.cs
git commit -m "feat(wave): WavePlanner picks markers, presets and extract assignments"
```

---

### Task 3: Config, markers and scene data

**Files:**
- Modify: `scripts/raid/RaidConfig.cs`, `scripts/raid/AiSpawnPoint.cs`, `scripts/raid/LootSpot.cs`, `data/raid/map1.tres`, `scenes/maps/map1.tscn`

**Interfaces:**
- Produces: `RaidConfig.WaveRemainingS` (float, 480), `WaveTelegraphLeadS` (float, 20), `WaveCount` (int, 5), `WaveMinDistanceFromPlayerM` (float, 60), `WaveRingRadiusM` (float, 14), `WavePresetIds` (`string[]`, `{"bandit"}`). `AiSpawnPoint.IsWave` (bool); wave markers join group `wave_spawns` and not `ai_spawns`. `LootSpot` joins group `loot_spots`.

- [ ] **Step 1: Edit `RaidConfig.cs`**, append inside the class:

```csharp
	/// <summary>Raid time remaining when the reinforcement wave arrives, seconds (spec s6: T-8 min).</summary>
	[Export] public float WaveRemainingS { get; set; } = 480f;
	/// <summary>How long before arrival the siren plays, seconds.</summary>
	[Export] public float WaveTelegraphLeadS { get; set; } = 20f;
	/// <summary>AI in the wave.</summary>
	[Export] public int WaveCount { get; set; } = 5;
	/// <summary>Wave markers closer than this to the player are skipped, metres.</summary>
	[Export] public float WaveMinDistanceFromPlayerM { get; set; } = 60f;
	/// <summary>Radius of the patrol loop around each extract, metres.</summary>
	[Export] public float WaveRingRadiusM { get; set; } = 14f;
	/// <summary>Preset ids from data/ai, used round-robin.</summary>
	[Export] public string[] WavePresetIds { get; set; } = { "bandit" };
```

- [ ] **Step 2: Edit `AiSpawnPoint.cs`**: add the property and replace `_Ready` body.

```csharp
	/// <summary>Reinforcement-wave marker (spec s6): not used at raid start, only when the wave arrives.</summary>
	[Export] public bool IsWave { get; set; }

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup(IsWave ? "wave_spawns" : "ai_spawns");
	}
```

- [ ] **Step 3: Edit `LootSpot.cs`**: in `_Ready`, directly after the `if (Engine.IsEditorHint()) return;` block add `AddToGroup("loot_spots");`.

- [ ] **Step 4: Edit `data/raid/map1.tres`**: append after `AiMinDistanceFromPlayer = 30.0`:

```
WaveRemainingS = 480.0
WaveTelegraphLeadS = 20.0
WaveCount = 5
WaveMinDistanceFromPlayerM = 60.0
WaveRingRadiusM = 14.0
WavePresetIds = PackedStringArray("bandit")
```

- [ ] **Step 5: Edit `scenes/maps/map1.tscn`**: after the `[node name="AiSpawns" ...]` header line (line ~1057) add a sibling node line `[node name="WaveSpawns" type="Node3D" parent="."]` followed by a blank line, and at the end of the file append four markers (script id `3` is `AiSpawnPoint.cs`, already declared):

```
[node name="Wave1" type="Marker3D" parent="WaveSpawns"]
transform = Transform3D(1.0000, 0.0000, 0.0000, 0.0000, 1.0000, 0.0000, -0.0000, 0.0000, 1.0000, 40.0000, 0.1000, 86.0000)
script = ExtResource("3")
IsWave = true
PresetId = "bandit"

[node name="Wave2" type="Marker3D" parent="WaveSpawns"]
transform = Transform3D(1.0000, 0.0000, 0.0000, 0.0000, 1.0000, 0.0000, -0.0000, 0.0000, 1.0000, 86.0000, 0.1000, -40.0000)
script = ExtResource("3")
IsWave = true
PresetId = "bandit"

[node name="Wave3" type="Marker3D" parent="WaveSpawns"]
transform = Transform3D(1.0000, 0.0000, 0.0000, 0.0000, 1.0000, 0.0000, -0.0000, 0.0000, 1.0000, 0.0000, 0.1000, -86.0000)
script = ExtResource("3")
IsWave = true
PresetId = "bandit"

[node name="Wave4" type="Marker3D" parent="WaveSpawns"]
transform = Transform3D(1.0000, 0.0000, 0.0000, 0.0000, 1.0000, 0.0000, -0.0000, 0.0000, 1.0000, -86.0000, 0.1000, 30.0000)
script = ExtResource("3")
IsWave = true
PresetId = "bandit"
```

- [ ] **Step 6: Build and run existing tests**

Run: `dotnet build "Project D.csproj"` then `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj`
Expected: no warnings, all tests pass. If the scene fails to load because of a node-ordering error, open it once with `godot --headless --path . --quit-after 2 res://scenes/maps/map1.tscn` and fix the reported line.

- [ ] **Step 7: Commit**

```bash
git add scripts/raid/RaidConfig.cs scripts/raid/AiSpawnPoint.cs scripts/raid/LootSpot.cs data/raid/map1.tres scenes/maps/map1.tscn
git commit -m "feat(wave): wave config fields, IsWave markers, loot_spots group, map1 wave markers"
```

---

### Task 4: WaveSpawner node and RaidLevel wiring

**Files:**
- Create: `scripts/raid/WaveSpawner.cs`
- Modify: `scripts/raid/RaidLevel.cs:33-48` (config override and spawner hookup)

**Interfaces:**
- Consumes: `WaveDirector`, `WaveTiming`, `WavePlanner`, `ExtractPoint`, `RaidConfig` wave fields, `RaidManager.Session.RemainingS`, `RaidManager.IsRunning`, group names `wave_spawns` and `extraction_zones`, `AiHuman.Preset`, `AiHuman.PatrolPoints`.
- Produces: `WaveSpawner` node: `[Export] RaidConfig? Config`, `RaidManager? Raid`, `bool ShowAiDebug`; prints `WAVE telegraphed` and `WAVE arrived n=<count>`.

- [ ] **Step 1: Write `scripts/raid/WaveSpawner.cs`**

```csharp
using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Spawns the reinforcement wave (spec s6): a siren ahead of time, then AI from the map's wave
/// markers that patrol the extract approaches. Timing and choices come from <see cref="WaveDirector"/>
/// and <see cref="WavePlanner"/>; this node only touches the scene.
/// </summary>
public partial class WaveSpawner : Node
{
	public RaidConfig? Config { get; set; }
	public RaidManager? Raid { get; set; }
	public Player? Player { get; set; }
	public bool ShowAiDebug { get; set; }

	private WaveDirector? _director;
	private AudioStreamPlayer? _siren;

	public override void _Ready()
	{
		var cfg = Config ?? new RaidConfig();
		_director = new WaveDirector(new WaveTiming(cfg.WaveRemainingS, cfg.WaveTelegraphLeadS));
		_director.Telegraphed += OnTelegraphed;
		_director.Arrived += OnArrived;
		_siren = new AudioStreamPlayer { Stream = BuildSiren(), VolumeDb = -6f };
		AddChild(_siren);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Raid is { IsRunning: true } raid)
			_director?.Update(raid.Session.RemainingS);
	}

	private void OnTelegraphed()
	{
		GD.Print("WAVE telegraphed");
		_siren?.Play();
	}

	private void OnArrived()
	{
		var cfg = Config ?? new RaidConfig();
		var tree = GetTree();
		var markers = tree.GetNodesInGroup("wave_spawns").OfType<Node3D>().Select(m => m.GlobalPosition).ToList();
		if (markers.Count == 0)
		{
			GD.PushWarning("WaveSpawner: map has no wave_spawns markers; no wave this raid");
			return;
		}
		var extracts = tree.GetNodesInGroup("extraction_zones").OfType<ExtractionZone>()
			.Select(z => new ExtractPoint(z.ZoneId, z.GlobalPosition)).ToList();
		var playerPos = Player?.GlobalPosition ?? Vector3.Zero;
		var plan = WavePlanner.Plan(markers, playerPos, extracts, cfg.WavePresetIds, cfg.WaveCount,
			cfg.WaveMinDistanceFromPlayerM);

		var map = GetViewport().GetWorld3D().NavigationMap;
		var parent = GetParent<Node3D>();
		var n = 0;
		foreach (var s in plan)
		{
			if (GD.Load<AiPreset>($"res://data/ai/{s.PresetId}.tres") is not { } preset)
			{
				GD.PushWarning($"WaveSpawner: unknown AI preset '{s.PresetId}', skipped");
				continue;
			}
			var zone = extracts.First(e => e.Id == s.ExtractId);
			var ring = WavePlanner.PatrolRing(zone.Position, cfg.WaveRingRadiusM, 4, GD.Randf() * Mathf.Tau)
				.Select(p => NavigationServer3D.MapGetClosestPoint(map, p)).ToArray();
			var ai = new AiHuman
			{
				Name = $"wave_{s.PresetId}_{++n}",
				Preset = preset,
				PatrolPoints = ring,
				Position = NavigationServer3D.MapGetClosestPoint(map, s.Position) + Vector3.Up * 0.1f,
				Rotation = new Vector3(0f, GD.Randf() * Mathf.Tau, 0f),
			};
			parent.AddChild(ai);
			ai.ShowDebug = ShowAiDebug;
		}
		GD.Print($"WAVE arrived n={n}");
	}

	/// <summary>Placeholder two-tone siren, 3 s, built in code so no audio asset is needed.</summary>
	private static AudioStreamWav BuildSiren()
	{
		const int rate = 22050;
		var data = new byte[rate * 3 * 2];
		for (var i = 0; i < rate * 3; i++)
		{
			var hz = (i / (rate / 2)) % 2 == 0 ? 520f : 760f;
			var v = (short)(Math.Sin(Mathf.Tau * hz * i / rate) * 9000);
			data[i * 2] = (byte)(v & 0xFF);
			data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
		}
		return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate };
	}
}
```

- [ ] **Step 2: Edit `RaidLevel.cs` config override** (replaces the hand-copy at lines 34-37 so new fields survive):

```csharp
			// Dev: --raidtime=secs overrides the raid length (testing the MIA path);
			// --wavetime=secs overrides the remaining time at which the wave arrives.
			var args = OS.GetCmdlineUserArgs();
			var cfg = Config is null ? new RaidConfig() : (RaidConfig)Config.Duplicate();
			var dev = args.FirstOrDefault(a => a.StartsWith("--raidtime="));
			if (dev is not null && float.TryParse(dev["--raidtime=".Length..], out var seconds))
				cfg.DurationS = seconds;
			var waveArg = args.FirstOrDefault(a => a.StartsWith("--wavetime="));
			if (waveArg is not null && float.TryParse(waveArg["--wavetime=".Length..], out var waveAt))
				cfg.WaveRemainingS = waveAt;
```

- [ ] **Step 3: Edit `RaidLevel.cs` spawner hookup**: directly after `Raid.Start(Player);` add:

```csharp
			AddChild(new WaveSpawner { Name = "Wave", Config = cfg, Raid = Raid, Player = Player, ShowAiDebug = ShowAiDebug });
```

- [ ] **Step 4: Build**

Run: `dotnet build "Project D.csproj"`
Expected: no warnings, no errors.

- [ ] **Step 5: Headless smoke run**

Run: `D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/maps/map1.tscn -- --raidtime=15 --wavetime=10 --autonew`
Expected: each of the 3 raids prints `WAVE telegraphed` at once (15 <= 10+20) and `WAVE arrived n=5` about 5 s later, before the MIA result; the process then quits after the third raid.

- [ ] **Step 6: Commit**

```bash
git add scripts/raid/WaveSpawner.cs scripts/raid/RaidLevel.cs
git commit -m "feat(wave): WaveSpawner node, siren telegraph, --wavetime dev arg"
```

---

### Task 5: MapValidator (pure rules)

**Files:**
- Create: `scripts/systems/MapValidator.cs`
- Test: `tests/ProjectD.Tests/MapValidatorTests.cs`

**Interfaces:**
- Produces:
  - `sealed record ValidatorRules(float MinSpawnSpacingM = 90f, float MinSpawnToExtractPathM = 60f, int MinExtractsPerSpawn = 2, int MinWaveMarkers = 2, int MinLow = 6, int MinMid = 4, int MinHigh = 2)`
  - `sealed record MapMarkers(IReadOnlyList<Vector3> PlayerSpawns, IReadOnlyList<ExtractPoint> Extracts, IReadOnlyList<(Vector3 Position, LootTier Tier)> Loot, IReadOnlyList<Vector3> WaveSpawns)`
  - `sealed record MapIssue(string Rule, string Message)`
  - `static class MapValidator`: `List<MapIssue> Validate(MapMarkers map, ValidatorRules rules, Func<Vector3, Vector3, float?> pathLengthM, Func<Vector3, Vector3, bool> hasLineOfSight)`
- Consumes: `ExtractPoint`, `LootTier` (already exists in `LootSpot.cs`).
- Rule names: `spawn-spacing`, `spawn-los`, `spawn-high-loot-los`, `extract-reach`, `extract-count`, `loot-tiers`, `wave-markers`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Godot;
using Xunit;

namespace ProjectD.Tests;

public class MapValidatorTests
{
	private static readonly ValidatorRules Rules = new();

	private static MapMarkers Good() => new(
		new[] { new Vector3(-80, 0, 0), new Vector3(80, 0, 0) },
		new[] { new ExtractPoint("a", new Vector3(0, 0, 80)), new ExtractPoint("b", new Vector3(0, 0, -80)) },
		Enumerable.Range(0, 6).Select(_ => (new Vector3(0, 0, 0), LootTier.Low))
			.Concat(Enumerable.Range(0, 4).Select(_ => (new Vector3(0, 0, 0), LootTier.Mid)))
			.Concat(Enumerable.Range(0, 2).Select(_ => (new Vector3(0, 0, 0), LootTier.High))).ToList(),
		new[] { new Vector3(0, 0, 85), new Vector3(0, 0, -85) });

	private static float? Straight(Vector3 a, Vector3 b) => a.DistanceTo(b);
	private static bool NoLos(Vector3 a, Vector3 b) => false;

	[Fact]
	public void GoodMapHasNoIssues()
		=> Assert.Empty(MapValidator.Validate(Good(), Rules, Straight, NoLos));

	[Fact]
	public void FlagsSpawnsCloserThanSpacing()
	{
		var m = Good() with { PlayerSpawns = new[] { new Vector3(0, 0, 0), new Vector3(50, 0, 0) } };
		Assert.Contains(MapValidator.Validate(m, Rules, Straight, NoLos), i => i.Rule == "spawn-spacing");
	}

	[Fact]
	public void FlagsSpawnWithLineOfSightToAnotherSpawnOrHighLoot()
	{
		var issues = MapValidator.Validate(Good(), Rules, Straight, (a, b) => true);
		Assert.Contains(issues, i => i.Rule == "spawn-los");
		Assert.Contains(issues, i => i.Rule == "spawn-high-loot-los");
	}

	[Fact]
	public void FlagsSpawnThatReachesTooFewExtractsOrOnlyTooCloseOnes()
	{
		float? OnlyFarOneReachable(Vector3 from, Vector3 to) => to.Z > 0 ? from.DistanceTo(to) : null;
		var issues = MapValidator.Validate(Good(), Rules, OnlyFarOneReachable, NoLos);
		Assert.Contains(issues, i => i.Rule == "extract-count");
		float? Short(Vector3 from, Vector3 to) => 10f;
		Assert.Contains(MapValidator.Validate(Good(), Rules, Short, NoLos), i => i.Rule == "extract-count");
	}

	[Fact]
	public void UnreachableExtractIsReportedNotThrown()
	{
		float? None(Vector3 from, Vector3 to) => null;
		Assert.Contains(MapValidator.Validate(Good(), Rules, None, NoLos), i => i.Rule == "extract-reach");
	}

	[Fact]
	public void FlagsMissingLootTiersAndWaveMarkers()
	{
		var m = Good() with { Loot = new[] { (Vector3.Zero, LootTier.Low) }, WaveSpawns = new[] { Vector3.Zero } };
		var issues = MapValidator.Validate(m, Rules, Straight, NoLos);
		Assert.Contains(issues, i => i.Rule == "loot-tiers");
		Assert.Contains(issues, i => i.Rule == "wave-markers");
	}
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter MapValidatorTests`
Expected: FAIL (compile error, `MapValidator` not defined).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

public sealed record ValidatorRules(
	float MinSpawnSpacingM = 90f,
	float MinSpawnToExtractPathM = 60f,
	int MinExtractsPerSpawn = 2,
	int MinWaveMarkers = 2,
	int MinLow = 6,
	int MinMid = 4,
	int MinHigh = 2);

public sealed record MapMarkers(
	IReadOnlyList<Vector3> PlayerSpawns,
	IReadOnlyList<ExtractPoint> Extracts,
	IReadOnlyList<(Vector3 Position, LootTier Tier)> Loot,
	IReadOnlyList<Vector3> WaveSpawns);

public sealed record MapIssue(string Rule, string Message);

/// <summary>
/// Checks marker data against the map 1 spec (s3, s4, s5, s7). Pure logic: path length and line of
/// sight come in as functions so the engine-side runner supplies navmesh and raycasts.
/// </summary>
public static class MapValidator
{
	/// <param name="pathLengthM">Walkable path length between two points, or null when unreachable.</param>
	public static List<MapIssue> Validate(MapMarkers map, ValidatorRules rules,
		Func<Vector3, Vector3, float?> pathLengthM, Func<Vector3, Vector3, bool> hasLineOfSight)
	{
		var issues = new List<MapIssue>();
		var spawns = map.PlayerSpawns;
		var highLoot = map.Loot.Where(l => l.Tier == LootTier.High).Select(l => l.Position).ToList();

		for (var i = 0; i < spawns.Count; i++)
		{
			for (var j = i + 1; j < spawns.Count; j++)
			{
				var d = spawns[i].DistanceTo(spawns[j]);
				if (d < rules.MinSpawnSpacingM)
					issues.Add(new("spawn-spacing", $"spawns {i + 1} and {j + 1} are {d:0} m apart (min {rules.MinSpawnSpacingM:0})"));
				if (hasLineOfSight(spawns[i], spawns[j]))
					issues.Add(new("spawn-los", $"spawns {i + 1} and {j + 1} see each other"));
			}
			foreach (var loot in highLoot.Where(l => hasLineOfSight(spawns[i], l)))
				issues.Add(new("spawn-high-loot-los", $"spawn {i + 1} sees high-tier loot at {loot}"));

			var ok = 0;
			foreach (var e in map.Extracts)
			{
				var len = pathLengthM(spawns[i], e.Position);
				if (len is null)
					issues.Add(new("extract-reach", $"spawn {i + 1} cannot reach extract '{e.Id}'"));
				else if (len >= rules.MinSpawnToExtractPathM)
					ok++;
			}
			if (ok < rules.MinExtractsPerSpawn)
				issues.Add(new("extract-count", $"spawn {i + 1} has {ok} extracts at >= {rules.MinSpawnToExtractPathM:0} m path (min {rules.MinExtractsPerSpawn})"));
		}

		var low = map.Loot.Count(l => l.Tier == LootTier.Low);
		var mid = map.Loot.Count(l => l.Tier == LootTier.Mid);
		var high = highLoot.Count;
		if (low < rules.MinLow || mid < rules.MinMid || high < rules.MinHigh)
			issues.Add(new("loot-tiers", $"loot low/mid/high = {low}/{mid}/{high}, need {rules.MinLow}/{rules.MinMid}/{rules.MinHigh}"));
		if (map.WaveSpawns.Count < rules.MinWaveMarkers)
			issues.Add(new("wave-markers", $"{map.WaveSpawns.Count} wave markers, need {rules.MinWaveMarkers}"));
		return issues;
	}
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj --filter MapValidatorTests`
Expected: PASS (6 tests). Note `FlagsSpawnThatReachesTooFewExtractsOrOnlyTooCloseOnes`: with the `OnlyFarOneReachable` function only extract "a" is reachable, so `ok` is 1 and the rule fires; the `Short` function gives 0 qualifying extracts.

- [ ] **Step 5: Commit**

```bash
git add scripts/systems/MapValidator.cs tests/ProjectD.Tests/MapValidatorTests.cs
git commit -m "feat(map): MapValidator rules for spawns, extracts, loot tiers, wave markers"
```

---

### Task 6: MapValidationRunner and `--validate`

**Files:**
- Create: `scripts/raid/MapValidationRunner.cs`
- Modify: `scripts/raid/RaidLevel.cs` (`ApplyDevArgs`: add `--validate`)

**Interfaces:**
- Consumes: `MapValidator.Validate`, `MapMarkers`, `ValidatorRules`, `ExtractPoint`, groups `player_spawns`, `extraction_zones`, `loot_spots` (`LootSpot.Tier`), `wave_spawns`.
- Produces: `static class MapValidationRunner` with `static int Run(RaidLevel level)` printing `MAPCHECK <rule>: <message>` per issue and `MAPCHECK done issues=<n>`, returning the issue count.

- [ ] **Step 1: Write `scripts/raid/MapValidationRunner.cs`**

```csharp
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Dev check: gathers a raid map's markers, supplies navmesh path length and eye-height raycasts to
/// <see cref="MapValidator"/> and prints the result. Run with <c>-- --validate</c>.
/// </summary>
public static class MapValidationRunner
{
	private const float EyeHeightM = 1.6f;
	private const float ReachToleranceM = 3f;

	public static int Run(RaidLevel level)
	{
		var tree = level.GetTree();
		var map = new MapMarkers(
			tree.GetNodesInGroup("player_spawns").OfType<Node3D>().Select(n => n.GlobalPosition).ToList(),
			tree.GetNodesInGroup("extraction_zones").OfType<ExtractionZone>()
				.Select(z => new ExtractPoint(z.ZoneId, z.GlobalPosition)).ToList(),
			tree.GetNodesInGroup("loot_spots").OfType<LootSpot>().Select(l => (l.GlobalPosition, l.Tier)).ToList(),
			tree.GetNodesInGroup("wave_spawns").OfType<Node3D>().Select(n => n.GlobalPosition).ToList());

		var navMap = level.GetWorld3D().NavigationMap;
		var space = level.GetWorld3D().DirectSpaceState;

		float? PathLength(Vector3 a, Vector3 b)
		{
			var path = NavigationServer3D.MapGetPath(navMap, a, b, true);
			if (path.Length == 0 || path[^1].DistanceTo(b) > ReachToleranceM)
				return null;
			var total = 0f;
			for (var i = 1; i < path.Length; i++)
				total += path[i - 1].DistanceTo(path[i]);
			return total;
		}

		bool LineOfSight(Vector3 a, Vector3 b)
		{
			var from = a + Vector3.Up * EyeHeightM;
			var to = b + Vector3.Up * EyeHeightM;
			return space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1u)).Count == 0;
		}

		var issues = MapValidator.Validate(map, new ValidatorRules(), PathLength, LineOfSight);
		foreach (var issue in issues)
			GD.Print($"MAPCHECK {issue.Rule}: {issue.Message}");
		GD.Print($"MAPCHECK done issues={issues.Count}");
		return issues.Count;
	}
}
```

- [ ] **Step 2: Edit `RaidLevel.ApplyDevArgs`**: add a `var validate = false;` local, an `else if (arg == "--validate") validate = true;` branch in the arg loop, and after the `if (report)` block:

```csharp
			if (validate)
			{
				await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
				MapValidationRunner.Run(this);
				GetTree().Quit();
				return;
			}
```

- [ ] **Step 3: Build and run**

Run: `dotnet build "Project D.csproj"` then `D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . res://scenes/maps/map1.tscn -- --validate`
Expected: no build warnings; output lines `MAPCHECK ...` ending with `MAPCHECK done issues=<n>`. Issues on the current blockout are expected (e.g. `spawn-spacing` for spawn pairs closer than 90 m); they are the to-do list for the graybox pass, not failures of this task. If `IntersectRay` mask `1u` misses walls, check collision layers on `Geometry` bodies and adjust the mask.

- [ ] **Step 4: Commit**

```bash
git add scripts/raid/MapValidationRunner.cs scripts/raid/RaidLevel.cs
git commit -m "feat(map): --validate dev arg prints spec rule violations for the loaded map"
```

---

### Task 7: Docs and handoff

**Files:**
- Modify: `docs/GDD.md` (section 15 raids: add one line on the wave), `CLAUDE.md` (no change unless milestone text needs it)

- [ ] **Step 1: Add to GDD §15 after the Spawns bullet**

```markdown
- **Reinforcement wave (map 1):** at T-8 min a siren sounds, then 4-6 AI arrive from the map edge and patrol the extract approaches. Later maps vary the pressure (closing extracts, other triggers).
```

- [ ] **Step 2: Full verification**

Run: `dotnet build "Project D.csproj"`, `dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj`, then the GUT smoke command from `CLAUDE.md`.
Expected: no warnings, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add docs/GDD.md
git commit -m "docs: GDD s15 reinforcement wave on map 1"
```

- [ ] **Step 4: Handoff checklist for the user**

Editor: open `scenes/maps/map1.tscn`, check the four `WaveSpawns/Wave*` markers sit on walkable edge ground and move them if not. Playtest: raid with `-- --wavetime=1100` (wave about 2 min in) and listen for the siren, then confirm 5 AI walk to the extract areas and do not path through the player spawn. Report: siren loudness, whether the wave feels too early or late at T-8, and any `MAPCHECK` lines you want the blockout fixed for.
