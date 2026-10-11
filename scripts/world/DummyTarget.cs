using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ProjectD;

/// <summary>
/// Humanoid test target: placeholder body, the shared hitbox rig, a HealthComponent, worn armor
/// and a debug label with per-limb HP. Resets a few seconds after dying.
/// </summary>
public partial class DummyTarget : Node3D
{
	private const float RespawnS = 3f;

	private readonly Dictionary<BodyPart, MeshInstance3D> _meshes = new();
	private Label3D _label = null!;
	private string _title = "Dummy";
	private float _deadTimer;
	private string[] _armorIds = System.Array.Empty<string>();

	public HealthComponent Health { get; private set; } = null!;

	public static DummyTarget Create(string title, HealthConfig? config, params ArmorData[] armor)
	{
		var d = new DummyTarget { _title = title, _armorIds = armor.Select(a => a.Id).ToArray() };
		d.Health = new HealthComponent { Name = "Health", Config = config };
		d.AddChild(d.Health);
		foreach (var a in armor)
			d.Health.Equip(a);
		return d;
	}

	public override void _Ready()
	{
		HitboxRig.Build(this, Health);
		Mesh(BodyPart.Head, new SphereMesh { Radius = 0.11f, Height = 0.22f }, new Vector3(0f, 1.65f, 0f));
		Mesh(BodyPart.Thorax, new BoxMesh { Size = new Vector3(0.36f, 0.34f, 0.2f) }, new Vector3(0f, 1.30f, 0f));
		Mesh(BodyPart.Stomach, new BoxMesh { Size = new Vector3(0.32f, 0.24f, 0.18f) }, new Vector3(0f, 1.02f, 0f));
		Mesh(BodyPart.LeftArm, new CapsuleMesh { Radius = 0.05f, Height = 0.6f }, new Vector3(-0.27f, 1.28f, 0f));
		Mesh(BodyPart.RightArm, new CapsuleMesh { Radius = 0.05f, Height = 0.6f }, new Vector3(0.27f, 1.28f, 0f));
		Mesh(BodyPart.LeftLeg, new CapsuleMesh { Radius = 0.08f, Height = 0.86f }, new Vector3(-0.1f, 0.44f, 0f));
		Mesh(BodyPart.RightLeg, new CapsuleMesh { Radius = 0.08f, Height = 0.86f }, new Vector3(0.1f, 0.44f, 0f));
		_label = new Label3D
		{
			Position = new Vector3(0f, 2.15f, 0f),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = 32,
			// Fixed screen size so the readout stays legible when standing next to the dummy.
			FixedSize = true,
			PixelSize = 0.0007f,
			NoDepthTest = true,
			Modulate = new Color(1f, 1f, 0.7f),
		};
		AddChild(_label);
		Health.Died += _ =>
		{
			_deadTimer = RespawnS;
			SpawnCorpse();
		};
	}

	/// <summary>A lootable body appears beside the dummy: its worn armor plus a rolled scav table.</summary>
	private void SpawnCorpse()
		=> CorpseSpawner.Spawn(GetParent(), GlobalPosition + new Vector3(0.9f, 0f, 0f), _armorIds);

	private void Mesh(BodyPart part, Godot.Mesh mesh, Vector3 position)
	{
		var m = new MeshInstance3D
		{
			Mesh = mesh,
			Position = position,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.5f, 0.4f) },
		};
		AddChild(m);
		_meshes[part] = m;
	}

	public override void _Process(double delta)
	{
		if (Health.IsDead)
		{
			_deadTimer -= (float)delta;
			if (_deadTimer <= 0f)
				Health.Reset();
		}
		var body = Health.Body;
		foreach (var (part, mesh) in _meshes)
		{
			var fraction = body.MaxHp(part) > 0f ? body.Hp(part) / body.MaxHp(part) : 0f;
			var mat = (StandardMaterial3D)mesh.MaterialOverride;
			mat.AlbedoColor = Health.IsDead
				? new Color(0.15f, 0.05f, 0.05f)
				: new Color(0.45f, 0.5f, 0.4f).Lerp(new Color(0.6f, 0.1f, 0.1f), 1f - fraction);
		}
		_label.Text = Describe(body);
	}

	private string Describe(BodyHealth b)
	{
		var sb = new StringBuilder(_title);
		if (Health.IsDead)
			return sb.Append("  DEAD").ToString();
		sb.Append($"\nH {b.Hp(BodyPart.Head):0}  T {b.Hp(BodyPart.Thorax):0}  S {b.Hp(BodyPart.Stomach):0}");
		sb.Append($"\nLA {b.Hp(BodyPart.LeftArm):0}  RA {b.Hp(BodyPart.RightArm):0}");
		sb.Append($"\nLL {b.Hp(BodyPart.LeftLeg):0}  RL {b.Hp(BodyPart.RightLeg):0}");
		var fx = Health.Effects;
		foreach (var part in fx.BleedingParts)
			sb.Append($"\n{fx.Bleed(part)} bleed ({part})");
		foreach (var part in fx.Fractures)
			sb.Append($"\nfracture ({part})");
		foreach (var a in Health.Armor)
			sb.Append($"\n{a.Spec.Id} T{a.EffectiveTier} {a.Durability:0}/{a.Spec.MaxDurability:0}");
		return sb.ToString();
	}
}
