using System;
using Godot;

namespace ProjectD;

/// <summary>
/// The hub's Nemeses tab (M8c spec s4): news of nemeses that died, then every nemesis with its rank, how often it killed
/// you, where, and what it carries. An overlay like the character screen; built in code.
/// </summary>
public partial class NemesisScreen : Control
{
	private readonly GameProfile _profile;
	private readonly Action _closed;
	private VBoxContainer _list = null!;

	public NemesisScreen(GameProfile profile, Action closed)
	{
		_profile = profile;
		_closed = closed;
		Visible = false;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
	}

	public void Open()
	{
		Visible = true;
		Rebuild();
		_profile.Nemeses.AcknowledgeFallen(); // the news has been read
	}

	public void Close()
	{
		Visible = false;
		_closed();
	}

	public override void _Ready()
	{
		var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.7f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(dim);
		var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(760f, 560f) };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.13f, 0.14f, 0.17f),
			BorderColor = new Color(0.4f, 0.4f, 0.45f),
			BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
			ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
		});
		center.AddChild(panel);
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 10);
		panel.AddChild(root);
		var top = new HBoxContainer();
		var title = new Label { Text = GameText.Get("nemesis.title"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		title.AddThemeFontSizeOverride("font_size", 26);
		top.AddChild(title);
		var close = new Button { Text = GameText.Get("character.close"), CustomMinimumSize = new Vector2(100f, 36f) };
		close.Pressed += Close;
		top.AddChild(close);
		root.AddChild(top);
		root.AddChild(new Label
		{
			Text = GameText.Get("nemesis.hint"),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(0.8f, 0.8f, 0.75f),
		});
		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_list.AddThemeConstantOverride("separation", 14);
		scroll.AddChild(_list);
		root.AddChild(scroll);
	}

	private void Rebuild()
	{
		foreach (var child in _list.GetChildren())
			child.QueueFree();
		Func<string, string> text = k => GameText.Get(k);
		foreach (var fallen in _profile.Nemeses.Fallen)
			_list.AddChild(new Label
			{
				Text = NemesisSummary.FallenLine(fallen, text),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Modulate = new Color(0.55f, 0.95f, 0.6f),
			});
		if (_profile.Nemeses.All.Count == 0)
		{
			_list.AddChild(new Label { Text = GameText.Get("nemesis.none"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
			return;
		}
		foreach (var n in _profile.Nemeses.All)
		{
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 2);
			var name = new Label { Text = $"{n.DisplayName}  -  {GameText.Get("faction." + n.Faction.ToString().ToLowerInvariant())}  -  {GameText.Get("nemesis.rank", n.Rank)}" };
			name.AddThemeFontSizeOverride("font_size", 20);
			name.Modulate = new Color(1f, 0.85f, 0.4f);
			box.AddChild(name);
			box.AddChild(new Label { Text = GameText.Get("nemesis.kills", n.KillCount) });
			foreach (var line in NemesisSummary.KillLog(n, text))
				box.AddChild(new Label { Text = "   " + line, Modulate = new Color(0.8f, 0.8f, 0.75f) });
			if (n.FriendlyOrigin)
				box.AddChild(new Label { Text = GameText.Get("nemesis.friendly"), Modulate = new Color(1f, 0.6f, 0.45f) });
			box.AddChild(new Label
			{
				Text = GameText.Get("nemesis.carries") + " " + string.Join(", ", NemesisSummary.Carries(n, GameText.ItemName)),
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			});
			_list.AddChild(box);
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}
}
