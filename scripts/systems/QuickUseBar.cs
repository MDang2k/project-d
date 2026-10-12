using System;
using System.Linq;

namespace ProjectD;

/// <summary>
/// Four quick-use slots bound to item ids (GDD s18 HUD). Pressing a slot uses the first matching
/// item that is carried (not the secure container). Pure logic.
/// </summary>
public sealed class QuickUseBar
{
	public const int SlotCount = 4;

	private readonly string?[] _ids = new string?[SlotCount];

	public event Action<int>? Changed;

	public string? IdAt(int slot) => slot is >= 0 and < SlotCount ? _ids[slot] : null;

	/// <summary>Binds an item id to a slot; the same id is removed from any other slot.</summary>
	public void Bind(int slot, string itemId)
	{
		if (slot is < 0 or >= SlotCount)
			return;
		for (var i = 0; i < SlotCount; i++)
		{
			if (_ids[i] == itemId && i != slot)
			{
				_ids[i] = null;
				Changed?.Invoke(i);
			}
		}
		_ids[slot] = itemId;
		Changed?.Invoke(slot);
	}

	public void Clear(int slot)
	{
		if (slot is < 0 or >= SlotCount || _ids[slot] is null)
			return;
		_ids[slot] = null;
		Changed?.Invoke(slot);
	}

	/// <summary>First carried item matching the slot's id, or null when unbound or none left.</summary>
	public ItemInstance? Resolve(int slot, CharacterInventory inventory)
	{
		var id = IdAt(slot);
		return id is null
			? null
			: inventory.StorageGrids.SelectMany(g => g.Items).FirstOrDefault(i => i.Spec.Id == id && i.Count > 0);
	}

	/// <summary>How many of the slot's item are carried (stack counts summed).</summary>
	public int CountOf(int slot, CharacterInventory inventory)
	{
		var id = IdAt(slot);
		return id is null
			? 0
			: inventory.StorageGrids.SelectMany(g => g.Items).Where(i => i.Spec.Id == id).Sum(i => Math.Max(1, i.Count));
	}

	/// <summary>Slots bound at the start of a character's run, editable from the inventory.</summary>
	public void ApplyDefaults()
	{
		_ids[0] = "bandage";
		_ids[1] = "morphine";
		_ids[2] = "canned_food";
		_ids[3] = "water_canteen";
	}
}
