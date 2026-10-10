using System;
using System.Collections.Generic;
using System.Linq;
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
	private VitalsModel? _vitals;
	private ConsumableController? _consumables;
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
	public VitalsModel Vitals { get { EnsureState(); return _vitals!; } }
	public ConsumableController Consumables { get { EnsureState(); return _consumables!; } }
	/// <summary>Set by the owner each frame so energy and hydration drain faster when active.</summary>
	public bool Sprinting { get; set; }
	public bool HeavyLoad { get; set; }
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
		_vitals = new VitalsModel(Cfg.ToVitalsTuning());
		_consumables = new ConsumableController(_vitals);
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
		_vitals = null;
		_consumables = null;
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
		_consumables!.Update((float)delta);
		var injured = _effects.Fractures.Count > 0 || _effects.BleedingParts.Any();
		var starvation = _vitals!.Update((float)delta, new VitalsActivity(Sprinting, HeavyLoad, injured));
		if (starvation > 0f && !Body.IsDead)
		{
			Body.TakeDamage(BodyPart.Thorax, starvation * (float)delta * 0.5f);
			Body.TakeDamage(BodyPart.Stomach, starvation * (float)delta * 0.5f);
		}
	}

	public HitResult ReceiveHit(BodyPart part, AmmoType ammo)
	{
		var result = DamageResolver.Resolve(ammo, part, Armor, Cfg.ToDamageTuning());
		return Apply(result);
	}

	/// <summary>Damage that skips armor completely (melee): full damage to the part, armor untouched.</summary>
	public HitResult ReceiveUnarmoredDamage(BodyPart part, float damage)
		=> Apply(DamageResolver.Unmitigated(part, damage));

	/// <summary>Non-bullet damage that armor can still stop (falls). Penetration tier 1 means armor mostly stops it.</summary>
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

	/// <summary>Starts eating or drinking; false if busy with another item.</summary>
	public bool UseConsumable(ConsumableData item)
	{
		EnsureState();
		if (_medical!.IsBusy || IsDead)
			return false;
		return _consumables!.Start(item.ToSpec());
	}

	/// <summary>Starts using a medical item on the (optional) target part.</summary>
	public bool UseMedical(MedicalData item, BodyPart? target = null)
	{
		EnsureState();
		if (_consumables!.IsBusy)
			return false;
		return _medical!.Start(item.ToSpec(), target);
	}
}
