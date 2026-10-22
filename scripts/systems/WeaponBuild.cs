using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>One slot of a weapon tree with its owner and whatever is attached.</summary>
public sealed record SlotEntry(WeaponSlotSpec Spec, ItemInstance Owner, ItemInstance? Part, bool Blocked);

public enum AttachFailure { None, NoSuchSlot, SlotTaken, SlotBlocked, WrongInterface, NotAPart, Cycle, RailOverflow, RailOverlap, ZoneConflict }

/// <summary>
/// Weapon modding rules (GDD s11, weapon modding spec): a weapon is a tree of parts. Fit by interface,
/// conflicts, completeness, stat stacking, value and deltas. Pure logic over <see cref="ItemInstance"/>.
/// </summary>
public static class WeaponBuild
{
	// --- the tree ---------------------------------------------------------------------------

	/// <summary>Every slot of the weapon: its own, then those added by attached parts (depth first).</summary>
	public static IEnumerable<SlotEntry> Slots(ItemInstance weapon)
	{
		var blocked = BlockedSlots(weapon);
		return Walk(weapon, blocked);
	}

	private static IEnumerable<SlotEntry> Walk(ItemInstance owner, HashSet<string> blocked)
	{
		var specs = owner.Spec.Build?.Slots ?? owner.Spec.Part?.AddsSlots ?? Array.Empty<WeaponSlotSpec>();
		foreach (var slot in specs)
		{
			owner.Parts.TryGetValue(slot.Id, out var part);
			yield return new SlotEntry(slot, owner, part, blocked.Contains(slot.Id));
			if (part is not null)
			{
				foreach (var child in Walk(part, blocked))
					yield return child;
			}
		}
	}

	/// <summary>Slot ids made unusable by the attached parts' Blocks.</summary>
	public static HashSet<string> BlockedSlots(ItemInstance weapon)
		=> AllParts(weapon).SelectMany(p => p.Spec.Part?.Blocks ?? Array.Empty<string>()).ToHashSet();

	/// <summary>All attached parts, children included (not the weapon itself).</summary>
	public static IEnumerable<ItemInstance> AllParts(ItemInstance weapon)
	{
		foreach (var part in weapon.Parts.Values.ToList())
		{
			yield return part;
			foreach (var child in AllParts(part))
				yield return child;
		}
	}

	public static SlotEntry? Find(ItemInstance weapon, string slotId)
		=> Slots(weapon).FirstOrDefault(s => s.Spec.Id == slotId);

	// --- fitting ----------------------------------------------------------------------------

	public static AttachFailure CanAttach(ItemInstance weapon, string slotId, ItemInstance part)
	{
		if (part.Spec.Part is not { } spec)
			return AttachFailure.NotAPart;
		if (ReferenceEquals(part, weapon) || AllParts(part).Contains(weapon))
			return AttachFailure.Cycle;
		if (Find(weapon, slotId) is not { } slot)
			return AttachFailure.NoSuchSlot;
		if (slot.Blocked)
			return AttachFailure.SlotBlocked;
		if (slot.Part is not null)
			return AttachFailure.SlotTaken;
		if (!spec.Interfaces.Any(slot.Spec.Accepts.Contains))
			return AttachFailure.WrongInterface;
		return Clearance(weapon, slot.Spec, part);
	}

	// --- clearance (rail steps and named zones) ------------------------------------------------

	/// <summary>Rail surfaces on the weapon: the receiver's plus those provided by attached parts.</summary>
	public static Dictionary<string, int> Rails(ItemInstance weapon)
	{
		var rails = new Dictionary<string, int>();
		foreach (var kv in weapon.Spec.Build?.Rails ?? new Dictionary<string, int>())
			rails[kv.Key] = kv.Value;
		foreach (var p in AllParts(weapon))
			foreach (var kv in p.Spec.Part?.Rails ?? new Dictionary<string, int>())
				rails[kv.Key] = kv.Value;
		return rails;
	}

	private static IEnumerable<string> ZonesOf(ItemInstance part)
		=> part.Spec.Part?.Zones ?? Array.Empty<string>();

	/// <summary>None when a part fits its slot's rail range and shares no zone with what is attached.</summary>
	private static AttachFailure Clearance(ItemInstance weapon, WeaponSlotSpec slot, ItemInstance part)
	{
		var len = part.Spec.Part!.Steps;
		if (slot.Rail != "" && len > 0)
		{
			if (Rails(weapon).TryGetValue(slot.Rail, out var railLength) && slot.RailStart + len > railLength)
				return AttachFailure.RailOverflow;
			foreach (var other in Slots(weapon))
			{
				if (other.Part is null || other.Spec.Rail != slot.Rail)
					continue;
				var olen = other.Part.Spec.Part?.Steps ?? 0;
				if (olen > 0 && slot.RailStart < other.Spec.RailStart + olen && other.Spec.RailStart < slot.RailStart + len)
					return AttachFailure.RailOverlap;
			}
		}
		var incoming = ZonesOf(part).Concat(AllParts(part).SelectMany(ZonesOf)).ToHashSet();
		if (incoming.Count > 0 && AllParts(weapon).SelectMany(ZonesOf).Any(incoming.Contains))
			return AttachFailure.ZoneConflict;
		return AttachFailure.None;
	}

	public static bool Attach(ItemInstance weapon, string slotId, ItemInstance part)
	{
		if (CanAttach(weapon, slotId, part) != AttachFailure.None)
			return false;
		Find(weapon, slotId)!.Owner.Parts[slotId] = part;
		return true;
	}

	/// <summary>Removes the part in a slot together with everything attached to it (one sub-assembly).</summary>
	public static ItemInstance? Detach(ItemInstance weapon, string slotId)
	{
		if (Find(weapon, slotId) is not { Part: { } part } entry)
			return null;
		entry.Owner.Parts.Remove(slotId);
		return part;
	}

	/// <summary>Parts from a pool that would fit a given slot right now.</summary>
	public static IEnumerable<ItemInstance> Fitting(ItemInstance weapon, string slotId, IEnumerable<ItemInstance> pool)
		=> pool.Where(p => CanAttach(weapon, slotId, p) == AttachFailure.None);

	// --- completeness -----------------------------------------------------------------------

	/// <summary>Required slots that are empty, including required slots added by attached parts.</summary>
	public static IReadOnlyList<WeaponSlotSpec> Missing(ItemInstance weapon)
		=> Slots(weapon).Where(s => s.Spec.Required && s.Part is null && !s.Blocked).Select(s => s.Spec).ToList();

	/// <summary>Non-weapons and weapons without a slot tree are always complete.</summary>
	public static bool IsComplete(ItemInstance weapon) => Missing(weapon).Count == 0;

	// --- defaults ---------------------------------------------------------------------------

	/// <summary>Attaches the weapon's default parts (and those of any slots they add) from a part factory.</summary>
	public static void ApplyDefaults(ItemInstance weapon, Func<string, ItemInstance?> makePart)
	{
		if (weapon.Spec.Build is not { } build)
			return;
		for (var pass = 0; pass < 8; pass++)
		{
			var attached = false;
			foreach (var slot in Slots(weapon).ToList())
			{
				if (slot.Part is not null || slot.Blocked || !build.DefaultParts.TryGetValue(slot.Spec.Id, out var partId))
					continue;
				if (makePart(partId) is { } part && Attach(weapon, slot.Spec.Id, part))
					attached = true;
			}
			if (!attached)
				break;
		}
	}

	/// <summary>A deep copy of the attached parts onto a fresh weapon (used for returned insured weapons).</summary>
	public static void CopyParts(ItemInstance from, ItemInstance to)
	{
		to.Parts.Clear();
		foreach (var (slotId, part) in from.Parts)
		{
			var copy = ItemInstance.Create(part.Spec, 1);
			copy.Durability = part.Durability;
			CopyParts(part, copy);
			to.Parts[slotId] = copy;
		}
	}

	// --- stats ------------------------------------------------------------------------------

	/// <summary>
	/// Final numbers: part points add to the weapon's base ratings (each clamped to 0-100), percent modifiers
	/// add. <paramref name="recoilSkillPts"/> is the Recoil Control skill bonus on both recoil ratings.
	/// </summary>
	public static WeaponStats Stats(ItemInstance weapon, float recoilSkillPts = 0f)
	{
		var parts = AllParts(weapon).Select(p => p.Spec.Part).Where(p => p is not null).Select(p => p!).ToList();
		float Sum(Func<PartSpec, float> f) => parts.Sum(f);
		var build = weapon.Spec.Build;
		var r = build?.Ratings ?? new WeaponRatings();
		var width = weapon.Spec.Width + parts.Sum(p => p.GridWidthAdd);
		float Rate(float baseValue, float pts) => Math.Clamp(baseValue + pts, 0f, 100f);
		return new WeaponStats(
			Rate(r.VerticalRecoil, Sum(p => p.VerticalRecoilPts) + recoilSkillPts),
			Rate(r.HorizontalRecoil, Sum(p => p.HorizontalRecoilPts) + recoilSkillPts),
			Rate(r.Stability, Sum(p => p.StabilityPts)),
			Rate(r.HipStability, Sum(p => p.HipStabilityPts)),
			Rate(build?.Ergonomics ?? 50f, Sum(p => p.ErgonomicsPts)),
			Math.Max(0.1f, 1f + Sum(p => p.VelocityPct)),
			Math.Max(0.1f, 1f + Sum(p => p.NoisePct)),
			parts.Count == 0 ? 0f : parts.Max(p => p.Zoom),
			TotalWeightKg(weapon),
			TotalValue(weapon),
			width,
			(build?.IntegratedBipod ?? false) || parts.Any(p => p.Bipod),
			parts.Any(p => p.Light));
	}

	/// <summary>Weapon weight plus every attached part (magazines and rounds are counted by the item itself).</summary>
	public static float TotalWeightKg(ItemInstance weapon)
		=> weapon.Spec.WeightKg * weapon.Count + AllParts(weapon).Sum(p => p.Spec.WeightKg);

	/// <summary>Trade value of the weapon with all its parts (insured and sold as one unit).</summary>
	public static int TotalValue(ItemInstance weapon)
		=> weapon.Spec.BaseValue + AllParts(weapon).Sum(p => p.Spec.BaseValue);

	/// <summary>Stats if this part replaced whatever is in the slot (UI preview); null when it cannot fit.</summary>
	public static (WeaponStats Before, WeaponStats After)? Preview(ItemInstance weapon, string slotId, ItemInstance part)
	{
		var before = Stats(weapon);
		var entry = Find(weapon, slotId);
		if (entry is null)
			return null;
		var existing = entry.Part;
		if (existing is not null)
			entry.Owner.Parts.Remove(slotId);
		try
		{
			if (CanAttach(weapon, slotId, part) != AttachFailure.None)
				return null;
			entry.Owner.Parts[slotId] = part;
			var after = Stats(weapon);
			entry.Owner.Parts.Remove(slotId);
			return (before, after);
		}
		finally
		{
			if (existing is not null)
				entry.Owner.Parts[slotId] = existing;
		}
	}
}
