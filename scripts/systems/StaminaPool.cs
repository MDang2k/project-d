using System;

namespace ProjectD;

/// <summary>
/// One stamina pool (legs or arms, GDD s6). Recovery pauses briefly after spending;
/// after hitting zero the pool is locked until it refills to ResumeThreshold.
/// </summary>
public sealed class StaminaPool
{
	public float MaxValue { get; }
	public float Value { get; private set; }
	public float ResumeThreshold { get; }
	public float RecoveryDelayS { get; }
	public bool IsExhausted { get; private set; }

	public event Action? Depleted;
	public event Action? Recovered;

	private float _delayLeft;

	public StaminaPool(float maxValue, float resumeThreshold, float recoveryDelayS)
	{
		MaxValue = maxValue;
		Value = maxValue;
		ResumeThreshold = resumeThreshold;
		RecoveryDelayS = recoveryDelayS;
	}

	public void Spend(float amount)
	{
		if (amount <= 0f)
			return;
		Value = MathF.Max(0f, Value - amount);
		_delayLeft = RecoveryDelayS;
		if (Value <= 0f && !IsExhausted)
		{
			IsExhausted = true;
			Depleted?.Invoke();
		}
	}

	/// <summary>Call every frame; recoveryRate is units per second (already scaled by weight tier).</summary>
	public void Tick(float delta, float recoveryRate)
	{
		if (_delayLeft > 0f)
		{
			_delayLeft = MathF.Max(0f, _delayLeft - delta);
			return;
		}
		Value = MathF.Min(MaxValue, Value + recoveryRate * delta);
		if (IsExhausted && Value >= ResumeThreshold)
		{
			IsExhausted = false;
			Recovered?.Invoke();
		}
	}

	public float Fraction => Value / MaxValue;
}
