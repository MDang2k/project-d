using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>What an AI knows when it considers throwing a grenade (throwables spec s6).</summary>
public sealed record AiGrenadeInputs(
	bool HasGrenade,
	/// <summary>Seconds since the target was last seen or strongly cued; the AI needs it within 8.</summary>
	float SecondsSinceTargetKnown,
	float DistanceM,
	/// <summary>How long the AI has had no line of fire to the target.</summary>
	float NoLineOfFireS,
	bool ArcClear,
	bool SquadmateNearLanding,
	float CooldownLeftS,
	string Archetype,
	/// <summary>Hurt, pinned or out of options: a Rat throws only then.</summary>
	bool Cornered = false);

/// <summary>When and how an AI throws (throwables spec s6). Pure logic.</summary>
public static class AiGrenadeDecision
{
	public const float MinRangeM = 8f;
	public const float MaxRangeM = 35f;
	public const float KnownWithinS = 8f;
	public const float NoLineOfFireNeedS = 3f;
	public const float CooldownS = 15f;

	/// <summary>Chance per check that this archetype throws once all the conditions hold. Infiltrators throw often, conscripts rarely, rats almost never.</summary>
	public static float Appetite(string archetype, bool cornered) => archetype switch
	{
		"infiltrator" => 0.5f,
		"operator" => 0.3f,
		"conscript" => 0.1f,
		"rat" => cornered ? 0.5f : 0.01f,
		_ => 0.2f,
	};

	/// <summary>Whether the hard conditions of the spec hold (known target, 8 to 35 m, no line of fire for 3 s, clear arc, no squadmate near the landing, off cooldown).</summary>
	public static bool Eligible(AiGrenadeInputs i)
		=> i.HasGrenade
			&& i.CooldownLeftS <= 0f
			&& i.SecondsSinceTargetKnown <= KnownWithinS
			&& i.DistanceM is >= MinRangeM and <= MaxRangeM
			&& i.NoLineOfFireS >= NoLineOfFireNeedS
			&& i.ArcClear
			&& !i.SquadmateNearLanding;

	public static bool ShouldThrow(AiGrenadeInputs i, Random rng)
		=> Eligible(i) && rng.NextDouble() < Appetite(i.Archetype, i.Cornered);

	/// <summary>Seconds the AI cooks the grenade before throwing: infiltrators 1 s, conscripts sometimes forget, the rest hold it only to throw.</summary>
	public static float CookS(string archetype, Random rng) => archetype switch
	{
		"infiltrator" => 1f,
		"conscript" => 0f,
		_ => rng.NextDouble() < 0.25 ? 0.5f : 0f,
	};

	/// <summary>Radius of the error of the throw around the target: good throwers land close, conscripts wide.</summary>
	public static float ErrorRadiusM(string archetype) => archetype switch
	{
		"infiltrator" => 0.8f,
		"operator" => 1.5f,
		"conscript" => 4.5f,
		"rat" => 3f,
		_ => 2.2f,
	};

	/// <summary>Operators throw smoke when they retreat, to break the line; nobody else does in this pass.</summary>
	public static bool ShouldSmoke(string archetype, bool retreating, bool hasSmoke, float cooldownLeftS)
		=> hasSmoke && retreating && cooldownLeftS <= 0f && archetype == "operator";

	// ---- dodging

	/// <summary>
	/// Whether a bot reacts to a grenade: it sees one land within 10 m or hears the clink within 8 m. Grenades it threw
	/// itself are ignored (it knows where they are).
	/// </summary>
	public static bool ShouldDodge(float distanceM, bool seesIt, bool heardClink, bool ownGrenade)
		=> !ownGrenade && ((seesIt && distanceM <= 10f) || (heardClink && distanceM <= 8f));

	/// <summary>An AI that hears a grenade pin pulled near it: an infiltrator rushes the thrower, everyone else gets away from it.</summary>
	public static bool PinSendsBotToCover(string archetype) => archetype != "infiltrator";
}

/// <summary>Where a thrown grenade goes (throwables spec s6): the launch velocity that lands it on a point. Pure logic.</summary>
public static class ThrowSolver
{
	/// <summary>The low-arc launch velocity at a given speed that reaches the target, or null when it is out of range.</summary>
	public static Vector3? Velocity(Vector3 from, Vector3 to, float speed, float gravity = 9.8f)
	{
		var flat = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
		var d = flat.Length();
		if (d < 0.01f)
			return Vector3.Up * speed;
		var dy = to.Y - from.Y;
		var v2 = speed * speed;
		var disc = v2 * v2 - gravity * (gravity * d * d + 2f * dy * v2);
		if (disc < 0f)
			return null;
		var tan = (v2 - MathF.Sqrt(disc)) / (gravity * d);
		var angle = MathF.Atan(tan);
		return flat / d * (speed * MathF.Cos(angle)) + Vector3.Up * (speed * MathF.Sin(angle));
	}

	/// <summary>Points along the flight, for checking that nothing is in the way.</summary>
	public static List<Vector3> Arc(Vector3 from, Vector3 velocity, float gravity = 9.8f, float stepS = 0.1f, float maxS = 3f)
	{
		var list = new List<Vector3> { from };
		for (var t = stepS; t <= maxS; t += stepS)
		{
			var p = from + velocity * t + Vector3.Down * (0.5f * gravity * t * t);
			list.Add(p);
			if (p.Y < from.Y - 3f)
				break;
		}
		return list;
	}
}

/// <summary>Which grenades an AI carries (throwables spec s6). Placeholder table until the Faction Loadouts sheet gets a Grenade slot.</summary>
public static class AiGrenadeLoadout
{
	public static List<string> Roll(Faction faction, Random rng)
	{
		var items = new List<string>();
		bool Chance(double p) => rng.NextDouble() < p;
		string Pick(params string[] ids) => ids[rng.Next(ids.Length)];
		switch (faction)
		{
			case Faction.AlliedPmc:
				if (Chance(0.6))
					items.Add("frag_grenade");
				if (items.Count > 0 && Chance(0.25))
					items.Add("frag_grenade");
				if (Chance(0.2))
					items.Add("smoke_grenade_west");
				break;
			case Faction.SovietPmc:
				if (Chance(0.6))
					items.Add(Chance(0.7) ? "offensive_frag_grenade" : "defensive_frag_grenade");
				if (items.Count > 0 && Chance(0.25))
					items.Add("offensive_frag_grenade");
				if (Chance(0.2))
					items.Add("smoke_grenade_east");
				break;
			case Faction.Deserters:
				items.Add(Pick("frag_grenade", "offensive_frag_grenade", "egg_grenade"));
				if (Chance(0.5))
					items.Add(Pick("frag_grenade", "offensive_frag_grenade", "egg_grenade"));
				break;
			case Faction.Bandits:
				if (Chance(0.25))
					items.Add(Pick("stick_grenade", "egg_grenade"));
				break;
			case Faction.Looters:
				if (Chance(0.1))
					items.Add("stick_grenade");
				break;
		}
		return items;
	}

	/// <summary>Disciplined factions throw without a shout; looters and bandits warn their friends.</summary>
	public static bool Shouts(Faction faction) => faction is Faction.Looters or Faction.Bandits;
}
