using System;

namespace ProjectD;

/// <summary>What an AI can decide to do in a fight (AI combat v2 spec s7); each is carried out by a <see cref="TacticState"/>.</summary>
public enum TacticAction { Push, Hold, Flank, Suppress, SeekCover, Retreat, Heal, Flee }

/// <summary>Temperament of an archetype, all 0..1: how bold, how keen to stay alive, how easily it breaks.</summary>
public readonly record struct Temperament(float Aggression, float SelfPreservation, float Panic);

/// <summary>Facts the scorer reads, gathered by the AI node.</summary>
public readonly record struct UtilityFacts(
	bool SeesTarget,
	float TargetDistance,
	float EngageDistance,
	float SecondsSinceSeen,
	/// <summary>0 unhurt .. 1 about to die.</summary>
	float Injury,
	/// <summary>0..1 incoming fire.</summary>
	float Suppression,
	bool CoverAvailable,
	bool HealItemAvailable,
	bool Healed,
	bool PartnerEngaged,
	bool FlankPointAvailable,
	/// <summary>Bursts fired since the last cover.</summary>
	int BurstsInOpen);

/// <summary>
/// Scores each action from the facts and the temperament and picks the best, with a bonus for the current action so
/// it does not flicker. Pure logic. The numbers are placeholders to tune in the AI range.
/// </summary>
public static class AiUtility
{
	/// <summary>Bonus for staying with the current action.</summary>
	public const float Stickiness = 0.12f;

	public static float Score(TacticAction action, in UtilityFacts f, in Temperament t)
	{
		var healthy = 1f - f.Injury;
		var pinned = Clamp(f.Suppression * (1f + t.Panic));
		switch (action)
		{
			case TacticAction.Push:
			{
				var far = f.TargetDistance > f.EngageDistance ? 1f : 0.45f;
				var lost = !f.SeesTarget ? 0.15f : 0f;
				return t.Aggression * (0.55f + lost) * far * healthy * (1f - pinned * (1f - t.Aggression * 0.5f));
			}
			case TacticAction.Hold:
				return 0.2f + (1f - t.Aggression) * 0.35f + t.SelfPreservation * 0.3f + pinned * 0.15f;
			case TacticAction.Flank:
				return f.PartnerEngaged && f.FlankPointAvailable ? t.Aggression * 0.85f * healthy : 0f;
			case TacticAction.Suppress:
				return !f.SeesTarget && f.SecondsSinceSeen < 12f ? 0.5f + t.Aggression * 0.2f : 0f;
			case TacticAction.SeekCover:
				return f.CoverAvailable
					? pinned * (0.7f + t.SelfPreservation * 0.9f) + Math.Min(f.BurstsInOpen, 6) * 0.05f * t.SelfPreservation
					: 0f;
			case TacticAction.Retreat:
				return f.CoverAvailable && f.Injury > 0.2f ? f.Injury * (0.3f + t.SelfPreservation * 1.5f) : 0f;
			case TacticAction.Heal:
				return f.HealItemAvailable && !f.Healed && f.Injury > 0.3f
					? f.Injury * (0.4f + t.SelfPreservation) * (f.SeesTarget ? 0.4f : 1.8f)
					: 0f;
			case TacticAction.Flee:
				// a broken nerve: hurt, under fire, easily scared and nothing left to patch up with
				return f.Injury > 0.6f && !(f.HealItemAvailable && !f.Healed)
					? t.Panic * f.Injury * (0.5f + pinned) * 1.6f
					: 0f;
			default:
				return 0f;
		}
	}

	/// <summary>The best action; ties and near ties go to the current one. <paramref name="noise"/> adds a little per-pick variety.</summary>
	public static TacticAction Choose(in UtilityFacts f, in Temperament t, TacticAction current, Func<float>? noise = null)
	{
		var best = current;
		var bestScore = float.MinValue;
		foreach (TacticAction a in Enum.GetValues(typeof(TacticAction)))
		{
			var s = Score(a, f, t) + (a == current ? Stickiness : 0f) + (noise?.Invoke() ?? 0f) * 0.05f;
			if (s > bestScore)
			{
				bestScore = s;
				best = a;
			}
		}
		return best;
	}

	/// <summary>Tactic states that already carry out an action; the node only forces a state when the chosen action is not served by the current one.</summary>
	public static bool Serves(TacticState state, TacticAction action) => action switch
	{
		TacticAction.Push => state is TacticState.Advance or TacticState.Shoot,
		TacticAction.Hold => state is TacticState.Hide or TacticState.Peek or TacticState.Shoot,
		TacticAction.Flank => state == TacticState.Flank,
		TacticAction.Suppress => state == TacticState.Suppress,
		TacticAction.SeekCover => state == TacticState.SeekCover,
		TacticAction.Retreat => state is TacticState.Retreat or TacticState.Heal,
		TacticAction.Heal => state == TacticState.Heal,
		TacticAction.Flee => state == TacticState.Flee,
		_ => false,
	};

	/// <summary>The state that starts an action.</summary>
	public static TacticState EntryState(TacticAction action, bool coverAvailable) => action switch
	{
		TacticAction.Push => TacticState.Advance,
		TacticAction.Hold => coverAvailable ? TacticState.SeekCover : TacticState.Shoot,
		TacticAction.Flank => TacticState.Flank,
		TacticAction.Suppress => TacticState.Suppress,
		TacticAction.SeekCover => TacticState.SeekCover,
		TacticAction.Retreat => TacticState.Retreat,
		TacticAction.Heal => TacticState.Heal,
		_ => TacticState.Flee,
	};

	private static float Clamp(float v) => Math.Clamp(v, 0f, 1.5f);
}
