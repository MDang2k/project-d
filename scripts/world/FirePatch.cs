using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// A patch of fire (throwables spec s4 and s5): a molotov's 3 m patch for 15 s, or thermite's 1 m patch for 20 s.
/// Anyone standing in it burns (the <see cref="BurnStatus"/> in their health), AI keep away from it, and thermite that
/// burns beside a locked door starts the door's thermite burn. A flickering light and flame meshes are the look.
/// </summary>
public partial class FirePatch : Node3D
{
	public static readonly List<FirePatch> Active = new();

	private GrenadeSpec _spec = null!;
	private float _ageS;
	private float _checkS;
	private float _crackleS;
	private OmniLight3D _light = null!;
	private readonly RandomNumberGenerator _rng = new();

	public float RadiusM => _spec.CloudRadiusM;
	public bool BurnsLocks => _spec.Kind == GrenadeKind.Thermite;

	public static FirePatch Spawn(Node context, GrenadeSpec spec, Vector3 position)
	{
		var patch = new FirePatch { _spec = spec, Name = "Fire" };
		context.GetTree().CurrentScene.AddChild(patch);
		patch.GlobalPosition = position;
		GD.Print($"FIRE {spec.Id} at {position.X:0},{position.Z:0}, radius {spec.CloudRadiusM:0.0} m, {spec.CloudLifeS:0} s");
		NoiseBus.Emit(position, spec.ImpactNoiseM > 0f ? spec.ImpactNoiseM : spec.NoiseRadiusM, "fire", patch);
		Sfx.Play3D(patch, spec.Kind == GrenadeKind.Thermite ? "flare_fire" : "glass_break", position);
		return patch;
	}

	public override void _Ready()
	{
		Active.Add(this);
		_rng.Randomize();
		_light = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2.5f, OmniRange = RadiusM * 4f + 4f, Position = new Vector3(0f, 0.8f, 0f) };
		AddChild(_light);
		var flame = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(1f, 0.5f, 0.12f, 0.75f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var count = Mathf.Clamp((int)(RadiusM * RadiusM * 2f), 4, 16);
		for (var i = 0; i < count; i++)
		{
			var a = _rng.Randf() * Mathf.Tau;
			var r = Mathf.Sqrt(_rng.Randf()) * RadiusM * 0.85f;
			var h = 0.5f + _rng.Randf() * 0.9f;
			AddChild(new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.25f + _rng.Randf() * 0.2f, Height = h, RadialSegments = 5, Rings = 1 },
				MaterialOverride = flame,
				Position = new Vector3(Mathf.Cos(a) * r, h / 2f, Mathf.Sin(a) * r),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
	}

	public override void _ExitTree() => Active.Remove(this);

	public override void _PhysicsProcess(double delta)
	{
		_ageS += (float)delta;
		if (_ageS >= _spec.CloudLifeS)
		{
			QueueFree();
			return;
		}
		_light.LightEnergy = 2f + _rng.Randf() * 1.5f; // flicker
		if ((_crackleS -= (float)delta) <= 0f)
		{
			_crackleS = 3f;
			Sfx.Play3D(this, "fire_crackle", GlobalPosition);
		}
		if ((_checkS -= (float)delta) > 0f)
			return;
		_checkS = 0.1f;
		foreach (var node in GetTree().GetNodesInGroup("perceivables").OfType<Node3D>())
		{
			var flat = new Vector2(node.GlobalPosition.X - GlobalPosition.X, node.GlobalPosition.Z - GlobalPosition.Z).Length();
			if (flat > RadiusM || Mathf.Abs(node.GlobalPosition.Y - GlobalPosition.Y) > 1.5f)
				continue;
			switch (node)
			{
				case Player p when !p.Health.IsDead:
					p.Health.Ignite();
					break;
				case AiHuman a when a.IsAlive:
					a.Health.Ignite();
					break;
			}
		}
		if (BurnsLocks)
		{
			foreach (var door in GetTree().GetNodesInGroup("locked_doors").OfType<LockedDoor>())
			{
				if (!door.IsOpen && door.GlobalPosition.DistanceTo(GlobalPosition) < 3.2f)
					door.StartBurn();
			}
		}
	}
}
