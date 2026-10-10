using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// Shared by the player and AI: per-limb health, worn armor, hit resolution, status effects and
/// medical item use. A thin node around <see cref="BodyHealth"/>, <see cref="DamageResolver"/>,
/// <see cref="StatusEffects"/> and <see cref="MedicalController"/>.
/// </summary>
public partial class HealthComponent : Node
{
	private HealthConfig? _config;
	private BodyHealth? _body;
	private StatusEffects? _effects;
	private MedicalController? _medical;
	private Random? _random;
	private readonly List<ArmorSpec> _armorSpecs = new();

	[Export]
	public HealthConfig? Config
	{
		get => _config;
		set => _config = value;
	}

	/// <summary>0 uses a random seed; any other value makes status-effect rolls repeatable.</summary>
	[Export] public int Seed { get; set; }

	public BodyHealth Body => _body ??= CreateBody();
	public StatusEffects Effects { get { EnsureState(); return _effects!; } }
	public MedicalController Medical { get { EnsureState(); return _medical!; } }
	public List<ArmorState> Armor { get; } = new();
	public bool IsDead => Body.IsDead;

	public event Action<HitResult>? HitTaken;
	public event Action<BodyPart>? LimbDestroyed;
	public event Action<BodyPart>? Died;
	public event Action<MedicalSpec, bool>? MedicalFinished;

	private HealthConfig Cfg => _config ??= new HealthConfig();

	private Random Rng => _random ??= Seed == 0 ? new Random() : new Random(Seed);

	private BodyHealth CreateBody()
	{
		var body = new BodyHealth(Cfg.ToHealthTuning());
		body.LimbDestroyed += p =>
		{
			_effects?.OnLimbDestroyed(p);
			LimbDestroyed?.Invoke(p);
		};
		body.Died += p => Died?.Invoke(p);
		return body;
	}

	private void EnsureState()
	{
		if (_effects is not null)
			return;
		_effects = new StatusEffects(Cfg.ToEffectsTuning());
		_medical = new MedicalController(Body, _effects);
		_medical.Finished += (spec, ok) => MedicalFinished?.Invoke(spec, ok);
	}

	public void Equip(ArmorData data)
	{
		var spec = data.ToSpec();
		_armorSpecs.Add(spec);
		Armor.Add(new ArmorState(spec));
	}

	/// <summary>Full health, no effects and fresh armor again (hub restore, dummy respawn).</summary>
	public void Reset()
	{
		_body = null;
		_effects = null;
		_medical = null;
		_body = CreateBody();
		EnsureState();
		Armor.Clear();
		foreach (var spec in _armorSpecs)
			Armor.Add(new ArmorState(spec));
	}

	public override void _PhysicsProcess(double delta)
	{
		EnsureState();
		_medical!.Update((float)delta);
		_effects!.Update((float)delta, Body);
	}

	public HitResult ReceiveHit(BodyPart part, AmmoType ammo)
	{
		var result = DamageResolver.Resolve(ammo, part, Armor, Cfg.ToDamageTuning());
		return Apply(result);
	}

	/// <summary>Non-bullet damage (melee, falls). Penetration tier 1 means armor mostly stops it.</summary>
	public HitResult ReceiveDamage(BodyPart part, float damage, int penetrationTier)
	{
		var result = DamageResolver.Resolve(damage, penetrationTier, part, Armor, Cfg.ToDamageTuning());
		return Apply(result);
	}

	private HitResult Apply(HitResult result)
	{
		EnsureState();
		Body.TakeDamage(result.Part, result.Damage);
		if (!Body.IsDestroyed(result.Part))
			_effects!.OnHit(result, () => Rng.NextDouble());
		HitTaken?.Invoke(result);
		return result;
	}

	/// <summary>Starts using a medical item on the (optional) target part.</summary>
	public bool UseMedical(MedicalData item, BodyPart? target = null)
	{
		EnsureState();
		return _medical!.Start(item.ToSpec(), target);
	}
}
