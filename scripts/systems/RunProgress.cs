namespace ProjectD;

/// <summary>
/// What carries over between raids until the save system and hub exist (M7): the character's gear
/// and a few counters. Extraction keeps all gear; death or MIA leaves only the secure container,
/// and the next raid then starts with the basic kit on top of it. Pure logic.
/// </summary>
public sealed class RunProgress
{
	/// <summary>Gear to adopt at the next raid, or null for a fresh character.</summary>
	public CharacterInventory? Inventory { get; private set; }

	/// <summary>True when the next raid must hand out the starting kit (after death or MIA).</summary>
	public bool NeedsKit { get; private set; }

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
		NeedsKit = outcome != RaidOutcome.Extracted;
	}

	/// <summary>Called once the next raid took the gear.</summary>
	public void MarkKitGiven() => NeedsKit = false;

	/// <summary>Replaces the run with state read from the save file.</summary>
	public void Restore(CharacterInventory? inventory, bool needsKit, int raids, int extractions, int deaths, int mia)
	{
		Inventory = inventory;
		NeedsKit = needsKit;
		Raids = raids;
		Extractions = extractions;
		Deaths = deaths;
		Mia = mia;
	}

	public void Reset()
	{
		Inventory = null;
		NeedsKit = false;
		Raids = Extractions = Deaths = Mia = 0;
	}
}

/// <summary>Process-wide holder of the current run (an autoload GameState replaces it in M7).</summary>
public static class RunState
{
	public static RunProgress Current { get; } = new();
}
