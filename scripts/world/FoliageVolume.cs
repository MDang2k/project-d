using Godot;

namespace ProjectD;

/// <summary>
/// Bush or tall grass: a collider on the foliage physics layer. Bullets and the player pass through it (it is not on
/// the world layer) and sight rays are not blocked, but each volume a sight line crosses adds its opacity, so about
/// three layered bushes hide a person (AI combat v2 spec s2).
/// </summary>
[Tool]
public partial class FoliageVolume : StaticBody3D
{
	/// <summary>Physics layer 6, used only by foliage.</summary>
	public const uint Layer = 1u << 5;

	[Export(PropertyHint.Range, "0,1,0.05")] public float Opacity { get; set; } = PerceptionEnvironment.DefaultFoliageOpacity;

	public override void _Ready()
	{
		CollisionLayer = Layer;
		CollisionMask = 0;
		if (!Engine.IsEditorHint())
			AddToGroup("foliage");
	}
}
