using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// An extraction point (GDD s15): a box the player must stand in for a few seconds. Place and
/// resize in the editor; <see cref="RaidManager"/> finds every zone in the group "extraction_zones".
/// </summary>
[Tool]
public partial class ExtractionZone : Node3D
{
	[Export] public string ZoneId { get; set; } = "extract";
	[Export] public ExtractKind Kind { get; set; } = ExtractKind.AlwaysOpen;
	[Export] public float Seconds { get; set; } = 5f;
	/// <summary>Item id that must be carried (conditional zones).</summary>
	[Export] public string RequiredItemId { get; set; } = "";
	/// <summary>Banknotes charged on extraction (RequiresPayment zones).</summary>
	[Export] public int Cost { get; set; }
	[Export] public Vector3 Size { get; set; } = new(6f, 3f, 6f);
	/// <summary>Draws a faint box for the blockout; art replaces it with a real landmark later.</summary>
	[Export] public bool ShowMarker { get; set; } = true;

	/// <summary>Flare zones: seconds from firing the flare to the helicopter touching down.</summary>
	[Export] public float HeliDelayS { get; set; } = 60f;

	/// <summary>Flare zones: the call of the helicopter (always present, only used by <see cref="ExtractKind.RequiresFlare"/>).</summary>
	public HeliCall Heli { get; private set; } = new();

	private Node3D? _helicopter;

	/// <summary>
	/// Fires a signal flare from the player (M-Border Strip s4): needs a flare zone the player stands in and no earlier flare.
	/// Lights a red flare, makes noise AI investigate, and the helicopter lands after the delay. Returns whether it was fired.
	/// </summary>
	public bool TryFireFlare(Player player)
	{
		if (Kind != ExtractKind.RequiresFlare || !Contains(player.GlobalPosition) || !Heli.Fire())
			return false;
		var flare = new OmniLight3D { LightColor = new Color(1f, 0.25f, 0.2f), LightEnergy = 4f, OmniRange = 30f, Position = new Vector3(0f, 1.5f, 0f) };
		AddChild(flare);
		var spark = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f }, Position = new Vector3(0f, 1.5f, 0f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.4f, 0.3f) } };
		AddChild(spark);
		NoiseBus.Emit(GlobalPosition, 90f, "flare", this);
		player.NotifyMessage(GameText.Get("flare.fired"));
		GD.Print($"FLARE fired at {ZoneId}, helicopter in {Heli.DelayS:0} s");
		return true;
	}

	/// <summary>Dev (--flarenow): stands the player in the zone, gives a flare and fires it, so the helicopter run can be checked.</summary>
	private async void DevFlare()
	{
		await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
		if (GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault() is not { } p || ItemDb.Create("signal_flare") is not { } flare)
			return;
		p.DebugTeleport(GlobalPosition);
		p.Inventory.Inventory.TryAutoStore(flare);
		p.UseFlare(flare);
	}

	private void OnHelicopterLanded()
	{
		_helicopter = new Node3D { Name = "Helicopter" };
		var dark = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.27f, 0.22f) };
		_helicopter.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.2f, 2f, 5f) }, MaterialOverride = dark, Position = new Vector3(0f, 1.3f, 0f) });
		_helicopter.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.4f, 5f) }, MaterialOverride = dark, Position = new Vector3(0f, 1.8f, 5f) });
		_helicopter.AddChild(new MeshInstance3D { Name = "Rotor", Mesh = new BoxMesh { Size = new Vector3(9f, 0.06f, 0.35f) }, MaterialOverride = dark, Position = new Vector3(0f, 2.5f, 0f) });
		AddChild(_helicopter);
		NoiseBus.Emit(GlobalPosition, 150f, "helicopter", this);
		GD.Print($"FLARE helicopter landed at {ZoneId}");
		if (GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault() is { } p)
			p.NotifyMessage(GameText.Get("flare.landed"));
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
			return;
		Heli.Update((float)delta);
		if (_helicopter?.GetNodeOrNull<Node3D>("Rotor") is { } rotor)
			rotor.RotateY((float)delta * 25f);
	}

	public ExtractionSpec ToSpec()
		=> new(ZoneId, Kind, Seconds, string.IsNullOrEmpty(RequiredItemId) ? null : RequiredItemId, Cost);

	/// <summary>True if the world point is inside the zone box.</summary>
	public bool Contains(Vector3 worldPoint)
	{
		var local = GlobalTransform.AffineInverse() * worldPoint;
		var half = Size * 0.5f;
		return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y && Mathf.Abs(local.Z) <= half.Z;
	}

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
		{
			AddToGroup("extraction_zones");
			var args = OS.GetCmdlineUserArgs();
			if (args.FirstOrDefault(a => a.StartsWith("--helidelay="))?["--helidelay=".Length..] is { } d && float.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
				HeliDelayS = seconds; // dev: shorten the helicopter delay
			Heli = new HeliCall(HeliDelayS);
			Heli.Landed += OnHelicopterLanded;
			if (Kind == ExtractKind.RequiresFlare && args.Contains("--flarenow"))
				DevFlare();
		}
		if (!ShowMarker)
			return;
		var color = Kind == ExtractKind.AlwaysOpen ? new Color(0.2f, 0.9f, 0.3f, 0.12f) : new Color(0.9f, 0.7f, 0.2f, 0.12f);
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = Size },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = color,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}
}
