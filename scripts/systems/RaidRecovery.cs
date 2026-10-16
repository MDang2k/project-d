using System;
using System.Collections.Generic;

namespace ProjectD;

/// <summary>
/// Quitting or crashing mid-raid counts as death on next launch (GDD s15). The save written at
/// raid start carries the at-risk loadout and <c>raidInProgress</c>; this applies the death rules.
/// Nemesis and insurance hook in here in later sub-projects.
/// </summary>
public static class RaidRecovery
{
	/// <returns>True when a death was recorded.</returns>
	/// <param name="onLost">Receives the gear lost to the death, so insurance can roll for it (M7d).</param>
	public static bool Apply(bool raidInProgress, RunProgress run, Action<IReadOnlyList<ItemInstance>>? onLost = null)
	{
		if (!raidInProgress)
			return false;
		var inventory = run.Inventory ?? new CharacterInventory();
		var lost = RaidOutcomeRules.Apply(RaidOutcome.Killed, inventory);
		run.Record(RaidOutcome.Killed, inventory);
		onLost?.Invoke(lost);
		return true;
	}
}
