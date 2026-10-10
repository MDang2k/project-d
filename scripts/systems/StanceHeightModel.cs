using System;

namespace ProjectD;

/// <summary>
/// Variable stand height (hold C and scroll). The crouch has a remembered level between
/// 0 (full crouch) and 1 (full stand); scrolling past 1 stands up, scrolling down from
/// standing crouches. Prone is separate and ignores the wheel. Pure logic.
/// </summary>
public sealed class StanceHeightModel
{
	private readonly float _step;

	public StanceHeightModel(float wheelStep) => _step = wheelStep;

	/// <summary>Remembered crouch height: 0 deepest crouch, 1 standing height.</summary>
	public float CrouchLevel { get; private set; }

	/// <summary>Puts the crouch level back (used when headroom blocks a raise).</summary>
	public void Restore(float crouchLevel) => CrouchLevel = Math.Clamp(crouchLevel, 0f, 1f);

	/// <summary>Height factor of a stance: stand 1, crouch its level, prone 0.</summary>
	public float LevelFor(Stance stance) => stance switch
	{
		Stance.Stand => 1f,
		Stance.Crouch => CrouchLevel,
		_ => 0f,
	};

	/// <summary>
	/// Applies mouse-wheel notches (positive = up) and returns the resulting stance. A crouch
	/// level that reaches 1 becomes Stand; scrolling down from Stand starts a crouch just below it.
	/// </summary>
	public Stance Adjust(Stance stance, float notches)
	{
		if (stance == Stance.Prone || notches == 0f)
			return stance;
		var level = Math.Clamp(LevelFor(stance) + notches * _step, 0f, 1f);
		if (level >= 1f)
			return Stance.Stand;
		CrouchLevel = level;
		return Stance.Crouch;
	}
}
