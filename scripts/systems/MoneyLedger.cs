using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>Counts and removes banknotes across grids (GDD s13: money is an item). Pure logic.</summary>
public static class MoneyLedger
{
	public const string MoneyId = "banknotes";

	public static int Count(IEnumerable<InventoryGrid> grids)
		=> grids.SelectMany(g => g.Items).Where(i => i.Spec.Id == MoneyId).Sum(i => i.Count);

	/// <summary>Removes exactly this much money, taking stacks in grid order; false and no change if there is not enough.</summary>
	public static bool Remove(IReadOnlyList<InventoryGrid> grids, int amount)
	{
		if (amount <= 0)
			return true;
		if (Count(grids) < amount)
			return false;
		var left = amount;
		foreach (var grid in grids)
		{
			foreach (var stack in grid.Items.Where(i => i.Spec.Id == MoneyId).ToList())
			{
				var take = Math.Min(left, stack.Count);
				stack.Count -= take;
				left -= take;
				if (stack.Count <= 0)
					grid.Remove(stack);
				if (left == 0)
					return true;
			}
		}
		return left == 0;
	}

	/// <summary>Splits an amount into stacks no bigger than maxStack.</summary>
	public static List<int> SplitStacks(int amount, int maxStack)
	{
		var stacks = new List<int>();
		var cap = Math.Max(1, maxStack);
		for (var left = amount; left > 0; left -= cap)
			stacks.Add(Math.Min(cap, left));
		return stacks;
	}
}
