using Godot;

namespace ProjectD;

/// <summary>
/// An item lying in the world as a physical object (Tarkov-style). It falls, settles, and is
/// picked up directly with F; no searching. The item instance keeps its state (rounds,
/// durability) while on the ground.
/// </summary>
public partial class DroppedItem : RigidBody3D
{
	private const float LifetimeS = 600f;

	public ItemInstance Item { get; private set; } = null!;

	public string Title => GameText.ItemName(Item.Spec.Id) + (Item.Count > 1 ? $" x{Item.Count}" : "");

	/// <summary>Creates the physical item at a position with an initial velocity.</summary>
	public static DroppedItem Create(ItemInstance item, Vector3 position, Vector3 velocity)
	{
		var d = new DroppedItem { Item = item, Position = position, LinearVelocity = velocity };
		d.Mass = Mathf.Clamp(item.WeightKg, 0.2f, 15f);
		return d;
	}

	public override void _Ready()
	{
		AddToGroup("dropped_items");
		// A box roughly the size of the item's grid footprint (7 cm per cell), flat on the ground.
		var size = new Vector3(Mathf.Max(0.08f, Item.Spec.Width * 0.07f), 0.06f,
			Mathf.Max(0.08f, Item.Spec.Height * 0.07f));
		var color = ItemDb.Data(Item.Spec.Id)?.IconColor ?? new Color(0.5f, 0.5f, 0.5f);
		AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
		});
		GetTree().CreateTimer(LifetimeS).Timeout += QueueFree;
	}
}
