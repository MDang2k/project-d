using System;

namespace ProjectD;

public enum FireMode { Semi, Auto }

/// <summary>
/// Trigger and rate-of-fire logic (GDD s7). Pure logic: returns how many shots are due this
/// frame so high rates stay correct at any frame time.
/// </summary>
public sealed class FireControl
{
	private readonly FireMode[] _modes;
	private readonly float _interval;
	private int _index;
	private float _cooldown;

	public FireControl(FireMode[] modes, float roundsPerMinute)
	{
		if (modes.Length == 0)
			throw new ArgumentException("At least one fire mode required", nameof(modes));
		_modes = modes;
		_interval = 60f / roundsPerMinute;
	}

	public FireMode Mode => _modes[_index];

	public void CycleMode() => _index = (_index + 1) % _modes.Length;

	/// <param name="pressed">Trigger went down this frame.</param>
	/// <param name="held">Trigger is down.</param>
	/// <param name="maxShots">Rounds available; caps the result.</param>
	public int Update(float delta, bool pressed, bool held, int maxShots)
	{
		_cooldown -= delta;
		var wants = Mode == FireMode.Auto ? held : pressed;
		if (!wants)
		{
			_cooldown = MathF.Max(_cooldown, 0f);
			return 0;
		}
		_cooldown = MathF.Max(_cooldown, -_interval * 3f);
		var shots = 0;
		while (_cooldown <= 0f && shots < maxShots)
		{
			shots++;
			_cooldown += _interval;
			if (Mode == FireMode.Semi)
				break;
		}
		return shots;
	}
}
