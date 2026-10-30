using System;
using Godot;

namespace ProjectD;

/// <summary>Vision and hearing numbers for one AI (GDD s16). Filled from the actor stats (<c>ActorTuning</c>).</summary>
public sealed record PerceptionTuning(
	/// <summary>Sight range in metres on a standing, moderately moving, fully lit target.</summary>
	float ViewDistance,
	/// <summary>Full horizontal angle of the main vision cone, degrees.</summary>
	float FovDeg,
	/// <summary>Wider close-range cone (catching movement at the edge of vision), degrees.</summary>
	float PeripheralDeg,
	float PeripheralDistance,
	/// <summary>Visibility multipliers of a crouched and a prone target.</summary>
	float CrouchVisibility,
	float ProneVisibility,
	/// <summary>Visibility multiplier of a motionless target; a sprinting one uses <see cref="MovingVisibility"/>.</summary>
	float StillVisibility,
	float MovingVisibility,
	/// <summary>Scales how far noises are heard (1 = the noise's own radius).</summary>
	float HearingMultiplier,
	/// <summary>Seconds of clear close-range view before becoming fully alert.</summary>
	float NoticeSeconds,
	/// <summary>Awareness at which the AI becomes suspicious and goes to investigate.</summary>
	float SuspicionThreshold,
	/// <summary>Seconds awareness is held after the last cue before it starts fading.</summary>
	float HoldSeconds,
	float DecayPerS)
{
	public static PerceptionTuning Default => new(
		ViewDistance: 80f, FovDeg: 110f, PeripheralDeg: 200f, PeripheralDistance: 8f,
		CrouchVisibility: 0.65f, ProneVisibility: 0.4f, StillVisibility: 0.8f, MovingVisibility: 1.25f,
		HearingMultiplier: 1f, NoticeSeconds: 1.2f, SuspicionThreshold: 0.35f,
		HoldSeconds: 3f, DecayPerS: 0.15f);
}

public enum AwarenessLevel { Unaware, Suspicious, Alert }

/// <summary>How a potential target looks to an observer this tick.</summary>
public readonly record struct TargetProfile(Stance Stance, float SpeedFraction);

/// <summary>
/// One AI's awareness meter. Vision and hearing feed it; it rises on cues, holds, then fades.
/// Pure logic: the AI node does the raycasts and passes numbers in.
/// </summary>
public sealed class PerceptionModel
{
	private PerceptionTuning _t;
	private bool _cueThisTick;
	private bool _alert;

	public PerceptionModel(PerceptionTuning tuning) => _t = tuning;

	/// <summary>Replaced when the actor's stats change; awareness already built up is kept.</summary>
	public PerceptionTuning Tuning { get => _t; set => _t = value; }

	/// <summary>0 unaware .. 1 fully aware of where the cue came from.</summary>
	public float Awareness { get; private set; }

	/// <summary>Last place something was seen or heard; null once awareness has faded.</summary>
	public Vector3? InterestPoint { get; private set; }

	/// <summary>True while the target was in view during the last tick.</summary>
	public bool SeesTarget { get; private set; }

	public float SecondsSinceCue { get; private set; }

	public AwarenessLevel Level => _alert
		? AwarenessLevel.Alert
		: Awareness >= _t.SuspicionThreshold ? AwarenessLevel.Suspicious : AwarenessLevel.Unaware;

	/// <summary>
	/// 0..1 sight strength. Zero outside the cone or range, blocked, or too dark; closer is stronger.
	/// angleDeg is the off-axis angle (0 straight ahead), visibleFraction the unblocked share of the target.
	/// </summary>
	public static float VisionStrength(PerceptionTuning t, float distance, float angleDeg, TargetProfile target,
		float visibleFraction, float light = 1f)
	{
		if (visibleFraction <= 0f || light <= 0f)
			return 0f;
		var stance = target.Stance switch
		{
			Stance.Crouch => t.CrouchVisibility,
			Stance.Prone => t.ProneVisibility,
			_ => 1f,
		};
		var move = Mathf.Lerp(t.StillVisibility, t.MovingVisibility, Mathf.Clamp(target.SpeedFraction, 0f, 1f));
		var effective = t.ViewDistance * stance * move * Mathf.Clamp(light, 0.1f, 1f);
		if (effective <= 0.01f || distance > effective)
			return 0f;
		var closeness = 1f - distance / effective;
		var strength = visibleFraction * (0.2f + 0.8f * closeness);
		if (angleDeg <= t.FovDeg * 0.5f)
			return strength;
		if (angleDeg <= t.PeripheralDeg * 0.5f && distance <= Math.Min(effective, t.PeripheralDistance))
			return strength * 0.6f;
		return 0f;
	}

	/// <summary>0..1 how clearly a noise of the given radius is heard from the given distance.</summary>
	public static float Loudness(PerceptionTuning t, float distance, float noiseRadius)
	{
		var reach = noiseRadius * t.HearingMultiplier;
		if (reach <= 0.01f || distance >= reach)
			return 0f;
		return 1f - distance / reach;
	}

	/// <summary>Target seen this tick: awareness climbs faster the stronger the sighting.</summary>
	public void See(float strength, Vector3 position, float delta)
	{
		if (strength <= 0f)
			return;
		var gain = (0.25f + 0.75f * Math.Clamp(strength, 0f, 1f)) / Math.Max(0.05f, _t.NoticeSeconds);
		Cue(Awareness + gain * delta, position);
		SeesTarget = true;
	}

	/// <summary>A noise reached the AI: a loud one makes it fully aware of the spot at once.</summary>
	public void Hear(float loudness, Vector3 position)
	{
		if (loudness <= 0f)
			return;
		var heard = _t.SuspicionThreshold + Math.Clamp(loudness, 0f, 1f) * (1f - _t.SuspicionThreshold);
		Cue(Math.Max(Awareness, heard), position);
	}

	private void Cue(float awareness, Vector3 position)
	{
		Awareness = Math.Clamp(awareness, 0f, 1f);
		InterestPoint = position;
		SecondsSinceCue = 0f;
		_cueThisTick = true;
		if (Awareness >= 1f)
			_alert = true;
	}

	/// <summary>Closes a perception tick: awareness holds, then fades when nothing new was noticed.</summary>
	public void Tick(float delta)
	{
		if (_cueThisTick)
		{
			_cueThisTick = false;
			return;
		}
		SeesTarget = false;
		SecondsSinceCue += delta;
		if (SecondsSinceCue > _t.HoldSeconds)
			Awareness = Math.Max(0f, Awareness - _t.DecayPerS * delta);
		// Alert relaxes to suspicious halfway between fully aware and the suspicion threshold.
		if (_alert && Awareness < (1f + _t.SuspicionThreshold) * 0.5f)
			_alert = false;
		if (Awareness <= 0f)
			InterestPoint = null;
	}

	/// <summary>Forgets everything (AI reset or respawn).</summary>
	public void Reset()
	{
		Awareness = 0f;
		InterestPoint = null;
		SeesTarget = false;
		SecondsSinceCue = 0f;
		_alert = false;
		_cueThisTick = false;
	}
}
