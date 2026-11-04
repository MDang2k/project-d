using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// The hub's Journal tab (M8d spec s5): what you know, grouped by kind. An overlay like the character screen; built in code.
/// </summary>
public partial class JournalScreen : Control
{
	private readonly GameProfile _profile;
	private readonly Action _closed;
	private VBoxContainer _list = null!;

	public JournalScreen(GameProfile profile, Action closed)
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
		var title = new Label { Text = GameText.Get("journal.title"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		title.AddThemeFontSizeOverride("font_size", 26);
		top.AddChild(title);
		var close = new Button { Text = GameText.Get("character.close"), CustomMinimumSize = new Vector2(100f, 36f) };
		close.Pressed += Close;
		top.AddChild(close);
		root.AddChild(top);
		root.AddChild(new Label
		{
			Text = GameText.Get("journal.hint"),
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
		var entries = KnowledgeSummary.Entries(_profile.Knowledge, GameText.Get, (k, a) => GameText.Get(k, a));
		if (entries.Count == 0)
		{
			_list.AddChild(new Label { Text = GameText.Get("journal.empty"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
			return;
		}
		foreach (var group in entries.GroupBy(e => e.Kind))
		{
			var head = new Label { Text = GameText.Get("journal.kind." + group.Key.ToString().ToLowerInvariant()) };
			head.AddThemeFontSizeOverride("font_size", 20);
			head.Modulate = new Color(1f, 0.85f, 0.4f);
			_list.AddChild(head);
			foreach (var e in group)
			{
				var box = new VBoxContainer();
				box.AddThemeConstantOverride("separation", 2);
				box.AddChild(new Label { Text = e.Title });
				if (e.Body != "")
					box.AddChild(new Label { Text = e.Body, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.8f, 0.8f, 0.75f) });
				if (e.Status != "")
					box.AddChild(new Label { Text = e.Status, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.6f, 0.9f, 1f) });
				_list.AddChild(box);
			}
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
