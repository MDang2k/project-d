using System;

namespace ProjectD;

/// <summary>How much each skill gives at level 10 (M8a spec s2); level 1 gives nothing. Placeholders to tune.</summary>
public sealed record SkillEffectTuning(
	/// <summary>Leg stamina maximum and recovery, as a fraction added.</summary>
	float EnduranceStamina = 0.30f,
	/// <summary>Kilograms added to the weight thresholds.</summary>
	float StrengthKg = 5f,
	/// <summary>Reload and magazine-check time removed, as a fraction.</summary>
	float HandlingTimeCut = 0.25f,
	/// <summary>Level from which a magazine check shows the exact count.</summary>
	int ExactMagCheckLevel = 8,
	float RecoilCut = 0.20f,
	float MedTimeCut = 0.30f,
	float SearchTimeCut = 0.30f,
	/// <summary>Maximum HP of every body part, as a fraction added.</summary>
	float VitalityHp = 0.15f,
	/// <summary>Hit damage removed after armor, as a fraction.</summary>
	float VitalityDamageCut = 0.10f);

/// <summary>
/// What the skill levels do (M8a spec s2): multipliers the movement, weight, weapon, health, medical and loot models
/// read at the moment they use them, linear from no change at level 1 to the full effect at level 10. Pure logic.
/// </summary>
public sealed class SkillEffects
{
	private readonly SkillBook _book;
	private readonly SkillEffectTuning _t;

	public SkillEffects(SkillBook book, SkillEffectTuning? tuning = null)
	{
		_book = book;
		_t = tuning ?? new SkillEffectTuning();
	}

	/// <summary>0 at level 1 to 1 at level 10.</summary>
	private float Share(string skill) => (_book.Level(skill) - 1) / (float)(SkillSpec.MaxLevel - 1);

	/// <summary>Multiplier on the leg stamina pool and its recovery.</summary>
	public float StaminaMultiplier => 1f + _t.EnduranceStamina * Share(SkillIds.Endurance);

	/// <summary>Kilograms added to the weight thresholds.</summary>
	public float WeightBonusKg => _t.StrengthKg * Share(SkillIds.Strength);

	/// <summary>Multiplier on reload and magazine-check time.</summary>
	public float HandlingTimeMultiplier => 1f - _t.HandlingTimeCut * Share(SkillIds.WeaponHandling);

	/// <summary>Whether a magazine check shows the exact round count.</summary>
	public bool ExactMagCheck => _book.Level(SkillIds.WeaponHandling) >= _t.ExactMagCheckLevel;

	public float RecoilMultiplier => 1f - _t.RecoilCut * Share(SkillIds.RecoilControl);

	public float MedTimeMultiplier => 1f - _t.MedTimeCut * Share(SkillIds.FirstAid);

	public float SearchTimeMultiplier => 1f - _t.SearchTimeCut * Share(SkillIds.Searching);

	/// <summary>Multiplier on the maximum HP of every body part, applied when the body is built at raid start.</summary>
	public float MaxHpMultiplier => 1f + _t.VitalityHp * Share(SkillIds.Vitality);

	/// <summary>Multiplier on hit damage after armor; not used for bleed, hunger or thirst.</summary>
	public float HitDamageMultiplier => 1f - _t.VitalityDamageCut * Share(SkillIds.Vitality);

	/// <summary>Damage after Vitality.</summary>
	public float AfterVitality(float damage) => MathF.Max(0f, damage * HitDamageMultiplier);
}
