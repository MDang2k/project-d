using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Runs the transit zones of a raid map (world map spec s3): finds the zone the player stands in, checks its gate with
/// <see cref="TransitRules"/>, runs the <see cref="TransitCountdown"/>, and on completion takes the fare and either moves
/// the player to the other end of a tunnel or carries them into the next map's raid. Dev: <c>--transitto=map</c> sends
/// every road to that map id, and <c>--gozone=id</c> puts the player in a zone after a second.
/// </summary>
public partial class TransitManager : Node
{
	private const string RegistryPath = "res://data/maps/registry.json";

	private readonly TransitCountdown _countdown = new();
	private RaidManager _raid = null!;
	private Player _player = null!;
	private string _mapId = "";
	private MapRegistry _registry = MapRegistry.Parse("{\"maps\":[]}");
	private string? _destinationOverride;
	private bool _busy;
	private CanvasLayer _layer = null!;
	private Label _title = null!;
	private Label _detail = null!;
	private ColorRect _bar = null!;
	private ColorRect _barBack = null!;
	private ColorRect _fade = null!;

	public void Start(RaidManager raid, Player player, string mapId, bool arrived = false)
	{
		_raid = raid;
		_player = player;
		_mapId = mapId;
		if (FileAccess.FileExists(RegistryPath))
			_registry = MapRegistry.Parse(FileAccess.GetFileAsString(RegistryPath));
		var args = OS.GetCmdlineUserArgs();
		_destinationOverride = args.FirstOrDefault(a => a.StartsWith("--transitto="))?["--transitto=".Length..];
		BuildHud();
		if (arrived)
		{
			_fade.Color = new Color(0f, 0f, 0f, 1f); // come in from black
			_ = Fade(0f, 1.2f);
		}
		if (args.FirstOrDefault(a => a.StartsWith("--cash="))?["--cash=".Length..] is { } cash && int.TryParse(cash, out var amount)
			&& ItemDb.Create(MoneyLedger.MoneyId, amount) is { } notes)
			_player.Inventory.Inventory.TryAutoStore(notes);
		if (args.FirstOrDefault(a => a.StartsWith("--gozone="))?["--gozone=".Length..] is { } go)
			GoToZone(go);
	}

	private async void GoToZone(string id)
	{
		await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
		if (Zones().FirstOrDefault(z => z.ZoneId == id) is { } zone)
			_player.GlobalPosition = zone.GlobalPosition + Vector3.Up * 0.2f;
	}

	private System.Collections.Generic.IEnumerable<TransitZone> Zones() => GetTree().GetNodesInGroup("transit_zones").OfType<TransitZone>();

	private string DestinationOf(TransitSpec spec) => _destinationOverride is { Length: > 0 } o ? o : spec.Destination;

	public override void _PhysicsProcess(double delta)
	{
		if (_busy || !_raid.IsRunning || !_player.IsAlive)
			return;
		var zone = Zones().FirstOrDefault(z => z.Contains(_player.GlobalPosition));
		var spec = zone?.ToSpec();
		var inv = _player.Inventory.Inventory;
		var block = spec is null
			? TransitBlock.None
			: TransitRules.Check(spec, MoneyLedger.Count(inv.StorageGrids), id => inv.AllItems().Any(i => i.Spec.Id == id),
				_registry.IsOpen(DestinationOf(spec)));
		var done = _countdown.Update((float)delta, spec, block);
		ShowHud(spec, block);
		if (done is not null && zone is not null)
			Perform(done, zone);
	}

	private async void Perform(TransitSpec spec, TransitZone zone)
	{
		_busy = true;
		var inv = _player.Inventory.Inventory;
		if (spec.Cost > 0)
			MoneyLedger.Remove(inv.StorageGrids.ToList(), spec.Cost);
		_player.Inventory.NotifyChanged();
		await Fade(1f, 0.5f);
		if (spec.Kind == TransitKind.Tunnel)
		{
			var pair = Zones().FirstOrDefault(z => z.ZoneId == spec.PairId);
			if (pair is null)
			{
				GD.PushWarning($"[Transit] tunnel '{spec.Id}' has no pair '{spec.PairId}'");
				await Fade(0f, 0.4f);
				_busy = false;
				return;
			}
			_player.GlobalPosition = pair.GlobalPosition + Vector3.Up * 0.2f;
			_player.Velocity = Vector3.Zero;
			_player.Rotation = new Vector3(0f, pair.GlobalRotation.Y, 0f);
			_countdown.Landed(pair.ZoneId);
			GD.Print($"TRANSIT tunnel {spec.Id} -> {pair.ZoneId} paid={spec.Cost}");
			await Fade(0f, 0.6f);
			_busy = false;
			return;
		}
		var destination = DestinationOf(spec);
		var scene = _registry.SceneOf(destination);
		if (scene is null)
		{
			await Fade(0f, 0.4f);
			_busy = false;
			return;
		}
		TransitCarry.Pending = _player.Health.Capture();
		TransitCarry.FromMap = _mapId;
		GD.Print($"TRANSIT road {spec.Id}: {_mapId} -> {destination} paid={spec.Cost}");
		GetTree().ChangeSceneToFile(scene);
	}

	private async System.Threading.Tasks.Task Fade(float alpha, float seconds)
	{
		var tween = CreateTween();
		tween.TweenProperty(_fade, "color:a", alpha, seconds);
		await ToSignal(tween, Tween.SignalName.Finished);
	}

	private void ShowHud(TransitSpec? spec, TransitBlock block)
	{
		if (spec is null)
		{
			_layer.Visible = false;
			return;
		}
		_layer.Visible = true;
		var unit = GameText.Get("currency.unit");
		if (block != TransitBlock.None)
		{
			_title.Text = GameText.Get("transit.blocked.title");
			_title.Modulate = new Color(1f, 0.55f, 0.4f);
			_detail.Text = block switch
			{
				TransitBlock.Closed => GameText.Get("transit.blocked.closed"),
				TransitBlock.MissingItem => GameText.Get("transit.blocked.item", GameText.ItemName(spec.RequiredItemId ?? "?")),
				_ => GameText.Get("transit.blocked.pay", spec.Cost, unit),
			};
			_barBack.Visible = false;
			return;
		}
		_title.Text = GameText.Get(spec.Kind == TransitKind.Tunnel ? "transit.title.tunnel" : "transit.title.road");
		_title.Modulate = new Color(0.7f, 0.85f, 1f);
		var fare = spec.Cost > 0 ? " - " + GameText.Get("transit.fare", spec.Cost, unit) : "";
		_detail.Text = _countdown.ZoneId is null ? GameText.Get("transit.stand") + fare : $"{_countdown.Remaining:0.0} s{fare}";
		_barBack.Visible = true;
		_bar.Size = new Vector2(300f * _countdown.Progress, 6f);
	}

	private void BuildHud()
	{
		_layer = new CanvasLayer { Layer = 4, Visible = false };
		AddChild(_layer);
		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		box.GrowHorizontal = Control.GrowDirection.Both;
		box.GrowVertical = Control.GrowDirection.Begin;
		box.OffsetTop = -190f;
		box.OffsetBottom = -130f;
		box.OffsetLeft = -200f;
		box.OffsetRight = 200f;
		_layer.AddChild(box);
		_title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_title.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(_title);
		_detail = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_detail.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(_detail);
		_barBack = new ColorRect { CustomMinimumSize = new Vector2(300f, 6f), Color = new Color(0f, 0f, 0f, 0.5f) };
		box.AddChild(_barBack);
		_bar = new ColorRect { Color = new Color(0.55f, 0.75f, 1f) };
		_barBack.AddChild(_bar);
		var fadeLayer = new CanvasLayer { Layer = 20 };
		AddChild(fadeLayer);
		_fade = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		fadeLayer.AddChild(_fade);
	}
}
