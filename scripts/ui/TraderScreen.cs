using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Trader window of the hub (GDD s13): a tab per trader with Buy, Sell and Barter lists, the money
/// total and a line for the last result. No drag and drop. Built in code, placeholder art.
/// </summary>
public partial class TraderScreen : Control
{
	private readonly GameProfile _profile;
	private readonly Action _changed;
	private string _selected = TraderCatalog.Order[0];
	private Label _money = null!;
	private Label _message = null!;
	private HBoxContainer _tabRow = null!;
	private VBoxContainer _page = null!;

	public TraderScreen(GameProfile profile, Action changed)
	{
		_profile = profile;
		_changed = changed;
		Visible = false;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
	}

	private static CharacterInventory Character => RunState.Current.Inventory ?? new CharacterInventory();

	public TradeEngine MakeEngine()
	{
		var registry = ItemDb.Instance?.Registry;
		var roundValues = new Dictionary<string, int>();
		if (registry is not null)
		{
			foreach (var id in registry.Ids)
			{
				if (registry.Spec(id) is { AmmoId: { } ammoId } spec)
					roundValues[ammoId] = spec.BaseValue;
			}
		}
		return new TradeEngine(_profile, Character, id => registry?.Spec(id),
			(id, count) => registry?.Create(id, count), ammoId => roundValues.GetValueOrDefault(ammoId));
	}

	/// <summary>Dev: opens a given trader and tab (--trader=medic:Tasks).</summary>
	public void DebugShow(string traderId, string? tab)
	{
		_selected = traderId;
		_debugTab = tab;
		Open();
	}

	private string? _debugTab;

	public void Open()
	{
		Visible = true;
		// Start on the first trader you can actually use.
		if (TraderCatalog.Find(_selected) is not { } current || !_profile.IsTraderAvailable(current))
			_selected = TraderCatalog.All.FirstOrDefault(_profile.IsTraderAvailable)?.Id ?? _selected;
		Rebuild();
	}

	public void Close()
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
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(980f, 700f) };
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
		top.AddChild(new Label { Text = "Traders", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		_money = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
		top.AddChild(_money);
		var close = new Button { Text = "Close" };
		close.Pressed += Close;
		top.AddChild(close);
		root.AddChild(top);

		_tabRow = new HBoxContainer();
		_tabRow.AddThemeConstantOverride("separation", 4);
		root.AddChild(_tabRow);

		_page = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		root.AddChild(_page);
		_message = new Label { Modulate = new Color(1f, 0.85f, 0.4f) };
		root.AddChild(_message);
	}

	// --- building -------------------------------------------------------------------------------

	private void Rebuild()
	{
		if (QuestSync.Run(_profile, Character) is { } rewardNote)
			_message.Text = rewardNote;
		var engine = MakeEngine();
		_money.Text = $"{engine.Money:N0} {GameText.Get("currency.unit")}";

		foreach (var child in _tabRow.GetChildren())
			child.QueueFree();
		foreach (var trader in TraderCatalog.All)
		{
			var id = trader.Id;
			var locked = !_profile.IsTraderAvailable(trader);
			var b = new Button
			{
				Text = GameText.Get(trader.NameKey) + (locked ? " (locked)" : ""),
				ToggleMode = true,
				ButtonPressed = id == _selected,
			};
			b.Pressed += () =>
			{
				_selected = id;
				_message.Text = "";
				Rebuild();
			};
			_tabRow.AddChild(b);
		}

		foreach (var child in _page.GetChildren())
			child.QueueFree();
		if (TraderCatalog.Find(_selected) is { } selected)
			BuildTrader(selected, engine);
	}

	private void BuildTrader(TraderSpec trader, TradeEngine engine)
	{
		var state = _profile.Trader(trader.Id);
		_page.AddChild(new Label { Text = $"{GameText.Get(trader.NameKey)}    level {state.Level}" });
		var standing = new Label { Text = ReputationSummary.TraderLine(_profile.Reputation, trader.Id, k => GameText.Get(k)) };
		standing.Modulate = _profile.Reputation.Refuses(trader.Id) ? new Color(1f, 0.5f, 0.4f) : new Color(0.8f, 0.8f, 0.75f);
		_page.AddChild(standing);
		if (_profile.LockReasonKey(trader) is { } reason)
		{
			_page.AddChild(new Label { Text = GameText.Get(reason) });
			return;
		}
		var level = trader.LevelSpec(state.Level);
		var quests = QuestCatalog.All
			.Where(q => QuestEngine.ResolveTrader(q.GiverTraderId, _profile.Side) == trader.Id
				&& _profile.Quests.StateOf(q) != QuestState.Locked)
			.ToList();
		if (level.Sells.Count == 0 && level.Buys.Count == 0 && level.Barters.Count == 0 && level.Insurance is null
			&& quests.Count == 0)
		{
			_page.AddChild(new Label { Text = GameText.Get("trader.nothing") });
			return;
		}

		var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		_page.AddChild(tabs);
		if (level.Sells.Count > 0)
			tabs.AddChild(BuyPage(trader, level, state, engine));
		if (level.Buys.Count > 0)
			tabs.AddChild(SellPage(trader, level, state, engine));
		if (level.Barters.Count > 0)
			tabs.AddChild(BarterPage(trader, level, state, engine));
		if (level.Insurance is { } terms)
			tabs.AddChild(InsurancePage(trader, terms, engine));
		if (quests.Count > 0)
			tabs.AddChild(TasksPage(quests));
		if (trader.Id == "scientist")
			tabs.AddChild(TranslatePage());
		if (_debugTab is not null)
		{
			for (var i = 0; i < tabs.GetChildCount(); i++)
			{
				if (tabs.GetChild(i).Name == _debugTab)
					tabs.CurrentTab = i;
			}
		}
	}

	/// <summary>Tasks tab (GDD s14): accept, track and turn in this trader's quests.</summary>
	private ScrollContainer TasksPage(List<QuestSpec> quests)
	{
		var scroll = Scroll("Tasks", out var list);
		var log = _profile.Quests;
		foreach (var q in quests.Where(q => log.StateOf(q) != QuestState.Completed))
		{
			var state = log.StateOf(q);
			var entry = log.Entry(q);
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 2);
			var head = new HBoxContainer();
			var tracked = log.TrackedId == q.Id ? "  [tracked]" : "";
			head.AddChild(new Label
			{
				Text = QuestText.Title(q) + (state == QuestState.ReadyToTurnIn ? "  - ready to turn in" : "") + tracked,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				Modulate = state == QuestState.ReadyToTurnIn ? new Color(0.6f, 1f, 0.6f) : Colors.White,
			});
			if (state == QuestState.Available)
				head.AddChild(ActionButton("Accept", () => FinishQuest(QuestEngine.Accept(log, q, _profile) ? "Task accepted." : (QuestEngine.GiverRefuses(q, _profile) ? GameText.Get("rep.refuses") : "Cannot accept."))));
			if (state is QuestState.Active or QuestState.ReadyToTurnIn && log.TrackedId != q.Id)
				head.AddChild(ActionButton("Track", () => FinishQuest(QuestEngine.Track(log, q) ? "Tracking." : "Cannot track.")));
			if (state == QuestState.ReadyToTurnIn)
				head.AddChild(ActionButton("Turn in", () => TurnIn(q)));
			box.AddChild(head);
			box.AddChild(new Label
			{
				Text = QuestText.Description(q),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Modulate = new Color(0.8f, 0.8f, 0.85f),
			});
			for (var i = 0; i < q.Objectives.Count; i++)
				box.AddChild(new Label { Text = "   - " + QuestText.Objective(q.Objectives[i], entry.Progress[i]) });
			box.AddChild(new Label { Text = "   Reward: " + QuestText.Rewards(q), Modulate = new Color(0.9f, 0.85f, 0.5f) });
			box.AddChild(new HSeparator());
			list.AddChild(box);
		}
		var done = quests.Count(q => log.StateOf(q) == QuestState.Completed);
		if (done > 0)
			list.AddChild(new Label { Text = $"{done} task(s) completed", Modulate = new Color(0.6f, 0.6f, 0.65f) });
		return scroll;
	}

	/// <summary>Translate tab (M8d spec s4): one lab note at a time, free, done after a few raids.</summary>
	private ScrollContainer TranslatePage()
	{
		var scroll = Scroll(GameText.Get("translate.tab"), out var list);
		var book = _profile.Knowledge;
		if (book.ActiveTranslation is { } active && book.Spec(active) is { } running)
			list.AddChild(new Label { Text = GameText.Get("translate.busy", GameText.Get(running.TitleKey), book.RaidsLeft(active)), Modulate = new Color(0.6f, 0.9f, 1f) });
		var waiting = KnowledgeSummary.Untranslated(book);
		if (waiting.Count == 0 && book.ActiveTranslation is null)
			list.AddChild(new Label { Text = GameText.Get("translate.none") });
		foreach (var spec in waiting)
		{
			var id = spec.Id;
			list.AddChild(Row(GameText.Get(spec.TitleKey), GameText.Get("trader.raids", spec.TranslationRaids), GameText.Get("translate.button"),
				() => FinishQuest(book.StartTranslation(id) ? GameText.Get("translate.started") : ""), book.ActiveTranslation is null));
		}
		return scroll;
	}

	private static Button ActionButton(string text, Action onPress)
	{
		var b = new Button { Text = text, CustomMinimumSize = new Vector2(90f, 0f) };
		b.Pressed += onPress;
		return b;
	}

	private void TurnIn(QuestSpec q)
	{
		var result = QuestEngine.TurnIn(QuestCatalog.All, _profile.Quests, _profile, Character, q);
		if (result != TurnInFailure.None)
		{
			_message.Text = result == TurnInFailure.MissingItems ? "You do not have the items." : "Not ready yet.";
			Rebuild();
			return;
		}
		var note = QuestSync.Run(_profile, Character);
		FinishQuest(note ?? "Task completed.");
	}

	private void FinishQuest(string message)
	{
		_message.Text = message;
		SaveManager.Instance?.SaveInHub();
		_changed();
		Rebuild();
	}

	/// <summary>Insurance tab (GDD s15): carried insurable items with the premium; unused insurance is lost at raid end.</summary>
	private ScrollContainer InsurancePage(TraderSpec trader, InsuranceTerms terms, TradeEngine engine)
	{
		var scroll = Scroll("Insurance", out var list);
		var unit = GameText.Get("currency.unit");
		list.AddChild(new Label
		{
			Text = $"Premium {terms.PremiumFraction * 100f:0}% of value, {terms.ReturnChance * 100f:0}% of insured lost items come back "
				+ "(after your next raid). Covers only the next raid. Insure what you carry before you deploy.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		});
		var items = InsuranceService.InsurableItems(Character).ToList();
		if (items.Count == 0)
			list.AddChild(new Label { Text = "You carry nothing that can be insured (equip gear from the stash first)." });
		foreach (var item in items)
		{
			var premium = InsuranceService.Premium(terms, item, _profile.Reputation.BuyFactor(trader.Id));
			var text = ItemLine(item) + (item.Insured ? "   [INSURED]" : "");
			list.AddChild(Row(text, $"{premium:N0} {unit}", item.Insured ? "Insured" : "Insure",
				() => FinishInsure(InsuranceService.Insure(_profile, Character, trader, item)),
				!item.Insured && engine.Money >= premium && !_profile.Reputation.Refuses(trader.Id)));
		}
		return scroll;
	}

	private void FinishInsure(InsureResult result)
	{
		_message.Text = result.Ok
			? $"Insured for {result.Premium:N0} {GameText.Get("currency.unit")}."
			: result.Failure switch
			{
				InsureFailure.NotEnoughMoney => "Not enough money.",
				InsureFailure.AlreadyInsured => "Already insured.",
				InsureFailure.NotInsurable => "That cannot be insured.",
				InsureFailure.NotCarried => "Only carried items can be insured.",
				InsureFailure.Refused => GameText.Get("rep.refuses"),
				_ => "This trader does not insure you.",
			};
		if (result.Ok)
		{
			SaveManager.Instance?.SaveInHub();
			_changed();
		}
		Rebuild();
	}

	private ScrollContainer Scroll(string name, out VBoxContainer list)
	{
		var scroll = new ScrollContainer { Name = name, SizeFlagsVertical = SizeFlags.ExpandFill };
		list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(list);
		return scroll;
	}

	private static HBoxContainer Row(string text, string price, string button, Action onPress, bool enabled = true)
	{
		var row = new HBoxContainer();
		row.AddChild(new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		row.AddChild(new Label { Text = price, CustomMinimumSize = new Vector2(140f, 0f), HorizontalAlignment = HorizontalAlignment.Right });
		var b = new Button { Text = button, CustomMinimumSize = new Vector2(90f, 0f), Disabled = !enabled };
		b.Pressed += onPress;
		row.AddChild(b);
		return row;
	}

	private ScrollContainer BuyPage(TraderSpec trader, TraderLevelSpec level, TraderState state, TradeEngine engine)
	{
		var scroll = Scroll("Buy", out var list);
		foreach (var listing in level.Sells)
		{
			// seen in a trader list: the gunsmith sandbox may use it
			if (ItemDb.Data(listing.ItemId)?.Payload is PartData)
				_profile.ExaminedParts.Add(listing.ItemId);
			var price = engine.BuyPrice(level, listing, state);
			var unit = GameText.Get("currency.unit");
			var canAfford = engine.Money >= price && !engine.Refuses(state);
			list.AddChild(Row(GameText.ItemName(listing.ItemId), $"{price:N0} {unit}", "Buy",
				() => Finish(engine.Buy(level, state, listing), "Bought"), canAfford));
		}
		return scroll;
	}

	private ScrollContainer SellPage(TraderSpec trader, TraderLevelSpec level, TraderState state, TradeEngine engine)
	{
		var scroll = Scroll("Sell", out var list);
		var items = engine.SellableItems(level).ToList();
		if (items.Count == 0)
			list.AddChild(new Label { Text = "You have nothing this trader wants (empty containers first)." });
		foreach (var item in items)
		{
			var payout = engine.SellPayout(level, item, state);
			list.AddChild(Row(ItemLine(item), $"{payout:N0} {GameText.Get("currency.unit")}", "Sell",
				() => Finish(engine.Sell(level, state, item), "Sold"), !engine.Refuses(state)));
		}
		return scroll;
	}

	private ScrollContainer BarterPage(TraderSpec trader, TraderLevelSpec level, TraderState state, TradeEngine engine)
	{
		var scroll = Scroll("Barter", out var list);
		foreach (var offer in level.Barters)
		{
			var give = string.Join(" + ", offer.Give.Select(g => g.Count > 1
				? $"{GameText.ItemName(g.ItemId)} x{g.Count}"
				: GameText.ItemName(g.ItemId)));
			var get = offer.GetCount > 1 ? $"{GameText.ItemName(offer.GetId)} x{offer.GetCount}" : GameText.ItemName(offer.GetId);
			list.AddChild(Row($"{give}   ->   {get}", "", "Barter",
				() => Finish(engine.Barter(level, state, offer), "Bartered"), !engine.Refuses(state)));
		}
		return scroll;
	}

	private static string ItemLine(ItemInstance item)
	{
		var text = GameText.ItemName(item.Spec.Id);
		if (item.Count > 1)
			text += $" x{item.Count}";
		if (item.Spec.MagazineCapacity > 0)
			text += $"  ({item.Rounds.Count}/{item.Spec.MagazineCapacity} rounds)";
		if (item.Spec.MaxDurability > 0f)
			text += $"  ({TradeEngine.Condition(item) * 100f:0}% condition)";
		return text;
	}

	private void Finish(TradeResult result, string verb)
	{
		_message.Text = result.Ok
			? $"{verb} for {result.Money:N0} {GameText.Get("currency.unit")}."
			: result.Failure switch
			{
				TradeFailure.NotEnoughMoney => "Not enough money.",
				TradeFailure.StashFull => "Your stash is full.",
				TradeFailure.HasContents => "Empty it first.",
				TradeFailure.MissingBarterItems => "You do not have the items for that barter.",
				TradeFailure.Refused => GameText.Get("rep.refuses"),
				TradeFailure.NotSellableHere => "This trader will not buy that.",
				_ => "Not possible.",
			};
		if (result.Ok)
		{
			SaveManager.Instance?.SaveInHub();
			_changed();
		}
		Rebuild();
	}
}
