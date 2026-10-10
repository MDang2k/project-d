using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>Armor piece definition (GDD s8). Placeholder numbers, tune in data/armor/*.tres.</summary>
[GlobalClass]
public partial class ArmorData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	/// <summary>Armor tier 1-5.</summary>
	[Export] public int Tier { get; set; } = 1;
	[Export] public float MaxDurability { get; set; } = 50f;
	[Export] public float WeightKg { get; set; } = 3f;
	[ExportGroup("Protected zones")]
	[Export] public bool ProtectsHead { get; set; }
	[Export] public bool ProtectsThorax { get; set; }
	[Export] public bool ProtectsStomach { get; set; }
	[Export] public bool ProtectsArms { get; set; }
	[Export] public bool ProtectsLegs { get; set; }

	public ArmorSpec ToSpec()
	{
		var zones = new List<BodyPart>();
		if (ProtectsHead)
			zones.Add(BodyPart.Head);
		if (ProtectsThorax)
			zones.Add(BodyPart.Thorax);
		if (ProtectsStomach)
			zones.Add(BodyPart.Stomach);
		if (ProtectsArms)
		{
			zones.Add(BodyPart.LeftArm);
			zones.Add(BodyPart.RightArm);
		}
		if (ProtectsLegs)
		{
			zones.Add(BodyPart.LeftLeg);
			zones.Add(BodyPart.RightLeg);
		}
		return new ArmorSpec(Id, Tier, MaxDurability, zones);
	}
}
