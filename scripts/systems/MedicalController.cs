using System;
using System.Linq;

namespace ProjectD;

public enum MedicalKind { Bandage, Tourniquet, Splint, Morphine, FieldMedkit, SurgeryKit }

/// <summary>Medical item numbers (GDD s9 slice list). Placeholders to tune in data/items.</summary>
public sealed record MedicalSpec(
	MedicalKind Kind,
	float UseTimeS,
	/// <summary>Morphine: pain suppression duration.</summary>
	float EffectDurationS = 0f,
	/// <summary>Field medkit: total HP it can restore.</summary>
	float HealPool = 0f,
	float HealRatePerS = 0f,
	/// <summary>Surgery kit: restored limb max HP as a fraction of normal until the raid ends.</summary>
	float RestoredMaxHpFraction = 0.5f);

/// <summary>
/// Using one medical item at a time: a timed action that applies its effect on completion
/// (GDD s9). Pure logic.
/// </summary>
public sealed class MedicalController
{
	private readonly BodyHealth _body;
	private readonly StatusEffects _effects;
	private float _elapsed;
	private BodyPart? _target;

	public MedicalController(BodyHealth body, StatusEffects effects)
	{
		_body = body;
		_effects = effects;
	}

	public MedicalSpec? Current { get; private set; }
	public bool IsBusy => Current is not null;
	public float Progress => Current is { UseTimeS: > 0f } c ? Math.Min(_elapsed / c.UseTimeS, 1f) : 0f;

	/// <summary>Fired when an item finishes; the flag says whether the effect was applied.</summary>
	public event Action<MedicalSpec, bool>? Finished;

	/// <summary>Starts using an item; false if busy, dead, or there is nothing to treat.</summary>
	public bool Start(MedicalSpec spec, BodyPart? target = null)
	{
		if (IsBusy || _body.IsDead || !CanApply(spec, target))
			return false;
		Current = spec;
		_target = target;
		_elapsed = 0f;
		return true;
	}

	public void Cancel()
	{
		Current = null;
		_elapsed = 0f;
	}

	public void Update(float delta)
	{
		if (Current is not { } spec)
			return;
		if (_body.IsDead)
		{
			Cancel();
			return;
		}
		_elapsed += delta;
		if (_elapsed < spec.UseTimeS)
			return;
		var applied = Apply(spec, _target);
		Cancel();
		Finished?.Invoke(spec, applied);
	}

	/// <summary>Whether the item would do anything right now.</summary>
	public bool CanApply(MedicalSpec spec, BodyPart? target = null) => spec.Kind switch
	{
		MedicalKind.Bandage => _effects.HasBleed(BleedLevel.Light, target),
		MedicalKind.Tourniquet => _effects.HasBleed(BleedLevel.Heavy, target) || _effects.HasBleed(BleedLevel.Light, target),
		MedicalKind.Splint => target is null ? _effects.Fractures.Count > 0 : _effects.IsFractured(target.Value),
		MedicalKind.Morphine => true,
		MedicalKind.FieldMedkit => BodyHealth.AllParts.Any(p => !_body.IsDestroyed(p) && _body.Hp(p) < _body.MaxHp(p)),
		MedicalKind.SurgeryKit => FindDestroyed(target) is not null,
		_ => false,
	};

	private bool Apply(MedicalSpec spec, BodyPart? target)
	{
		switch (spec.Kind)
		{
			case MedicalKind.Bandage:
				return _effects.Bandage(target);
			case MedicalKind.Tourniquet:
				return _effects.Tourniquet(target);
			case MedicalKind.Splint:
				return _effects.Splint(target);
			case MedicalKind.Morphine:
				_effects.Morphine(spec.EffectDurationS);
				return true;
			case MedicalKind.FieldMedkit:
				_effects.StartHealOverTime(spec.HealPool, spec.HealRatePerS);
				return true;
			case MedicalKind.SurgeryKit:
				if (FindDestroyed(target) is not { } part)
					return false;
				return _body.Restore(part, spec.RestoredMaxHpFraction);
			default:
				return false;
		}
	}

	private BodyPart? FindDestroyed(BodyPart? target)
	{
		bool Ok(BodyPart p) => !BodyHealth.IsVital(p) && _body.IsDestroyed(p);
		if (target is { } t)
			return Ok(t) ? t : null;
		return BodyHealth.AllParts.Cast<BodyPart?>().FirstOrDefault(p => Ok(p!.Value));
	}
}
