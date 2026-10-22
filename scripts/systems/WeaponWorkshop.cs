using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>A named build: which part item sits in which slot (slot ids are unique per weapon tree).</summary>
public sealed record PartPreset(string Name, string WeaponId, IReadOnlyDictionary<string, string> Parts);

/// <summary>
/// Workshop sandbox rules (weapon modding v2 s6): build with any examined part on a throwaway copy of a
/// weapon, save the build as a named preset, list what is still missing, and apply a preset only from
/// parts you own. Nothing is ever bought or created automatically. Pure logic.
/// </summary>
public static class WeaponWorkshop
{
	/// <summary>Records every part currently attached, nested ones included.</summary>
	public static PartPreset Capture(string name, ItemInstance weapon)
		=> new(name, weapon.Spec.Id, WeaponBuild.Slots(weapon).Where(s => s.Part is not null)
			.ToDictionary(s => s.Spec.Id, s => s.Part!.Spec.Id));

	/// <summary>A fresh copy of the weapon with the preset's parts attached (virtual parts, no ownership).</summary>
	public static ItemInstance Build(PartPreset preset, ItemSpec weaponSpec, Func<string, ItemSpec?> lookup)
	{
		var weapon = ItemInstance.Create(weaponSpec);
		Fill(weapon, preset, id => lookup(id) is { } spec ? ItemInstance.Create(spec) : null, null);
		return weapon;
	}

	/// <summary>Attaches preset parts from a source until nothing more fits (slots appear as parents attach).</summary>
	private static void Fill(ItemInstance weapon, PartPreset preset, Func<string, ItemInstance?> take, Action<ItemInstance>? giveBack)
	{
		for (var pass = 0; pass < 12; pass++)
		{
			var progressed = false;
			foreach (var slot in WeaponBuild.Slots(weapon).ToList())
			{
				if (slot.Part is not null || !preset.Parts.TryGetValue(slot.Spec.Id, out var partId))
					continue;
				var part = take(partId);
				if (part is null)
					continue;
				if (WeaponBuild.Attach(weapon, slot.Spec.Id, part))
					progressed = true;
				else
					giveBack?.Invoke(part);
			}
			if (!progressed)
				break;
		}
	}

	/// <summary>Sandbox copy of a real weapon: same spec and parts (copied), safe to edit.</summary>
	public static ItemInstance Copy(ItemInstance weapon)
	{
		var copy = ItemInstance.Create(weapon.Spec);
		WeaponBuild.CopyParts(weapon, copy);
		return copy;
	}

	/// <summary>Part item ids the player may use in the sandbox: examined, owned loose, or attached to an owned weapon.</summary>
	public static IEnumerable<string> ExaminedIds(GameProfile profile, CharacterInventory character)
		=> profile.ExaminedParts
			.Concat(Gunsmith.PartPool(profile, character).Select(p => p.Spec.Id))
			.Concat(Gunsmith.Weapons(profile, character).SelectMany(WeaponBuild.AllParts).Select(p => p.Spec.Id))
			.Distinct();

	/// <summary>Remembers owned parts so they stay available in the sandbox after being sold.</summary>
	public static void RememberOwned(GameProfile profile, CharacterInventory character)
	{
		foreach (var id in ExaminedIds(profile, character).ToList())
			profile.ExaminedParts.Add(id);
	}

	/// <summary>Part item ids (with counts) the preset needs that are neither attached to the weapon nor owned loose.</summary>
	public static IReadOnlyDictionary<string, int> Missing(PartPreset preset, GameProfile profile, CharacterInventory character,
		ItemInstance? weapon)
	{
		var have = new Dictionary<string, int>();
		void Add(string id) => have[id] = have.GetValueOrDefault(id) + 1;
		foreach (var p in Gunsmith.PartPool(profile, character))
			Add(p.Spec.Id);
		if (weapon is not null)
		{
			foreach (var p in WeaponBuild.AllParts(weapon))
				Add(p.Spec.Id);
		}
		var missing = new Dictionary<string, int>();
		foreach (var need in preset.Parts.Values.GroupBy(id => id))
		{
			var lacking = need.Count() - have.GetValueOrDefault(need.Key);
			if (lacking > 0)
				missing[need.Key] = lacking;
		}
		return missing;
	}

	/// <summary>
	/// Rebuilds the real weapon to match the preset from owned parts. Refused when parts are missing or the stash
	/// cannot take what comes off; an equipped weapon never stays equipped incomplete.
	/// </summary>
	public static GunsmithResult Apply(GameProfile profile, CharacterInventory character, ItemInstance weapon, PartPreset preset)
	{
		if (preset.WeaponId != weapon.Spec.Id)
			return GunsmithResult.CannotFit;
		if (Missing(preset, profile, character, weapon).Count > 0)
			return GunsmithResult.PartNotOwned;
		// Everything off first (a top-level part brings its children), so owned instances are free to reuse.
		foreach (var slot in WeaponBuild.Slots(weapon).Where(s => s.Part is not null && ReferenceEquals(s.Owner, weapon)).ToList())
		{
			var part = WeaponBuild.Detach(weapon, slot.Spec.Id)!;
			if (!profile.Stash.TryPlaceAnywhere(part))
			{
				WeaponBuild.Attach(weapon, slot.Spec.Id, part);
				return GunsmithResult.StashFull;
			}
		}
		ItemInstance? Take(string id)
		{
			foreach (var grid in new[] { profile.Stash }.Concat(character.AllGrids))
			{
				var found = grid.Items.FirstOrDefault(i => i.Spec.Id == id && i.Spec.Category == ItemCategory.WeaponPart);
				if (found is null)
					continue;
				grid.Remove(found);
				return found;
			}
			return null;
		}
		var returned = new List<ItemInstance>();
		Fill(weapon, preset, Take, returned.Add);
		foreach (var part in returned)
			profile.Stash.TryPlaceAnywhere(part);
		Gunsmith.KeepEquippedComplete(profile, character, weapon);
		return GunsmithResult.Ok;
	}
}
