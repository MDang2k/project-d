using System;

namespace ProjectD;

/// <summary>Prone turning numbers (animation spec s1.7). Placeholders to tune.</summary>
public sealed record ProneTurnTuning(
	/// <summary>The view turns freely this far either side of the body axis.</summary>
	float ConeDeg = 30f,
	/// <summary>Beyond the cone the view turns at most this fast, whatever the mouse does.</summary>
	float RateDegS = 45f,
	float MaxBankDeg = 8f,
	float BankSmoothS = 0.12f);

/// <summary>
/// Turning while lying down. The view moves freely inside a cone around the body axis; past it the turn is a hip-drag:
/// the view yaw rate is clamped to a fixed slow rate (the mouse delta beyond it is dropped, not queued) and the
/// pelvis is pulled after the view. A turn that would push the legs into geometry stops cold: the pelvis stays and the
/// view locks at the cone edge on that side. Pure logic; the player node feeds it the requested yaw and the sweep results.
/// </summary>
public sealed class ProneTurnModel
{
	private readonly ProneTurnTuning _t;

	public ProneTurnModel(ProneTurnTuning? tuning = null) => _t = tuning ?? new ProneTurnTuning();

	public ProneTurnTuning Tuning => _t;

	/// <summary>Yaw of the body axis (hips and legs), degrees, positive turning left.</summary>
	public float PelvisYawDeg { get; private set; }

	/// <summary>View yaw minus pelvis yaw, within the cone: how far the head is turned on the body.</summary>
	public float TwistDeg { get; private set; }

	/// <summary>Hips are being dragged round this frame.</summary>
	public bool Dragging { get; private set; }

	/// <summary>The turn wanted to go on but the legs are against something.</summary>
	public bool Blocked { get; private set; }

	/// <summary>Camera roll for the hip drag, degrees (positive rolls left).</summary>
	public float BankDeg { get; private set; }

	public void Reset(float yawDeg)
	{
		PelvisYawDeg = yawDeg;
		TwistDeg = 0f;
		Dragging = Blocked = false;
		BankDeg = 0f;
	}

	/// <summary>
	/// One frame. <paramref name="viewYawDeg"/> is where the view was, <paramref name="requestedYawDeg"/> where the
	/// mouse wants it. Returns the allowed view yaw. <paramref name="blockedLeft"/> and <paramref name="blockedRight"/>
	/// say that turning the pelvis that way hits something.
	/// </summary>
	public float Step(float delta, float viewYawDeg, float requestedYawDeg, bool blockedLeft, bool blockedRight)
	{
		var rel = PivotModel.WrapDeg(viewYawDeg - PelvisYawDeg);
		var target = rel + PivotModel.WrapDeg(requestedYawDeg - viewYawDeg);
		var cone = _t.ConeDeg;
		var excess = 0f;
		Blocked = false;

		if (Math.Abs(target) <= cone)
		{
			rel = target;
		}
		else
		{
			var sign = Math.Sign(target);
			var edge = sign * cone;
			// free up to the cone edge, then only the clamped rate
			var beyond = target - edge;
			var step = Math.Clamp(beyond, -_t.RateDegS * delta, _t.RateDegS * delta);
			var blocked = sign > 0 ? blockedLeft : blockedRight;
			if (blocked)
			{
				step = 0f;
				Blocked = true;
			}
			excess = step;
			PelvisYawDeg += step;
			rel = edge;
		}

		Dragging = Math.Abs(excess) > 1e-4f;
		var targetBank = Dragging ? Math.Sign(excess) * _t.MaxBankDeg : 0f;
		BankDeg += (targetBank - BankDeg) * Math.Min(1f, delta / Math.Max(0.01f, _t.BankSmoothS));
		TwistDeg = rel;
		return PivotModel.WrapDeg(PelvisYawDeg + rel);
	}
}
