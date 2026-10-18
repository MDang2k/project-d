using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// The AI's physical gear: a <see cref="CharacterInventory"/> that mirrors what it fights with (weapon,
/// armor, helmet, rig, backpack, spare magazines, pocket loot). When it dies this becomes the body you
/// search, like in Tarkov.
/// </summary>
public partial class AiHuman
{
	/// <summary>What this AI carries; becomes the body on death.</summary>
	public CharacterInventory Gear { get; private set; } = new();

	/// <summary>Builds the gear inventory from the rolled weapon and armor and the preset's carrying kit.</summary>
	private void BuildGear()
	{
		var inv = new CharacterInventory();
		foreach (var id in _wornIds)
		{
			if (ItemDb.Create(id) is not { } worn)
				continue;
			var slot = worn.Spec.Category == ItemCategory.Helmet ? EquipSlot.Head : EquipSlot.Vest;
			inv.TryEquip(worn, slot);
		}
		if (Preset!.RigId != "" && ItemDb.Create(Preset.RigId) is { } rig)
			inv.TryEquip(rig, EquipSlot.Rig);
		if (Preset.BackpackId != "" && _rng.NextDouble() <= Preset.BackpackChance && ItemDb.Create(Preset.BackpackId) is { } pack)
			inv.TryEquip(pack, EquipSlot.Backpack);
		if (_rng.NextDouble() < 0.5 && ItemDb.Create("combat_knife") is { } knife)
			inv.TryEquip(knife, EquipSlot.Melee);

		if (_weaponData is { } data && ItemDb.Create(data.Id) is { } weapon)
		{
			var slot = weapon.Spec.WeaponClass == WeaponClass.Pistol ? EquipSlot.Pistol : EquipSlot.Primary;
			inv.TryEquip(weapon, slot);
			var caliber = data.Ammo?.Caliber ?? "";
			if (ItemDb.MagazineFor(caliber) is { } magData && data.Ammo is { } ammo)
			{
				for (var i = 0; i < Preset.SpareMagazines; i++)
				{
					if (InventoryComponent.MakeMagazine(magData.Id, ammo.Id, magData.MagazineCapacity) is { } spare)
						inv.TryAutoStore(spare);
				}
			}
		}

		if (Preset.PocketTable is { } table)
		{
			var rolls = _rng.Next(table.MinRolls, System.Math.Max(table.MinRolls, table.MaxRolls) + 1);
			foreach (var (id, count) in LootRoller.Roll(table.ToEntries(), rolls, _rng.NextDouble))
			{
				if (ItemDb.Create(id, count) is { } loot)
					inv.TryAutoStore(loot);
			}
		}
		Gear = inv;
	}

	/// <summary>On death the weapon in the gear gets the magazine and chamber it really had left.</summary>
	private void SyncGearWithWeapon()
	{
		if (Weapon is not { } live || _weaponData is not { } data)
			return;
		var slot = data.Ammo is null ? EquipSlot.Primary : (Gear.InSlot(EquipSlot.Primary) is not null ? EquipSlot.Primary : EquipSlot.Pistol);
		if (Gear.InSlot(slot) is not { } item)
			return;
		var magData = ItemDb.MagazineFor(data.Ammo?.Caliber ?? "");
		if (magData is not null && data.Ammo is { } ammo && live.State.Magazine is { } mag)
			item.InsertedMagazine = InventoryComponent.MakeMagazine(magData.Id, ammo.Id, mag.Count);
		item.Chambered = live.State.Chamber?.Id;
	}
}
