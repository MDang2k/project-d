using System;
using Godot;

namespace ProjectD;

/// <summary>
/// Numbers of one AI's shooting skill (AI combat v2 spec s6). Error is the slow wander around the target: at
/// accuracy 0 it spans <c>ErrorMaxDeg</c>, at 1 only <c>ErrorMinDeg</c>, plus a share that grows with distance.
/// </summary>
public sealed record AimMotorTuning(
	float Accuracy,
	float ErrorMaxDeg,
	float ErrorMinDeg,
	float ErrorPer100MDeg,
	/// <summary>Error multiplier at full sprint speed (shooter or target).</summary>
	float MovingMult,
	/// <summary>Error multiplier at full suppression.</summary>
	float SuppressedMult,
	/// <summary>Seconds of steady aim at a still target to reach the settled error.</summary>
	float SettleS,
	/// <summary>Share of the error left when fully settled.</summary>
	float SettleFloor,
	/// <summary>0..1: share of each shot's climb the shooter pulls back against.</summary>
	float RecoilControl,
	/// <summary>How fast the error wanders, cycles per second.</summary>
	float WanderHz,
	/// <summary>Extra shake in degrees at full suppression.</summary>
	float ShakeDeg);

/// <summary>What the shooter and target are doing this frame.</summary>
public readonly record struct AimContext(
	float Distance,
	/// <summary>Shooter speed over sprint speed, 0..1.</summary>
	float OwnSpeedFraction,
	/// <summary>Target speed over sprint speed, 0..1.</summary>
	float TargetSpeedFraction,
	/// <summary>0..1 incoming fire.</summary>
	float Suppression,
	/// <summary>Weapon sway amplitude in degrees, from the handling model.</summary>
	float SwayDeg,
	/// <summary>Recoil strength from stance and aiming.</summary>
	float RecoilMult);

/// <summary>
/// Aims through the gun, as the player does: <c>aim = target + recoil + sway + shake + error</c>. Recoil is the
/// player's <see cref="RecoilModel"/> with the bot pulling back by its recoil control; sway is the player's
/// <see cref="SwayModel"/>; error is a smooth wander that shrinks while the bot holds a still target.
/// Replaces the old random spread cone. Pure logic.
/// </summary>
public sealed class AiAimMotor
{
	private readonly RecoilModel _recoil = new();
	private readonly SwayModel _sway = new();
	private readonly Random _rng;
	private readonly float[] _phase = new float[4];
	private float _time;
	private float _unsettled = 1f;
	private float _sinceShotS = 99f;
	private Vector2 _swayDeg;
	private AimContext _last;

	public AiAimMotor(AimMotorTuning tuning, Random rng)
	{
		Tuning = tuning;
		_rng = rng;
		for (var i = 0; i < _phase.Length; i++)
			_phase[i] = (float)_rng.NextDouble() * Mathf.Tau;
	}

	/// <summary>Replaced when the actor's stats change.</summary>
	public AimMotorTuning Tuning { get; set; }

	/// <summary>Climb left after the bot's own correction, x yaw and y pitch in degrees.</summary>
	public Vector2 Recoil => _recoil.Offset;

	/// <summary>0 settled .. 1 just lost the aim.</summary>
	public float Unsettled => _unsettled;

	/// <summary>The bot moved its aim a long way (new target, turned): the settle starts over.</summary>
	public void Disturb() => _unsettled = 1f;

	public void Update(float delta, in AimContext c, RecoilTuning recoil)
	{
		_time += delta;
		_last = c;
		_recoil.Update(delta, recoil);
		_sinceShotS += delta;
		if (_sinceShotS > 0.15f)
		{
			// between bursts the bot brings the muzzle back down, as the player drags it down
			var rate = (6f + 24f * Math.Clamp(Tuning.RecoilControl, 0f, 1f)) * delta;
			var o = _recoil.Offset;
			_recoil.Pull(new Vector2(-Math.Clamp(o.X, -rate, rate), -Math.Clamp(o.Y, -rate, rate)));
		}
		_swayDeg = _sway.Update(delta, c.SwayDeg);

		var still = c.OwnSpeedFraction < 0.1f && c.TargetSpeedFraction < 0.25f;
		if (still)
			_unsettled = Math.Max(0f, _unsettled - delta / Math.Max(0.1f, Tuning.SettleS));
		else
			_unsettled = Math.Min(1f, _unsettled + delta * 2f);
	}

	/// <summary>One round fired: the gun climbs and the bot pulls back by its control.</summary>
	public void Shot(RecoilTuning recoil, float recoilMult)
	{
		_sinceShotS = 0f;
		var before = _recoil.Offset;
		_recoil.Kick(recoil, recoilMult, (float)(_rng.NextDouble() * 2.0 - 1.0));
		var added = _recoil.Offset - before;
		var control = Math.Clamp(Tuning.RecoilControl, 0f, 1f);
		_recoil.Pull(new Vector2(-added.X * control, -added.Y * control));
		// firing shakes the aim loose
		_unsettled = Math.Max(_unsettled, 0.3f);
	}

	/// <summary>Size of the error wander, degrees, for the current context.</summary>
	public float ErrorAmplitudeDeg()
	{
		var t = Tuning;
		var acc = Math.Clamp(t.Accuracy, 0f, 1f);
		var amp = Mathf.Lerp(t.ErrorMaxDeg, t.ErrorMinDeg, acc) + _last.Distance * 0.01f * t.ErrorPer100MDeg * (1f - acc * 0.6f);
		amp *= Mathf.Lerp(t.SettleFloor, 1f, _unsettled);
		var motion = Math.Clamp(Math.Max(_last.OwnSpeedFraction, _last.TargetSpeedFraction), 0f, 1f);
		amp *= 1f + (t.MovingMult - 1f) * motion;
		amp *= 1f + (t.SuppressedMult - 1f) * Math.Clamp(_last.Suppression, 0f, 1f);
		return amp;
	}

	/// <summary>Total offset from the target point, x yaw and y pitch in degrees.</summary>
	public Vector2 OffsetDeg()
	{
		var w = Tuning.WanderHz * Mathf.Tau * _time;
		var wander = new Vector2(
			MathF.Sin(w + _phase[0]) + 0.5f * MathF.Sin(w * 2.3f + _phase[1]),
			MathF.Sin(w * 1.1f + _phase[2]) + 0.5f * MathF.Sin(w * 2.7f + _phase[3])) / 1.5f;
		var shake = new Vector2(MathF.Sin(_time * 31f + _phase[1]), MathF.Sin(_time * 27f + _phase[2]))
			* (Tuning.ShakeDeg * Math.Clamp(_last.Suppression, 0f, 1f));
		return Recoil + _swayDeg + wander * ErrorAmplitudeDeg() + shake;
	}

	/// <summary>The direction a shot leaves in: toward the aim point turned by the current offset.</summary>
	public Vector3 Direction(Vector3 from, Vector3 aimPoint)
	{
		var to = (aimPoint - from).Normalized();
		var offset = OffsetDeg();
		var yaw = MathF.Atan2(-to.X, -to.Z) + Mathf.DegToRad(offset.X);
		var pitch = MathF.Asin(Math.Clamp(to.Y, -1f, 1f)) + Mathf.DegToRad(offset.Y);
		var cp = MathF.Cos(pitch);
		return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp).Normalized();
	}

	/// <summary>
	/// Where to aim at a moving target: ahead by its travel during the flight, raised by the drop. Quality 0..1
	/// scales how much of the lead the bot applies (a poor shot under-leads).
	/// </summary>
	public static Vector3 LeadPoint(Vector3 shooter, Vector3 target, Vector3 targetVelocity, float muzzleVelocity,
		float gravity, float quality)
	{
		var distance = shooter.DistanceTo(target);
		var flight = distance / Math.Max(1f, muzzleVelocity);
		var q = Math.Clamp(quality, 0f, 1f);
		var ahead = targetVelocity * flight * q;
		var drop = Vector3.Up * (0.5f * gravity * flight * flight) * q;
		return target + ahead + drop;
	}
}
