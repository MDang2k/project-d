using System.Collections.Generic;

namespace ProjectD;

/// <summary>
/// Who killed the player (M8c spec s1): the AI that dealt the killing blow, or, when the player bled out, the last AI that
/// hit them within a time window. Dying with no attacker (MIA, a crash, a mine, a fall) credits nobody. Pure logic;
/// attackers are identified by any stable id (an instance id).
/// </summary>
public sealed class KillAttribution
{
	private readonly float _windowS;
	private long? _lastAttacker;
	private float _lastHitS = float.NegativeInfinity;

	/// <param name="windowS">How long after its last hit an AI is still blamed for a bleed-out.</param>
	public KillAttribution(float windowS = 60f) => _windowS = windowS;

	/// <summary>An AI hit the player at this raid time (seconds).</summary>
	public void RecordHit(long attacker, float nowS)
	{
		_lastAttacker = attacker;
		_lastHitS = nowS;
	}

	/// <summary>Resets for a new raid.</summary>
	public void Reset()
	{
		_lastAttacker = null;
		_lastHitS = float.NegativeInfinity;
	}

	/// <summary>
	/// The killer. <paramref name="killingBlow"/> is the AI whose hit took the last HP (null for a bleed-out or a
	/// non-AI death); then the last attacker counts if it hit within the window.
	/// </summary>
	public long? Killer(long? killingBlow, float nowS)
	{
		if (killingBlow is { } blow)
			return blow;
		return _lastAttacker is { } last && nowS - _lastHitS <= _windowS ? last : null;
	}
}
