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

	/// <summary>Maximum HP factor from the Vitality skill, read when the body is built at raid start.</summary>
	public float MaxHpScale { get; set; } = 1f;
	/// <summary>Factor on hit damage after armor from the Vitality skill (not bleeding, hunger or thirst).</summary>
	public float DamageScale { get; set; } = 1f;
	/// <summary>Factor on medical use time from the First Aid skill.</summary>
	public float MedTimeScale { get; set; } = 1f;

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
	/// <summary>A bullet or a melee blow from another character landed (not falls, mines, bleeding or hunger).</summary>
	public event Action<HitResult>? EnemyHitTaken;
	public event Action<BodyPart>? LimbDestroyed;
	public event Action<BodyPart>? Died;
	public event Action<MedicalSpec, bool>? MedicalFinished;
	public event Action<ConsumableSpec>? ConsumableFinished;

	private HealthConfig Cfg => _config ??= new HealthConfig();

	private Random Rng => _random ??= Seed == 0 ? new Random() : new Random(Seed);

	private BodyHealth CreateBody()
	{
		var body = new BodyHealth(Cfg.ToHealthTuning().Scaled(MaxHpScale));
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
		_consumables.Finished += spec => ConsumableFinished?.Invoke(spec);
	}

	public void Equip(ArmorData data)
	{
		var spec = data.ToSpec();
		_armorSpecs.Add(spec);
		Armor.Add(new ArmorState(spec));
	}

	/// <summary>Replaces the worn armor (inventory changed); durability is kept per piece.</summary>
	public void SetArmor(IEnumerable<(ArmorSpec Spec, float Durability)> worn)
	{
		_armorSpecs.Clear();
		Armor.Clear();
		foreach (var (spec, durability) in worn)
		{
			_armorSpecs.Add(spec);
			Armor.Add(new ArmorState(spec, durability));
		}
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
		var applied = Apply(Scaled(result));
		EnemyHitTaken?.Invoke(applied);
		return applied;
	}

	/// <summary>Damage that skips armor completely (melee): full damage to the part, armor untouched.</summary>
	public HitResult ReceiveUnarmoredDamage(BodyPart part, float damage)
	{
		var applied = Apply(Scaled(DamageResolver.Unmitigated(part, damage)));
		EnemyHitTaken?.Invoke(applied);
		return applied;
	}

	/// <summary>Non-bullet damage that armor can still stop (falls). Penetration tier 1 means armor mostly stops it.</summary>
	public HitResult ReceiveDamage(BodyPart part, float damage, int penetrationTier)
	{
		var result = DamageResolver.Resolve(damage, penetrationTier, part, Armor, Cfg.ToDamageTuning());
		return Apply(Scaled(result));
	}

	/// <summary>The body, injuries and meters as they are now, for a transit to carry over.</summary>
	public CharacterSnapshot Capture() { EnsureState(); return CharacterSnapshot.Capture(Body, _effects!, _vitals!); }

	/// <summary>Restores a snapshot taken on the map the player came from.</summary>
	public void Restore(CharacterSnapshot snapshot) { EnsureState(); snapshot.Apply(Body, _effects!, _vitals!); }

	/// <summary>A mine blast: each hit goes through armor, then the stepped leg may break and bleed heavily.</summary>
	public void ReceiveBlast(BlastPlan plan)
	{
		foreach (var hit in plan.Hits)
			if (!IsDead)
				ReceiveDamage(hit.Part, hit.Damage, hit.PenetrationTier);
		EnsureState();
		_effects!.Inflict(plan.SteppedLeg, plan.Fracture, plan.HeavyBleed ? BleedLevel.Heavy : BleedLevel.None, Body);
	}

	/// <summary>
	/// A grenade blast (throwables spec s3): every share goes through armor like a hit of its penetration tier, with the
	/// usual bleed and fracture rolls. Listeners see one hit (the heaviest share carrying the total damage), not one per
	/// share, and it counts as enemy damage like the fragments. Returns the damage dealt.
	/// </summary>
	public float ReceiveExplosion(IReadOnlyList<BlastHit> hits)
	{
		var total = 0f;
		HitResult? heaviest = null;
		foreach (var hit in hits)
		{
			if (IsDead)
				break;
			var applied = ApplyQuiet(Scaled(DamageResolver.Resolve(hit.Damage, hit.PenetrationTier, hit.Part, Armor, Cfg.ToDamageTuning())));
			total += applied.Damage;
			if (heaviest is null || applied.Damage > heaviest.Value.Damage)
				heaviest = applied;
		}
		if (heaviest is { } h)
		{
			var combined = h with { Damage = total };
			HitTaken?.Invoke(combined);
			EnemyHitTaken?.Invoke(combined);
		}
		return total;
	}

	private HitResult Scaled(HitResult hit) => DamageScale == 1f ? hit : hit with { Damage = hit.Damage * DamageScale };

	private HitResult Apply(HitResult result)
	{
		ApplyQuiet(result);
		HitTaken?.Invoke(result);
		return result;
	}

	/// <summary>Damage and its bleed and fracture rolls, without raising <see cref="HitTaken"/>.</summary>
	private HitResult ApplyQuiet(HitResult result)
	{
		EnsureState();
		Body.TakeDamage(result.Part, result.Damage);
		if (!Body.IsDestroyed(result.Part))
			_effects!.OnHit(result, () => Rng.NextDouble());
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
		var spec = item.ToSpec();
		return _medical!.Start(MedTimeScale == 1f ? spec : spec with { UseTimeS = spec.UseTimeS * MedTimeScale }, target);
	}
}
