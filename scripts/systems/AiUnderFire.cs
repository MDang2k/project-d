using System;

namespace ProjectD;

public enum UnderFirePhase { None, Reacting, TakingCover, Prone, GettingUp }

/// <summary>How an AI answers being shot at (AI combat v2 spec s4). Filled from the <c>react.*</c> and <c>move.get_up_s</c> stats.</summary>
public sealed record UnderFireTuning(float DelayMinS, float DelayMaxS, float CoverMaxM, float ProneHoldS, float GetUpS);

/// <summary>
/// Hit, near miss or hit squadmate: a short reaction delay, then either a sprint to cover within reach or, with
/// none, drop prone; prone is held a while and left through a get-up. Pure logic; the AI body supplies the cover
/// distance and tells when it arrived.
/// </summary>
public sealed class AiUnderFire
{
	private readonly Random _rng;
	private float _timer;

	public AiUnderFire(UnderFireTuning tuning, Random rng)
	{
		Tuning = tuning;
		_rng = rng;
	}

	/// <summary>Replaced when the actor's stats change.</summary>
	public UnderFireTuning Tuning { get; set; }

	public UnderFirePhase Phase { get; private set; }

	/// <summary>Trigger finger off: reacting, running to cover or getting up.</summary>
	public bool BlocksFire => Phase is UnderFirePhase.Reacting or UnderFirePhase.TakingCover or UnderFirePhase.GettingUp;

	public bool Sprints => Phase == UnderFirePhase.TakingCover;
	public bool IsProne => Phase is UnderFirePhase.Prone or UnderFirePhase.GettingUp;

	/// <summary>Turns fast while reacting or diving for cover.</summary>
	public bool TurnsFast => Phase is UnderFirePhase.Reacting or UnderFirePhase.TakingCover;

	/// <summary>Being shot at. Starts a reaction; a bot already down stays down longer; one on its way keeps going.</summary>
	public void Trigger()
	{
		switch (Phase)
		{
			case UnderFirePhase.None:
				Phase = UnderFirePhase.Reacting;
				_timer = Tuning.DelayMinS + (float)_rng.NextDouble() * Math.Max(0f, Tuning.DelayMaxS - Tuning.DelayMinS);
				break;
			case UnderFirePhase.Prone:
				_timer = Tuning.ProneHoldS;
				break;
		}
	}

	/// <summary>
	/// Advances the response. <paramref name="coverDistance"/> is asked once, when the delay ends: metres to the nearest
	/// usable cover or null for none. <paramref name="arrived"/> says the AI reached the cover point.
	/// </summary>
	public void Update(float delta, Func<float?> coverDistance, bool arrived)
	{
		switch (Phase)
		{
			case UnderFirePhase.Reacting:
				_timer -= delta;
				if (_timer > 0f)
					break;
				if (coverDistance() is { } d && d <= Tuning.CoverMaxM)
					Phase = UnderFirePhase.TakingCover;
				else
				{
					Phase = UnderFirePhase.Prone;
					_timer = Tuning.ProneHoldS;
				}
				break;
			case UnderFirePhase.TakingCover:
				if (arrived)
					Phase = UnderFirePhase.None;
				break;
			case UnderFirePhase.Prone:
				_timer -= delta;
				if (_timer <= 0f)
				{
					Phase = UnderFirePhase.GettingUp;
					_timer = Tuning.GetUpS;
				}
				break;
			case UnderFirePhase.GettingUp:
				_timer -= delta;
				if (_timer <= 0f)
					Phase = UnderFirePhase.None;
				break;
		}
	}

	public void Reset() => Phase = UnderFirePhase.None;
}
