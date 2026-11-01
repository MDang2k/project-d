using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>F1 debug menu of the AI range: spawn bots by faction, respawn and freeze switches, bodies, time scale.</summary>
public partial class AiRange
{
	private static readonly (string Id, string Title)[] SpawnChoices =
	{
		("looter", "Looter"), ("bandit", "Bandit"), ("allied_pmc", "Allied PMC"), ("soviet_pmc", "Soviet PMC"),
	};

	private PanelContainer _menu = null!;
	private Label _menuInfo = null!;
	private CheckButton _noclipBox = null!;
	private OptionButton _sideBox = null!;
	private int _spawned;

	private void BuildMenu(CanvasLayer layer)
	{
		_menu = new PanelContainer { Visible = false };
		_menu.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_menu.GrowHorizontal = Control.GrowDirection.Begin;
		_menu.OffsetLeft = -12f;
		_menu.OffsetRight = -12f;
		_menu.OffsetTop = 12f;
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(290f, 0f) };
		box.AddThemeConstantOverride("separation", 4);
		_menu.AddChild(box);
		layer.AddChild(_menu);

		box.AddChild(new Label { Text = "AI RANGE DEBUG  (F1 closes)" });
		_menuInfo = new Label();
		box.AddChild(_menuInfo);

		box.AddChild(new Label { Text = "Spawn a bot 12 m ahead of you:" });
		var grid = new GridContainer { Columns = 2 };
		foreach (var (id, title) in SpawnChoices)
		{
			var presetId = id;
			var b = new Button { Text = "+ " + title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			b.Pressed += () => SpawnBotAhead(presetId, patrol: true);
			grid.AddChild(b);
		}
		box.AddChild(grid);

		box.AddChild(Toggle("Bots respawn after dying", AiHuman.AutoRespawn, on => AiHuman.AutoRespawn = on));
		box.AddChild(Toggle("Freeze all bots", AiHuman.Paused, on => AiHuman.Paused = on));
		box.AddChild(Toggle("Show bot cones and labels", _debug, on => SetAiDebug(on)));
		_noclipBox = Toggle("Noclip (ghost, F2)", false, on => _player.DebugNoclip = on);
		box.AddChild(_noclipBox);

		box.AddChild(new Label { Text = "Your side:" });
		_sideBox = new OptionButton();
		foreach (var f in System.Enum.GetValues<Faction>())
			_sideBox.AddItem(f.ToString(), (int)f);
		_sideBox.Select((int)_player.Faction);
		_sideBox.ItemSelected += i => _player.DebugFaction = (Faction)(int)i;
		box.AddChild(_sideBox);

		box.AddChild(new Label { Text = "Camera (look at your character, ` cycles):" });
		var views = new GridContainer { Columns = 4 };
		foreach (var view in System.Enum.GetValues<DebugCameraView>())
		{
			var v = view;
			views.AddChild(Action(v == DebugCameraView.FirstPerson ? "1st person" : v.ToString(), () => _player.DebugView = v));
		}
		box.AddChild(views);

		box.AddChild(new Label { Text = "Game speed:" });
		var speed = new HSlider { MinValue = 0.1, MaxValue = 3.0, Step = 0.1, Value = 1.0 };
		speed.ValueChanged += v => Engine.TimeScale = v;
		box.AddChild(speed);

		box.AddChild(Action("Looter look test: 8 mannequins", () => SpawnLooterLineup(8)));

		var row = new GridContainer { Columns = 2 };
		row.AddChild(Action("Spawn body ahead", () => CorpseSpawner.Spawn(this, AheadPoint(8f),
			new[] { "flak_vest_t1" })));
		row.AddChild(Action("Kill all bots", () =>
		{
			foreach (var ai in Bots())
				ai.Health.Body.TakeDamage(BodyPart.Head, 9999f);
		}));
		row.AddChild(Action("Remove all bots", () =>
		{
			foreach (var ai in Bots())
				ai.QueueFree();
		}));
		row.AddChild(Action("Remove all bodies", () =>
		{
			foreach (var c in GetTree().GetNodesInGroup("loot_containers").OfType<LootContainer>().Where(c => c.IsCorpse))
				c.QueueFree();
		}));
		row.AddChild(Action("Heal me / respawn me", () =>
		{
			_player.Health.Reset();
			_player.Velocity = Vector3.Zero;
		}));
		row.AddChild(Action("Teleport me to spawn", () => _player.Position = SpawnPoint));
		box.AddChild(row);
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

	private System.Collections.Generic.IEnumerable<AiHuman> Bots()
		=> GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().ToList();

	private void SetAiDebug(bool on)
	{
		_debug = on;
		foreach (var ai in Bots())
			ai.ShowDebug = on;
	}

	/// <summary>A point on the ground a few metres in front of where the player looks.</summary>
	private Vector3 AheadPoint(float distance)
	{
		var forward = -_player.GlobalBasis.Z;
		forward.Y = 0f;
		forward = forward.Normalized();
		var p = _player.GlobalPosition + forward * distance;
		return new Vector3(p.X, 0.1f, p.Z);
	}

	private void SpawnBotAhead(string presetId, bool patrol)
	{
		var at = AheadPoint(12f);
		var toPlayer = _player.GlobalPosition - at;
		var yaw = Mathf.RadToDeg(Mathf.Atan2(-toPlayer.X, -toPlayer.Z));
		var loop = patrol
			? new[]
			{
				at, at + new Vector3(8f, 0f, 0f), at + new Vector3(8f, 0f, -8f), at + new Vector3(0f, 0f, -8f),
			}
			: System.Array.Empty<Vector3>();
		SpawnAi(presetId, at, yaw, loop, $"_dbg{++_spawned}");
	}

	/// <summary>A row of looter mannequins facing the player, each with its own random face and outfit, for a look test.</summary>
	private void SpawnLooterLineup(int count, bool backsToPlayer = false)
	{
		var forward = -_player.GlobalBasis.Z;
		forward.Y = 0f;
		forward = forward.Normalized();
		var right = forward.Cross(Vector3.Up);
		var centre = AheadPoint(6f);
		for (var i = 0; i < count; i++)
		{
			var at = centre + right * ((i - (count - 1) * 0.5f) * 1.1f);
			var toPlayer = _player.GlobalPosition - at;
			var ai = new AiHuman
			{
				Name = $"looter_look{++_spawned}",
				Actor = ActorDb.Create("looter"),
				Mannequin = true,
				Position = at,
				RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(-toPlayer.X, -toPlayer.Z)) + (backsToPlayer ? 180f : 0f), 0f),
			};
			AddChild(ai);
			ai.ShowDebug = false;
			GD.Print($"LOOK {ai.Name}: {ai.LookLabel}");
		}
	}

	private void ToggleMenu()
	{
		_menu.Visible = !_menu.Visible;
		Input.MouseMode = _menu.Visible ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}

	private void RefreshMenu()
	{
		if (!_menu.Visible)
			return;
		var bots = Bots().ToList();
		var alive = bots.Count(b => b.IsAlive);
		var bodies = GetTree().GetNodesInGroup("loot_containers").OfType<LootContainer>().Count(c => c.IsCorpse && !c.Taken);
		_menuInfo.Text = $"bots {alive} alive / {bots.Count} total   bodies {bodies}   noclip {(_player.DebugNoclip ? "on" : "off")}";
		if (_noclipBox.ButtonPressed != _player.DebugNoclip)
			_noclipBox.SetPressedNoSignal(_player.DebugNoclip);
		if (_sideBox.Selected != (int)_player.Faction)
			_sideBox.Select((int)_player.Faction);
	}
}
