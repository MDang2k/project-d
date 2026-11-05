using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Simple debug map (dev aid): a round top-down radar at the top left, north up, showing the player, the knowledge
/// documents still lying on the map (the German lab note in gold), Ewigstein deposits and extraction zones. Anything
/// outside the radar range is pinned to its rim as an arrow, and the lab note also gets a line with its distance and
/// bearing. Toggle in the F1 debug menu.
/// </summary>
public partial class DebugMapOverlay : Control
{
	private const float RadiusPx = 110f;
	private const float RangeM = 120f;
	private const string LabNoteItem = "german_lab_note_probe_7";

	private Player _player = null!;

	public void Start(Player player)
	{
		_player = player;
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (Visible)
			QueueRedraw();
	}

	public override void _Draw()
	{
		var centre = new Vector2(RadiusPx + 20f, RadiusPx + 70f);
		var font = ThemeDB.FallbackFont;
		DrawCircle(centre, RadiusPx, new Color(0.05f, 0.07f, 0.06f, 0.6f));
		DrawArc(centre, RadiusPx, 0f, Mathf.Tau, 64, new Color(0.7f, 0.8f, 0.7f, 0.6f), 1.5f);
		DrawString(font, centre + new Vector2(-4f, -RadiusPx + 14f), "N", HorizontalAlignment.Left, -1, 12, new Color(0.9f, 0.9f, 0.9f, 0.8f));
		var origin = _player.GlobalPosition;

		void Mark(Vector3 world, Color color, string label, float size = 4f)
		{
			var offset = new Vector2(world.X - origin.X, world.Z - origin.Z) / RangeM * RadiusPx;
			if (offset.Length() > RadiusPx - 6f)
			{
				var dir = offset.Normalized();
				var tip = centre + dir * (RadiusPx - 2f);
				var side = new Vector2(-dir.Y, dir.X) * 4f;
				DrawColoredPolygon(new[] { tip, tip - dir * 9f + side, tip - dir * 9f - side }, color);
				return;
			}
			DrawCircle(centre + offset, size, color);
			if (label != "")
				DrawString(font, centre + offset + new Vector2(6f, 4f), label, HorizontalAlignment.Left, -1, 11, color);
		}

		foreach (var zone in GetTree().GetNodesInGroup("extraction_zones").OfType<Node3D>())
			Mark(zone.GlobalPosition, new Color(0.4f, 0.8f, 1f), "EXIT", 5f);
		foreach (var dep in GetTree().GetNodesInGroup("ewigstein_deposits").OfType<Node3D>())
			Mark(dep.GlobalPosition, new Color(0.6f, 0.95f, 0.95f), "crystal");
		Vector3? labNote = null;
		foreach (var spot in GetTree().GetNodesInGroup("knowledge_spots").OfType<KnowledgeSpot>())
		{
			if (spot.ItemId == "" || !(GameState.Instance?.Profile.Knowledge.ShouldSpawn(spot.ItemId) ?? true))
				continue;
			var lab = spot.ItemId == LabNoteItem;
			if (lab)
				labNote = spot.GlobalPosition;
			Mark(spot.GlobalPosition, lab ? new Color(1f, 0.85f, 0.3f) : new Color(0.85f, 0.85f, 0.7f), lab ? "LAB NOTE" : "doc", lab ? 6f : 3f);
		}

		// the player: a triangle pointing the way they face
		var forward = -_player.GlobalBasis.Z;
		var facing = new Vector2(forward.X, forward.Z).Normalized();
		var perp = new Vector2(-facing.Y, facing.X);
		DrawColoredPolygon(new[] { centre + facing * 9f, centre - facing * 6f + perp * 5f, centre - facing * 6f - perp * 5f }, new Color(0.4f, 1f, 0.4f));

		if (labNote is { } target)
		{
			var delta = target - origin;
			var flat = new Vector2(delta.X, delta.Z);
			var bearing = Mathf.RadToDeg(flat.AngleTo(facing)) * -1f; // + is to the right of where you look
			var side = bearing switch { > 15f => "right", < -15f => "left", _ => "ahead" };
			DrawString(font, centre + new Vector2(-RadiusPx, RadiusPx + 18f),
				$"Lab note: {flat.Length():0} m, {side} ({Mathf.Abs(bearing):0} deg)", HorizontalAlignment.Left, -1, 14, new Color(1f, 0.85f, 0.3f));
		}
		else
		{
			DrawString(font, centre + new Vector2(-RadiusPx, RadiusPx + 18f), "No lab note on this map", HorizontalAlignment.Left, -1, 14, new Color(0.8f, 0.8f, 0.8f));
		}
	}
}
