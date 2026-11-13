using Godot;

namespace ProjectD;

/// <summary>
/// A thrown signal flare that has landed (throwables spec s4): a red light over a 25 m area for 60 s, bright enough to be
/// seen from far, no damage. AI hear it ignite and go look. (Fired at a landing zone from the inventory it works as the
/// helicopter call instead; the same item.)
/// </summary>
public partial class FlareLight : Node3D
{
	private float _ageS;
	private float _lifeS = 60f;
	private OmniLight3D _light = null!;
	private readonly RandomNumberGenerator _rng = new();

	public static FlareLight Spawn(Node context, GrenadeSpec spec, Vector3 position)
	{
		var flare = new FlareLight { Name = "FlareLight", _lifeS = spec.CloudLifeS > 0f ? spec.CloudLifeS : 60f };
		context.GetTree().CurrentScene.AddChild(flare);
		flare.GlobalPosition = position + Vector3.Up * 0.15f;
		NoiseBus.Emit(position, spec.ImpactNoiseM > 0f ? spec.ImpactNoiseM : 60f, "flare", flare);
		Sfx.Play3D(flare, "flare_fire", position);
		GD.Print($"FLARE lit at {position.X:0},{position.Z:0} for {flare._lifeS:0} s");
		return flare;
	}

	public override void _Ready()
	{
		_rng.Randomize();
		_light = new OmniLight3D { LightColor = new Color(1f, 0.22f, 0.15f), LightEnergy = 4f, OmniRange = 25f, Position = new Vector3(0f, 0.3f, 0f) };
		AddChild(_light);
		AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.45f, 0.35f) },
		});
	}

	public override void _Process(double delta)
	{
		_ageS += (float)delta;
		if (_ageS >= _lifeS)
		{
			QueueFree();
			return;
		}
		var fade = Mathf.Clamp((_lifeS - _ageS) / 6f, 0f, 1f);
		_light.LightEnergy = (3.2f + _rng.Randf() * 1.6f) * fade;
	}
}
