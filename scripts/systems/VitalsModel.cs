using System;

namespace ProjectD;

/// <summary>Energy and hydration numbers (GDD s9). Meters are 0-100; placeholders to tune.</summary>
public sealed record VitalsTuning(
	float EnergyDrainPerS,
	float HydrationDrainPerS,
	float SprintDrainMultiplier,
	float HeavyLoadDrainMultiplier,
	float InjuredDrainMultiplier,
	/// <summary>Meters below this fraction of 100 start to hurt.</summary>
	float LowThreshold,
	/// <summary>Stamina recovery multiplier when a meter is empty (scaled up to 1 at the threshold).</summary>
	float EmptyRecoveryMultiplier,
	/// <summary>Sway multiplier when hydration is empty (scaled up to 1 at the threshold).</summary>
	float EmptyHydrationSwayMultiplier,
	/// <summary>HP per second lost, split between thorax and stomach, while a meter is at 0.</summary>
	float StarvationDamagePerS)
{
	public static VitalsTuning Default => new(
		EnergyDrainPerS: 0.03f, HydrationDrainPerS: 0.04f, SprintDrainMultiplier: 4f,
		HeavyLoadDrainMultiplier: 1.5f, InjuredDrainMultiplier: 2f, LowThreshold: 25f,
		EmptyRecoveryMultiplier: 0.5f, EmptyHydrationSwayMultiplier: 1.4f, StarvationDamagePerS: 0.15f);
}

/// <summary>What the character is doing, used to scale the drain.</summary>
public readonly record struct VitalsActivity(bool Sprinting, bool HeavyLoad, bool Injured);

/// <summary>Energy and hydration (GDD s9). Pure logic.</summary>
public sealed class VitalsModel
{
	private readonly VitalsTuning _t;

	public VitalsModel(VitalsTuning tuning) => _t = tuning;

	public float Energy { get; private set; } = 100f;
	public float Hydration { get; private set; } = 100f;

	public bool Starving => Energy <= 0f || Hydration <= 0f;

	/// <summary>Both meters back to full (between raids).</summary>
	public void Reset()
	{
		Energy = 100f;
		Hydration = 100f;
	}

	public void Set(float energy, float hydration)
	{
		Energy = Math.Clamp(energy, 0f, 100f);
		Hydration = Math.Clamp(hydration, 0f, 100f);
	}

	public void Eat(float energy, float hydration = 0f)
	{
		Energy = Math.Min(100f, Energy + energy);
		Hydration = Math.Min(100f, Hydration + hydration);
	}

	/// <returns>HP per second to take from thorax/stomach (0 unless a meter is empty).</returns>
	public float Update(float delta, in VitalsActivity activity)
	{
		var mult = 1f;
		if (activity.Sprinting)
			mult *= _t.SprintDrainMultiplier;
		if (activity.HeavyLoad)
			mult *= _t.HeavyLoadDrainMultiplier;
		if (activity.Injured)
			mult *= _t.InjuredDrainMultiplier;
		Energy = Math.Max(0f, Energy - _t.EnergyDrainPerS * mult * delta);
		Hydration = Math.Max(0f, Hydration - _t.HydrationDrainPerS * mult * delta);
		return Starving ? _t.StarvationDamagePerS : 0f;
	}

	/// <summary>Low energy and low hydration both slow stamina recovery (the worse one counts).</summary>
	public float StaminaRecoveryMultiplier
		=> Math.Min(Scale(Energy, _t.EmptyRecoveryMultiplier), Scale(Hydration, _t.EmptyRecoveryMultiplier));

	/// <summary>Low hydration increases sway.</summary>
	public float SwayMultiplier => Scale(Hydration, _t.EmptyHydrationSwayMultiplier);

	/// <summary>1 above the threshold, blending to atEmpty at 0.</summary>
	private float Scale(float meter, float atEmpty)
	{
		if (meter >= _t.LowThreshold)
			return 1f;
		return atEmpty + (1f - atEmpty) * (meter / _t.LowThreshold);
	}
}

/// <summary>Food or drink item numbers. Placeholders to tune in data/items.</summary>
public sealed record ConsumableSpec(string Id, float UseTimeS, float Energy, float Hydration);

/// <summary>Eating and drinking as a timed action, one item at a time. Pure logic.</summary>
public sealed class ConsumableController
{
	private readonly VitalsModel _vitals;
	private float _elapsed;

	public ConsumableController(VitalsModel vitals) => _vitals = vitals;

	public ConsumableSpec? Current { get; private set; }
	public bool IsBusy => Current is not null;
	public float Progress => Current is { UseTimeS: > 0f } c ? Math.Min(_elapsed / c.UseTimeS, 1f) : 0f;

	public event Action<ConsumableSpec>? Finished;

	public bool Start(ConsumableSpec spec)
	{
		if (IsBusy)
			return false;
		Current = spec;
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
		_elapsed += delta;
		if (_elapsed < spec.UseTimeS)
			return;
		_vitals.Eat(spec.Energy, spec.Hydration);
		Cancel();
		Finished?.Invoke(spec);
	}
}
