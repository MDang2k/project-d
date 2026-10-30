using Godot;

namespace ProjectD;

/// <summary>
/// A box on the map marking how well lit it is (AI combat v2 spec s2). AI see a target standing inside less far when
/// the zone is dim or dark. Size and place it in the editor; overlapping zones use the smallest one.
/// </summary>
[Tool]
public partial class LightZone : Node3D
{
	[Export] public LightLevel Level { get; set; } = LightLevel.Dim;
	[Export] public Vector3 Size { get; set; } = new(10f, 4f, 10f);

	public override void _Ready()
	{
		if (!Engine.IsEditorHint())
			AddToGroup("light_zones");
	}

	public bool Contains(Vector3 world)
	{
		var local = GlobalTransform.AffineInverse() * world;
		var half = Size * 0.5f;
		return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y && Mathf.Abs(local.Z) <= half.Z;
	}

	public float Volume => Size.X * Size.Y * Size.Z;

	/// <summary>Light a point is in: the smallest zone around it, lit when none.</summary>
	public static LightLevel At(SceneTree tree, Vector3 world)
	{
		if (tree.GetNodeCountInGroup("light_zones") == 0)
			return LightLevel.Lit;
		LightZone? best = null;
		foreach (var node in tree.GetNodesInGroup("light_zones"))
		{
			if (node is LightZone z && z.Contains(world) && (best is null || z.Volume < best.Volume))
				best = z;
		}
		return best?.Level ?? LightLevel.Lit;
	}
}
