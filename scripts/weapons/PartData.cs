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
	[Export] public float RecoilPct { get; set; }
	[Export] public float ErgonomicsPts { get; set; }
	[Export] public float SwayPct { get; set; }
	[Export] public float DispersionPct { get; set; }
	[Export] public float VelocityPct { get; set; }
	[Export] public float NoisePct { get; set; }
	/// <summary>Optics: magnification (0 = none).</summary>
	[Export] public float Zoom { get; set; }
	[Export] public int GridWidthAdd { get; set; }
	[Export] public bool Bipod { get; set; }
	[Export] public bool Light { get; set; }

	public PartSpec ToSpec() => new(Interfaces.ToList(), AddsSlots.Select(s => s.ToSpec()).ToList(), Blocks.ToList(),
		RecoilPct, ErgonomicsPts, SwayPct, DispersionPct, VelocityPct, NoisePct, Zoom, GridWidthAdd, Bipod, Light);
}
