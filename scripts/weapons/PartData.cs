using System.Linq;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>
/// Item payload of a weapon part (GDD s11, weapon modding spec). Percent modifiers are fractions
/// (0.05 = +5%) and stack additively across the build. Source of truth: docs/design/catalog/weapon_parts.csv.
/// </summary>
[GlobalClass]
public partial class PartData : Resource
{
	/// <summary>Interface tags this part fits; any one matching an accepting slot is enough.</summary>
	[Export] public string[] Interfaces { get; set; } = System.Array.Empty<string>();
	/// <summary>Slots this part adds to the weapon (a mount adds an optic slot).</summary>
	[Export] public Array<WeaponSlotData> AddsSlots { get; set; } = new();
	/// <summary>Slot ids made unusable while this part is attached.</summary>
	[Export] public string[] Blocks { get; set; } = System.Array.Empty<string>();

	[ExportGroup("Modifiers")]
	/// <summary>Rating points (0-100 scale, can be negative) added to the weapon's ratings.</summary>
	[Export] public float VerticalRecoilPts { get; set; }
	[Export] public float HorizontalRecoilPts { get; set; }
	[Export] public float StabilityPts { get; set; }
	[Export] public float HipStabilityPts { get; set; }
	[Export] public float ErgonomicsPts { get; set; }
	[Export] public float VelocityPct { get; set; }
	[Export] public float NoisePct { get; set; }
	/// <summary>Optics: magnification (0 = none).</summary>
	[Export] public float Zoom { get; set; }
	[Export] public int GridWidthAdd { get; set; }
	[Export] public bool Bipod { get; set; }
	[Export] public bool Light { get; set; }

	[ExportGroup("Clearance")]
	/// <summary>Length on a rail in 10 mm steps (0 = not rail-bound).</summary>
	[Export] public int Steps { get; set; }
	/// <summary>Named zones this part occupies; two parts in one zone conflict.</summary>
	[Export] public string[] Zones { get; set; } = System.Array.Empty<string>();
	/// <summary>Rail surfaces this part provides (parallel arrays): id and length in steps.</summary>
	[Export] public string[] RailIds { get; set; } = System.Array.Empty<string>();
	[Export] public int[] RailLengths { get; set; } = System.Array.Empty<int>();

	public static System.Collections.Generic.Dictionary<string, int> RailMap(string[] ids, int[] lengths)
	{
		var map = new System.Collections.Generic.Dictionary<string, int>();
		for (var i = 0; i < ids.Length && i < lengths.Length; i++)
			map[ids[i]] = lengths[i];
		return map;
	}

	public PartSpec ToSpec() => new(Interfaces.ToList(), AddsSlots.Select(s => s.ToSpec()).ToList(), Blocks.ToList(),
		VerticalRecoilPts, HorizontalRecoilPts, StabilityPts, HipStabilityPts, ErgonomicsPts, VelocityPct, NoisePct,
		Zoom, GridWidthAdd, Bipod, Light, Steps, Zones.ToList(), RailMap(RailIds, RailLengths));
}
