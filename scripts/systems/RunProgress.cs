namespace ProjectD;

/// <summary>
/// What carries over between raids: the character's gear and a few counters. Extraction keeps all
/// gear; death or MIA leaves only the secure container. The stash and side live in
/// <see cref="GameProfile"/>; new gear only comes from the hub. Pure logic.
/// </summary>
public sealed class RunProgress
{
	/// <summary>Gear to adopt at the next raid, or null for a fresh character.</summary>
	public CharacterInventory? Inventory { get; private set; }

	/// <summary>Quick-use bindings; they survive raids.</summary>
	public QuickUseBar QuickUse { get; } = CreateBar();

	private static QuickUseBar CreateBar()
	{
		var bar = new QuickUseBar();
		bar.ApplyDefaults();
		return bar;
	}

	public int Raids { get; private set; }
	public int Extractions { get; private set; }
	public int Deaths { get; private set; }
	public int Mia { get; private set; }

	/// <summary>Records a finished raid. The inventory has already had the outcome rules applied.</summary>
	public void Record(RaidOutcome outcome, CharacterInventory inventory)
	{
		if (outcome == RaidOutcome.None)
			return;
		Raids++;
		switch (outcome)
		{
			case RaidOutcome.Extracted:
				Extractions++;
				break;
			case RaidOutcome.Killed:
				Deaths++;
				break;
			case RaidOutcome.Mia:
				Mia++;
				break;
		}
		Inventory = inventory;
	}

	/// <summary>Replaces the run with state read from the save file.</summary>
	public void Restore(CharacterInventory? inventory, int raids, int extractions, int deaths, int mia)
	{
		Inventory = inventory;
		Raids = raids;
		Extractions = extractions;
		Deaths = deaths;
		Mia = mia;
	}

	public void Reset()
	{
		Inventory = null;
		Raids = Extractions = Deaths = Mia = 0;
	}
}

/// <summary>Process-wide holder of the current run (an autoload GameState replaces it in M7).</summary>
public static class RunState
{
	public static RunProgress Current { get; } = new();
}
