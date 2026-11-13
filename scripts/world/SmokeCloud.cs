using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// A smoke cloud (throwables spec s4): grows from the burst to its full radius, lasts 45 s, drifts a little and thins
/// out. It blocks sight for AI (<see cref="Blocks"/>, read by their vision) and for the player (a pile of translucent
/// puffs, and a screen veil while the camera is inside). The shape rules are the pure <see cref="CloudModel"/>.
/// </summary>
public partial class SmokeCloud : Node3D
{
	/// <summary>Every cloud alive now, for the AI vision test.</summary>
	public static readonly List<SmokeCloud> Active = new();

	private GrenadeSpec _spec = null!;
	private float _ageS;
	private Vector3 _drift;
	private Node3D _puffs = null!;
	private ColorRect _veil = null!;
	private Color _color;

	public float Radius { get; private set; }

	/// <summary>Tear gas: the same growing cloud, but it hurts the lungs instead of hiding anyone.</summary>
	public bool IsGas => _spec.Kind == GrenadeKind.Gas;

	/// <summary>How thick the tear gas is at a point, 0 none to 1 at the centre of the thickest cloud.</summary>
	public static float GasAt(Vector3 point)
	{
		var best = 0f;
		foreach (var cloud in Active)
		{
			if (cloud.IsGas)
				best = Mathf.Max(best, CloudModel.Opacity(cloud.GlobalPosition.DistanceTo(point), cloud.Radius));
		}
		return best;
	}

	/// <summary>The centre of the nearest tear gas cloud that covers a point, or null.</summary>
	public static Vector3? GasCentreAt(Vector3 point)
	{
		foreach (var cloud in Active)
		{
			if (cloud.IsGas && cloud.GlobalPosition.DistanceTo(point) < cloud.Radius)
				return cloud.GlobalPosition;
		}
		return null;
	}

	/// <summary>Whether a sight line from a watcher's eye to a body point runs through any cloud's dense core.</summary>
	public static bool Blocks(Vector3 from, Vector3 to)
	{
		foreach (var cloud in Active)
		{
			if (!cloud.IsGas && CloudModel.SegmentBlocked(from, to, cloud.GlobalPosition, cloud.Radius))
				return true;
		}
		return false;
	}

	public static SmokeCloud Spawn(Node context, GrenadeSpec spec, Vector3 position)
	{
		var cloud = new SmokeCloud { _spec = spec, Name = "Smoke" };
		context.GetTree().CurrentScene.AddChild(cloud);
		cloud.GlobalPosition = position + Vector3.Up * 0.8f;
		Sfx.Play3D(cloud, "smoke_hiss", position);
		NoiseBus.Emit(position, 20f, "smoke_pop", cloud);
		GD.Print($"SMOKE {spec.Id} at {position.X:0},{position.Z:0}, radius {spec.CloudRadiusM:0} m, {spec.CloudLifeS:0} s");
		return cloud;
	}

	public override void _Ready()
	{
		Active.Add(this);
		var rng = new RandomNumberGenerator();
		rng.Randomize();
		_color = IsGas ? new Color(0.72f, 0.82f, 0.3f) : _spec.RandomColor ? Color.FromHsv(rng.Randf(), 0.55f, 0.95f) : _spec.CloudColor ?? new Color(0.82f, 0.84f, 0.85f);
		_drift = new Vector3(rng.Randf() - 0.5f, 0f, rng.Randf() - 0.5f).Normalized() * 0.2f;
		_puffs = new Node3D();
		AddChild(_puffs);
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(_color, IsGas ? 0.22f : 0.34f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		for (var i = 0; i < 16; i++)
		{
			var dir = new Vector3(rng.Randf() - 0.5f, (rng.Randf() - 0.5f) * 0.6f, rng.Randf() - 0.5f).Normalized() * (0.1f + rng.Randf() * 0.5f);
			_puffs.AddChild(new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f, RadialSegments = 10, Rings = 5 },
				MaterialOverride = material,
				Position = dir,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
		var layer = new CanvasLayer { Layer = 4 };
		_veil = new ColorRect { Color = new Color(_color, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_veil);
		AddChild(layer);
		_veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	public override void _ExitTree() => Active.Remove(this);

	public override void _Process(double delta)
	{
		_ageS += (float)delta;
		if (!CloudModel.IsAlive(_ageS, _spec.CloudLifeS))
		{
			QueueFree();
			return;
		}
		GlobalPosition += _drift * (float)delta;
		Radius = CloudModel.Radius(_ageS, _spec.CloudRadiusM, _spec.CloudGrowS, _spec.CloudLifeS);
		_puffs.Scale = Vector3.One * Mathf.Max(0.01f, Radius * 1.6f);
		// the veil: thick when the camera is in the dense part, none outside
		var veil = 0f;
		if (!IsGas && GetViewport().GetCamera3D() is { } camera)
			veil = CloudModel.Opacity(camera.GlobalPosition.DistanceTo(GlobalPosition), Radius);
		_veil.Color = new Color(_color, Mathf.Clamp(veil * 1.25f, 0f, 0.96f));
	}
}
