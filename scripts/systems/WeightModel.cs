namespace ProjectD;

public enum WeightTier { Light, Moderate, Heavy, Overloaded }

/// <summary>Weight tier numbers (GDD s6). Arrays are indexed by <see cref="WeightTier"/>.</summary>
public sealed record WeightTuning(
	float[] ThresholdsKg,
	float[] SpeedMult,
	float[] AccelMult,
	float[] TurnMult,
	float[] StaminaDrainMult,
	float[] StaminaRecoveryMult,
	int SprintMaxTier)
{
	public static WeightTuning Default => new(
		new[] { 20f, 35f, 50f },
		new[] { 1f, 0.9f, 0.7f, 0.3f },
		new[] { 1f, 0.8f, 0.55f, 0.35f },
		new[] { 1f, 0.9f, 0.75f, 0.5f },
		new[] { 1f, 1.4f, 1.8f, 2.5f },
		new[] { 1f, 0.8f, 0.6f, 0.4f },
		1);
}

/// <summary>Maps carried weight to a mobility tier and its multipliers. Pure logic.</summary>
public sealed class WeightModel
{
	private readonly WeightTuning _t;

	public WeightModel(WeightTuning tuning) => _t = tuning;

	/// <summary>strengthBonusKg raises every threshold (Strength skill).</summary>
	public WeightTier TierFor(float weightKg, float strengthBonusKg = 0f)
	{
		var tier = 0;
		foreach (var threshold in _t.ThresholdsKg)
		{
			if (weightKg >= threshold + strengthBonusKg)
				tier++;
		}
		return (WeightTier)tier;
	}

	public float SpeedMult(WeightTier tier) => _t.SpeedMult[(int)tier];
	public float AccelMult(WeightTier tier) => _t.AccelMult[(int)tier];
	public float TurnMult(WeightTier tier) => _t.TurnMult[(int)tier];
	public float StaminaDrainMult(WeightTier tier) => _t.StaminaDrainMult[(int)tier];
	public float StaminaRecoveryMult(WeightTier tier) => _t.StaminaRecoveryMult[(int)tier];
	public bool CanSprint(WeightTier tier) => (int)tier <= _t.SprintMaxTier;
}
