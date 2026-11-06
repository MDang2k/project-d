using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Raid debug menu (dev aid, debug builds only): F1 opens it. God mode, noclip, heal, give an item by id, teleport to
/// documents, deposits, exits and the spawn, exposure, bots frozen or killed, game speed, and switches for the hit log
/// and the debug map. Dev flags: <c>--debugmenu</c> opens it at start, <c>--debugmap</c> shows the map, <c>--debughitboxes</c> shows NPC hitboxes. Plain buttons over existing Player and node dev hooks; nothing here saves to the profile.
/// </summary>
public partial class PlayerDebugMenu : CanvasLayer
{
	private Player _player = null!;
	private DebugHitLog _hitLog = null!;
	private DebugMapOverlay _map = null!;
	private PanelContainer _panel = null!;
	private Label _info = null!;
	private CheckButton _god = null!;
	private CheckButton _noclip = null!;
	private CheckButton _mapBox = null!;
	private CheckButton _hitboxBox = null!;
	private OptionButton _targets = null!;
	private LineEdit _itemId = null!;
	private readonly List<Vector3> _targetPositions = new();
	private float _infoLeft;
	private float _hitboxScanLeft;
	private bool _showHitboxes;
	private const string HitboxMarker = "DebugShape";

	public void Start(Player player, ProjectileManager projectiles, RaidLevel level)
	{
		_player = player;
		Layer = 20;
		_hitLog = new DebugHitLog { Name = "HitLog" };
		AddChild(_hitLog);
		_hitLog.Start(player, projectiles);
		_map = new DebugMapOverlay { Name = "DebugMap" };
		AddChild(_map);
		_map.Start(player);
		Build(level);
		var args = OS.GetCmdlineUserArgs();
		if (args.Contains("--debugmenu"))
			ToggleMenu();
		if (args.Contains("--debughitboxes"))
			_hitboxBox.ButtonPressed = true;
		if (args.Contains("--debugmap"))
			_mapBox.ButtonPressed = true;
	}

	private void Build(RaidLevel level)
	{
		_panel = new PanelContainer { Visible = false };
		_panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_panel.GrowHorizontal = Control.GrowDirection.Begin;
		_panel.OffsetLeft = -12f;
		_panel.OffsetRight = -12f;
		_panel.OffsetTop = 12f;
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(320f, 0f) };
		box.AddThemeConstantOverride("separation", 4);
		_panel.AddChild(box);
		AddChild(_panel);

		box.AddChild(new Label { Text = "DEBUG  (F1 closes)" });
		_info = new Label();
		box.AddChild(_info);

		_god = Toggle("God mode (no hit damage)", false, on => _player.Health.DamageScale = on ? 0f : 1f);
		box.AddChild(_god);
		_noclip = Toggle("Noclip (F2)", false, on => _player.DebugNoclip = on);
		box.AddChild(_noclip);
		box.AddChild(Toggle("Hit log (bottom right)", true, on => _hitLog.Visible = on));
		_mapBox = Toggle("Debug map (top left)", false, on => _map.Visible = on);
		box.AddChild(_mapBox);
		box.AddChild(Toggle("Show bot cones and labels (F4)", level.ShowAiDebug, on =>
		{
			level.ShowAiDebug = on;
			foreach (var ai in Bots())
				ai.ShowDebug = on;
		}));
		_hitboxBox = Toggle("Show NPC hitboxes", false, SetHitboxes);
		box.AddChild(_hitboxBox);
		box.AddChild(Toggle("Show zone volumes (foliage, quests, transits)", false, on => MapDresser.SetVolumesVisible(GetTree(), on)));
		box.AddChild(Toggle("Freeze all bots", AiHuman.Paused, on => AiHuman.Paused = on));

		box.AddChild(new Label { Text = "Teleport to:" });
		var tp = new HBoxContainer();
		_targets = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		tp.AddChild(_targets);
		tp.AddChild(Action("Go", () =>
		{
			if (_targets.Selected >= 0 && _targets.Selected < _targetPositions.Count)
				_player.DebugTeleport(_targetPositions[_targets.Selected]);
		}));
		tp.AddChild(Action("Refresh", RefreshTargets));
		box.AddChild(tp);

		box.AddChild(new Label { Text = "Give (into your pockets):" });
		var give = new GridContainer { Columns = 2 };
		foreach (var (id, title) in new[]
		{
			("ewigstein_shard", "Ewigstein shard"), ("small_ewigstein_canister", "Small canister"), ("large_ewigstein_canister", "Large canister"),
			("german_lab_note_probe_7", "German lab note"), ("bunker_side_room_door_code", "Door code"), ("radio_room_key", "Radio room key"),
		})
		{
			var itemId = id;
			give.AddChild(Action("+ " + title, () => Give(itemId)));
		}
		box.AddChild(give);
		var row = new HBoxContainer();
		_itemId = new LineEdit { PlaceholderText = "any item id", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		row.AddChild(_itemId);
		row.AddChild(Action("Give", () => Give(_itemId.Text.Trim())));
		box.AddChild(row);

		var misc = new GridContainer { Columns = 2 };
		misc.AddChild(Action("Heal me", () => _player.Health.Reset()));
		misc.AddChild(Action("Exposure reset", () => _player.Exposure.Reset()));
		misc.AddChild(Action("Exposure +25", () => _player.Exposure.Update(25f, 1, System.Array.Empty<float>())));
		misc.AddChild(Action("Kill all bots", () =>
		{
			foreach (var ai in Bots())
				ai.Health.Body.TakeDamage(BodyPart.Head, 9999f);
		}));
		box.AddChild(misc);

		box.AddChild(new Label { Text = "Game speed:" });
		var speed = new HSlider { MinValue = 0.1, MaxValue = 3.0, Step = 0.1, Value = 1.0 };
		speed.ValueChanged += v => Engine.TimeScale = v;
		box.AddChild(speed);
		foreach (var bus in new[] { "Master", "SFX", "Ambient", "UI" })
		{
			var name = bus;
			box.AddChild(new Label { Text = $"Volume: {name}" });
			var slider = new HSlider { MinValue = 0.0, MaxValue = 1.0, Step = 0.05, Value = Sfx.GetBusVolume(name) };
			slider.ValueChanged += v => Sfx.SetBusVolume(name, (float)v);
			box.AddChild(slider);
		}
		RefreshTargets();
	}

	private static CheckButton Toggle(string text, bool initial, System.Action<bool> apply)
	{
		var b = new CheckButton { Text = text, ButtonPressed = initial };
		b.Toggled += on => apply(on);
		return b;
	}

	private static Button Action(string text, System.Action action)
	{
		var b = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		b.Pressed += action;
		return b;
	}

	private IEnumerable<AiHuman> Bots() => GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().ToList();

	private void Give(string id)
	{
		if (id == "" || ItemDb.Create(id) is not { } item)
		{
			_player.NotifyMessage($"Unknown item '{id}'");
			return;
		}
		if (_player.Inventory.Inventory.TryAutoStore(item))
			_player.Inventory.NotifyChanged();
		else
			_player.NotifyMessage("No room");
	}

	private void RefreshTargets()
	{
		_targets.Clear();
		_targetPositions.Clear();
		void Add(string label, Vector3 at)
		{
			_targets.AddItem(label);
			_targetPositions.Add(at);
		}
		foreach (var s in GetTree().GetNodesInGroup("player_spawns").OfType<Node3D>().Take(1))
			Add("Player spawn", s.GlobalPosition);
		foreach (var spot in GetTree().GetNodesInGroup("knowledge_spots").OfType<KnowledgeSpot>().Where(k => k.ItemId != ""))
			Add("Document: " + GameText.ItemName(spot.ItemId), spot.GlobalPosition + Vector3.Back * 1.5f);
		foreach (var d in GetTree().GetNodesInGroup("ewigstein_deposits").OfType<Node3D>())
			Add("Deposit: " + d.Name, d.GlobalPosition + Vector3.Back * 3f);
		foreach (var z in GetTree().GetNodesInGroup("extraction_zones").OfType<Node3D>())
			Add("Exit: " + z.Name, z.GlobalPosition);
		if (_targets.ItemCount > 0)
			_targets.Select(0);
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 })
			return;
		ToggleMenu();
		GetViewport().SetInputAsHandled();
	}

	private void ToggleMenu()
	{
		_panel.Visible = !_panel.Visible;
		Input.MouseMode = _panel.Visible ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
		if (_panel.Visible)
			RefreshTargets();
	}

	/// <summary>Draws every NPC hitbox as a translucent shape (head red, torso orange, limbs blue) so you can see what a shot can hit.</summary>
	private void SetHitboxes(bool on)
	{
		_showHitboxes = on;
		_hitboxScanLeft = 0f;
		if (!on)
		{
			foreach (var area in GetTree().GetNodesInGroup("hitboxes").OfType<HitboxArea>())
				area.GetNodeOrNull(HitboxMarker)?.QueueFree();
		}
	}

	private void AddHitboxShapes()
	{
		var added = 0;
		foreach (var area in GetTree().GetNodesInGroup("hitboxes").OfType<HitboxArea>())
		{
			if (area.Health?.GetParent() is not AiHuman || area.HasNode(HitboxMarker))
				continue;
			var shape = area.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape;
			if (shape?.GetDebugMesh() is not { } mesh)
				continue;
			var color = area.Part switch
			{
				BodyPart.Head => new Color(1f, 0.2f, 0.2f, 0.55f),
				BodyPart.Thorax or BodyPart.Stomach => new Color(1f, 0.6f, 0.15f, 0.45f),
				_ => new Color(0.3f, 0.6f, 1f, 0.4f),
			};
			area.AddChild(new MeshInstance3D
			{
				Name = HitboxMarker,
				Mesh = mesh,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new StandardMaterial3D
				{
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					AlbedoColor = color,
					NoDepthTest = false,
				},
			});
			added++;
		}
		if (added > 0)
			GD.Print($"HITBOXES shown on {added} new hitboxes");
	}

	public override void _Process(double delta)
	{
		if (_showHitboxes && (_hitboxScanLeft -= (float)delta) <= 0f)
		{
			_hitboxScanLeft = 0.5f; // bots that spawn later get their shapes within half a second
			AddHitboxShapes();
		}
		if (!_panel.Visible || (_infoLeft -= (float)delta) > 0f)
			return;
		_infoLeft = 0.25f;
		var p = _player.GlobalPosition;
		_info.Text = $"pos {p.X:0}, {p.Y:0}, {p.Z:0}   hp {_player.Health.Body.TotalHp:0}   exposure {_player.Exposure.Level:0}%   fps {Engine.GetFramesPerSecond():0}";
		if (_noclip.ButtonPressed != _player.DebugNoclip)
			_noclip.SetPressedNoSignal(_player.DebugNoclip);
	}
}
