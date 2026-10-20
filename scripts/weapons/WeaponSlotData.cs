using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>One attachment point in a .tres file (weapon slot tree or the slots a part adds).</summary>
[GlobalClass]
public partial class WeaponSlotData : Resource
{
	[Export] public string Id { get; set; } = "";
	/// <summary>optic, mount, muzzle, underbarrel, light, stock, barrel, handguard, grip, receiver_cover, trigger.</summary>
	[Export] public string Type { get; set; } = "";
	/// <summary>Interface tags this slot accepts.</summary>
	[Export] public string[] Accepts { get; set; } = System.Array.Empty<string>();
	[Export] public bool Required { get; set; }
	/// <summary>Swappable inside a raid.</summary>
	[Export] public bool Accessory { get; set; }
	/// <summary>Model marker the part sits at (Muzzle, OpticMount, BipodMount, ...).</summary>
	[Export] public string Marker { get; set; } = "";

	public WeaponSlotSpec ToSpec() => new(Id, Type, Accepts.ToList(), Required, Accessory, Marker);
}
