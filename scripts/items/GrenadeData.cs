using Godot;

namespace ProjectD;

/// <summary>
/// A hand-thrown frag grenade as authored in an item's payload (throwables spec s3 and s8). Numbers are placeholders to
/// tune; the catalog supplies fuse, radius and weight (blast core = catalog damage radius x 0.4).
/// </summary>
[GlobalClass]
public partial class GrenadeData : Resource
{
	[Export] public string Id { get; set; } = "";
	/// <summary>Catalog Fuse s.</summary>
	[Export] public float FuseS { get; set; } = 4.5f;
	/// <summary>Random spread on the fuse of each grenade, so cooking stays a risk.</summary>
	[Export] public float FuseSpreadS { get; set; } = 0.3f;
	[Export] public int Fragments { get; set; } = 70;
	[Export] public float FragmentDamage { get; set; } = 28f;
	[Export] public int FragmentTier { get; set; } = 2;
	[Export] public float FragmentRangeM { get; set; } = 25f;
	/// <summary>Share of fragment damage left at max range.</summary>
	[Export] public float FragmentMinScale { get; set; } = 0.3f;
	/// <summary>Blast core radius, metres (catalog damage radius x 0.4).</summary>
	[Export] public float BlastCoreM { get; set; } = 4.8f;
	/// <summary>Total blast damage at the centre of the core.</summary>
	[Export] public float BlastDamage { get; set; } = 60f;
	[Export] public int BlastTier { get; set; } = 2;
	/// <summary>How far the bang carries for AI hearing, metres.</summary>
	[Export] public float NoiseRadiusM { get; set; } = 200f;
	/// <summary>Stick grenades throw further (catalog note "long throw").</summary>
	[Export] public float ThrowSpeedMult { get; set; } = 1f;

	public GrenadeSpec ToSpec() => new(Id, FuseS, FuseSpreadS, Fragments, FragmentDamage, FragmentTier, FragmentRangeM,
		FragmentMinScale, BlastCoreM, BlastDamage, BlastTier, NoiseRadiusM, ThrowSpeedMult);
}
