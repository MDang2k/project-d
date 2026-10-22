using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

public enum SwapKind { Free, Tooled, Never }

public enum FieldStripResult { Ok, NeedsKit, Never, NoRoom, NotOwned, CannotFit, NothingThere }

/// <summary>Seconds per swap in a raid (weapon modding v2 s7, tune).</summary>
public sealed record SwapTuning(
	float FreeOpticS = 2.5f,
	float FreeLightS = 2f,
	float FreeOtherS = 3.5f,
	float ToolMinorS = 8f,
	float ToolMajorS = 12f,
	float ToolBarrelS = 15f)
{
	public static SwapTuning Default { get; } = new();
}

/// <summary>
/// Swapping parts on the weapon inside a raid: tool-free accessories for anyone, tooled parts only with the
/// field armorer's kit in rig or pockets, receivers never. Pure logic.
/// </summary>
public static class FieldStrip
{
	public const string KitItemId = "field_armorer_kit";

	/// <summary>How a part may be swapped (catalog column "In-raid swap"); receivers never.</summary>
	public static SwapKind Kind(ItemInstance part) => part.Spec.Part?.Swap ?? SwapKind.Never;

	public static float Duration(WeaponSlotSpec slot, SwapKind kind, SwapTuning? tuning = null)
	{
		var t = tuning ?? SwapTuning.Default;
		if (kind == SwapKind.Free)
		{
			return slot.Type switch
			{
				"optic" => t.FreeOpticS,
				"light" => t.FreeLightS,
				_ => t.FreeOtherS,
			};
		}
		return slot.Type switch
		{
			"barrel" => t.ToolBarrelS,
			"bolt" or "gas" => t.ToolMajorS,
			_ => t.ToolMinorS,
		};
	}

	/// <summary>The kit counts only when carried on the person: pockets, vest or rig (not backpack or secure container).</summary>
	public static bool HasKit(CharacterInventory character)
		=> character.Pockets.Concat(new[] { EquipSlot.Vest, EquipSlot.Rig }.Where(s => character.InSlot(s) is not null)
			.SelectMany(s => character.InSlot(s)!.Grids))
			.Any(g => g.Items.Any(i => i.Spec.Id == KitItemId));

	/// <summary>Whether this part may be worked on right now (kit, never rule); says nothing about fit or room.</summary>
	public static FieldStripResult CanWork(CharacterInventory character, ItemInstance part) => Kind(part) switch
	{
		SwapKind.Never => FieldStripResult.Never,
		SwapKind.Tooled when !HasKit(character) => FieldStripResult.NeedsKit,
		_ => FieldStripResult.Ok,
	};

	/// <summary>Parts carried on the person that could be installed.</summary>
	public static IEnumerable<ItemInstance> CarriedParts(CharacterInventory character)
		=> character.StorageGrids.SelectMany(g => g.Items).Where(i => i.Spec.Category == ItemCategory.WeaponPart).ToList();

	/// <summary>Takes the part (with its children) off into a carried grid. Nothing changes when there is no room.</summary>
	public static FieldStripResult Remove(CharacterInventory character, ItemInstance weapon, string slotId)
	{
		if (WeaponBuild.Find(weapon, slotId) is not { Part: { } part } entry)
			return FieldStripResult.NothingThere;
		if (CanWork(character, part) is var gate && gate != FieldStripResult.Ok)
			return gate;
		WeaponBuild.Detach(weapon, slotId);
		if (character.StorageGrids.Any(g => g.TryPlaceAnywhere(part)))
			return FieldStripResult.Ok;
		WeaponBuild.Attach(weapon, slotId, part);
		return FieldStripResult.NoRoom;
	}

	/// <summary>Mounts a carried part into an empty slot.</summary>
	public static FieldStripResult Install(CharacterInventory character, ItemInstance weapon, string slotId, ItemInstance part)
	{
		if (WeaponBuild.Find(weapon, slotId) is null)
			return FieldStripResult.CannotFit;
		if (CanWork(character, part) is var gate && gate != FieldStripResult.Ok)
			return gate;
		var source = character.StorageGrids.FirstOrDefault(g => g.Contains(part));
		if (source is null)
			return FieldStripResult.NotOwned;
		if (WeaponBuild.CanAttach(weapon, slotId, part) != AttachFailure.None)
			return FieldStripResult.CannotFit;
		source.Remove(part);
		return WeaponBuild.Attach(weapon, slotId, part) ? FieldStripResult.Ok : FieldStripResult.CannotFit;
	}
}

/// <summary>A running swap: counts down, cancels on damage or sprinting, completes once. Pure logic.</summary>
public sealed class FieldSwapTimer
{
	private Action? _apply;

	public bool Active { get; private set; }
	public float Total { get; private set; }
	public float Elapsed { get; private set; }
	public string Label { get; private set; } = "";
	public float Progress => Total > 0f ? Math.Clamp(Elapsed / Total, 0f, 1f) : 0f;

	public event Action? Cancelled;

	/// <param name="apply">Runs once when the time is up.</param>
	public bool Start(float seconds, string label, Action apply)
	{
		if (Active)
			return false;
		Active = true;
		Total = seconds;
		Elapsed = 0f;
		Label = label;
		_apply = apply;
		return true;
	}

	public void Update(float delta, bool interrupted)
	{
		if (!Active)
			return;
		if (interrupted)
		{
			Cancel();
			return;
		}
		Elapsed += delta;
		if (Elapsed < Total)
			return;
		var apply = _apply;
		Reset();
		apply?.Invoke();
	}

	public void Cancel()
	{
		if (!Active)
			return;
		Reset();
		Cancelled?.Invoke();
	}

	private void Reset()
	{
		Active = false;
		_apply = null;
		Elapsed = 0f;
		Total = 0f;
		Label = "";
	}
}
