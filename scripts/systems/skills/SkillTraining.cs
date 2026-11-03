namespace ProjectD;

/// <summary>Counts continuous activity in whole units (M8a spec s1: sprinting and carrying count per 5 seconds).</summary>
public sealed class UnitTimer
{
	private readonly float _unit;
	private float _acc;

	public UnitTimer(float unit) => _unit = unit;

	/// <summary>Adds time and returns how many whole units have passed; the remainder carries on.</summary>
	public int Add(float amount)
	{
		if (amount <= 0f)
			return 0;
		_acc += amount;
		var units = (int)(_acc / _unit);
		_acc -= units * _unit;
		return units;
	}

	/// <summary>Drops the part-unit built up so far (activity stopped).</summary>
	public void Reset() => _acc = 0f;
}

/// <summary>What trains which skill and by how much, apart from the numbers in data/skills (M8a spec s1). Pure.</summary>
public static class SkillTraining
{
	/// <summary>Seconds in one unit of a continuous action.</summary>
	public const float UnitSeconds = 5f;

	/// <summary>Rounds loaded into magazines that make one unit of Weapon Handling XP.</summary>
	public const int RoundsPerUnit = 5;

	/// <summary>Carried weight above which moving trains Strength, kilograms.</summary>
	public const float CarryStartKg = 20f;
	public const float HeavyKg = 30f;
	public const float ExtremeKg = 40f;

	/// <summary>Moving slower than this (m/s) counts as standing still.</summary>
	public const float MovingSpeed = 0.5f;

	/// <summary>The Strength action for the weight carried, heavier tiers paying more; null at or below the start weight.</summary>
	public static string? CarryAction(float kg)
		=> kg >= ExtremeKg ? "carry_extreme_5s"
			: kg >= HeavyKg ? "carry_heavy_5s"
			: kg > CarryStartKg ? "carry_light_5s"
			: null;
}
