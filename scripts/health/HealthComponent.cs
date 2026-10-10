using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// Shared by the player and AI: per-limb health, worn armor and hit resolution. A thin node
/// around <see cref="BodyHealth"/> and <see cref="DamageResolver"/>.
/// </summary>
public partial class HealthComponent : Node
{
	private HealthConfig? _config;
	private BodyHealth? _body;
	private readonly List<ArmorSpec> _armorSpecs = new();

	[Export]
	public HealthConfig? Config
	{
		get => _config;
		set => _config = value;
	}

	public BodyHealth Body => _body ??= CreateBody();
	public List<ArmorState> Armor { get; } = new();
	public bool IsDead => Body.IsDead;

	public event Action<HitResult>? HitTaken;
	public event Action<BodyPart>? LimbDestroyed;
	public event Action<BodyPart>? Died;

	private HealthConfig Cfg => _config ??= new HealthConfig();

	private BodyHealth CreateBody()
	{
		var body = new BodyHealth(Cfg.ToHealthTuning());
		body.LimbDestroyed += p => LimbDestroyed?.Invoke(p);
		body.Died += p => Died?.Invoke(p);
		return body;
	}

	public void Equip(ArmorData data)
	{
		var spec = data.ToSpec();
		_armorSpecs.Add(spec);
		Armor.Add(new ArmorState(spec));
	}

	/// <summary>Full health and fresh armor again (hub restore, dummy respawn).</summary>
	public void Reset()
	{
		_body = CreateBody();
		Armor.Clear();
		foreach (var spec in _armorSpecs)
			Armor.Add(new ArmorState(spec));
	}

	public HitResult ReceiveHit(BodyPart part, AmmoType ammo)
	{
		var result = DamageResolver.Resolve(ammo, part, Armor, Cfg.ToDamageTuning());
		Body.TakeDamage(part, result.Damage);
		HitTaken?.Invoke(result);
		return result;
	}

	/// <summary>Non-bullet damage (melee, falls). Penetration tier 1 means armor mostly stops it.</summary>
	public HitResult ReceiveDamage(BodyPart part, float damage, int penetrationTier)
	{
		var result = DamageResolver.Resolve(damage, penetrationTier, part, Armor, Cfg.ToDamageTuning());
		Body.TakeDamage(part, result.Damage);
		HitTaken?.Invoke(result);
		return result;
	}
}
