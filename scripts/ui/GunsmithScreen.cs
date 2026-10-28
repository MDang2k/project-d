using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Hub gunsmith (weapon modding v2 UI): the weapon with a floating box per slot, a flyout of compatible parts
/// per box (stash first, then what traders sell), live rating deltas on hover, and a sandbox that builds with
/// examined parts without touching anything. Assemble only mounts parts you own; nothing is bought here.
/// </summary>
public partial class GunsmithScreen : Control
{
	private readonly GameProfile _profile;
	private readonly Action _changed;
	private ItemInstance? _selected;
	private bool _sandbox;
	private ItemInstance? _sbx;

	private HBoxContainer _weapons = null!;
	private GunsmithBoard _board = null!;
	private VBoxContainer _telemetry = null!;
	private Label _footer = null!;
	private Label _message = null!;
	private Button _assemble = null!;
	private PanelContainer _presetPanel = null!;
	private VBoxContainer _presetRows = null!;
	private Control? _flyout;
	private CheckBox _fVital = null!, _fFunctional = null!, _fGear = null!, _fUnowned = null!;
	private (string SlotId, ItemInstance Part)? _hover;

	public GunsmithScreen(GameProfile profile, Action changed)
	{
		_profile = profile;
		_changed = changed;
		Visible = false;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
	}

	private static CharacterInventory Character => RunState.Current.Inventory ?? new CharacterInventory();

	/// <summary>The weapon on the bench: the real one, or the throwaway copy in sandbox mode.</summary>
	private ItemInstance? Work => _sandbox ? _sbx : _selected;

	public void Open()
	{
		Visible = true;
		Rebuild();
	}

	/// <summary>Dev: opens with a given weapon selected.</summary>
	public void DebugOpen(string weaponId)
	{
		_selected = Gunsmith.Weapons(_profile, Character).FirstOrDefault(w => w.Spec.Id == weaponId);
		Open();
	}

	private static readonly string[] TestParts = { "test_comp_heavy","test_flash_hider","test_qd_host","test_qd_suppressor","test_threaded_suppressor","test_red_dot","test_scope_8x","test_long_side_mount","test_foregrip","test_laser_box","test_match_barrel","test_hq_grip","test_poly_stock","test_fal_brake" };

	/// <summary>Dev: puts one of each placeholder test part in the stash.</summary>
	private void AddTestParts()
	{
		var added = 0;
		foreach (var id in TestParts)
		{
			if (ItemDb.Create(id) is { } part && _profile.Stash.TryPlaceAnywhere(part))
				added++;
		}
		_message.Text = $"Added {added} test parts to the stash.";
		SaveManager.Instance?.SaveInHub();
		Rebuild();
	}

	/// <summary>Dev: opens the flyout of a slot (screenshots).</summary>
	public void DebugFlyout(string slotId)
	{
		if (Work is { } w && WeaponBuild.Find(w, slotId) is { } entry)
			OpenFlyout(entry, _board);
	}

	private void Close()
	{
		CloseFlyout();
		Visible = false;
		_changed();
	}

	// --- frame ----------------------------------------------------------------------------------

	public override void _Ready()
	{
		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(dim);
		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(1240f, 700f) };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.1f, 0.11f, 0.13f),
			BorderColor = new Color(0.4f, 0.4f, 0.45f),
			BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
			ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
		});
		center.AddChild(panel);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		panel.AddChild(root);

		// header: weapon picker, filters, sandbox, close
		var top = new HFlowContainer();
		top.AddThemeConstantOverride("h_separation", 10);
		_weapons = new HBoxContainer();
		top.AddChild(_weapons);
		_fVital = Filter("Vital", top);
		_fFunctional = Filter("Functional", top);
		_fGear = Filter("Gear", top);
		_fUnowned = Filter("Show unowned", top);
		var mode = new Button { Text = "Sandbox: off", ToggleMode = true };
		mode.Toggled += on =>
		{
			_sandbox = on;
			mode.Text = on ? "Sandbox: on (nothing is changed)" : "Sandbox: off";
			_sbx = null;
			Rebuild();
		};
		top.AddChild(mode);
		var dev = new Button { Text = "[dev] +test parts", TooltipText = "Adds placeholder test parts to the stash (remove this button later)." };
		dev.Pressed += AddTestParts;
		top.AddChild(dev);
		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		top.AddChild(close);
		root.AddChild(top);

		var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 12);
		root.AddChild(body);
		_board = new GunsmithBoard { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		_board.BoxClicked += OpenFlyout;
		body.AddChild(_board);
		_telemetry = new VBoxContainer { CustomMinimumSize = new Vector2(300f, 0f) };
		body.AddChild(_telemetry);

		// footer
		var foot = new HBoxContainer();
		foot.AddThemeConstantOverride("separation", 14);
		_footer = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		foot.AddChild(_footer);
		var presets = new Button { Text = "Presets" };
		presets.Pressed += () =>
		{
			_presetPanel.Visible = !_presetPanel.Visible;
			BuildPresets();
		};
		foot.AddChild(presets);
		_assemble = new Button { Text = "Assemble", TooltipText = "Sandbox only: mounts the sandbox build from parts you own." };
		_assemble.Pressed += AssembleSandbox;
		foot.AddChild(_assemble);
		root.AddChild(foot);
		_message = new Label { Modulate = new Color(1f, 0.85f, 0.4f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
		root.AddChild(_message);

		_presetPanel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(560f, 0f) };
		_presetPanel.SetAnchorsPreset(LayoutPreset.CenterBottom);
		_presetPanel.OffsetLeft = -280f;
		_presetPanel.OffsetRight = 280f;
		_presetPanel.OffsetBottom = -90f;
		_presetPanel.OffsetTop = -330f;
		_presetPanel.GrowVertical = GrowDirection.Begin;
		_presetPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.14f, 0.15f, 0.18f), BorderColor = new Color(0.5f, 0.5f, 0.55f), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
		_presetRows = new VBoxContainer();
		_presetPanel.AddChild(_presetRows);
		AddChild(_presetPanel);
	}

	private CheckBox Filter(string text, Control parent)
	{
		var box = new CheckBox { Text = text, ButtonPressed = true };
		box.Toggled += _ => Rebuild();
		parent.AddChild(box);
		return box;
	}

	// --- rebuild --------------------------------------------------------------------------------

	private void Rebuild()
	{
		CloseFlyout();
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
			var b = new Button
			{
				Text = GameText.ItemName(w.Spec.Id) + (Gunsmith.MissingText(w) != "" ? " !" : ""),
				ToggleMode = true,
				ButtonPressed = w == _selected,
			};
			b.Pressed += () =>
			{
				_selected = weapon;
				_sbx = null;
				Rebuild();
			};
			_weapons.AddChild(b);
		}
		if (Work is { } work)
		{
			var hidden = new HashSet<ItemInstance>();
			_board.Rebuild(WeaponBuild.Slots(work), work, e => InfoFor(e, hidden));
		}
		ShowTelemetry();
		_assemble.Visible = _sandbox;
		_footer.Text = Footer();
		if (_presetPanel.Visible)
			BuildPresets();
	}

	private static string ClassOf(WeaponSlotSpec s) => s.Required ? "vital" : s.Type is "stock" or "handguard" ? "gear" : "functional";

	/// <summary>The box for a slot, or null when a filter hides it (and everything that hangs off it).</summary>
	private BoxInfo? InfoFor(SlotEntry e, HashSet<ItemInstance> hidden)
	{
		var visible = !hidden.Contains(e.Owner) && ClassOf(e.Spec) switch
		{
			"vital" => _fVital.ButtonPressed,
			"gear" => _fGear.ButtonPressed,
			_ => _fFunctional.ButtonPressed,
		};
		if (!visible)
		{
			if (e.Part is not null)
				hidden.Add(e.Part);
			return null;
		}
		var title = GameText.Get("slot." + e.Spec.Id);
		if (e.Blocked)
			return new BoxInfo(title, "blocked", BoxState.Conflict);
		if (e.Part is { } part)
			return new BoxInfo(title, Short(GameText.ItemName(part.Spec.Id)), BoxState.Populated);
		if (e.Spec.Required)
			return new BoxInfo(title, "MISSING", BoxState.MissingVital);
		return new BoxInfo(title, "+ open", BoxState.Open);
	}

	private static string Short(string s) => s.Length > 20 ? s[..19] + "." : s;

	private string Footer()
	{
		var parts = Gunsmith.PartPool(_profile, Character).Count();
		var value = Work is { } w ? WeaponBuild.TotalValue(w) : 0;
		var missing = _selected is { } s ? Gunsmith.MissingText(s) : "";
		return $"Parts in stash and gear: {parts}     Build value: {value:N0} {GameText.Get("currency.unit")}"
			+ (missing != "" ? $"     Real weapon is missing: {missing}" : "");
	}

	// --- telemetry ------------------------------------------------------------------------------

	private void ShowTelemetry()
	{
		foreach (var child in _telemetry.GetChildren())
			child.QueueFree();
		if (Work is not { } weapon)
			return;
		var before = WeaponBuild.Stats(weapon);
		WeaponStats? after = _hover is { } h ? WeaponBuild.Preview(weapon, h.SlotId, h.Part)?.After : null;
		var a = after ?? before;
		var preview = after is not null;
		_telemetry.AddChild(new Label { Text = "TELEMETRY (0-100)" });
		Bar("Vert. recoil", before.VerticalRecoil, a.VerticalRecoil, preview);
		Bar("Horiz. recoil", before.HorizontalRecoil, a.HorizontalRecoil, preview);
		Bar("Stability", before.Stability, a.Stability, preview);
		Bar("Hip stability", before.HipStability, a.HipStability, preview);
		Bar("Ergonomics", before.Ergonomics, a.Ergonomics, preview);
		_telemetry.AddChild(new HSeparator());
		var ammo = (ItemDb.Data(weapon.Spec.Id)?.Payload as WeaponData)?.Ammo;
		var speed = (ammo?.MuzzleVelocity ?? 0f) * before.VelocityMult;
		var speedAfter = (ammo?.MuzzleVelocity ?? 0f) * a.VelocityMult;
		Line($"Accuracy   {before.Moa:0.0} MOA" + Delta(before.Moa, a.Moa, preview, "0.0"));
		Line($"Velocity   {speed:0} m/s" + Delta(speed, speedAfter, preview, "0"));
		Line($"Weight   {before.WeightKg:0.00} kg" + Delta(before.WeightKg, a.WeightKg, preview, "0.00"));
		Line($"Noise   x{before.NoiseMult:0.00}" + Delta(before.NoiseMult, a.NoiseMult, preview, "0.00"));
		Line(before.Zoom > 0f ? $"Zoom   {before.Zoom:0.#}x" : "Zoom   -");
		Line(before.HasBipod ? "Bipod   yes" : "Bipod   no");
		if (Gunsmith.MissingText(weapon) is { Length: > 0 } miss)
			_telemetry.AddChild(new Label { Text = $"Cannot fire: missing {miss}", Modulate = new Color(1f, 0.45f, 0.4f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
	}

	private void Bar(string name, float value, float next, bool preview)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(100f, 0f) });
		var bar = new ProgressBar { MinValue = 0, MaxValue = 100, Value = preview ? next : value, ShowPercentage = false, CustomMinimumSize = new Vector2(110f, 14f), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
		row.AddChild(bar);
		row.AddChild(new Label { Text = $"{value:0}" });
		if (preview && Math.Abs(next - value) > 0.01f)
		{
			var d = new Label { Text = $"{(next > value ? "+" : "")}{next - value:0}" };
			d.Modulate = next > value ? new Color(0.4f, 0.95f, 0.5f) : new Color(1f, 0.45f, 0.4f);
			row.AddChild(d);
		}
		_telemetry.AddChild(row);
	}

	private static string Delta(float before, float after, bool preview, string fmt)
		=> !preview || Math.Abs(after - before) < 0.0001f ? "" : $"  ({(after > before ? "+" : "")}{(after - before).ToString(fmt)})";

	private void Line(string text) => _telemetry.AddChild(new Label { Text = text });

	// --- flyout ---------------------------------------------------------------------------------

	private void CloseFlyout()
	{
		_flyout?.QueueFree();
		_flyout = null;
		if (_hover is { } h)
		{
			_hover = null;
			_board?.SetGhost(h.SlotId, null);
			_board?.SetPreview(h.SlotId, null);
		}
	}

	private IEnumerable<ItemInstance> Pool()
		=> _sandbox
			? WeaponWorkshop.ExaminedIds(_profile, Character).Select(id => ItemDb.Create(id)).OfType<ItemInstance>()
				.Where(i => i.Spec.Category == ItemCategory.WeaponPart)
			: Gunsmith.PartPool(_profile, Character);

	/// <summary>Part items the traders you can use sell right now, with their price.</summary>
	private IEnumerable<(string ItemId, string Trader, int Price)> Offers()
	{
		foreach (var trader in TraderCatalog.All.Where(_profile.IsTraderAvailable))
		{
			var level = trader.Levels[Math.Clamp(_profile.Trader(trader.Id).Level, 1, trader.Levels.Count) - 1];
			foreach (var listing in level.Sells)
			{
				if (ItemDb.Data(listing.ItemId) is not { Payload: PartData } data)
					continue;
				yield return (listing.ItemId, GameText.Get(trader.NameKey), (int)Math.Ceiling(data.BaseValue * level.BuyMultiplier * listing.PriceMultiplier));
			}
		}
	}

	private void OpenFlyout(SlotEntry slot, Control anchor)
	{
		CloseFlyout();
		if (Work is not { } weapon)
			return;
		var slotId = slot.Spec.Id;

		var holder = new Control { MouseFilter = MouseFilterEnum.Ignore };
		holder.SetAnchorsPreset(LayoutPreset.FullRect);
		var catcher = new ColorRect { Color = new Color(0f, 0f, 0f, 0.01f) };
		catcher.SetAnchorsPreset(LayoutPreset.FullRect);
		catcher.GuiInput += ev =>
		{
			if (ev is InputEventMouseButton { Pressed: true })
				CloseFlyout();
		};
		holder.AddChild(catcher);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(300f, 0f) };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.14f, 0.15f, 0.18f), BorderColor = new Color(0.7f, 0.72f, 0.8f), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 });
		var box = new VBoxContainer();
		panel.AddChild(box);
		box.AddChild(new Label { Text = GameText.Get("slot." + slotId).ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center });

		var pool = Pool().ToList();
		var refused = pool.Where(p => WeaponBuild.CanAttach(weapon, slotId, p) is AttachFailure.RailOverlap or AttachFailure.RailOverflow or AttachFailure.ZoneConflict)
			.GroupBy(p => p.Spec.Id).Select(g => g.First()).ToList();

		if (slot.Blocked)
			box.AddChild(new Label { Text = "Blocked: another part covers this slot.", Modulate = new Color(0.95f, 0.7f, 0.2f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
		else if (slot.Part is not null)
		{
			var remove = new Button { Text = "[X] Remove part" };
			remove.Pressed += () =>
			{
				if (_sandbox)
				{
					WeaponBuild.Detach(weapon, slotId);
					Rebuild();
				}
				else
					Act(Gunsmith.Detach(_profile, Character, weapon, slotId), "Detached.");
			};
			box.AddChild(remove);
		}
		else
		{
			box.AddChild(new Label { Text = _sandbox ? "-- EXAMINED PARTS --" : "-- FROM STASH --" });
			var fitting = WeaponBuild.Fitting(weapon, slotId, pool).ToList();
			if (_sandbox)
				fitting = fitting.GroupBy(p => p.Spec.Id).Select(g => g.First()).ToList();
			if (fitting.Count == 0 && refused.Count == 0)
				box.AddChild(new Label { Text = "nothing fits", Modulate = new Color(0.6f, 0.6f, 0.65f) });
			foreach (var candidate in fitting)
			{
				var cand = candidate;
				var b = new Button { Text = GameText.ItemName(cand.Spec.Id), Alignment = HorizontalAlignment.Left };
				b.Pressed += () =>
				{
					if (_sandbox)
					{
						WeaponBuild.Attach(weapon, slotId, cand);
						Rebuild();
					}
					else
						Act(Gunsmith.Attach(_profile, Character, weapon, slotId, cand), "Attached.");
				};
				Hover(b, slotId, cand);
				box.AddChild(b);
			}
			foreach (var blocked in refused)
			{
				var why = WeaponBuild.CanAttach(weapon, slotId, blocked) switch
				{
					AttachFailure.RailOverlap => "rail space taken",
					AttachFailure.RailOverflow => "too long for the rail",
					_ => "clashes with another part",
				};
				box.AddChild(new Label { Text = $"{GameText.ItemName(blocked.Spec.Id)} - {why}", Modulate = new Color(0.95f, 0.7f, 0.2f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
			}
			if (_fUnowned.ButtonPressed)
				AddTraderSection(box, weapon, slotId, pool);
		}

		panel.Position = FlyoutPosition(anchor);
		holder.AddChild(panel);
		AddChild(holder);
		_flyout = holder;
	}

	private void AddTraderSection(VBoxContainer box, ItemInstance weapon, string slotId, List<ItemInstance> pool)
	{
		var owned = pool.Select(p => p.Spec.Id).ToHashSet();
		var rows = Offers()
			.Where(o => !owned.Contains(o.ItemId))
			.Select(o => (Offer: o, Item: ItemDb.Create(o.ItemId)))
			.Where(x => x.Item is not null && WeaponBuild.CanAttach(weapon, slotId, x.Item) == AttachFailure.None)
			.GroupBy(x => x.Offer.ItemId).Select(g => g.OrderBy(x => x.Offer.Price).First())
			.ToList();
		if (rows.Count == 0)
			return;
		box.AddChild(new Label { Text = "-- TRADERS --" });
		foreach (var (offer, item) in rows)
		{
			var part = item!;
			var b = new Button
			{
				Text = $"{GameText.ItemName(offer.ItemId)}\n{offer.Trader}  {offer.Price:N0} {GameText.Get("currency.unit")}",
				Alignment = HorizontalAlignment.Left,
				TooltipText = _sandbox ? "" : "Buy it from the trader first.",
				// real mode: shown for sourcing only, nothing is bought here
				FocusMode = _sandbox ? FocusModeEnum.All : FocusModeEnum.None,
			};
			if (_sandbox)
			{
				b.Pressed += () =>
				{
					WeaponBuild.Attach(weapon, slotId, part);
					Rebuild();
				};
			}
			Hover(b, slotId, part);
			box.AddChild(b);
		}
	}

	private void Hover(Button b, string slotId, ItemInstance part)
	{
		b.MouseEntered += () =>
		{
			_hover = (slotId, part);
			_board.SetGhost(slotId, GameText.ItemName(part.Spec.Id));
			_board.SetPreview(slotId, part);
			ShowTelemetry();
		};
		b.MouseExited += () =>
		{
			_hover = null;
			_board.SetGhost(slotId, null);
			_board.SetPreview(slotId, null);
			ShowTelemetry();
		};
	}

	private Vector2 FlyoutPosition(Control anchor)
	{
		var rect = anchor.GetGlobalRect();
		var screen = GetViewportRect().Size;
		var x = Mathf.Min(rect.End.X + 8f, screen.X - 320f);
		var y = Mathf.Clamp(rect.Position.Y, 8f, screen.Y - 380f);
		return new Vector2(x, y);
	}

	// --- actions --------------------------------------------------------------------------------

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
		_sbx = null;
		Rebuild();
	}

	/// <summary>Sandbox only: mounts the sandbox build on the real weapon from owned parts, or lists what is missing.</summary>
	private void AssembleSandbox()
	{
		if (_sbx is null || _selected is null)
			return;
		var preset = WeaponWorkshop.Capture("", _sbx);
		var missing = WeaponWorkshop.Missing(preset, _profile, Character, _selected);
		if (missing.Count > 0)
		{
			_message.Text = "Still to source: " + MissingText(missing);
			return;
		}
		Act(WeaponWorkshop.Apply(_profile, Character, _selected, preset), "Assembled.");
	}

	private static string MissingText(IReadOnlyDictionary<string, int> missing)
		=> string.Join(", ", missing.Select(kv => kv.Value > 1 ? $"{GameText.ItemName(kv.Key)} x{kv.Value}" : GameText.ItemName(kv.Key)));

	// --- presets --------------------------------------------------------------------------------

	private void BuildPresets()
	{
		foreach (var child in _presetRows.GetChildren())
			child.QueueFree();
		if (_selected is not { } real)
			return;
		_presetRows.AddChild(new Label { Text = "Saved builds - " + GameText.ItemName(real.Spec.Id) });
		if (_sandbox && _sbx is { } sbx)
		{
			var current = WeaponWorkshop.Capture("", sbx);
			var missing = WeaponWorkshop.Missing(current, _profile, Character, real);
			_presetRows.AddChild(new Label
			{
				Text = missing.Count == 0 ? "You own every part of the sandbox build." : "Still to source: " + MissingText(missing),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			});
			var save = new Button { Text = "Save sandbox build as preset" };
			save.Pressed += () =>
			{
				var n = _profile.Presets.Count(p => p.WeaponId == real.Spec.Id) + 1;
				_profile.Presets.Add(WeaponWorkshop.Capture($"{GameText.ItemName(real.Spec.Id)} build {n}", sbx));
				SaveManager.Instance?.SaveInHub();
				_message.Text = "Preset saved.";
				BuildPresets();
			};
			_presetRows.AddChild(save);
		}
		else
			_presetRows.AddChild(new Label { Text = "Turn the sandbox on to save a build.", Modulate = new Color(0.65f, 0.65f, 0.7f) });
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
				_presetPanel.Visible = false;
				Rebuild();
			};
			row.AddChild(load);
			var apply = new Button { Text = "Apply", Disabled = missing.Count > 0 };
			apply.Pressed += () =>
			{
				_presetPanel.Visible = false;
				Act(WeaponWorkshop.Apply(_profile, Character, real, p), "Preset applied.");
			};
			row.AddChild(apply);
			var del = new Button { Text = "Delete" };
			del.Pressed += () =>
			{
				_profile.Presets.Remove(p);
				SaveManager.Instance?.SaveInHub();
				BuildPresets();
			};
			row.AddChild(del);
			_presetRows.AddChild(row);
		}
	}
}
