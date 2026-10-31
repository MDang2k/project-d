using System;

namespace ProjectD;

public enum TransitionKind
{
	None,
	/// <summary>Stand and crouch changes, including the granular steps: can be reversed.</summary>
	Change,
	/// <summary>Stand or crouch to prone: uninterruptible, hands off the weapon.</summary>
	ProneEntry,
	/// <summary>Prone to crouch or stand: uninterruptible, hands off the weapon.</summary>
	GetUp,
}

/// <summary>Durations and cues of stance changes (animation spec s1.6). Placeholders to tune.</summary>
public sealed record StanceTransitionTuning(
	float StandToCrouchS = 0.35f,
	float CrouchToStandS = 0.40f,
	/// <summary>One notch of the granular crouch (C and the wheel).</summary>
	float StepS = 0.15f,
	float ToProneS = 1.25f,
	float GetUpS = 1.30f,
	/// <summary>After a locked transition the weapon takes this long to come back to the view.</summary>
	float RecenterS = 0.2f,
	/// <summary>How far through a get-up the body counts as standing for perception and hit zones.</summary>
	float StandingProfileAt = 0.5f,
	/// <summary>Part of the prone entry spent letting go of the weapon.</summary>
	float HandsOffFraction = 0.15f,
	/// <summary>The camera dips this far below the final prone eye height at the slam.</summary>
	float SlamSettleM = 0.03f,
	float SlamPitchDeg = 18f,
	float GetUpPitchDeg = 12f);

/// <summary>
/// Stance changes as physical, time-gated animations (animation spec s1.6). Going prone and getting up cannot be
/// cancelled; during them stance input, movement, firing, ADS and every weapon action are locked, the hands let go of
/// the weapon, and the weapon returns to the view only after the animation. Stand and crouch changes are quick and can
/// be reversed. Pure logic; the player and the AI read the locks and the camera cues from here.
/// </summary>
public sealed class StanceTransition
{
	private readonly StanceTransitionTuning _t;
	private float _duration = 1f;
	private float _recenterLeft;

	public StanceTransition(StanceTransitionTuning? tuning = null) => _t = tuning ?? new StanceTransitionTuning();

	public TransitionKind Kind { get; private set; }
	public Stance From { get; private set; } = Stance.Stand;
	public Stance To { get; private set; } = Stance.Stand;

	/// <summary>0 at the start .. 1 at the end of the current transition.</summary>
	public float Progress { get; private set; }

	public bool Active => Kind != TransitionKind.None;

	/// <summary>Prone entry or get-up in progress: nothing can interrupt it.</summary>
	public bool Locked => Kind is TransitionKind.ProneEntry or TransitionKind.GetUp;

	/// <summary>Locked, or the weapon is still coming back to the view.</summary>
	private bool WeaponAway => Locked || _recenterLeft > 0f;

	public bool BlocksFire => WeaponAway;
	public bool BlocksAds => WeaponAway;
	public bool BlocksWeaponActions => WeaponAway;
	public bool BlocksMovement => Locked;
	public bool BlocksStanceInput => Locked;

	/// <summary>0 while the weapon holds its carry pose, 1 once it follows the view again.</summary>
	public float WeaponFollowsView => Locked ? 0f : _recenterLeft > 0f ? 1f - _recenterLeft / Math.Max(0.01f, _t.RecenterS) : 1f;

	/// <summary>1 with both hands on the weapon, 0 with them on the ground.</summary>
	public float GripWeight => Locked
		? Math.Clamp(1f - Progress / Math.Max(0.01f, _t.HandsOffFraction), 0f, 1f)
		: WeaponFollowsView;

	public static float DurationS(StanceTransitionTuning t, Stance from, Stance to)
	{
		if (to == Stance.Prone && from != Stance.Prone)
			return t.ToProneS;
		if (from == Stance.Prone && to != Stance.Prone)
			return t.GetUpS;
		return from == Stance.Stand ? t.StandToCrouchS : t.CrouchToStandS;
	}

	/// <summary>
	/// Starts a change of stance. False when a locked transition is running (the input is ignored) or nothing changes.
	/// <paramref name="speed"/> above 1 shortens it (a light, fit character).
	/// </summary>
	public bool TryStart(Stance from, Stance to, float speed = 1f)
	{
		if (Locked || from == to)
			return false;
		From = from;
		To = to;
		_duration = DurationS(_t, from, to) / Math.Max(0.1f, speed);
		Progress = 0f;
		_recenterLeft = 0f;
		Kind = to == Stance.Prone ? TransitionKind.ProneEntry : from == Stance.Prone ? TransitionKind.GetUp : TransitionKind.Change;
		return true;
	}

	/// <summary>One notch of the granular crouch: a short, reversible change.</summary>
	public bool StartStep(Stance from, Stance to)
	{
		if (Locked || from == Stance.Prone || to == Stance.Prone)
			return false;
		From = from;
		To = to;
		_duration = _t.StepS;
		Progress = 0f;
		Kind = TransitionKind.Change;
		return true;
	}

	public void Update(float delta)
	{
		if (_recenterLeft > 0f)
			_recenterLeft = Math.Max(0f, _recenterLeft - delta);
		if (Kind == TransitionKind.None)
			return;
		Progress = Math.Min(1f, Progress + delta / Math.Max(0.01f, _duration));
		if (Progress < 1f)
			return;
		_recenterLeft = Locked ? _t.RecenterS : 0f;
		Kind = TransitionKind.None;
	}

	/// <summary>The stance perception and the hit zones see: a getting-up body counts as standing from the half way mark.</summary>
	public Stance PerceivedStance(Stance logical)
	{
		switch (Kind)
		{
			case TransitionKind.GetUp:
				return Progress >= _t.StandingProfileAt ? To : Stance.Prone;
			case TransitionKind.ProneEntry:
				return Progress >= 0.5f ? Stance.Prone : From;
			default:
				return logical;
		}
	}

	/// <summary>How far the eye has travelled from the old height to the new one, 0..1.</summary>
	public float EyeBlend => Kind switch
	{
		TransitionKind.ProneEntry => Math.Clamp(MathF.Pow(Progress / 0.85f, 2.2f), 0f, 1f), // ease in: the slam
		TransitionKind.GetUp => Progress * Progress * (3f - 2f * Progress), // slow start while pushing up
		TransitionKind.Change => Progress,
		_ => 1f,
	};

	/// <summary>Metres to add to the eye height: the camera dips a little further at the end of the slam and comes back.</summary>
	public float EyeSettleM => Kind == TransitionKind.ProneEntry && Progress > 0.8f
		? -_t.SlamSettleM * MathF.Sin(MathF.PI * (Progress - 0.8f) / 0.2f)
		: 0f;

	/// <summary>Extra camera pitch in degrees (negative looks down): forward at the slam, slowly up on the way out.</summary>
	public float CameraPitchDeg => Kind switch
	{
		TransitionKind.ProneEntry => -_t.SlamPitchDeg * MathF.Sin(MathF.PI * Progress),
		TransitionKind.GetUp => -_t.GetUpPitchDeg * (1f - EyeBlend),
		_ => 0f,
	};

	public void Reset()
	{
		Kind = TransitionKind.None;
		Progress = 0f;
		_recenterLeft = 0f;
	}
}
