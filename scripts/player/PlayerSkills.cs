using System;

namespace ProjectD;

/// <summary>
/// Turns what the player does in a raid into skill XP (M8a spec s1). Plain class owned by the player: the player calls
/// the methods below where the action happens, and the <see cref="SkillBook"/> decides whether it counts (only inside
/// a raid) and how much. Continuous actions are counted in 5-second units.
/// </summary>
public sealed class PlayerSkills
{
	private readonly Func<SkillBook?> _book;
	private readonly UnitTimer _sprint = new(SkillTraining.UnitSeconds);
	private readonly UnitTimer _carry = new(SkillTraining.UnitSeconds);
	private int _roundsLoaded;
	private string? _carryAction;

	public PlayerSkills(Func<SkillBook?> book) => _book = book;

	private void Report(string skill, string action, float amount = 1f) => _book()?.Report(skill, action, amount);

	/// <summary>Every physics frame: sprinting trains Endurance, moving under a heavy load trains Strength.</summary>
	public void Update(float delta, bool sprinting, float speed, float carriedKg)
	{
		if (sprinting)
		{
			var units = _sprint.Add(delta);
			if (units > 0)
				Report(SkillIds.Endurance, "sprint_5s", units);
		}
		else
			_sprint.Reset();

		var action = speed >= SkillTraining.MovingSpeed ? SkillTraining.CarryAction(carriedKg) : null;
		if (action != _carryAction)
			_carry.Reset(); // a different tier starts a new unit
		_carryAction = action;
		if (action is not null && _carry.Add(delta) is var n and > 0)
			Report(SkillIds.Strength, action, n);
	}

	public void Mantled() => Report(SkillIds.Endurance, "mantle");

	public void Shot() => Report(SkillIds.RecoilControl, "shot");

	public void Reloaded() => Report(SkillIds.WeaponHandling, "reload");

	public void MagChecked() => Report(SkillIds.WeaponHandling, "mag_check");

	public void ChamberChecked() => Report(SkillIds.WeaponHandling, "chamber_check");

	/// <summary>A round went into a magazine; every fifth counts as one unit.</summary>
	public void RoundLoaded()
	{
		if (++_roundsLoaded < SkillTraining.RoundsPerUnit)
			return;
		_roundsLoaded = 0;
		Report(SkillIds.WeaponHandling, "load_5_rounds");
	}

	public void MedUsed() => Report(SkillIds.FirstAid, "med_use");

	public void ItemRevealed() => Report(SkillIds.Searching, "item_revealed");

	/// <summary>A bullet or melee hit landed on the player; the XP scales with the damage that got through.</summary>
	public void HitTaken(float damage) => Report(SkillIds.Vitality, "damage_point", damage);
}
