using System.Collections.Generic;

namespace ProjectD;

/// <summary>Per-limb health state: current and maximum HP (a surgery kit can lower a maximum).</summary>
public sealed record BodyState(IReadOnlyDictionary<BodyPart, (float Hp, float Max)> Parts);

/// <summary>Injuries and healing in progress.</summary>
public sealed record EffectsState(
	IReadOnlyCollection<BodyPart> Fractures,
	IReadOnlyDictionary<BodyPart, BleedLevel> Bleeds,
	float Pain,
	float MorphineLeft,
	float HealPool,
	float HealRate);

/// <summary>
/// The character as a raid transit carries it from one map to the next (world map spec s3): limb health, fractures,
/// bleeds, pain, and the energy and hydration meters. Gear travels in the run's inventory. Pure data.
/// </summary>
public sealed record CharacterSnapshot(BodyState Body, EffectsState Effects, float Energy, float Hydration)
{
	public static CharacterSnapshot Capture(BodyHealth body, StatusEffects effects, VitalsModel vitals)
		=> new(body.State(), effects.State(), vitals.Energy, vitals.Hydration);

	public void Apply(BodyHealth body, StatusEffects effects, VitalsModel vitals)
	{
		body.LoadState(Body);
		effects.LoadState(Effects);
		vitals.Set(Energy, Hydration);
	}
}

/// <summary>The snapshot waiting for the next map to pick it up, and where it came from. Static so a scene change keeps it.</summary>
public static class TransitCarry
{
	public static CharacterSnapshot? Pending { get; set; }

	/// <summary>Map id the player left, to choose the arrival point that matches.</summary>
	public static string FromMap { get; set; } = "";

	public static void Clear()
	{
		Pending = null;
		FromMap = "";
	}
}
