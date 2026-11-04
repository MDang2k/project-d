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
		if (_loadout.Rig != "" && ItemDb.Create(_loadout.Rig) is { } rig)
			inv.TryEquip(rig, EquipSlot.Rig);
		if (_loadout.Backpack != "" && _rng.NextDouble() <= S(Stats.Carry.BackpackChance) && ItemDb.Create(_loadout.Backpack) is { } pack)
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
				for (var i = 0; i < SInt(Stats.Carry.SpareMagazines); i++)
				{
					if (InventoryComponent.MakeMagazine(magData.Id, ammo.Id, magData.MagazineCapacity) is { } spare)
						inv.TryAutoStore(spare);
				}
			}
		}

		if (ActorDb.Pockets(Def.Text("pocket_table")) is { } table)
		{
			var rolls = _rng.Next(table.MinRolls, System.Math.Max(table.MinRolls, table.MaxRolls) + 1);
			foreach (var (id, count) in LootRoller.Roll(table.ToEntries(), rolls, _rng.NextDouble))
			{
				if (ItemDb.Create(id, count) is { } loot)
					inv.TryAutoStore(loot);
			}
			foreach (var id in table.GuaranteedIds)
			{
				if (ItemDb.Create(id, 1) is { } guaranteed)
					inv.TryAutoStore(guaranteed);
			}
		}
		Gear = inv;
		ApplyNemesisGear();
	}

	private readonly System.Collections.Generic.List<ItemInstance> _nemesisArmorItems = new();

	/// <summary>
	/// A nemesis fights in what it took from the player (M8c spec s2): the worn armor and helmet with their wear, the best
	/// weapon (primary, then secondary, then pistol; none keeps the preset's), and the real inventory becomes its body.
	/// </summary>
	private void ApplyNemesisGear()
	{
		if (Nemesis is not { } n)
			return;
		Gear = n.Gear;
		_nemesisArmorItems.Clear();
		var worn = new System.Collections.Generic.List<(ArmorSpec, float)>();
		foreach (var slot in new[] { EquipSlot.Vest, EquipSlot.Head })
		{
			if (Gear.InSlot(slot) is not { } item || ItemDb.Data(item.Spec.Id)?.Payload is not ArmorData armor)
				continue;
			var spec = armor.ToSpec();
			worn.Add((spec, item.Durability < 0f ? spec.MaxDurability : item.Durability));
			_nemesisArmorItems.Add(item);
		}
		Health.SetArmor(worn);
		if (NemesisGear.BestWeapon(Gear) is { } weapon && ItemDb.Data(weapon.Spec.Id)?.Payload is WeaponData data)
			_weaponData = data;
	}

	/// <summary>Writes the armor wear it took this raid back onto the items, so the nemesis keeps it (and the save has it).</summary>
	public void SyncNemesisArmor()
	{
		for (var i = 0; i < _nemesisArmorItems.Count && i < Health.Armor.Count; i++)
			_nemesisArmorItems[i].Durability = Health.Armor[i].Durability;
	}

	/// <summary>On death the weapon in the gear gets the magazine and chamber it really had left.</summary>
	private void SyncGearWithWeapon()
	{
		if (Nemesis is not null || Weapon is not { } live || _weaponData is not { } data)
			return; // a nemesis's weapon is the player's real one, with its own magazine
		var slot = data.Ammo is null ? EquipSlot.Primary : (Gear.InSlot(EquipSlot.Primary) is not null ? EquipSlot.Primary : EquipSlot.Pistol);
		if (Gear.InSlot(slot) is not { } item)
			return;
		var magData = ItemDb.MagazineFor(data.Ammo?.Caliber ?? "");
		if (magData is not null && data.Ammo is { } ammo && live.State.Magazine is { } mag)
			item.InsertedMagazine = InventoryComponent.MakeMagazine(magData.Id, ammo.Id, mag.Count);
		item.Chambered = live.State.Chamber?.Id;
	}
}
