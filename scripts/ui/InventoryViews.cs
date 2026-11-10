using System;
using Godot;

namespace ProjectD;

/// <summary>
/// The no-ammo-counter rule (CLAUDE.md, GDD s7): round counts are learned through magazine checks, never shown. Dev
/// flag <c>--showammo</c> (off by default) shows them in the inventory, tooltips and trader lists for debugging.
/// </summary>
public static class AmmoDisplay
{
	public static readonly bool ShowCounts = Array.IndexOf(OS.GetCmdlineUserArgs(), "--showammo") >= 0;
}

/// <summary>Placeholder icon drawing shared by grid and slot views (colour block plus label).</summary>
public static class ItemIcon
{
	public static void Draw(CanvasItem canvas, ItemInstance item, Rect2 rect, Font font, bool dim = false)
	{
		var data = ItemDb.Data(item.Spec.Id);
		var color = data?.IconColor ?? new Color(0.5f, 0.5f, 0.5f);
		if (dim)
			color = color.Darkened(0.5f);
		canvas.DrawRect(rect.Grow(-1f), color);
		canvas.DrawRect(rect.Grow(-1f), new Color(0f, 0f, 0f, 0.55f), false, 1.5f);
		var label = !string.IsNullOrEmpty(data?.IconLabel) ? data!.IconLabel : item.Spec.Id;
		canvas.DrawString(font, rect.Position + new Vector2(3f, 14f), label, HorizontalAlignment.Left,
			rect.Size.X - 4f, 12, Colors.White);
		if (item.Insured)
		{
			canvas.DrawRect(new Rect2(rect.End.X - 22f, rect.Position.Y + 1f, 21f, 11f), new Color(0.1f, 0.35f, 0.7f, 0.9f));
			canvas.DrawString(font, new Vector2(rect.End.X - 21f, rect.Position.Y + 10f), "INS", HorizontalAlignment.Left, -1, 9,
				Colors.White);
		}
		if (item.Count > 1)
		{
			canvas.DrawString(font, rect.Position + new Vector2(0f, rect.Size.Y - 4f), $"x{item.Count}",
				HorizontalAlignment.Right, rect.Size.X - 3f, 12, new Color(1f, 1f, 0.6f));
		}
		if (item.Spec.MagazineCapacity > 0 && AmmoDisplay.ShowCounts)
		{
			canvas.DrawString(font, rect.Position + new Vector2(0f, rect.Size.Y - 4f),
				$"{item.Rounds.Count}/{item.Spec.MagazineCapacity}", HorizontalAlignment.Right, rect.Size.X - 3f, 11,
				new Color(0.8f, 1f, 0.8f));
		}
	}
}

/// <summary>Draws one <see cref="InventoryGrid"/>; input is handled by the owning screen.</summary>
public partial class GridView : Control
{
	public const float Cell = 44f;

	public InventoryGrid Grid { get; }
	public string Title { get; }
	/// <summary>Item currently being dragged; it is not drawn in its grid.</summary>
	public Func<ItemInstance?>? Dragged { get; set; }
	/// <summary>Items not yet revealed by a search are drawn as unknown blocks.</summary>
	public Func<ItemInstance, bool>? IsHidden { get; set; }

	public GridView(InventoryGrid grid, string title)
	{
		Grid = grid;
		Title = title;
		CustomMinimumSize = new Vector2(grid.Width * Cell, grid.Height * Cell);
		SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		SizeFlagsVertical = SizeFlags.ShrinkBegin;
		MouseFilter = MouseFilterEnum.Pass;
	}

	public (int X, int Y) CellAt(Vector2 global)
	{
		var local = global - GlobalPosition;
		return ((int)Mathf.Floor(local.X / Cell), (int)Mathf.Floor(local.Y / Cell));
	}

	public ItemInstance? ItemAtGlobal(Vector2 global)
	{
		var (x, y) = CellAt(global);
		return Grid.ItemAt(x, y);
	}

	public override void _Draw()
	{
		var font = ThemeDB.FallbackFont;
		DrawRect(new Rect2(Vector2.Zero, Grid.Width * Cell, Grid.Height * Cell), new Color(0.1f, 0.1f, 0.12f, 0.95f));
		for (var x = 0; x <= Grid.Width; x++)
			DrawLine(new Vector2(x * Cell, 0), new Vector2(x * Cell, Grid.Height * Cell), new Color(0.3f, 0.3f, 0.35f));
		for (var y = 0; y <= Grid.Height; y++)
			DrawLine(new Vector2(0, y * Cell), new Vector2(Grid.Width * Cell, y * Cell), new Color(0.3f, 0.3f, 0.35f));
		var dragged = Dragged?.Invoke();
		foreach (var item in Grid.Items)
		{
			if (Grid.PositionOf(item) is not { } pos)
				continue;
			var rect = new Rect2(pos.X * Cell, pos.Y * Cell, item.Width * Cell, item.Height * Cell);
			if (IsHidden?.Invoke(item) == true)
			{
				DrawRect(rect.Grow(-1f), new Color(0.22f, 0.22f, 0.25f));
				DrawRect(rect.Grow(-1f), new Color(0f, 0f, 0f, 0.6f), false, 1.5f);
				DrawString(font, rect.Position + new Vector2(0f, rect.Size.Y * 0.5f + 7f), "?",
					HorizontalAlignment.Center, rect.Size.X, 20, new Color(0.6f, 0.6f, 0.65f));
				continue;
			}
			ItemIcon.Draw(this, item, rect, font, dim: item == dragged);
		}
	}
}

/// <summary>Draws one equipment slot with its item.</summary>
public partial class SlotView : Control
{
	public EquipSlot Slot { get; }
	public CharacterInventory Inventory { get; }
	public Func<ItemInstance?>? Dragged { get; set; }

	public SlotView(EquipSlot slot, CharacterInventory inventory, Vector2 size)
	{
		Slot = slot;
		Inventory = inventory;
		CustomMinimumSize = size;
		MouseFilter = MouseFilterEnum.Pass;
	}

	public ItemInstance? Item => Inventory.InSlot(Slot);

	public override void _Draw()
	{
		var font = ThemeDB.FallbackFont;
		var rect = new Rect2(Vector2.Zero, Size);
		DrawRect(rect, new Color(0.14f, 0.14f, 0.17f, 0.95f));
		DrawRect(rect, new Color(0.4f, 0.4f, 0.45f), false, 1.5f);
		DrawString(font, new Vector2(4f, 13f), Slot.ToString(), HorizontalAlignment.Left, -1, 11,
			new Color(0.65f, 0.65f, 0.7f));
		if (Item is { } item)
		{
			var inner = new Rect2(6f, 16f, Size.X - 12f, Size.Y - 22f);
			ItemIcon.Draw(this, item, inner, font, dim: item == Dragged?.Invoke());
		}
	}
}
