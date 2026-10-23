using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// The hub between raids (GDD s4, s10): pick a side once, move gear between the stash and the
/// character by drag and drop, then deploy to map 1. Menu-based, built in code, placeholder art.
/// Every successful move is saved at once.
/// </summary>
public partial class HubScreen : CanvasLayer
{
	private const string MapPath = "res://scenes/maps/map1.tscn";

	private Control _root = null!;
	private Control _content = null!;
	private Label _header = null!;
	private Label _message = null!;
	private PanelContainer _tooltip = null!;
	private Label _tooltipText = null!;
	private DragOverlay _drag = null!;
	private TraderScreen _traders = null!;
	private GunsmithScreen _gunsmith = null!;
	private readonly List<GridView> _grids = new();
	private readonly List<SlotView> _slots = new();

	private string _pendingNote = "";
	private ItemInstance? _held;
	private ItemLocation _heldFrom;
	private bool _heldWasRotated;
	private Vector2 _grabOffset;

	private GameProfile Profile => GameState.Instance?.Profile ?? _fallbackProfile;
	private readonly GameProfile _fallbackProfile = new();
	private static RunProgress Run => RunState.Current;

	public override void _Ready()
	{
		Layer = 5;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_root = new Control();
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		var back = new ColorRect { Color = new Color(0.1f, 0.11f, 0.13f), MouseFilter = Control.MouseFilterEnum.Ignore };
		back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(back);

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
		_drag = new DragOverlay { Source = () => _held is null ? null : (_held, GetViewport().GetMousePosition() - _grabOffset) };
		_root.AddChild(_drag);
		_traders = new TraderScreen(Profile, Rebuild);
		_root.AddChild(_traders);
		_gunsmith = new GunsmithScreen(Profile, Rebuild);
		_root.AddChild(_gunsmith);

		SaveManager.Instance?.SaveInHub();
		Rebuild();
		ApplyDevArgs();
	}

	/// <summary>
	/// Dev args after "--": --side=allied|soviet (pick the side), --equipkit (equip the usual loadout from
	/// the stash), --deploy, --hubreport (print what the profile holds), --shot=path (screenshot and quit).
	/// </summary>
	private async void ApplyDevArgs()
	{
		var args = OS.GetCmdlineUserArgs();
		string? shot = null;
		foreach (var a in args)
		{
			if (a.StartsWith("--side=") && !Profile.HasChosenSide)
				ChooseSide(a.EndsWith("soviet") ? PlayerSide.Soviet : PlayerSide.Allied);
			else if (a.StartsWith("--shot="))
				shot = a["--shot=".Length..];
		}
		if (args.Contains("--equipkit") && Run.Inventory is { } c)
		{
			foreach (var (id, slot) in new[]
			{
				("fal_type", EquipSlot.Primary), ("ak_type", EquipSlot.Primary), ("pistol", EquipSlot.Pistol),
				("combat_knife", EquipSlot.Melee), ("flak_vest_t2", EquipSlot.Vest),
				("steel_helmet_t1", EquipSlot.Head), ("light_rig", EquipSlot.Rig), ("field_pack", EquipSlot.Backpack),
			})
			{
				var item = Profile.Stash.Items.FirstOrDefault(i => i.Spec.Id == id);
				if (item is not null)
					c.TryEquipFrom(item, new ItemLocation(null, Profile.Stash), slot);
			}
			// A few magazines and bandages into the pockets and pack so the raid has something to use.
			foreach (var id in new[] { "fal_mag", "ak_mag", "fal_mag", "ak_mag", "pistol_mag", "bandage", "morphine" })
			{
				var item = Profile.Stash.Items.FirstOrDefault(i => i.Spec.Id == id);
				if (item is not null && c.TryAutoStore(item))
					Profile.Stash.Remove(item);
			}
			SaveManager.Instance?.SaveInHub();
			Rebuild();
		}
		if (args.Contains("--insure") && Run.Inventory is { } ch && TraderCatalog.Find("allied_qm") is { } qm)
		{
			var bought = 0;
			foreach (var item in InsuranceService.InsurableItems(ch).ToList())
				bought += InsuranceService.Insure(Profile, ch, args.Contains("--soviet") ? TraderCatalog.Find("soviet_qm")! : qm, item).Ok ? 1 : 0;
			GD.Print($"INSURE insured={bought} money={_traders.MakeEngine().Money}");
			SaveManager.Instance?.SaveInHub();
		}
		if (args.Contains("--hubreport"))
		{
			var carried = Run.Inventory?.AllItems().Count() ?? 0;
			GD.Print($"HUB side={Profile.Side} stash={Profile.Stash.ItemCount} carried={carried} " +
				$"weapon={Run.Inventory?.InSlot(EquipSlot.Primary)?.Spec.Id ?? "none"} raids={Run.Raids} " +
				$"money={_traders.MakeEngine().Money} traders={TraderCatalog.All.Count} returns={Profile.InsuranceReturns.Count} " +
				$"insuredCarried={Run.Inventory?.AllItems().Count(i => i.Insured) ?? 0} " +
				$"build=[{string.Join(",", WeaponBuild.AllParts(Run.Inventory?.InSlot(EquipSlot.Primary) ?? ItemDb.Create("ak_type")!).Select(p => p.Spec.Id))}]");
		}
		foreach (var a in args)
		{
			if (a.StartsWith("--accept="))
			{
				QuestSync.Run(Profile, Run.Inventory);
				var q = QuestCatalog.Find(a["--accept=".Length..]);
				GD.Print($"QUEST accept {q?.Id}: {(q is not null && QuestEngine.Accept(Profile.Quests, q))}");
			}
			else if (a.StartsWith("--turnin="))
			{
				QuestSync.Run(Profile, Run.Inventory);
				var q = QuestCatalog.Find(a["--turnin=".Length..]);
				var result = q is null ? TurnInFailure.UnknownQuest : QuestEngine.TurnIn(QuestCatalog.All, Profile.Quests, Profile, Run.Inventory!, q);
				var note = QuestSync.Run(Profile, Run.Inventory);
				GD.Print($"QUEST turnin {q?.Id}: {result} {note}");
			}
			else if (a == "--givefound" && Run.Inventory is not null)
			{
				foreach (var (id, n) in new[] { ("bandage", 3), ("morphine", 1) })
				{
					if (ItemDb.Create(id, n) is { } found)
					{
						found.FoundInRaid = true;
						Profile.Stash.TryPlaceAnywhere(found);
					}
				}
			}
		}
		if (args.Any(x => x.StartsWith("--accept=") || x.StartsWith("--turnin=") || x == "--givefound"))
			SaveManager.Instance?.SaveInHub();
		if (args.Contains("--questreport"))
		{
			QuestSync.Run(Profile, Run.Inventory);
			foreach (var q in QuestCatalog.All)
				GD.Print($"QUEST {q.Id} state={Profile.Quests.StateOf(q)} progress=[{string.Join(",", Profile.Quests.Entry(q).Progress)}] tracked={Profile.Quests.TrackedId == q.Id}");
			GD.Print($"QUEST traders: medic L{Profile.Trader("medic").Level} engineer L{Profile.Trader("engineer").Level} " +
				$"allied_qm L{Profile.Trader("allied_qm").Level} soviet_unlocked={Profile.UnlockedTraders.Contains("soviet_qm")} money={_traders.MakeEngine().Money}");
		}
		if (args.Contains("--tradetest"))
			TradeSelfTest();
		if (args.Contains("--traders"))
			_traders.Open();
		foreach (var a in args)
		{
			if (a.StartsWith("--flyout="))
				_gunsmith.DebugFlyout(a["--flyout=".Length..]);
			if (a.StartsWith("--hubshot="))
			{
				var path = a["--hubshot=".Length..];
				GetTree().CreateTimer(1.5).Timeout += () =>
				{
					GetViewport().GetTexture().GetImage().SavePng(path);
					GetTree().Quit();
				};
			}
			if (a.StartsWith("--gunsmith="))
				_gunsmith.DebugOpen(a["--gunsmith=".Length..]);
			if (a.StartsWith("--trader="))
			{
				var parts = a["--trader=".Length..].Split(':');
				_traders.DebugShow(parts[0], parts.Length > 1 ? parts[1] : null);
			}
		}
		if (shot is not null && !args.Contains("--deploy"))
		{
			await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
			GetViewport().GetTexture().GetImage().SavePng(shot);
			GetTree().Quit();
			return;
		}
		if (args.Contains("--deploy"))
			Deploy();
	}

	/// <summary>Dev (--tradetest): runs a buy, a sell and a failing barter against the real catalog and prints the results.</summary>
	private void TradeSelfTest()
	{
		var engine = _traders.MakeEngine();
		var trader = TraderCatalog.All.First(t => _profile().IsTraderAvailable(t) && t.LevelSpec(1).Sells.Count > 0);
		var state = Profile.Trader(trader.Id);
		var level = trader.LevelSpec(1);
		var listing = level.Sells.First(l => engine.BuyPrice(level, l) > 0);
		var before = engine.Money;
		var buy = engine.Buy(level, state, listing);
		GD.Print($"TRADE buy {listing.ItemId} from {trader.Id}: {buy.Failure} price={buy.Money} money {before}->{engine.Money}");
		var item = engine.SellableItems(level).FirstOrDefault(i => i.Spec.Id == listing.ItemId);
		if (item is not null)
		{
			var sell = engine.Sell(level, state, item);
			GD.Print($"TRADE sell {item.Spec.Id}: {sell.Failure} payout={sell.Money} money->{engine.Money} spent={state.Spent}");
		}
		var smuggler = TraderCatalog.Find("smuggler")!;
		var barter = engine.Barter(smuggler.LevelSpec(1), Profile.Trader("smuggler"), smuggler.LevelSpec(1).Barters[0]);
		GD.Print($"TRADE barter: {barter.Failure}");
	}

	private GameProfile _profile() => Profile;

	// --- layout ---------------------------------------------------------------------------------

	private void Rebuild()
	{
		// Insured returns that are due go to the stash; a full stash keeps them waiting for the next try.
		var questNote = QuestSync.Run(Profile, Run.Inventory);
		if (questNote is not null)
			SaveManager.Instance?.SaveInHub();
		var delivered = InsuranceService.TryDeliver(Profile);
		if (delivered.Count > 0)
		{
			SaveManager.Instance?.SaveInHub();
			_pendingNote = $"{delivered.Count} insured item(s) came back to your stash.";
		}
		if (questNote is not null)
			_pendingNote = (_pendingNote == "" ? "" : _pendingNote + "  ") + questNote;
		foreach (var child in _content.GetChildren())
			child.QueueFree();
		_grids.Clear();
		_slots.Clear();
		_tooltip.Visible = false;
		if (!Profile.HasChosenSide)
		{
			BuildSidePicker();
		}
		else
		{
			EnsureCharacter();
			BuildLoadout();
		}
		_drag.MoveToFront();
		_tooltip.MoveToFront();
		_traders.MoveToFront();
		_gunsmith.MoveToFront();
	}

	private void BuildSidePicker()
	{
		var box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 16);
		var title = new Label { Text = "Choose your side", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 36);
		box.AddChild(title);
		box.AddChild(new Label
		{
			Text = "Allied PMCs and Soviet PMCs hire contractors for the same zone. Your side decides who is friendly "
				+ "and which quartermaster you can use. The choice is made once.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(640f, 0f),
		});
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 20);
		row.AddChild(SideButton(PlayerSide.Allied, "Allied PMC\nWestern contractors, NATO gear"));
		row.AddChild(SideButton(PlayerSide.Soviet, "Soviet PMC\nEastern contractors, Warsaw Pact gear"));
		box.AddChild(row);
		_content.AddChild(box);
	}

	private Button SideButton(PlayerSide side, string text)
	{
		var b = new Button { Text = text, CustomMinimumSize = new Vector2(300f, 90f) };
		b.Pressed += () => ChooseSide(side);
		return b;
	}

	private void BuildLoadout()
	{
		var character = Run.Inventory!;
		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 20);
		_header = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		top.AddChild(_header);
		var gunsmith = new Button { Text = "Gunsmith", CustomMinimumSize = new Vector2(140f, 44f) };
		gunsmith.Pressed += () => _gunsmith.Open();
		top.AddChild(gunsmith);
		var traders = new Button { Text = "Traders", CustomMinimumSize = new Vector2(140f, 44f) };
		traders.Pressed += () => _traders.Open();
		top.AddChild(traders);
		var deploy = new Button { Text = "DEPLOY to map 1", CustomMinimumSize = new Vector2(220f, 44f) };
		deploy.Pressed += Deploy;
		top.AddChild(deploy);
		_content.AddChild(top);
		_message = new Label { Modulate = new Color(1f, 0.85f, 0.4f), Text = _pendingNote };
		_pendingNote = "";
		_content.AddChild(_message);

		var columns = new HBoxContainer();
		columns.AddThemeConstantOverride("separation", 20);
		columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		columns.MouseFilter = Control.MouseFilterEnum.Ignore;
		_content.AddChild(columns);

		var equip = new VBoxContainer();
		equip.AddChild(new Label { Text = "Equipment" });
		equip.AddChild(Row(Slot(EquipSlot.Head, character, 90, 90), Slot(EquipSlot.Vest, character, 90, 90)));
		equip.AddChild(Row(Slot(EquipSlot.Rig, character, 90, 70), Slot(EquipSlot.Backpack, character, 90, 70)));
		equip.AddChild(Slot(EquipSlot.Primary, character, 280, 90));
		equip.AddChild(Slot(EquipSlot.Secondary, character, 280, 90));
		equip.AddChild(Row(Slot(EquipSlot.Pistol, character, 90, 70), Slot(EquipSlot.Melee, character, 90, 90)));
		equip.AddChild(Slot(EquipSlot.SecureContainer, character, 90, 90));
		columns.AddChild(equip);

		var carry = new VBoxContainer();
		carry.AddThemeConstantOverride("separation", 6);
		carry.AddChild(new Label { Text = "Carried" });
		var carryList = new VBoxContainer();
		carryList.AddThemeConstantOverride("separation", 6);
		var pockets = new HBoxContainer();
		foreach (var p in character.Pockets)
			pockets.AddChild(Grid(p, "Pocket"));
		carryList.AddChild(new Label { Text = "Pockets" });
		carryList.AddChild(pockets);
		AddItemGrids(carryList, character, EquipSlot.Rig, "Rig");
		AddItemGrids(carryList, character, EquipSlot.Backpack, "Backpack");
		AddItemGrids(carryList, character, EquipSlot.SecureContainer, "Secure container");
		var carryScroll = new ScrollContainer { CustomMinimumSize = new Vector2(280f, 0f), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		carryScroll.AddChild(carryList);
		carry.AddChild(carryScroll);
		columns.AddChild(carry);

		var stash = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		stash.AddChild(new Label { Text = $"Stash ({GameProfile.StashWidth}x{GameProfile.StashHeight})" });
		var stashScroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(GameProfile.StashWidth * GridView.Cell + 20f, 0f),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		stashScroll.AddChild(Grid(Profile.Stash, "Stash"));
		stash.AddChild(stashScroll);
		columns.AddChild(stash);
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

	public override void _Process(double delta)
	{
		if (_header is null || !GodotObject.IsInstanceValid(_header) || Run.Inventory is not { } c)
			return;
		_header.Text = $"Side: {Profile.Side}    Carried {c.TotalWeightKg:0.0} kg    Raids {Run.Raids}  " +
			$"Extracted {Run.Extractions}  Killed {Run.Deaths}  MIA {Run.Mia}\n" +
			"Drag items between the stash and your character, R rotates the held item, then Deploy." +
			(Profile.InsuranceReturns.Count > 0
				? $"\nInsured returns waiting: {Profile.InsuranceReturns.Count}"
				: "");
		foreach (var g in _grids)
			g.QueueRedraw();
		foreach (var s in _slots)
			s.QueueRedraw();
	}

	// --- profile actions -----------------------------------------------------------------------

	/// <summary>The character carries only the secure pouch at first; everything else is in the stash.</summary>
	private static void EnsureCharacter()
	{
		if (Run.Inventory is not null)
			return;
		var inv = new CharacterInventory();
		if (ItemDb.Create("secure_pouch") is { } pouch)
			inv.TryEquip(pouch, EquipSlot.SecureContainer);
		Run.Restore(inv, Run.Raids, Run.Extractions, Run.Deaths, Run.Mia);
	}

	private void ChooseSide(PlayerSide side)
	{
		Profile.Side = side;
		EnsureCharacter();
		var result = StarterKit.PlaceInGrid(Profile.Stash,
			StarterKit.Build(StarterKitData.For(side).Entries, ItemDb.Create));
		SaveManager.Instance?.SaveInHub();
		Rebuild();
		if (result.Dropped.Count > 0)
			_message.Text = $"Stash full: {result.Dropped.Count} starter items did not fit and were dropped.";
		else
			_message.Text = "Your starter kit is in the stash. Equip what you want to carry, then Deploy.";
	}

	private void Deploy()
	{
		var side = Profile.Side!.Value;
		var issued = SafetyNet.Issue(Run.Inventory!, Profile, StarterKitData.For(side), ItemDb.Create);
		if (issued is not null)
		{
			SaveManager.Instance?.SaveInHub();
			Rebuild();
			_message.Text = issued.Dropped.Count > 0
				? $"No weapon anywhere: a basic kit was issued, but the stash is full and {issued.Dropped.Count} items were dropped. Equip a weapon, then Deploy."
				: "No weapon anywhere: a basic kit was issued to your stash. Equip a weapon, then Deploy.";
			return;
		}
		SaveManager.Instance?.BeginRaid(Run.Inventory!);
		GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MapPath);
	}

	// --- input and dragging ---------------------------------------------------------------------

	public override void _Input(InputEvent @event)
	{
		if (_traders.Visible || _gunsmith.Visible)
			return;
		switch (@event)
		{
			case InputEventKey { Pressed: true, PhysicalKeycode: Key.R, Echo: false } when _held is not null:
				_held.Rotated = !_held.Rotated;
				_drag.QueueRedraw();
				GetViewport().SetInputAsHandled();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
				if (mb.Pressed)
					OnPress(mb.GlobalPosition);
				else
					OnRelease(mb.GlobalPosition);
				break;
			case InputEventMouseMotion mm:
				OnMove(mm.GlobalPosition);
				break;
		}
	}

	/// <summary>A view inside a scroll container only counts where the container is actually visible.</summary>
	private static bool Reachable(Control view, Vector2 pos)
	{
		if (!view.GetGlobalRect().HasPoint(pos))
			return false;
		for (var n = view.GetParent(); n is not null; n = n.GetParent())
		{
			if (n is ScrollContainer sc && !sc.GetGlobalRect().HasPoint(pos))
				return false;
		}
		return true;
	}

	private (ItemInstance Item, ItemLocation From, Vector2 Offset)? ItemAt(Vector2 pos)
	{
		foreach (var g in _grids)
		{
			if (!Reachable(g, pos) || g.ItemAtGlobal(pos) is not { } item)
				continue;
			var cell = g.Grid.PositionOf(item)!.Value;
			var topLeft = g.GlobalPosition + new Vector2(cell.X, cell.Y) * GridView.Cell;
			return (item, new ItemLocation(null, g.Grid), pos - topLeft);
		}
		foreach (var s in _slots)
		{
			if (Reachable(s, pos) && s.Item is { } item)
				return (item, new ItemLocation(s.Slot, null), new Vector2(item.Width, item.Height) * GridView.Cell * 0.5f);
		}
		return null;
	}

	private void OnPress(Vector2 pos)
	{
		if (ItemAt(pos) is not { } hit)
			return;
		_held = hit.Item;
		_heldFrom = hit.From;
		_heldWasRotated = hit.Item.Rotated;
		_grabOffset = hit.Offset;
		_tooltip.Visible = false;
		_drag.QueueRedraw();
		GetViewport().SetInputAsHandled();
	}

	private void OnRelease(Vector2 pos)
	{
		if (_held is not { } item)
			return;
		var inv = Run.Inventory!;
		var done = false;
		var topLeft = pos - _grabOffset;
		foreach (var g in _grids)
		{
			if (!Reachable(g, pos))
				continue;
			var x = (int)Mathf.Floor((topLeft.X - g.GlobalPosition.X + GridView.Cell * 0.5f) / GridView.Cell);
			var y = (int)Mathf.Floor((topLeft.Y - g.GlobalPosition.Y + GridView.Cell * 0.5f) / GridView.Cell);
			done = inv.TryMoveToGrid(item, _heldFrom, g.Grid, x, y);
			break;
		}
		if (!done)
		{
			foreach (var s in _slots)
			{
				if (!Reachable(s, pos))
					continue;
				done = inv.TryEquipFrom(item, _heldFrom, s.Slot);
				break;
			}
		}
		if (!done)
			item.Rotated = _heldWasRotated;
		_held = null;
		_drag.QueueRedraw();
		GetViewport().SetInputAsHandled();
		if (!done)
			return;
		SaveManager.Instance?.SaveInHub();
		Rebuild();
	}

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
		_tooltipText.Text = InventoryScreen.Describe(hit.Item);
		_tooltip.Visible = true;
		_tooltip.Position = pos + new Vector2(16f, 16f);
	}
}
