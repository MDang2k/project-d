using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// In-raid weapon modification (weapon modding v2 s7): lists the held weapon's slots with how each part may
/// be swapped. Starting a swap closes the inventory; the work runs on a timer in the world and is cancelled
/// by damage or sprinting.
/// </summary>
public partial class FieldStripPanel : PanelContainer
{
	private readonly Player _player;
	private readonly Action _closeInventory;
	private VBoxContainer _rows = null!;
	private Label _message = null!;

	public FieldStripPanel(Player player, Action closeInventory)
	{
		_player = player;
		_closeInventory = closeInventory;
		Visible = false;
		CustomMinimumSize = new Vector2(620f, 0f);
		SetAnchorsPreset(LayoutPreset.Center);
	}

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.13f, 0.14f, 0.17f),
			BorderColor = new Color(0.4f, 0.4f, 0.45f),
			BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
			ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
		});
		var box = new VBoxContainer();
		AddChild(box);
		var top = new HBoxContainer();
		top.AddChild(new Label { Text = "Modify weapon", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var close = new Button { Text = "Close" };
		close.Pressed += () => Visible = false;
		top.AddChild(close);
		box.AddChild(top);
		_rows = new VBoxContainer();
		_rows.AddThemeConstantOverride("separation", 3);
		box.AddChild(_rows);
		_message = new Label { Modulate = new Color(1f, 0.85f, 0.4f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_message);
	}

	private CharacterInventory Character => _player.Inventory.Inventory;

	public void Toggle()
	{
		Visible = !Visible;
		if (Visible)
			Rebuild();
	}

	public void Rebuild()
	{
		if (!Visible)
			return;
		foreach (var child in _rows.GetChildren())
			child.QueueFree();
		var weapon = Character.InSlot(_player.ActiveSlot);
		if (weapon is null || weapon.Spec.Build is null)
		{
			_rows.AddChild(new Label { Text = "Hold a weapon to work on it." });
			return;
		}
		_rows.AddChild(new Label
		{
			Text = GameText.ItemName(weapon.Spec.Id) + (FieldStrip.HasKit(Character) ? "   (armorer's kit in hand)" : "   (no armorer's kit)"),
		});
		var carried = FieldStrip.CarriedParts(Character).ToList();
		foreach (var slot in WeaponBuild.Slots(weapon))
			_rows.AddChild(Row(weapon, slot, carried));
	}

	private Control Row(ItemInstance weapon, SlotEntry slot, System.Collections.Generic.List<ItemInstance> carried)
	{
		var row = new HFlowContainer();
		row.AddThemeConstantOverride("h_separation", 6);
		var name = new Label { Text = GameText.Get("slot." + slot.Spec.Id), CustomMinimumSize = new Vector2(150f, 0f) };
		row.AddChild(name);
		if (slot.Blocked)
		{
			row.AddChild(new Label { Text = "blocked", Modulate = new Color(0.8f, 0.5f, 0.4f) });
			return row;
		}
		var slotId = slot.Spec.Id;
		if (slot.Part is { } part)
		{
			row.AddChild(new Label { Text = GameText.ItemName(part.Spec.Id) });
			var kind = FieldStrip.Kind(part);
			var remove = new Button { Text = $"Remove ({KindText(kind)})", Disabled = kind == SwapKind.Never };
			remove.Pressed += () => Begin(weapon, slot.Spec, part, kind, "Removing", () => FieldStrip.Remove(Character, weapon, slotId));
			row.AddChild(remove);
			return row;
		}
		var options = carried.Where(c => WeaponBuild.CanAttach(weapon, slotId, c) == AttachFailure.None).ToList();
		if (options.Count == 0)
		{
			row.AddChild(new Label { Text = "empty", Modulate = new Color(0.6f, 0.6f, 0.65f) });
			return row;
		}
		foreach (var option in options)
		{
			var candidate = option;
			var kind = FieldStrip.Kind(candidate);
			var fit = new Button { Text = $"Fit {GameText.ItemName(candidate.Spec.Id)} ({KindText(kind)})", Disabled = kind == SwapKind.Never };
			fit.Pressed += () => Begin(weapon, slot.Spec, candidate, kind, "Fitting", () => FieldStrip.Install(Character, weapon, slotId, candidate));
			row.AddChild(fit);
		}
		return row;
	}

	private static string KindText(SwapKind kind) => kind switch
	{
		SwapKind.Free => "no tools",
		SwapKind.Tooled => "needs kit",
		_ => "not in raid",
	};

	private void Begin(ItemInstance weapon, WeaponSlotSpec slot, ItemInstance part, SwapKind kind, string verb,
		Func<FieldStripResult> apply)
	{
		if (kind == SwapKind.Tooled && !FieldStrip.HasKit(Character))
		{
			_message.Text = "You need a field armorer's kit in your pockets, vest or rig.";
			return;
		}
		var seconds = FieldStrip.Duration(slot, kind);
		var label = $"{verb} {GameText.ItemName(part.Spec.Id)}";
		if (!_player.BeginFieldSwap(seconds, label, () => apply()))
		{
			_message.Text = "Already busy.";
			return;
		}
		Visible = false;
		_closeInventory();
	}
}
