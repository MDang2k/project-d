using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

public enum GrenadeKind { Frag, Concussion, Smoke, Distraction }

/// <summary>
/// A frag grenade's numbers (throwables spec s3 and s8), built from <c>GrenadeData</c>. Fragment values: how many are
/// simulated, their damage and penetration tier, how far they fly and the share of damage left at that range. Blast
/// values: the core radius and the total blast damage at its centre.
/// </summary>
public sealed record GrenadeSpec(
	string Id,
	float FuseS,
	float FuseSpreadS,
	int Fragments,
	float FragmentDamage,
	int FragmentTier,
	float FragmentRangeM,
	float FragmentMinScale,
	float BlastCoreM,
	float BlastDamage,
	int BlastTier,
	float NoiseRadiusM,
	float ThrowSpeedMult = 1f,
	GrenadeKind Kind = GrenadeKind.Frag,
	/// <summary>Smoke: radius, seconds to grow, seconds it lasts, colour, and whether each cloud picks a random colour (M18).</summary>
	float CloudRadiusM = 0f,
	float CloudGrowS = 4f,
	float CloudLifeS = 45f,
	Color? CloudColor = null,
	bool RandomColor = false,
	/// <summary>Distraction: goes off on the first contact, with this much noise (a bottle 20 m, a rock 12 m).</summary>
	float ImpactNoiseM = 0f,
	/// <summary>How far the clink of its first landing carries for AI hearing.</summary>
	float ClinkNoiseM = 15f);

public enum GrenadeStage { Safe, PinOut, Cooking, Thrown, Detonated }

/// <summary>
/// The fuse of one grenade in hand (throwables spec s2): pin in, pin out, spoon released (cooking) and thrown. The fuse
/// only runs once the spoon is gone: either cooked in the hand or at the throw. A cooked grenade that runs out goes off
/// in the hand. Pure logic.
/// </summary>
public sealed class GrenadeFuse
{
	public GrenadeFuse(float fuseS, float spreadS, Random rng)
		=> FuseS = Math.Max(0.5f, fuseS + (float)(rng.NextDouble() * 2.0 - 1.0) * spreadS);

	/// <summary>This grenade's own fuse time: the catalog value with its random spread.</summary>
	public float FuseS { get; }

	public GrenadeStage Stage { get; private set; }
	public float RemainingS { get; private set; }

	/// <summary>The cooked grenade ran out while still in the hand.</summary>
	public event Action? ExplodedInHand;

	/// <summary>Only a grenade with the pin in or out can be put away; one with the spoon gone must be thrown.</summary>
	public bool CanPutAway => Stage is GrenadeStage.Safe or GrenadeStage.PinOut;

	public bool PullPin()
	{
		if (Stage != GrenadeStage.Safe)
			return false;
		Stage = GrenadeStage.PinOut;
		return true;
	}

	public bool PutPinBack()
	{
		if (Stage != GrenadeStage.PinOut)
			return false;
		Stage = GrenadeStage.Safe;
		return true;
	}

	/// <summary>Releases the spoon in the hand: the fuse starts now.</summary>
	public bool Cook()
	{
		if (Stage != GrenadeStage.PinOut)
			return false;
		Stage = GrenadeStage.Cooking;
		RemainingS = FuseS;
		return true;
	}

	/// <summary>The grenade leaves the hand (thrown, or dropped live). Returns the fuse time left, or -1 when the pin is still in.</summary>
	public float Release()
	{
		if (Stage is not (GrenadeStage.PinOut or GrenadeStage.Cooking))
			return -1f;
		if (Stage == GrenadeStage.PinOut)
			RemainingS = FuseS;
		Stage = GrenadeStage.Thrown;
		return RemainingS;
	}

	public void Update(float delta)
	{
		if (Stage != GrenadeStage.Cooking)
			return;
		RemainingS = Math.Max(0f, RemainingS - delta);
		if (RemainingS > 0f)
			return;
		Stage = GrenadeStage.Detonated;
		ExplodedInHand?.Invoke();
	}
}

/// <summary>
/// Throwing feel numbers (throwables spec s2), set in <c>PlayerConfig</c>: throw speeds by stance, the wind-up, the upward
/// bias, the pin-back time and how far the pin and spoon are heard.
/// </summary>
public sealed record ThrowTuning(
	float StandSpeed = 20f,
	float CrouchSpeed = 17f,
	float ProneSpeed = 10f,
	/// <summary>A lob or a roll; ignores the wind-up.</summary>
	float UnderhandSpeed = 6f,
	/// <summary>Seconds of wind-up for a full overhand throw.</summary>
	float WindUpS = 0.5f,
	/// <summary>Share of a full throw when released at once.</summary>
	float MinWindUp = 0.6f,
	float UpBiasDeg = 15f,
	float UnderhandBiasDeg = 2f,
	float PinBackS = 1.2f,
	float PinNoiseM = 10f,
	float SpoonNoiseM = 6f)
{
	public static ThrowTuning Default { get; } = new();
}

/// <summary>Throw speed and direction (throwables spec s2). Pure logic.</summary>
public static class ThrowModel
{
	/// <summary>How much of a full overhand throw a wind-up of this length gives: the minimum when released at once, 100% after the wind-up time.</summary>
	public static float WindUpFraction(float heldS, ThrowTuning? t = null)
	{
		t ??= ThrowTuning.Default;
		return t.MinWindUp + (1f - t.MinWindUp) * Math.Clamp(heldS / t.WindUpS, 0f, 1f);
	}

	/// <param name="strengthBonus">Fraction added by the Strength skill (0.02 per level).</param>
	public static float Speed(Stance stance, bool underhand, float windUpFraction, float strengthBonus = 0f, float itemMultiplier = 1f,
		ThrowTuning? t = null)
	{
		t ??= ThrowTuning.Default;
		var baseSpeed = underhand
			? t.UnderhandSpeed
			: stance switch { Stance.Prone => t.ProneSpeed, Stance.Crouch => t.CrouchSpeed, _ => t.StandSpeed };
		var fraction = underhand ? 1f : Math.Clamp(windUpFraction, t.MinWindUp, 1f);
		return baseSpeed * fraction * (1f + strengthBonus) * itemMultiplier;
	}

	/// <summary>The throw leaves along the view, tipped upward by a fixed bias (a lob goes flatter).</summary>
	public static Vector3 Direction(Vector3 forward, bool underhand, ThrowTuning? t = null)
	{
		t ??= ThrowTuning.Default;
		var f = forward.Normalized();
		var right = f.Cross(Vector3.Up);
		if (right.LengthSquared() < 1e-6f)
			return f;
		return f.Rotated(right.Normalized(), Mathf.DegToRad(underhand ? t.UnderhandBiasDeg : t.UpBiasDeg)).Normalized();
	}
}

/// <summary>Fragment directions and damage falloff (throwables spec s3). Pure logic.</summary>
public static class FragmentBurst
{
	/// <summary>
	/// A uniform sphere of directions, squashed toward the horizontal (people stand on the ground): the vertical part is
	/// multiplied by <paramref name="verticalShare"/> before normalizing.
	/// </summary>
	public static List<Vector3> Directions(int count, Random rng, float verticalShare = 0.75f)
	{
		var list = new List<Vector3>(count);
		for (var i = 0; i < count; i++)
		{
			var y = (float)(rng.NextDouble() * 2.0 - 1.0);
			var phi = (float)(rng.NextDouble() * Math.PI * 2.0);
			var r = MathF.Sqrt(Math.Max(0f, 1f - y * y));
			list.Add(new Vector3(r * MathF.Cos(phi), y * verticalShare, r * MathF.Sin(phi)).Normalized());
		}
		return list;
	}

	/// <summary>Damage scale of a fragment that has flown a distance: 1 at the burst, falling linearly to the minimum at max range.</summary>
	public static float DamageScale(float distanceM, float maxRangeM, float minScale)
		=> maxRangeM <= 0f ? minScale : Math.Clamp(1f - (1f - minScale) * (distanceM / maxRangeM), minScale, 1f);
}

/// <summary>The blast core of a grenade (throwables spec s3): damage by distance and how it spreads over the body. Pure logic.</summary>
public static class BlastModel
{
	/// <summary>Share of the total blast damage per body part (sums to 1).</summary>
	public static readonly (BodyPart Part, float Share)[] Shares =
	{
		(BodyPart.Thorax, 0.30f), (BodyPart.Stomach, 0.25f), (BodyPart.LeftArm, 0.10f), (BodyPart.RightArm, 0.10f),
		(BodyPart.LeftLeg, 0.125f), (BodyPart.RightLeg, 0.125f),
	};

	/// <summary>Total blast damage at a distance from the burst: full at the centre, none at the core edge.</summary>
	public static float TotalDamage(float distanceM, float coreRadiusM, float damageAtCentre)
		=> distanceM >= coreRadiusM || coreRadiusM <= 0f ? 0f : damageAtCentre * (1f - Math.Max(0f, distanceM) / coreRadiusM);

	/// <summary>The hits to apply to one character (empty when out of the core or when the burst has no clear line to them).</summary>
	public static List<BlastHit> Hits(float distanceM, bool clearLine, GrenadeSpec spec)
	{
		var hits = new List<BlastHit>();
		var total = clearLine ? TotalDamage(distanceM, spec.BlastCoreM, spec.BlastDamage) : 0f;
		if (total <= 0f)
			return hits;
		foreach (var (part, share) in Shares)
			hits.Add(new BlastHit(part, total * share, spec.BlastTier));
		return hits;
	}
}
