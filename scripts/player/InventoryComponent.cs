using System;
using Godot;

namespace ProjectD;

/// <summary>
/// Holds a character's <see cref="CharacterInventory"/> and raises Changed so weight, worn armor
/// and held weapons can follow. Items come from <see cref="ItemDb"/>.
/// </summary>
public partial class InventoryComponent : Node
{
	public CharacterInventory Inventory { get; } = new();

	public event Action? Changed;

	public void NotifyChanged() => Changed?.Invoke();

	/// <summary>Creates an item by id; null with a warning if the id is unknown.</summary>
	public static ItemInstance? Make(string id, int count = 1)
	{
		var item = ItemDb.Create(id, count);
		if (item is null)
			GD.PushWarning($"[Inventory] unknown item id '{id}'");
		return item;
	}

	/// <summary>Fills a magazine item with a number of rounds of one ammo id.</summary>
	public static ItemInstance? MakeMagazine(string magId, string ammoId, int rounds)
	{
		var mag = Make(magId);
		if (mag is null)
			return null;
		for (var i = 0; i < rounds && i < mag.Spec.MagazineCapacity; i++)
			mag.Rounds.Add(ammoId);
		return mag;
	}

	/// <summary>Developer loadout used until the stash and traders exist (M7).</summary>
	public void GiveStartingKit()
	{
		Load(Equip("ak_type", EquipSlot.Primary), "ak_mag", "7_62x39", 30);
		Load(Equip("pistol", EquipSlot.Pistol), "pistol_mag", "9x19", 8);
		Equip("combat_knife", EquipSlot.Melee);
		Equip("flak_vest_t2", EquipSlot.Vest);
		Equip("steel_helmet_t1", EquipSlot.Head);
		Equip("light_rig", EquipSlot.Rig);
		Equip("field_pack", EquipSlot.Backpack);
		Equip("secure_pouch", EquipSlot.SecureContainer);

		for (var i = 0; i < 3; i++)
			Store(MakeMagazine("ak_mag", "7_62x39", 30));
		Store(MakeMagazine("ak_mag", "7_62x39", 12));
		for (var i = 0; i < 2; i++)
			Store(MakeMagazine("pistol_mag", "9x19", 8));
		Store(Make("ammo_7_62x39", 60));
		Store(Make("ammo_9x19", 24));
		Store(Make("bandage", 2));
		Store(Make("tourniquet"));
		Store(Make("splint"));
		Store(Make("morphine"));
		Store(Make("canned_food"));
		Store(Make("water_canteen"));
		NotifyChanged();
	}

	private ItemInstance? Equip(string id, EquipSlot slot)
	{
		var item = Make(id);
		if (item is not null && !Inventory.TryEquip(item, slot))
			GD.PushWarning($"[Inventory] could not equip {id} in {slot}");
		return item;
	}

	/// <summary>Inserts a full magazine and chambers a round, as if loaded before the raid.</summary>
	private static void Load(ItemInstance? weapon, string magId, string ammoId, int rounds)
	{
		if (weapon is null)
			return;
		var mag = MakeMagazine(magId, ammoId, rounds);
		if (mag is null || mag.Rounds.Count == 0)
			return;
		mag.Rounds.RemoveAt(mag.Rounds.Count - 1);
		weapon.InsertedMagazine = mag;
		weapon.Chambered = ammoId;
	}

	private void Store(ItemInstance? item)
	{
		if (item is not null && !Inventory.TryAutoStore(item))
			GD.PushWarning($"[Inventory] no room for {item.Spec.Id}");
	}
}
