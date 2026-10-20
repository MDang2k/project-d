using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum GunsmithResult { Ok, CannotFit, StashFull, PartNotOwned, NotAWeapon }

/// <summary>
/// Hub-side weapon modding actions (weapon modding spec): which weapons and parts you own, attaching a
/// part from the stash or your gear, detaching into the stash. A weapon that becomes incomplete while
/// equipped is taken off. Pure logic on top of <see cref="WeaponBuild"/>.
/// </summary>
public static class Gunsmith
{
	private static IEnumerable<InventoryGrid> Grids(GameProfile profile, CharacterInventory character)
		=> new[] { profile.Stash }.Concat(character.AllGrids);

	/// <summary>Every weapon you own: equipped, carried or in the stash.</summary>
	public static IEnumerable<ItemInstance> Weapons(GameProfile profile, CharacterInventory character)
		=> character.EquippedItems.Select(kv => kv.Value)
			.Concat(Grids(profile, character).SelectMany(g => g.Items))
			.Where(i => i.Spec.Category == ItemCategory.Weapon)
			.Distinct()
			.ToList();

	/// <summary>Loose parts in the stash and in carried grids that can be attached.</summary>
	public static IEnumerable<ItemInstance> PartPool(GameProfile profile, CharacterInventory character)
		=> Grids(profile, character).SelectMany(g => g.Items).Where(i => i.Spec.Category == ItemCategory.WeaponPart).ToList();

	public static GunsmithResult Attach(GameProfile profile, CharacterInventory character, ItemInstance weapon,
		string slotId, ItemInstance part)
	{
		if (weapon.Spec.Build is null)
			return GunsmithResult.NotAWeapon;
		var source = Grids(profile, character).FirstOrDefault(g => g.Contains(part));
		if (source is null)
			return GunsmithResult.PartNotOwned;
		if (WeaponBuild.CanAttach(weapon, slotId, part) != AttachFailure.None)
			return GunsmithResult.CannotFit;
		source.Remove(part);
		return WeaponBuild.Attach(weapon, slotId, part) ? GunsmithResult.Ok : GunsmithResult.CannotFit;
	}

	/// <summary>
	/// Takes the part (and its children) off into the stash. If the weapon is equipped and now incomplete it
	/// goes to the stash too; with no room for either, nothing changes.
	/// </summary>
	public static GunsmithResult Detach(GameProfile profile, CharacterInventory character, ItemInstance weapon, string slotId)
	{
		if (WeaponBuild.Find(weapon, slotId) is not { Part: { } part })
			return GunsmithResult.CannotFit;
		var unequip = character.Locate(weapon) is { Slot: { } }
			&& WeaponBuild.Missing(weapon).Count == 0
			&& WouldBecomeIncomplete(weapon, slotId);
		WeaponBuild.Detach(weapon, slotId);
		if (!profile.Stash.TryPlaceAnywhere(part))
		{
			WeaponBuild.Attach(weapon, slotId, part);
			return GunsmithResult.StashFull;
		}
		if (unequip && character.Locate(weapon) is { Slot: { } } && !WeaponBuild.IsComplete(weapon))
		{
			character.Remove(weapon);
			if (!profile.Stash.TryPlaceAnywhere(weapon))
			{
				// no room for the weapon: put everything back
				profile.Stash.Remove(part);
				WeaponBuild.Attach(weapon, slotId, part);
				return GunsmithResult.StashFull;
			}
		}
		return GunsmithResult.Ok;
	}

	private static bool WouldBecomeIncomplete(ItemInstance weapon, string slotId)
	{
		var entry = WeaponBuild.Find(weapon, slotId);
		if (entry is null)
			return false;
		var part = entry.Part!;
		entry.Owner.Parts.Remove(slotId);
		var incomplete = !WeaponBuild.IsComplete(weapon);
		entry.Owner.Parts[slotId] = part;
		return incomplete;
	}

	/// <summary>Why a weapon cannot be used yet, for the UI (empty when complete).</summary>
	public static string MissingText(ItemInstance weapon)
		=> string.Join(", ", WeaponBuild.Missing(weapon).Select(s => s.Id));
}
