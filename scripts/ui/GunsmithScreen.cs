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
	private bool _sandbox;
	private ItemInstance? _sbx;
	private VBoxContainer _presets = null!;
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

	/// <summary>The weapon on the bench: the real one, or the throwaway copy in sandbox mode.</summary>
	private ItemInstance? Work => _sandbox ? _sbx : _selected;

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
		var mode = new Button { Text = "Sandbox: off", ToggleMode = true };
		mode.Toggled += on =>
		{
			_sandbox = on;
			mode.Text = on ? "Sandbox: on (nothing is changed)" : "Sandbox: off";
			_sbx = null;
			Rebuild();
		};
		top.AddChild(mode);
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
		var midBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_slots = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_slots.AddThemeConstantOverride("separation", 3);
		midBox.AddChild(_slots);
		_presets = new VBoxContainer();
		midBox.AddChild(_presets);
		mid.AddChild(midBox);
		columns.AddChild(mid);

		_stats = new Label { CustomMinimumSize = new Vector2(280f, 0f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		columns.AddChild(_stats);

		_message = new Label { Modulate = new Color(1f, 0.85f, 0.4f) };
		root.AddChild(_message);
	}

	// --- building -------------------------------------------------------------------------------

	private void Rebuild()
	{
		WeaponWorkshop.RememberOwned(_profile, Character);
		var weapons = Gunsmith.Weapons(_profile, Character).ToList();
		if (_selected is null || !weapons.Contains(_selected))
			_selected = weapons.FirstOrDefault();
		if (_sandbox && _selected is not null && (_sbx is null || _sbx.Spec.Id != _selected.Spec.Id))
			_sbx = WeaponWorkshop.Copy(_selected);
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
				_sbx = null;
				Rebuild();
			};
			_weapons.AddChild(b);
		}
		if (weapons.Count == 0)
			_weapons.AddChild(new Label { Text = "No weapons." });
		BuildSlots();
		BuildPresets();
		ShowStats(null);
	}

	private void BuildSlots()
	{
		foreach (var child in _slots.GetChildren())
			child.QueueFree();
		if (Work is not { } weapon)
			return;
		var pool = _sandbox
			? WeaponWorkshop.ExaminedIds(_profile, Character).Select(id => ItemDb.Create(id)).OfType<ItemInstance>()
				.Where(i => i.Spec.Category == ItemCategory.WeaponPart).ToList()
			: Gunsmith.PartPool(_profile, Character).ToList();
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
			if (_sandbox)
				detach.Pressed += () =>
				{
					WeaponBuild.Detach(weapon, slotId);
					Rebuild();
				};
			else
				detach.Pressed += () => Act(Gunsmith.Detach(_profile, Character, weapon, slotId), "Detached.");
			row.AddChild(detach);
			return row;
		}
		var fitting = WeaponBuild.Fitting(weapon, slot.Spec.Id, pool).ToList();
		var refused = pool.Where(p => WeaponBuild.CanAttach(weapon, slot.Spec.Id, p) is AttachFailure.RailOverlap
			or AttachFailure.RailOverflow or AttachFailure.ZoneConflict)
			.GroupBy(p => p.Spec.Id).Select(g => g.First()).ToList();
		if (_sandbox)
			fitting = fitting.GroupBy(p => p.Spec.Id).Select(g => g.First()).ToList();
		if (fitting.Count == 0 && refused.Count == 0)
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
			if (_sandbox)
				b.Pressed += () =>
				{
					WeaponBuild.Attach(weapon, slotId, cand);
					Rebuild();
				};
			else
				b.Pressed += () => Act(Gunsmith.Attach(_profile, Character, weapon, slotId, cand), "Attached.");
			b.MouseEntered += () => ShowStats((slotId, cand));
			b.MouseExited += () => ShowStats(null);
			options.AddChild(b);
		}
		foreach (var blocked in refused)
		{
			var why = WeaponBuild.CanAttach(weapon, slot.Spec.Id, blocked) switch
			{
				AttachFailure.RailOverlap => "rail space taken",
				AttachFailure.RailOverflow => "too long for the rail",
				_ => "clashes with another part",
			};
			options.AddChild(new Button { Text = $"{GameText.ItemName(blocked.Spec.Id)} ({why})", Disabled = true });
		}
		row.AddChild(options);
		return row;
	}

	private void BuildPresets()
	{
		foreach (var child in _presets.GetChildren())
			child.QueueFree();
		if (_selected is not { } real)
			return;
		_presets.AddChild(new HSeparator());
		_presets.AddChild(new Label { Text = _sandbox ? "Sandbox build" : "Saved builds" });
		if (_sandbox && _sbx is { } sbx)
		{
			var current = WeaponWorkshop.Capture("", sbx);
			var missing = WeaponWorkshop.Missing(current, _profile, Character, real);
			_presets.AddChild(new Label
			{
				Text = missing.Count == 0 ? "You own every part of this build." : "Still to source: " + MissingText(missing),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			});
			var save = new Button { Text = "Save as preset" };
			save.Pressed += () =>
			{
				var n = _profile.Presets.Count(p => p.WeaponId == real.Spec.Id) + 1;
				_profile.Presets.Add(WeaponWorkshop.Capture($"{GameText.ItemName(real.Spec.Id)} build {n}", sbx));
				SaveManager.Instance?.SaveInHub();
				_message.Text = "Preset saved.";
				Rebuild();
			};
			_presets.AddChild(save);
		}
		foreach (var preset in _profile.Presets.Where(p => p.WeaponId == real.Spec.Id).ToList())
		{
			var p = preset;
			var row = new HBoxContainer();
			var missing = WeaponWorkshop.Missing(p, _profile, Character, real);
			row.AddChild(new Label
			{
				Text = p.Name + (missing.Count == 0 ? "" : "  (missing: " + MissingText(missing) + ")"),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			});
			var load = new Button { Text = "Load in sandbox" };
			load.Pressed += () =>
			{
				_sandbox = true;
				_sbx = WeaponWorkshop.Build(p, real.Spec, id => ItemDb.Create(id)?.Spec);
				Rebuild();
			};
			row.AddChild(load);
			var apply = new Button { Text = "Apply", Disabled = missing.Count > 0 };
			apply.Pressed += () => Act(WeaponWorkshop.Apply(_profile, Character, real, p), "Preset applied.");
			row.AddChild(apply);
			var del = new Button { Text = "Delete" };
			del.Pressed += () =>
			{
				_profile.Presets.Remove(p);
				SaveManager.Instance?.SaveInHub();
				Rebuild();
			};
			row.AddChild(del);
			_presets.AddChild(row);
		}
	}

	private static string MissingText(System.Collections.Generic.IReadOnlyDictionary<string, int> missing)
		=> string.Join(", ", missing.Select(kv => kv.Value > 1 ? $"{GameText.ItemName(kv.Key)} x{kv.Value}" : GameText.ItemName(kv.Key)));

	/// <summary>Current stats of the weapon, with the change a hovered part would make.</summary>
	private void ShowStats((string SlotId, ItemInstance Part)? hover)
	{
		if (Work is not { } weapon)
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
		void Line(string label, string value, string delta) => sb.AppendLine($"{label,-14}{value}{delta}");
		string D(float a, float b, string fmt = "0.##", bool inverse = false)
			=> after is null || Math.Abs(a - b) < 0.0001f ? "" : $"   ({(b - a >= 0 ? "+" : "")}{(b - a).ToString(fmt)})";
		var a = after ?? before;
		Line("Vert. recoil", $"{before.VerticalRecoil:0}", D(before.VerticalRecoil, a.VerticalRecoil, "0"));
		Line("Horiz. recoil", $"{before.HorizontalRecoil:0}", D(before.HorizontalRecoil, a.HorizontalRecoil, "0"));
		Line("Stability", $"{before.Stability:0}", D(before.Stability, a.Stability, "0"));
		Line("Hip stability", $"{before.HipStability:0}", D(before.HipStability, a.HipStability, "0"));
		Line("Ergonomics", $"{before.Ergonomics:0}", D(before.Ergonomics, a.Ergonomics, "0"));
		Line("Velocity", $"x{before.VelocityMult:0.00}", D(before.VelocityMult, a.VelocityMult, "0.00"));
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
