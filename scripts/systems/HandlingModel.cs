using System;
using Godot;

namespace ProjectD;

/// <summary>Global weapon-handling numbers (GDD s7). Placeholders to tune in playtest.</summary>
public sealed record HandlingTuning(
	float CrouchSwayMult,
	float ProneSwayMult,
	/// <summary>Extra sway factor at empty arm stamina (amplitude x (1 + gain * (1 - fraction))).</summary>
	float ExhaustionSwayGain,
	/// <summary>Extra sway while moving at sprint speed.</summary>
	float MoveSwayGain,
	float AdsSwayMult,
	float BreathHoldSwayMult,
	/// <summary>Sway growth per kg of weapon weight.</summary>
	float WeightSwayPerKg,
	float CrouchRecoilMult,
	float ProneRecoilMult,
	float AdsRecoilMult,
	float RestedSwayMult = 0.15f,
	float BipodSwayMult = 0.05f,
	float RestedRecoilMult = 0.5f,
	float BipodRecoilMult = 0.3f)
{
	public static HandlingTuning Default => new(
		CrouchSwayMult: 0.75f, ProneSwayMult: 0.5f, ExhaustionSwayGain: 1.5f, MoveSwayGain: 1.2f,
		AdsSwayMult: 0.6f, BreathHoldSwayMult: 0.1f, WeightSwayPerKg: 0.04f,
		CrouchRecoilMult: 0.85f, ProneRecoilMult: 0.65f, AdsRecoilMult: 0.8f);
}

/// <summary>Per-weapon recoil numbers, degrees and seconds.</summary>
public sealed record RecoilTuning(
	float VerticalDeg,
	float HorizontalDeg,
	/// <summary>Share of the climb that settles back after release.</summary>
	float SettleFraction,
	float SettleTimeS,
	/// <summary>Pause after the last shot before settling starts.</summary>
	float ReleaseDelayS);

public readonly record struct HandlingContext(
	Stance Stance,
	float WeaponWeightKg,
	float ArmsFraction,
	/// <summary>Current speed divided by sprint speed, 0..1.</summary>
	float SpeedFraction,
	float AdsAmount,
	bool HoldingBreath,
	/// <summary>0..1 blend toward resting on a ledge.</summary>
	float RestAmount = 0f,
	bool BipodDeployed = false);

/// <summary>Sway amplitude and recoil multipliers from handling context. Pure logic.</summary>
public static class HandlingModel
{
	/// <summary>Weapon sway amplitude in degrees (peak yaw/pitch).</summary>
	public static float SwayAmplitude(HandlingTuning t, float weaponBaseDeg, in HandlingContext c)
	{
		var stance = c.Stance switch
		{
			Stance.Prone => t.ProneSwayMult,
			Stance.Crouch => t.CrouchSwayMult,
			_ => 1f,
		};
		var exhaustion = 1f + t.ExhaustionSwayGain * (1f - Mathf.Clamp(c.ArmsFraction, 0f, 1f));
		var movement = 1f + t.MoveSwayGain * Mathf.Clamp(c.SpeedFraction, 0f, 1f);
		var weight = 1f + t.WeightSwayPerKg * c.WeaponWeightKg;
		var ads = Mathf.Lerp(1f, t.AdsSwayMult, Mathf.Clamp(c.AdsAmount, 0f, 1f));
		var breath = c.HoldingBreath ? t.BreathHoldSwayMult : 1f;
		var support = c.BipodDeployed ? t.BipodSwayMult : Mathf.Lerp(1f, t.RestedSwayMult, Mathf.Clamp(c.RestAmount, 0f, 1f));
		return weaponBaseDeg * stance * exhaustion * movement * weight * ads * breath * support;
	}

	/// <summary>
	/// Un-aimed spread cone in degrees: the base cone scaled by the weapon's hip stability and closed while
	/// aiming down sights (weapon modding v2 s4).
	/// </summary>
	public static float HipSpreadDeg(float baseDeg, float dispersionMult, float adsAmount)
		=> baseDeg * dispersionMult * (1f - Mathf.Clamp(adsAmount, 0f, 1f));

	public static float RecoilMultiplier(HandlingTuning t, Stance stance, float adsAmount,
		float restAmount = 0f, bool bipodDeployed = false)
	{
		var s = stance switch
		{
			Stance.Prone => t.ProneRecoilMult,
			Stance.Crouch => t.CrouchRecoilMult,
			_ => 1f,
		};
		var support = bipodDeployed ? t.BipodRecoilMult : Mathf.Lerp(1f, t.RestedRecoilMult, Mathf.Clamp(restAmount, 0f, 1f));
		return s * Mathf.Lerp(1f, t.AdsRecoilMult, Mathf.Clamp(adsAmount, 0f, 1f)) * support;
	}
}

/// <summary>Smooth two-axis weapon sway from summed sines (deterministic, no allocation).</summary>
public sealed class SwayModel
{
	private float _t;

	/// <summary>Returns (yaw, pitch) in degrees.</summary>
	public Vector2 Update(float delta, float amplitudeDeg)
	{
		_t += delta;
		var yaw = MathF.Sin(_t * 1.3f + 0.7f) + 0.5f * MathF.Sin(_t * 2.9f);
		var pitch = MathF.Sin(_t * 1.1f) + 0.5f * MathF.Sin(_t * 3.7f + 1.2f);
		return new Vector2(yaw, pitch) * (amplitudeDeg / 1.5f);
	}
}

/// <summary>
/// Accumulated view kick: vertical climb plus random horizontal drift. No recovery during a burst. After
/// the trigger is released the muzzle settles back a fraction of the climb; the player drags the rest
/// down with <see cref="Pull"/> (weapon modding v2 s5).
/// </summary>
public sealed class RecoilModel
{
	private float _delayLeft;
	private Vector2 _settleLeft;

	/// <summary>x = yaw, y = pitch (positive = up), degrees.</summary>
	public Vector2 Offset { get; private set; }

	/// <param name="random">Horizontal randomness in [-1, 1], injected so tests are deterministic.</param>
	public void Kick(RecoilTuning tuning, float multiplier, float random)
	{
		Offset += new Vector2(tuning.HorizontalDeg * random, tuning.VerticalDeg) * multiplier;
		_delayLeft = tuning.ReleaseDelayS;
		_settleLeft = Vector2.Zero; // armed again once the delay passes
	}

	public void Update(float delta, RecoilTuning tuning)
	{
		if (_delayLeft > 0f)
		{
			_delayLeft = MathF.Max(0f, _delayLeft - delta);
			if (_delayLeft > 0f)
				return;
			_settleLeft = Offset * tuning.SettleFraction;
		}
		if (_settleLeft == Vector2.Zero)
			return;
		var step = tuning.SettleTimeS > 0.001f ? MathF.Min(1f, delta / tuning.SettleTimeS) : 1f;
		var take = _settleLeft * step;
		_settleLeft -= take;
		Offset -= take;
	}

	/// <summary>
	/// The player moves the view by <paramref name="deg"/> (x = yaw, y = pitch, positive up). Movement that opposes
	/// the accumulated kick pays it down first; the unused rest is returned for the normal look.
	/// </summary>
	public Vector2 Pull(Vector2 deg)
	{
		static float One(float offset, float move, out float rest)
		{
			if (offset > 0f && move < 0f)
			{
				var used = MathF.Min(offset, -move);
				rest = move + used;
				return offset - used;
			}
			if (offset < 0f && move > 0f)
			{
				var used = MathF.Min(-offset, move);
				rest = move - used;
				return offset + used;
			}
			rest = move;
			return offset;
		}
		var x = One(Offset.X, deg.X, out var restX);
		var y = One(Offset.Y, deg.Y, out var restY);
		Offset = new Vector2(x, y);
		_settleLeft = new Vector2(Clip(_settleLeft.X, x), Clip(_settleLeft.Y, y));
		return new Vector2(restX, restY);
	}

	/// <summary>Pending settle can never exceed what is left of the offset in the same direction.</summary>
	private static float Clip(float settle, float offset)
		=> MathF.Sign(settle) == MathF.Sign(offset) ? MathF.Sign(settle) * MathF.Min(MathF.Abs(settle), MathF.Abs(offset)) : 0f;
}

/// <summary>Aim-down-sights blend 0..1 over the weapon's ADS time.</summary>
public sealed class AimModel
{
	public float Amount { get; private set; }

	public void Update(bool wantsAds, float delta, float adsTimeS)
	{
		var step = adsTimeS > 0.001f ? delta / adsTimeS : 1f;
		Amount = Mathf.Clamp(Amount + (wantsAds ? step : -step), 0f, 1f);
	}
}

/// <summary>Breath hold drains the arm pool and cannot be used while it is exhausted.</summary>
public static class BreathHold
{
	public static bool Update(bool wantsHold, StaminaPool arms, float drainPerS, float delta)
	{
		if (!wantsHold || arms.IsExhausted)
			return false;
		arms.Spend(drainPerS * delta);
		return !arms.IsExhausted;
	}
}
