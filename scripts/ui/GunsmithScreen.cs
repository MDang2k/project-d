using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace ProjectD;

/// <summary>
/// Hub gunsmith (GDD s11, weapon modding spec): pick a weapon, see its slot tree, attach parts from the
/// stash or your gear, detach them back to the stash, and compare stats before you commit. Built in
/// code with buttons; placeholder art.
/// </summary>
public partial class GunsmithScreen : Control
{
	private readonly GameProfile _profile;
	private readonly Action _changed;
	private ItemInstance? _selected;
	private VBoxContainer _weapons = null!;
	private VBoxContainer _slots = null!;
	private Label _stats = null!;
	private Label _message = null!;

	public GunsmithScreen(GameProfile profile, Action changed)
	{
		_profile = profile;
		_changed = changed;
		Visible = false;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
	}

	private static CharacterInventory Character => RunState.Current.Inventory ?? new CharacterInventory();

	public void Open()
	{
		Visible = true;
		Rebuild();
	}

	/// <summary>Dev: opens with the first weapon selected.</summary>
	public void DebugOpen(string weaponId)
	{
		_selected = Gunsmith.Weapons(_profile, Character).FirstOrDefault(w => w.Spec.Id == weaponId);
		Open();
	}

	private void Close()
	{
		Visible = false;
		_changed();
	}

	public override void _Ready()
	{
		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.7f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(dim);
		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(1180f, 720f) };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.13f, 0.14f, 0.17f),
			BorderColor = new Color(0.4f, 0.4f, 0.45f),
			BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
			ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
		});
		center.AddChild(panel);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		panel.AddChild(root);

		var top = new HBoxContainer();
		top.AddChild(new Label { Text = "Gunsmith", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		top.AddChild(close);
		root.AddChild(top);

		var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 14);
		root.AddChild(columns);

		var left = new ScrollContainer { CustomMinimumSize = new Vector2(240f, 0f) };
		_weapons = new VBoxContainer();
		left.AddChild(_weapons);
		columns.AddChild(left);

		var mid = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_slots = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_slots.AddThemeConstantOverride("separation", 3);
		mid.AddChild(_slots);
		columns.AddChild(mid);

		_stats = new Label { CustomMinimumSize = new Vector2(280f, 0f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		columns.AddChild(_stats);

		_message = new Label { Modulate = new Color(1f, 0.85f, 0.4f) };
		root.AddChild(_message);
	}

	// --- building -------------------------------------------------------------------------------

	private void Rebuild()
	{
		var weapons = Gunsmith.Weapons(_profile, Character).ToList();
		if (_selected is null || !weapons.Contains(_selected))
			_selected = weapons.FirstOrDefault();
		foreach (var child in _weapons.GetChildren())
			child.QueueFree();
		foreach (var w in weapons)
		{
			var weapon = w;
			var missing = Gunsmith.MissingText(w);
			var b = new Button
			{
				Text = GameText.ItemName(w.Spec.Id) + (missing != "" ? "  [incomplete]" : ""),
				ToggleMode = true,
				ButtonPressed = w == _selected,
				Alignment = HorizontalAlignment.Left,
			};
			b.Pressed += () =>
			{
				_selected = weapon;
				Rebuild();
			};
			_weapons.AddChild(b);
		}
		if (weapons.Count == 0)
			_weapons.AddChild(new Label { Text = "No weapons." });
		BuildSlots();
		ShowStats(null);
	}

	private void BuildSlots()
	{
		foreach (var child in _slots.GetChildren())
			child.QueueFree();
		if (_selected is not { } weapon)
			return;
		var pool = Gunsmith.PartPool(_profile, Character).ToList();
		var depth = new Dictionary<ItemInstance, int> { [weapon] = 0 };
		foreach (var slot in WeaponBuild.Slots(weapon))
		{
			var d = depth.GetValueOrDefault(slot.Owner);
			if (slot.Part is not null)
				depth[slot.Part] = d + 1;
			_slots.AddChild(SlotRow(weapon, slot, d, pool));
		}
	}

	private Control SlotRow(ItemInstance weapon, SlotEntry slot, int depth, List<ItemInstance> pool)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		row.AddChild(new Control { CustomMinimumSize = new Vector2(depth * 22f, 0f) });
		var tags = (slot.Spec.Required ? "required" : "") + (slot.Spec.Accessory ? "" : " (hub only)");
		row.AddChild(new Label
		{
			Text = GameText.Get("slot." + slot.Spec.Id) + (tags.Trim() != "" ? $"  [{tags.Trim()}]" : ""),
			CustomMinimumSize = new Vector2(190f, 0f),
		});
		if (slot.Blocked)
		{
			row.AddChild(new Label { Text = "blocked by another part", Modulate = new Color(0.8f, 0.5f, 0.4f) });
			return row;
		}
		if (slot.Part is { } part)
		{
			row.AddChild(new Label { Text = GameText.ItemName(part.Spec.Id), SizeFlagsHorizontal = SizeFlags.ExpandFill });
			var detach = new Button { Text = "Detach" };
			var slotId = slot.Spec.Id;
			detach.Pressed += () => Act(Gunsmith.Detach(_profile, Character, weapon, slotId), "Detached.");
			row.AddChild(detach);
			return row;
		}
		var fitting = WeaponBuild.Fitting(weapon, slot.Spec.Id, pool).ToList();
		if (fitting.Count == 0)
		{
			row.AddChild(new Label
			{
				Text = slot.Spec.Required ? "EMPTY - nothing in stock fits" : "empty",
				Modulate = slot.Spec.Required ? new Color(1f, 0.5f, 0.4f) : new Color(0.6f, 0.6f, 0.65f),
			});
			return row;
		}
		var options = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		foreach (var candidate in fitting)
		{
			var cand = candidate;
			var slotId = slot.Spec.Id;
			var b = new Button { Text = "+ " + GameText.ItemName(cand.Spec.Id) };
			b.Pressed += () => Act(Gunsmith.Attach(_profile, Character, weapon, slotId, cand), "Attached.");
			b.MouseEntered += () => ShowStats((slotId, cand));
			b.MouseExited += () => ShowStats(null);
			options.AddChild(b);
		}
		row.AddChild(options);
		return row;
	}

	/// <summary>Current stats of the weapon, with the change a hovered part would make.</summary>
	private void ShowStats((string SlotId, ItemInstance Part)? hover)
	{
		if (_selected is not { } weapon)
		{
			_stats.Text = "";
			return;
		}
		var before = WeaponBuild.Stats(weapon);
		WeaponStats? after = hover is { } h ? WeaponBuild.Preview(weapon, h.SlotId, h.Part)?.After : null;
		var sb = new StringBuilder();
		sb.AppendLine(GameText.ItemName(weapon.Spec.Id));
		var missing = Gunsmith.MissingText(weapon);
		if (missing != "")
			sb.AppendLine($"MISSING: {missing} - cannot be equipped or fired");
		sb.AppendLine();
		void Line(string label, string value, string delta) => sb.AppendLine($"{label,-12}{value}{delta}");
		string D(float a, float b, string fmt = "0.##", bool inverse = false)
			=> after is null || Math.Abs(a - b) < 0.0001f ? "" : $"   ({(b - a >= 0 ? "+" : "")}{(b - a).ToString(fmt)})";
		var a = after ?? before;
		Line("Recoil", $"x{before.RecoilMult:0.00}", D(before.RecoilMult, a.RecoilMult, "0.00"));
		Line("Ergonomics", $"{before.Ergonomics:0}", D(before.Ergonomics, a.Ergonomics, "0"));
		Line("Sway", $"x{before.SwayMult:0.00}", D(before.SwayMult, a.SwayMult, "0.00"));
		Line("Velocity", $"x{before.VelocityMult:0.00}", D(before.VelocityMult, a.VelocityMult, "0.00"));
		Line("Dispersion", $"x{before.DispersionMult:0.00}", D(before.DispersionMult, a.DispersionMult, "0.00"));
		Line("Noise", $"x{before.NoiseMult:0.00}", D(before.NoiseMult, a.NoiseMult, "0.00"));
		Line("Zoom", before.Zoom > 0f ? $"{before.Zoom:0.#}x" : "-", D(before.Zoom, a.Zoom, "0.#"));
		Line("Weight", $"{before.WeightKg:0.00} kg", D(before.WeightKg, a.WeightKg, "0.00"));
		Line("Value", $"{before.Value:N0}", D(before.Value, a.Value, "0"));
		Line("Bipod", before.HasBipod ? "yes" : "no", after is not null && after.HasBipod != before.HasBipod ? "   (changes)" : "");
		_stats.Text = sb.ToString();
	}

	private void Act(GunsmithResult result, string okText)
	{
		_message.Text = result switch
		{
			GunsmithResult.Ok => okText,
			GunsmithResult.StashFull => "Your stash is full.",
			GunsmithResult.CannotFit => "That does not fit.",
			GunsmithResult.PartNotOwned => "You do not have that part.",
			_ => "Not possible.",
		};
		if (result == GunsmithResult.Ok)
		{
			SaveManager.Instance?.SaveInHub();
			_changed();
		}
		Rebuild();
	}
}
