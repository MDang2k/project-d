using System.Collections.Generic;

namespace ProjectD;

/// <summary>The side the player works for (GDD s5). Chosen once, at the first hub visit.</summary>
public enum PlayerSide { Allied, Soviet }

public static class PlayerSideExtensions
{
	public static Faction ToFaction(this PlayerSide side)
		=> side == PlayerSide.Allied ? Faction.AlliedPmc : Faction.SovietPmc;
}

/// <summary>
/// Long-lived player data that lives in the hub (GDD s10, s15): the chosen side and the stash. The
/// carried character lives in <see cref="RunProgress"/>. Pure logic.
/// </summary>
public sealed class GameProfile
{
	public const int StashWidth = 10;
	public const int StashHeight = 30;

	public PlayerSide? Side { get; set; }

	public InventoryGrid Stash { get; private set; } = new(StashWidth, StashHeight);

	public bool HasChosenSide => Side is not null;

	/// <summary>Replaces the stash (loading a save).</summary>
	public void ReplaceStash(InventoryGrid stash) => Stash = stash;

	public void Reset()
	{
		Side = null;
		Stash = new InventoryGrid(StashWidth, StashHeight);
	}

	/// <summary>Every item in the stash, nested grids included.</summary>
	public IEnumerable<ItemInstance> AllStashItems()
	{
		foreach (var item in Stash.Items)
		{
			yield return item;
			foreach (var nested in StarterKit.Flatten(item))
				yield return nested;
		}
	}
}
