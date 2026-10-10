using System;
using Godot;

namespace ProjectD;

/// <summary>Weapon support numbers: resting on ledges and bipod deployment (GDD s7).</summary>
public sealed record SupportTuning(
	/// <summary>Surface height range above the player's feet that counts as restable.</summary>
	float RestMinHeight,
	float RestMaxHeight,
	float MinSurfaceNormalY,
	float RestBlendTimeS,
	/// <summary>Speed above which rest and bipod are lost (m/s).</summary>
	float MoveSpeedLimit,
	float BipodYawLimitDeg)
{
	public static SupportTuning Default => new(
		RestMinHeight: 0.45f, RestMaxHeight: 1.5f, MinSurfaceNormalY: 0.7f,
		RestBlendTimeS: 0.25f, MoveSpeedLimit: 0.6f, BipodYawLimitDeg: 30f);
}

/// <summary>
/// Rest and bipod state (GDD s7). Resting is automatic near a ledge; the bipod deploys on
/// request when prone or over a surface, restricts horizontal aim and folds when the player
/// moves, sprints or loses its support. Pure logic.
/// </summary>
public sealed class SupportModel
{
	private readonly SupportTuning _t;
	private bool _onSurface;

	public SupportModel(SupportTuning tuning) => _t = tuning;

	/// <summary>0..1 blend toward fully rested.</summary>
	public float RestAmount { get; private set; }
	public bool BipodDeployed { get; private set; }
	public float DeployYawRad { get; private set; }

	public bool IsRestSurface(float heightAboveFeet, float normalY)
		=> normalY >= _t.MinSurfaceNormalY
			&& heightAboveFeet >= _t.RestMinHeight && heightAboveFeet <= _t.RestMaxHeight;

	/// <summary>Deploys when prone, or when a surface sits under the muzzle. False if not possible.</summary>
	public bool TryDeployBipod(bool hasBipod, Stance stance, bool surfaceUnderMuzzle, float yawRad)
	{
		if (!hasBipod || BipodDeployed)
			return false;
		var prone = stance == Stance.Prone;
		if (!prone && !surfaceUnderMuzzle)
			return false;
		BipodDeployed = true;
		_onSurface = !prone;
		DeployYawRad = yawRad;
		return true;
	}

	public void FoldBipod() => BipodDeployed = false;

	/// <returns>True if the bipod was folded by this update.</returns>
	public bool Update(float delta, bool restSurface, bool surfaceUnderMuzzle, bool hasBipod,
		Stance stance, float speed, bool sprinting)
	{
		var steady = !sprinting && speed <= _t.MoveSpeedLimit;
		var target = restSurface && steady ? 1f : 0f;
		var step = _t.RestBlendTimeS > 0.001f ? delta / _t.RestBlendTimeS : 1f;
		RestAmount = Mathf.MoveToward(RestAmount, target, step);

		if (!BipodDeployed)
			return false;
		var lost = !hasBipod || !steady
			|| (_onSurface ? !surfaceUnderMuzzle : stance != Stance.Prone);
		if (lost)
			BipodDeployed = false;
		return lost;
	}

	/// <summary>Limits yaw to the bipod's horizontal arc while deployed.</summary>
	public float ClampYaw(float desiredYawRad)
	{
		if (!BipodDeployed)
			return desiredYawRad;
		var limit = Mathf.DegToRad(_t.BipodYawLimitDeg);
		var diff = Mathf.Wrap(desiredYawRad - DeployYawRad, -MathF.PI, MathF.PI);
		return DeployYawRad + Mathf.Clamp(diff, -limit, limit);
	}
}
