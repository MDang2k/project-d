using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// M2 shooting range, built in code. Targets at 15/50/100/200 m along -Z.
/// Debug keys: 1-5 weapon (pistol, AK, FAL, EM-2, MG), Delete refills the magazine, Backspace respawns.
/// The overlay never shows ammo (GDD s18); it shows weapon, fire mode and last impact.
/// </summary>
public partial class M2Range : Node3D
{
	private static readonly string[] WeaponPaths =
	{
		"res://data/weapons/pistol.tres",
		"res://data/weapons/ak_type.tres",
		"res://data/weapons/fal_type.tres",
		"res://data/weapons/em2.tres",
		"res://data/weapons/mg_type.tres",
	};
	private static readonly BodyPart[] DebugParts =
	{
		BodyPart.Head, BodyPart.Thorax, BodyPart.Stomach, BodyPart.LeftArm, BodyPart.RightArm,
		BodyPart.LeftLeg, BodyPart.RightLeg,
	};
	private static readonly (Key Key, string Item)[] MedicalKeys =
	{
		(Key.Insert, "bandage"), (Key.Home, "tourniquet"), (Key.Pageup, "splint"),
		(Key.Pagedown, "morphine"), (Key.End, "field_medkit"), (Key.Backslash, "surgery_kit"),
	};
	private static readonly (Key Key, string Item)[] ConsumableKeys =
	{
		(Key.Semicolon, "canned_food"), (Key.Apostrophe, "ration_pack"), (Key.Slash, "water_canteen"),
	};
	private static readonly string[] SpawnItemIds = { "pistol", "ak_type", "fal_type", "em2", "mg_type" };
	private static readonly float[] TargetDistances = { 15f, 50f, 100f, 200f };
	private static readonly Vector3 SpawnPoint = new(0f, 0.1f, 0f);

	private Player _player = null!;
	private Label _label = null!;
	private ProjectileManager _projectiles = null!;
	private string _lastImpact = "-";
	private string _message = "";
	private double _messageUntil;
	private int _hits;
	private static readonly float[] MarkDistances = { 25f, 50f, 100f, 150f, 200f };
	private bool _showTrajectory;
	private ImmediateMesh _previewMesh = null!;
	private MeshInstance3D _previewLine = null!;
	private readonly Label3D[] _marks = new Label3D[MarkDistances.Length];

	public override void _Ready()
	{
		BuildWorld();
		_projectiles = new ProjectileManager { Name = "Projectiles" };
		AddChild(_projectiles);
		_projectiles.Impact += OnImpact;

		var scene = GD.Load<PackedScene>("res://scenes/player/player.tscn");
		_player = scene.Instantiate<Player>();
		_player.Position = SpawnPoint;
		AddChild(_player);
		AddDummies();
		AddContainers();
		NoiseBus.Noise += n =>
		{
			_message = $"noise: {n.Kind}, heard within {n.Radius:0} m";
			_messageUntil = Time.GetTicksMsec() / 1000.0 + 1.5;
		};
		_player.WeaponMessage += text =>
		{
			_message = text;
			_messageUntil = Time.GetTicksMsec() / 1000.0 + 2.5;
		};

		var layer = new CanvasLayer();
		_label = new Label { Position = new Vector2(12f, 12f) };
		layer.AddChild(_label);
		AddChild(layer);
		BuildTrajectoryDebug();
		ApplyDevArgs();
	}

	/// <summary>Dev only (--actions=mag,reload,fast,chamber): runs actions and prints round totals.</summary>
	private async System.Threading.Tasks.Task RunDevActions(string[] actions)
	{
		if (actions.Length == 0)
			return;
		var w = _player.Weapon;
		var c = w!.Controller;
		_player.WeaponMessage += text => GD.Print($"MSG {text}");
		_player.MeleeHit += r => GD.Print($"MELEE HIT {r.Part} {r.Damage:0.0} dmg armor={r.ArmorHit} pen={r.Penetrated}");
		foreach (var a in actions)
		{
			if (a.StartsWith("dmg:"))
			{
				var f = a.Split(':');
				_player.Health.ReceiveDamage(System.Enum.Parse<BodyPart>(f[1]), float.Parse(f[2]), 1);
				GD.Print($"DMG {a} -> {EffectsLine()}");
				continue;
			}
			if (a.StartsWith("med:"))
			{
				var item = GD.Load<MedicalData>($"res://data/items/{a[4..]}.tres");
				var ok = _player.Health.UseMedical(item);
				GD.Print($"MED {a} started={ok}");
				while (_player.Health.Medical.IsBusy)
					await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"MED done -> {EffectsLine()}");
				continue;
			}
			if (a.StartsWith("key:"))
			{
				var key = System.Enum.Parse<Key>(a[4..], true);
				Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
				await ToSignal(GetTree().CreateTimer(0.12), SceneTreeTimer.SignalName.Timeout);
				Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
				await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"KEY {a} mouse={Input.MouseMode} melee-action={InputMap.HasAction("melee")}");
				continue;
			}
			if (a == "melee")
			{
				var swung = _player.DebugMelee();
				GD.Print($"MELEE started={swung}");
				while (_player.MeleeBusy)
					await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
				continue;
			}
			if (a.StartsWith("vit:"))
			{
				var f = a.Split(':');
				_player.Health.Vitals.Set(float.Parse(f[1]), float.Parse(f[2]));
				GD.Print($"VIT {a} -> {EffectsLine()}");
				continue;
			}
			if (a.StartsWith("eat:"))
			{
				var item = GD.Load<ConsumableData>($"res://data/items/{a[4..]}.tres");
				GD.Print($"EAT {a} started={_player.Health.UseConsumable(item)}");
				while (_player.Health.Consumables.IsBusy)
					await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"EAT done -> {EffectsLine()}");
				continue;
			}
			if (a.StartsWith("use:"))
			{
				var id = a[4..];
				var item = _player.Inventory.Inventory.AllItems().FirstOrDefault(i => i.Spec.Id == id);
				var ok = item is not null && _player.UseItem(item);
				GD.Print($"USE {id} started={ok}");
				await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
				while (_player.Health.Medical.IsBusy || _player.Health.Consumables.IsBusy)
					await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
				var left = _player.Inventory.Inventory.AllItems().Where(i => i.Spec.Id == id).Sum(i => i.Count);
				GD.Print($"USE done: {id} left={left} weight={_player.TotalCarriedKg:0.00} -> {EffectsLine()}");
				continue;
			}
			if (a.StartsWith("drag:"))
			{
				var f = a.Split(':');
				await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
				var moved = _player.Screen.DebugDragToGrid(f[1], int.Parse(f[2]), int.Parse(f[3]), int.Parse(f[4]));
				await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
				var where = _player.Inventory.Inventory.Locate(_player.Inventory.Inventory.AllItems().First(i => i.Spec.Id == f[1]));
				var inGrid = where?.Grid is { } wg ? $"grid {wg.Width}x{wg.Height}" : where?.Slot?.ToString() ?? "nowhere";
				GD.Print($"DRAG {a} attempted={moved} now in {inGrid}");
				continue;
			}
			if (a.StartsWith("load:"))
			{
				var inv = _player.Inventory.Inventory.AllItems().ToList();
				var mag = inv.Where(i => i.Spec.Category == ItemCategory.Magazine && i.Spec.Caliber == "7.62x39")
					.OrderBy(i => i.Rounds.Count).First();
				var ammo = inv.First(i => i.Spec.Id == "ammo_7_62x39");
				var before = mag.Rounds.Count;
				var loading = _player.TryLoadMagazine(mag, ammo);
				GD.Print($"LOAD started={loading} mag {before}/{mag.Spec.MagazineCapacity} stack={ammo.Count}");
				while (!(_player.LoaderStatus == ""))
					await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"LOAD done mag {mag.Rounds.Count}/{mag.Spec.MagazineCapacity} stack={ammo.Count} weight={_player.TotalCarriedKg:0.00}");
				continue;
			}
			if (a.StartsWith("dropout:"))
			{
				await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
				var id = a[8..];
				var before = _player.Inventory.Inventory.AllItems().Count(i => i.Spec.Id == id);
				var ok = _player.Screen.DebugDropOut(id);
				await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
				var after = _player.Inventory.Inventory.AllItems().Count(i => i.Spec.Id == id);
				var pickups = GetTree().GetNodesInGroup("dropped_items").Count;
				GD.Print($"DROP {id} attempted={ok} count {before}->{after} pickups on ground={pickups} weight={_player.TotalCarriedKg:0.00}");
				continue;
			}
			if (a == "pickup")
			{
				var ok = _player.DebugPickUpNearest();
				var left = GetTree().GetNodesInGroup("dropped_items").Count;
				await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"PICKUP ok={ok} ground items left={left} weight={_player.TotalCarriedKg:0.00}");
				continue;
			}
			if (a.StartsWith("jitter:"))
			{
				var seconds = double.Parse(a[7..]);
				var frames = 0;
				var maxStep = 0f;
				var sum = 0f;
				var last = _player.DebugHands();
				var end = Time.GetTicksMsec() / 1000.0 + seconds;
				while (Time.GetTicksMsec() / 1000.0 < end)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
					var now = _player.DebugHands();
					var step = Mathf.Max(now.Left.DistanceTo(last.Left), now.Right.DistanceTo(last.Right));
					maxStep = Mathf.Max(maxStep, step);
					sum += step;
					frames++;
					last = now;
				}
				GD.Print($"JITTER frames={frames} mean step={sum / Mathf.Max(1, frames) * 100f:0.00} cm, max step={maxStep * 100f:0.00} cm, retract={_player.WeaponRetract * 100f:0} cm");
				continue;
			}
			if (a.StartsWith("slot:"))
			{
				_player.SelectSlot(System.Enum.Parse<EquipSlot>(a[5..]));
				GD.Print($"SLOT {a} -> weapon {_player.Weapon?.Data.Id ?? "none"} active={_player.ActiveSlot}");
				continue;
			}
			if (a.StartsWith("wait:"))
			{
				await ToSignal(GetTree().CreateTimer(double.Parse(a[5..])), SceneTreeTimer.SignalName.Timeout);
				GD.Print($"WAIT {a} -> {PlayerHealthLine()} | {EffectsLine()}");
				continue;
			}
			var started = a switch
			{
				"mag" => c.StartMagCheck(),
				"reload" => c.StartReload(false),
				"fast" => c.StartReload(true),
				"chamber" => c.StartChamberCheck(),
				_ => false,
			};
			GD.Print($"ACTION {a} started={started}");
			while (c.IsBusy)
				await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
			GD.Print($"DONE {a}: total rounds {c.State.RoundsAvailable}, mag {c.State.Magazine?.Count}, spares {_player.Inventory.Inventory.AllItems().Count(i => i.Spec.Category == ItemCategory.Magazine)}");
		}
	}

	/// <summary>Search targets near the start: a low-tier crate, a mid-tier locker, a weapons crate (M4).</summary>
	private void AddContainers()
	{
		void Add(string title, string table, Vector3 pos, Vector2I grid, float secs, float noise, Vector3 box, Color color)
		{
			var c = new LootContainer
			{
				TitleKey = title,
				Table = GD.Load<LootTable>($"res://data/loot/{table}.tres"),
				GridSize = grid, SecondsPerItem = secs, NoiseRadius = noise, BoxSize = box, BoxColor = color,
				Position = pos,
			};
			AddChild(c);
		}
		Add("container.crate", "low_tier", new Vector3(2f, 0f, -2f), new Vector2I(5, 4), 1.2f, 8f,
			new Vector3(0.9f, 0.6f, 0.6f), new Color(0.45f, 0.33f, 0.2f));
		Add("container.locker", "mid_tier", new Vector3(-2.5f, 0f, -2f), new Vector2I(4, 6), 1.5f, 14f,
			new Vector3(0.7f, 1.9f, 0.5f), new Color(0.35f, 0.38f, 0.4f));
		Add("container.weapons_crate", "high_tier", new Vector3(0f, 0f, -1.6f), new Vector2I(6, 4), 2.0f, 10f,
			new Vector3(1.2f, 0.5f, 0.6f), new Color(0.3f, 0.34f, 0.25f));
	}

	/// <summary>Armored and unarmored humanoid targets 10 m out (M3 damage testing).</summary>
	private void AddDummies()
	{
		var cfg = GD.Load<HealthConfig>("res://data/health/health_config.tres");
		static ArmorData Armor(string id) => GD.Load<ArmorData>($"res://data/armor/{id}.tres");
		var specs = new (string Title, Vector3 Pos, string[] Armor)[]
		{
			("Unarmored", new Vector3(-3f, 0f, -10f), System.Array.Empty<string>()),
			("Flak vest T2", new Vector3(0f, 0f, -10f), new[] { "flak_vest_t2" }),
			("Helmet T1 + proto vest T4", new Vector3(3f, 0f, -10f), new[] { "steel_helmet_t1", "prototype_vest_t4" }),
		};
		foreach (var (title, pos, armor) in specs)
		{
			var d = DummyTarget.Create(title, cfg, System.Array.ConvertAll(armor, Armor));
			d.Position = pos;
			AddChild(d);
		}
	}

	private void BuildTrajectoryDebug()
	{
		_previewMesh = new ImmediateMesh();
		_previewLine = new MeshInstance3D
		{
			Mesh = _previewMesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.2f, 1f, 0.4f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = true,
			Visible = false,
		};
		AddChild(_previewLine);
		for (var i = 0; i < _marks.Length; i++)
		{
			_marks[i] = new Label3D
			{
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = true,
				FontSize = 32,
				FixedSize = true,
				PixelSize = 0.0007f,
				Offset = new Vector2(70f, -45f * i),
				Modulate = new Color(0.2f, 1f, 0.4f),
				TopLevel = true,
				Visible = false,
			};
			AddChild(_marks[i]);
		}
	}

	private void SetTrajectoryDebug(bool on)
	{
		_showTrajectory = on;
		_projectiles.ShowTrajectories = on;
		_previewLine.Visible = on;
		foreach (var m in _marks)
			m.Visible = on;
	}

	/// <summary>Draws the predicted bullet path from the muzzle with drop labels (debug, F3).</summary>
	public override void _PhysicsProcess(double delta)
	{
		if (!_showTrajectory || _player.Weapon is not { } w)
			return;
		var ammo = w.State.Chamber ?? w.Data.Ammo?.ToType() ?? new AmmoType("generic", 40f, 2, 700f);
		var origin = w.Muzzle.GlobalPosition;
		var dir = -w.Muzzle.GlobalBasis.Z;
		var path = _projectiles.PredictPath(ammo, origin, dir, _player.GetRid(), out _);
		ProjectileManager.FillLine(_previewMesh, path);

		for (var i = 0; i < _marks.Length; i++)
		{
			var d = MarkDistances[i];
			var found = false;
			foreach (var p in path)
			{
				if (origin.DistanceTo(p) < d)
					continue;
				var bore = origin + dir.Normalized() * d;
				_marks[i].GlobalPosition = p + Vector3.Up * 0.25f;
				_marks[i].Text = $"{d:0} m  drop {(bore.Y - p.Y) * 100f:0} cm";
				found = true;
				break;
			}
			_marks[i].Visible = found;
		}
	}

	/// <summary>
	/// Dev screenshots: godot ... -- --weapon=3 --fire=1.5 --shot=C:/x.png
	/// equips weapon N, holds the trigger for the given seconds, saves a screenshot and quits.
	/// </summary>
	private async void ApplyDevArgs()
	{
		string? shot = null;
		var fireSeconds = 0.0;
		var actions = System.Array.Empty<string>();
		var bipod = false;
		var searchContainer = -1;
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--shot="))
				shot = arg["--shot=".Length..];
			else if (arg.StartsWith("--actions="))
				actions = arg["--actions=".Length..].Split(',');
			else if (arg.StartsWith("--wheel=") && float.TryParse(arg["--wheel=".Length..], out var notches))
				_player.DebugAdjustHeight(notches);
			else if (arg.StartsWith("--seed=") && int.TryParse(arg["--seed=".Length..], out var seed))
				_player.Health.Seed = seed;
			else if (arg == "--inventory")
				_player.DebugToggleInventory();
			else if (arg.StartsWith("--search=") && int.TryParse(arg["--search=".Length..], out var searchIndex))
				searchContainer = searchIndex;
			else if (arg.StartsWith("--pitch=") && float.TryParse(arg["--pitch=".Length..], out var pitchDeg))
				_player.DebugSetPitchDeg(pitchDeg);
			else if (arg == "--prone")
				_player.DebugSetStance(Stance.Prone);
			else if (arg == "--crouch")
				_player.DebugSetStance(Stance.Crouch);
			else if (arg.StartsWith("--pos=") && arg["--pos=".Length..].Split(',') is { Length: 2 } xy
				&& float.TryParse(xy[0], out var px) && float.TryParse(xy[1], out var pz))
				_player.Position = new Vector3(px, 0.1f, pz);
			else if (arg == "--bipod")
				bipod = true;
			else if (arg == "--ads")
				_player.DebugForceAds = true;
			else if (arg == "--traj")
				SetTrajectoryDebug(true);
			else if (arg.StartsWith("--fire=") && double.TryParse(arg["--fire=".Length..], out var f))
				fireSeconds = f;
			else if (arg.StartsWith("--weapon=") && int.TryParse(arg["--weapon=".Length..], out var w)
				&& w >= 1 && w <= WeaponPaths.Length)
				_player.EquipWeapon(GD.Load<WeaponData>(WeaponPaths[w - 1]));
		}
		if (fireSeconds > 0.0)
		{
			await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
			_player.DebugForceTrigger = true;
			await ToSignal(GetTree().CreateTimer(fireSeconds), SceneTreeTimer.SignalName.Timeout);
			_player.DebugForceTrigger = false;
		}
		if (bipod)
		{
			await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
			_player.DebugToggleBipod();
		}
		if (searchContainer >= 0)
		{
			await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
			var found = GetTree().GetNodesInGroup("loot_containers").OfType<LootContainer>().ToList();
			if (searchContainer < found.Count)
				_player.Screen.OpenWithContainer(found[searchContainer]);
			GD.Print($"SEARCH opened {found.Count} containers, hidden={found.ElementAtOrDefault(searchContainer)?.Model.HiddenCount}");
		}
		await RunDevActions(actions);
		if (shot is null)
			return;
		await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
		GetViewport().GetTexture().GetImage().SavePng(shot);
		GD.Print($"IMPACT {_lastImpact} hits={_hits}");
		GetTree().Quit();
	}

	private void BuildWorld()
	{
		Box(new Vector3(0f, -0.5f, -100f), new Vector3(60f, 1f, 260f), new Color(0.3f, 0.35f, 0.25f));
		Box(new Vector3(0f, 4f, -235f), new Vector3(60f, 8f, 4f), new Color(0.4f, 0.32f, 0.25f));
		// Tall wall: the weapon pokes into it unless the clearance retraction works.
		Box(new Vector3(-8f, 1.25f, -4f), new Vector3(3f, 2.5f, 0.5f), new Color(0.5f, 0.45f, 0.45f));
		// Restable surfaces (GDD s7, s19): sandbag wall, window frame with sill, tall ledge.
		var cover = new Color(0.55f, 0.5f, 0.4f);
		Box(new Vector3(-4f, 0.45f, -6f), new Vector3(3f, 0.9f, 0.5f), cover);
		Box(new Vector3(4f, 0.5f, -6f), new Vector3(0.5f, 1f, 0.5f), cover);
		Box(new Vector3(6.5f, 0.5f, -6f), new Vector3(0.5f, 1f, 0.5f), cover);
		Box(new Vector3(5.25f, 0.95f, -6f), new Vector3(2.5f, 0.1f, 0.5f), cover);
		Box(new Vector3(5.25f, 1.8f, -6f), new Vector3(3.5f, 0.6f, 0.5f), cover);
		Box(new Vector3(0f, 0.6f, -4f), new Vector3(2f, 1.2f, 0.4f), cover);
		// Targets sit off the centre line (alternating sides) so a straight shot is never blocked:
		// the trajectory debug can then show drop labels out to the backstop.
		for (var i = 0; i < TargetDistances.Length; i++)
		{
			var d = TargetDistances[i];
			var x = (i % 2 == 0 ? -1f : 1f) * (2.5f + i);
			var t = Box(new Vector3(x, 0.9f, -d), new Vector3(0.6f, 1.8f, 0.1f), new Color(0.75f, 0.2f, 0.2f));
			t.SetMeta("target", true);
			Box(new Vector3(0f, 0.05f, -d), new Vector3(0.3f, 0.1f, 0.3f), new Color(0.9f, 0.8f, 0.2f));
		}
		AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50f, 30f, 0f), ShadowEnabled = true });
		AddChild(new WorldEnvironment
		{
			Environment = new Godot.Environment
			{
				BackgroundMode = Godot.Environment.BGMode.Color,
				BackgroundColor = new Color(0.55f, 0.7f, 0.9f),
				AmbientLightSource = Godot.Environment.AmbientSource.Color,
				AmbientLightColor = new Color(0.6f, 0.65f, 0.7f),
			},
		});
	}

	private StaticBody3D Box(Vector3 pos, Vector3 size, Color color)
	{
		var body = new StaticBody3D { Position = pos };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		var mat = new StandardMaterial3D { AlbedoColor = color };
		body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = mat } });
		AddChild(body);
		return body;
	}

	private void OnImpact(BulletImpact hit)
	{
		var marker = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.9f, 0.2f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
			Position = hit.Position,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(marker);
		GetTree().CreateTimer(8.0).Timeout += marker.QueueFree;

		_lastImpact = $"{hit.TravelDistance:0} m, speed {hit.Velocity.Length():0} m/s";
		if (hit.Hit is { } r)
			_lastImpact += $"  {r.Part} {r.Damage:0} dmg{(r.ArmorHit ? (r.Penetrated ? " (penetrated)" : " (stopped)") : "")}";
		if (hit.Collider is Node node && node.HasMeta("target"))
		{
			_hits++;
			_lastImpact += " TARGET";
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } k)
			return;
		var index = (int)k.PhysicalKeycode - (int)Key.Key1;
		if (k.CtrlPressed && index >= 0 && index < SpawnItemIds.Length)
		{
			var spawned = InventoryComponent.Make(SpawnItemIds[index]);
			var stored = spawned is not null && _player.Inventory.Inventory.TryAutoStore(spawned);
			_message = stored ? $"Added {GameText.ItemName(SpawnItemIds[index])} to the inventory" : "No room in the inventory";
			_messageUntil = Time.GetTicksMsec() / 1000.0 + 2.5;
			if (stored)
				_player.Inventory.NotifyChanged();
			return;
		}
		if (index >= 0 && index < WeaponPaths.Length)
			return;
		switch (k.PhysicalKeycode)
		{
			case Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11:
				_player.Health.ReceiveDamage(DebugParts[(int)k.PhysicalKeycode - (int)Key.F5], 20f, 1);
				break;
			case Key.Insert or Key.Home or Key.Pageup or Key.Pagedown or Key.End or Key.Backslash:
				UseMedical(System.Array.Find(MedicalKeys, m => m.Key == k.PhysicalKeycode).Item);
				break;
			case Key.Semicolon or Key.Apostrophe or Key.Slash:
				UseConsumable(System.Array.Find(ConsumableKeys, c => c.Key == k.PhysicalKeycode).Item);
				break;
			case Key.Minus:
				_player.Health.Vitals.Set(18f, 18f);
				break;
			case Key.Equal:
				_player.Health.Vitals.Reset();
				break;
			case Key.F12:
				_player.Health.Reset();
				break;
			case Key.F3:
				SetTrajectoryDebug(!_showTrajectory);
				break;
			case Key.Delete:
				_player.Weapon?.RefillDebug();
				break;
			case Key.Backspace:
				_player.Position = SpawnPoint;
				_player.Velocity = Vector3.Zero;
				break;
		}
	}

	private void UseConsumable(string id)
	{
		var item = GD.Load<ConsumableData>($"res://data/items/{id}.tres");
		var started = _player.Health.UseConsumable(item);
		_message = started ? $"Using {id}..." : $"Cannot use {id} now";
		_messageUntil = Time.GetTicksMsec() / 1000.0 + 2.5;
	}

	private void UseMedical(string id)
	{
		var item = GD.Load<MedicalData>($"res://data/items/{id}.tres");
		var started = _player.Health.UseMedical(item);
		_message = started ? $"Using {id}..." : $"Cannot use {id} now";
		_messageUntil = Time.GetTicksMsec() / 1000.0 + 2.5;
	}

	private string EffectsLine()
	{
		var fx = _player.Health.Effects;
		var sb = new System.Text.StringBuilder("effects:");
		foreach (var part in fx.BleedingParts)
			sb.Append($" {fx.Bleed(part)}-bleed({part})");
		foreach (var part in fx.Fractures)
			sb.Append($" fracture({part})");
		var p = fx.ComputePenalties(_player.Health.Body);
		sb.Append($"  pain {fx.Pain:0}{(fx.PainSuppressed ? " (morphine " + fx.MorphineLeft.ToString("0") + "s)" : "")}");
		sb.Append($"  sway x{p.SwayMultiplier:0.0} speed x{p.MoveSpeedMultiplier:0.0}");
		if (p.SprintBlocked)
			sb.Append(" NO-SPRINT");
		var vit = _player.Health.Vitals;
		sb.Append($"  energy {vit.Energy:0} hydration {vit.Hydration:0}{(vit.Starving ? " STARVING" : "")}");
		var eat = _player.Health.Consumables;
		if (eat.IsBusy)
			sb.Append($"   [{eat.Current!.Id} {eat.Progress * 100f:0}%]");
		var med = _player.Health.Medical;
		if (med.IsBusy)
			sb.Append($"   [{med.Current!.Kind} {med.Progress * 100f:0}%]");
		if (fx.HealPoolLeft > 0f)
			sb.Append($"  medkit {fx.HealPoolLeft:0} HP left");
		return sb.ToString();
	}

	private string PlayerHealthLine()
	{
		var b = _player.Health.Body;
		if (b.IsDead)
			return "YOU: DEAD (F12 reset)";
		return $"YOU  H {b.Hp(BodyPart.Head):0}  T {b.Hp(BodyPart.Thorax):0}  S {b.Hp(BodyPart.Stomach):0}  " +
			$"LA {b.Hp(BodyPart.LeftArm):0}  RA {b.Hp(BodyPart.RightArm):0}  " +
			$"LL {b.Hp(BodyPart.LeftLeg):0}  RL {b.Hp(BodyPart.RightLeg):0}   [G melee | ; canned ' ration / water | - low vitals, = full | F5-F11 dmg 20, F12 reset | Ins bandage Home tourniquet PgUp splint PgDn morphine End medkit \\ surgery]";
	}

	public override void _Process(double delta)
	{
		_label.Visible = !_player.InventoryOpen;
		var w = _player.Weapon;
		var name = w is null ? "none" : Tr(w.Data.NameKey);
		var mode = w?.State.Fire.Mode.ToString() ?? "-";
		_label.Text =
			$"{name}  mode {mode}  stance {_player.Stance}{(_player.IsSprinting ? "  SPRINT" : "")}\n" +
			$"kit {_player.TotalCarriedKg:0.0} kg ({_player.Tier})  " +
			$"height {_player.HeightLevel * 100f:0}%  retract {_player.WeaponRetract * 100f:0} cm  " +
			$"rest {_player.RestAmount * 100f:0}%{(float.IsNaN(_player.RestSurfaceHeight) ? "" : $" (surface {_player.RestSurfaceHeight:0.00} m)")}{(_player.BipodDeployed ? "  BIPOD" : "")}  " +
			$"ADS {_player.AdsAmount * 100f:0}%{(_player.IsHoldingBreath ? "  BREATH HELD" : "")}  sway {_player.SwayAmplitudeDeg:0.00} deg  " +
			$"recoil {_player.RecoilOffsetDeg.Y:0.0} deg  arms {_player.Arms.Fraction * 100f:0}%{(_player.Arms.IsExhausted ? " EXHAUSTED" : "")}\n" +
			$"{PlayerHealthLine()}\n{EffectsLine()}\nmelee {_player.MeleeDebug}\n" +
			$"last impact: {_lastImpact}   target hits {_hits}\n" +
			$"{(Time.GetTicksMsec() / 1000.0 < _messageUntil ? _message : "")}{(w is { Controller.IsBusy: true } ? $"   [{w.Controller.Action} {w.Controller.Progress * 100f:0}%]" : "")}\n" +
			"1-3 draw slot, Ctrl+1-5 add weapon, Tab inventory  V fire  RMB aim  Shift(aiming) breath  R reload (2x fast)  T mag check  Shift+T chamber  hold C+wheel height  Delete refill  F3 trajectory " + (_showTrajectory ? "ON" : "off") + "  Backspace respawn";
	}
}
