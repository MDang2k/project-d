using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ProjectD;

/// <summary>Draws the item being dragged on top of every panel.</summary>
public partial class DragOverlay : Control
{
	public Func<(ItemInstance Item, Vector2 TopLeft)?>? Source { get; set; }

	public DragOverlay()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
	}

	public override void _Draw()
	{
		if (Source?.Invoke() is not { } held)
			return;
		var size = new Vector2(held.Item.Width * GridView.Cell, held.Item.Height * GridView.Cell);
		ItemIcon.Draw(this, held.Item, new Rect2(held.TopLeft, size), ThemeDB.FallbackFont);
	}
}

/// <summary>
/// Tarkov-style inventory screen (GDD s10, s18): drag and drop between grids and equipment slots,
/// R rotates the held item, double-click uses or equips, tooltips show size, weight and stats.
/// The raid keeps running while it is open. Placeholder UI art.
/// </summary>
public partial class InventoryScreen : CanvasLayer
{
	private Player _player = null!;
	private Control _root = null!;
	private VBoxContainer _content = null!;
	private Label _header = null!;
	private PanelContainer _tooltip = null!;
	private Label _tooltipText = null!;
	private DragOverlay _drag = null!;
	private HealthPanel _health = null!;
	private readonly List<GridView> _grids = new();
	private readonly List<SlotView> _slots = new();

	private LootContainer? _container;
	private ProgressBar? _searchBar;
	private Label? _searchLabel;
	private ItemInstance? _held;
	private ItemLocation _heldFrom;
	private bool _heldWasRotated;
	private Vector2 _grabOffset;

	public bool IsOpen => _root.Visible;
	public LootContainer? Container => _container;

	/// <summary>Opens the inventory with a world container beside it and starts searching it.</summary>
	public void OpenWithContainer(LootContainer container)
	{
		_container?.Model.StopSearch();
		_container = container;
		container.Model.StartSearch();
		if (IsOpen)
			Rebuild();
		else
			Open();
	}

	public void Bind(Player player)
	{
		_player = player;
		player.Inventory.Changed += () =>
		{
			if (IsOpen)
				Rebuild();
		};
	}

	public override void _Ready()
	{
		Layer = 5;
		_root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(dim);

		var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		foreach (var side in new[] { "left", "right", "top", "bottom" })
			margin.AddThemeConstantOverride($"margin_{side}", 24);
		_root.AddChild(margin);
		_content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		margin.AddChild(_content);

		_tooltipText = new Label();
		_tooltip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		_tooltip.AddChild(_tooltipText);
		_root.AddChild(_tooltip);

		_drag = new DragOverlay { Source = HeldForDraw };
		_root.AddChild(_drag);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("inventory") && !@event.IsEcho())
		{
			Toggle();
			GetViewport().SetInputAsHandled();
			return;
		}
		if (!IsOpen)
			return;
		switch (@event)
		{
			case InputEventKey { Pressed: true, PhysicalKeycode: Key.R, Echo: false } when _held is not null:
				_held.Rotated = !_held.Rotated;
				_drag.QueueRedraw();
				GetViewport().SetInputAsHandled();
				break;
			case InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape }:
				Close();
				GetViewport().SetInputAsHandled();
				break;
			case InputEventKey { Pressed: true, Echo: false } when _held is null && TryBindQuickUse(@event):
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
				if (mb.Pressed)
					OnPress(mb.GlobalPosition, mb.DoubleClick);
				else
					OnRelease(mb.GlobalPosition);
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseMotion mm:
				OnMove(mm.GlobalPosition);
				break;
		}
	}

	/// <summary>Hover an item and press 4-7 to bind it to that quick-use slot.</summary>
	private bool TryBindQuickUse(InputEvent e)
	{
		for (var slot = 0; slot < QuickUseBar.SlotCount; slot++)
		{
			if (!e.IsActionPressed($"quick_use_{slot + 1}"))
				continue;
			if (ItemAt(GetViewport().GetMousePosition()) is not { } hit
				|| ItemDb.Data(hit.Item.Spec.Id)?.Payload is not (MedicalData or ConsumableData))
				return true;
			_player.QuickUse.Bind(slot, hit.Item.Spec.Id);
			_tooltipText.Text = GameText.Get("msg.quickuse.bound", GameText.ItemName(hit.Item.Spec.Id), slot + 4);
			_tooltip.Visible = true;
			return true;
		}
		return false;
	}

	public void Toggle()
	{
		if (IsOpen)
			Close();
		else
			Open();
	}

	private void Open()
	{
		_root.Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Rebuild();
	}

	/// <summary>Closes only the container column (player walked away), keeping the inventory open.</summary>
	public void CloseContainer()
	{
		_container?.Model.StopSearch();
		_container = null;
		if (IsOpen)
			Rebuild();
	}

	private void Close()
	{
		_container?.Model.StopSearch();
		_container = null;
		CancelDrag();
		_tooltip.Visible = false;
		_root.Visible = false;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Process(double delta)
	{
		if (!IsOpen)
			return;
		_header.Text = $"Weight {_player.TotalCarriedKg:0.0} kg ({_player.Tier})   |   Drag to move, R rotate, " +
			"double-click use, equip or unload, drop ammo on a magazine to load, " +
			"release over empty space to drop, Tab or Esc close.   " +
			_player.LoaderStatus;
		if (_container is not null && _searchBar is not null && _searchLabel is not null)
		{
			var m = _container.Model;
			_searchBar.Value = m.Progress * 100.0;
			_searchLabel.Text = m.FullyRevealed ? "Search complete" : $"Searching... {m.HiddenCount} unknown";
		}
		foreach (var g in _grids)
			g.QueueRedraw();
		foreach (var s in _slots)
			s.QueueRedraw();
		_health.QueueRedraw();
	}

	// --- layout ---------------------------------------------------------------------------------

	private void Rebuild()
	{
		foreach (var child in _content.GetChildren())
			child.QueueFree();
		_grids.Clear();
		_slots.Clear();
		var inv = _player.Inventory.Inventory;

		_header = new Label();
		_content.AddChild(_header);

		var columns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		columns.AddThemeConstantOverride("separation", 18);
		_content.AddChild(columns);

		// Left: equipment slots.
		var equip = new VBoxContainer();
		equip.AddChild(new Label { Text = "Equipment" });
		equip.AddChild(Row(Slot(EquipSlot.Head, inv, 90, 90), Slot(EquipSlot.Vest, inv, 90, 90)));
		equip.AddChild(Row(Slot(EquipSlot.Rig, inv, 90, 70), Slot(EquipSlot.Backpack, inv, 90, 70)));
		equip.AddChild(Slot(EquipSlot.Primary, inv, 280, 90));
		equip.AddChild(Slot(EquipSlot.Secondary, inv, 280, 90));
		equip.AddChild(Row(Slot(EquipSlot.Pistol, inv, 90, 70), Slot(EquipSlot.Melee, inv, 90, 90)));
		equip.AddChild(Slot(EquipSlot.SecureContainer, inv, 90, 90));
		columns.AddChild(equip);

		// Middle: health screen.
		var mid = new VBoxContainer();
		mid.AddChild(new Label { Text = "Health" });
		_health = new HealthPanel();
		_health.Bind(_player.Health);
		mid.AddChild(_health);
		columns.AddChild(mid);

		// Right: carrying grids.
		var right = new VBoxContainer();
		right.AddThemeConstantOverride("separation", 6);
		right.AddChild(new Label { Text = "Pockets" });
		var pockets = new HBoxContainer();
		foreach (var p in inv.Pockets)
			pockets.AddChild(Grid(p, "Pocket"));
		right.AddChild(pockets);
		AddItemGrids(right, inv, EquipSlot.Rig, "Rig");
		AddItemGrids(right, inv, EquipSlot.Backpack, "Backpack");
		AddItemGrids(right, inv, EquipSlot.SecureContainer, "Secure container");
		columns.AddChild(right);

		if (_container is not null)
		{
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 6);
			box.AddChild(new Label { Text = _container.Title });
			_searchLabel = new Label();
			box.AddChild(_searchLabel);
			_searchBar = new ProgressBar
			{
				MinValue = 0, MaxValue = 100, ShowPercentage = false,
				CustomMinimumSize = new Vector2(220f, 12f),
			};
			box.AddChild(_searchBar);
			var model = _container.Model;
			var view = new GridView(model.Grid, "Container")
			{
				Dragged = () => _held,
				IsHidden = item => !model.IsRevealed(item),
			};
			_grids.Add(view);
			box.AddChild(view);
			columns.AddChild(box);
		}

		_drag.MoveToFront();
		_tooltip.MoveToFront();
	}

	private void AddItemGrids(VBoxContainer parent, CharacterInventory inv, EquipSlot slot, string title)
	{
		if (inv.InSlot(slot) is not { } item)
			return;
		parent.AddChild(new Label { Text = $"{title}: {GameText.ItemName(item.Spec.Id)}" });
		foreach (var g in item.Grids)
			parent.AddChild(Grid(g, title));
	}

	private static HBoxContainer Row(params Control[] children)
	{
		var row = new HBoxContainer();
		foreach (var c in children)
			row.AddChild(c);
		return row;
	}

	private SlotView Slot(EquipSlot slot, CharacterInventory inv, float w, float h)
	{
		var view = new SlotView(slot, inv, new Vector2(w, h)) { Dragged = () => _held };
		_slots.Add(view);
		return view;
	}

	private GridView Grid(InventoryGrid grid, string title)
	{
		var view = new GridView(grid, title) { Dragged = () => _held };
		_grids.Add(view);
		return view;
	}

	// --- dragging -------------------------------------------------------------------------------

	private (ItemInstance, Vector2)? HeldForDraw()
		=> _held is null ? null : (_held, GetViewport().GetMousePosition() - _grabOffset);

	private (ItemInstance Item, ItemLocation From, Vector2 Offset)? ItemAt(Vector2 pos)
	{
		foreach (var g in _grids)
		{
			if (!g.GetGlobalRect().HasPoint(pos) || g.ItemAtGlobal(pos) is not { } item)
				continue;
			if (g.IsHidden?.Invoke(item) == true)
				return null;
			var cell = g.Grid.PositionOf(item)!.Value;
			var topLeft = g.GlobalPosition + new Vector2(cell.X, cell.Y) * GridView.Cell;
			return (item, new ItemLocation(null, g.Grid), pos - topLeft);
		}
		foreach (var s in _slots)
		{
			if (s.GetGlobalRect().HasPoint(pos) && s.Item is { } item)
			{
				var half = new Vector2(item.Width, item.Height) * GridView.Cell * 0.5f;
				return (item, new ItemLocation(s.Slot, null), half);
			}
		}
		return null;
	}

	private void OnPress(Vector2 pos, bool doubleClick)
	{
		if (ItemAt(pos) is not { } hit)
			return;
		if (doubleClick)
		{
			CancelDrag();
			DoQuickAction(hit.Item, hit.From);
			return;
		}
		_held = hit.Item;
		_heldFrom = hit.From;
		_heldWasRotated = hit.Item.Rotated;
		_grabOffset = hit.Offset;
		_tooltip.Visible = false;
		_drag.QueueRedraw();
	}

	private void OnRelease(Vector2 pos)
	{
		if (_held is not { } item)
			return;
		var inv = _player.Inventory.Inventory;
		var done = false;
		var topLeft = pos - _grabOffset;
		foreach (var g in _grids)
		{
			if (!g.GetGlobalRect().HasPoint(pos))
				continue;
			var x = (int)Mathf.Floor((topLeft.X - g.GlobalPosition.X + GridView.Cell * 0.5f) / GridView.Cell);
			var y = (int)Mathf.Floor((topLeft.Y - g.GlobalPosition.Y + GridView.Cell * 0.5f) / GridView.Cell);
			// Ammo dropped on a magazine loads it over time instead of moving the stack.
			var (px, py) = g.CellAt(pos);
			if (g.Grid.ItemAt(px, py) is { } under && under != item && MagazineLoader.CanLoad(under, item))
			{
				_player.TryLoadMagazine(under, item);
				item.Rotated = _heldWasRotated;
				_held = null;
				_drag.QueueRedraw();
				return;
			}
			done = inv.TryMoveToGrid(item, _heldFrom, g.Grid, x, y);
			break;
		}
		if (!done)
		{
			foreach (var s in _slots)
			{
				if (!s.GetGlobalRect().HasPoint(pos))
					continue;
				done = inv.TryEquipFrom(item, _heldFrom, s.Slot);
				break;
			}
		}
		if (!done && !OverAnyPanel(pos))
			done = DropHeld(item);
		if (!done)
			item.Rotated = _heldWasRotated;
		_held = null;
		_drag.QueueRedraw();
		if (done)
			_player.Inventory.NotifyChanged();
	}

	private bool OverAnyPanel(Vector2 pos)
		=> _grids.Any(g => g.GetGlobalRect().HasPoint(pos)) || _slots.Any(s => s.GetGlobalRect().HasPoint(pos))
			|| _health.GetGlobalRect().HasPoint(pos);

	/// <summary>Releasing an item over empty space drops it on the ground (a pickup you can search).</summary>
	private bool DropHeld(ItemInstance item)
	{
		var inv = _player.Inventory.Inventory;
		if (_heldFrom.Slot is { } slot)
		{
			if (inv.InSlot(slot) != item)
				return false;
			inv.Remove(item);
		}
		else if (_heldFrom.Grid is { } grid)
		{
			if (!grid.Remove(item))
				return false;
		}
		else
		{
			return false;
		}
		_player.DropItem(item);
		return true;
	}

	private void CancelDrag()
	{
		if (_held is { } item)
			item.Rotated = _heldWasRotated;
		_held = null;
		_drag.QueueRedraw();
	}

	private void DoQuickAction(ItemInstance item, ItemLocation from)
	{
		var inv = _player.Inventory.Inventory;
		var action = InventoryActions.Quick(inv, item, from);
		var changed = false;
		// Double-click on something in a world container: equip it if it is gear, otherwise take it.
		if (from.Grid is { } sourceGrid && _container is { } box && sourceGrid == box.Model.Grid
			&& action.Kind != QuickActionKind.Equip)
		{
			if (inv.TryTake(item, from))
				_player.Inventory.NotifyChanged();
			return;
		}
		switch (action.Kind)
		{
			case QuickActionKind.Equip:
				changed = inv.TryEquipFrom(item, from, action.Slot!.Value);
				break;
			case QuickActionKind.Unequip:
				changed = inv.TryUnequip(action.Slot!.Value);
				break;
			case QuickActionKind.Use:
				_player.UseItem(item);
				break;
			case QuickActionKind.Unload:
				_player.TryUnloadMagazine(item);
				break;
		}
		if (changed)
			_player.Inventory.NotifyChanged();
	}

	// --- dev helpers (used by scripted runs) ---------------------------------------------------

	/// <summary>Dev: drags the first item with this id to a grid cell as if done with the mouse.</summary>
	public bool DebugDragToGrid(string itemId, int gridIndex, int cellX, int cellY)
	{
		if (!IsOpen)
			Open();
		foreach (var g in _grids)
		{
			var item = g.Grid.Items.FirstOrDefault(i => i.Spec.Id == itemId);
			if (item is null || g.Grid.PositionOf(item) is not { } p)
				continue;
			if (gridIndex < 0 || gridIndex >= _grids.Count)
				return false;
			var from = g.GlobalPosition + new Vector2(p.X + 0.5f, p.Y + 0.5f) * GridView.Cell;
			var target = _grids[gridIndex];
			var to = target.GlobalPosition + new Vector2(cellX + 0.5f, cellY + 0.5f) * GridView.Cell;
			OnPress(from, false);
			OnRelease(to);
			return true;
		}
		return false;
	}

	/// <summary>Dev: drags the first item with this id out of every panel and releases it.</summary>
	public bool DebugDropOut(string itemId)
	{
		if (!IsOpen)
			Open();
		foreach (var g in _grids)
		{
			var item = g.Grid.Items.FirstOrDefault(i => i.Spec.Id == itemId);
			if (item is null || g.Grid.PositionOf(item) is not { } p)
				continue;
			var from = g.GlobalPosition + new Vector2(p.X + 0.5f, p.Y + 0.5f) * GridView.Cell;
			OnPress(from, false);
			OnRelease(new Vector2(1500f, 900f));
			return true;
		}
		return false;
	}

	/// <summary>Dev: how the grid views are ordered (index, title, size).</summary>
	public string DebugDescribeGrids()
		=> string.Join("; ", _grids.Select((g, i) => $"{i}:{g.Title} {g.Grid.Width}x{g.Grid.Height}"));

	// --- tooltip --------------------------------------------------------------------------------

	private void OnMove(Vector2 pos)
	{
		if (_held is not null)
		{
			_drag.QueueRedraw();
			return;
		}
		if (ItemAt(pos) is not { } hit)
		{
			_tooltip.Visible = false;
			return;
		}
		_tooltipText.Text = Describe(hit.Item);
		_tooltip.Visible = true;
		_tooltip.Position = pos + new Vector2(16f, 16f);
	}

	public static string Describe(ItemInstance item)
	{
		var data = ItemDb.Data(item.Spec.Id);
		var sb = new StringBuilder();
		sb.Append(GameText.ItemName(item.Spec.Id));
		if (item.Count > 1)
			sb.Append($"  x{item.Count}");
		sb.Append($"\nSize {item.Spec.Width}x{item.Spec.Height}   Weight {item.WeightKg:0.00} kg");
		switch (data?.Payload)
		{
			case WeaponData w:
				sb.Append($"\n{w.Ammo?.Caliber}  {w.RoundsPerMinute:0} rpm  mag {w.MagazineCapacity}");
				break;
			case ArmorData a:
				var dur = item.Durability < 0f ? a.MaxDurability : item.Durability;
				sb.Append($"\nTier {a.Tier}  durability {dur:0}/{a.MaxDurability:0}");
				break;
			case MedicalData m:
				sb.Append($"\n{m.Kind}  use time {m.UseTimeS:0.#} s");
				break;
			case ConsumableData c:
				sb.Append($"\nEnergy +{c.Energy:0}  Hydration +{c.Hydration:0}  use time {c.UseTimeS:0.#} s");
				break;
			case MeleeData me:
				sb.Append($"\nDamage {me.Damage:0}  range {me.Range:0.0} m  ignores armor");
				break;
			case AmmoData am:
				sb.Append($"\n{am.Caliber}  damage {am.Damage:0}  penetration {am.PenetrationTier}  {am.MuzzleVelocity:0} m/s");
				break;
		}
		if (item.Spec.MagazineCapacity > 0)
			sb.Append($"\n{item.Spec.Caliber}  rounds {item.Rounds.Count}/{item.Spec.MagazineCapacity}");
		if (item.Spec.Grids is { Count: > 0 })
			sb.Append("\nStorage " + string.Join(" + ", item.Spec.Grids.Select(g => $"{g.Width}x{g.Height}")));
		return sb.ToString();
	}
}
