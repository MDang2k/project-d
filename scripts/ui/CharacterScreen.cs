using System;
using Godot;

namespace ProjectD;

/// <summary>
/// The hub's Character tab (M8a spec s3): every skill with its level, a progress bar to the next level and the XP.
/// Reputation joins it with 8b. An overlay like the trader and gunsmith screens; built in code.
/// </summary>
public partial class CharacterScreen : Control
{
	private readonly GameProfile _profile;
	private readonly Action _closed;
	private VBoxContainer _list = null!;

	public CharacterScreen(GameProfile profile, Action closed)
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
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(620f, 0f) };
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
		var title = new Label { Text = GameText.Get("character.title"), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		title.AddThemeFontSizeOverride("font_size", 26);
		top.AddChild(title);
		var close = new Button { Text = GameText.Get("character.close"), CustomMinimumSize = new Vector2(100f, 36f) };
		close.Pressed += Close;
		top.AddChild(close);
		root.AddChild(top);
		root.AddChild(new Label
		{
			Text = GameText.Get("character.hint"),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Modulate = new Color(0.8f, 0.8f, 0.75f),
		});
		_list = new VBoxContainer();
		_list.AddThemeConstantOverride("separation", 8);
		root.AddChild(_list);
	}

	private void Rebuild()
	{
		foreach (var child in _list.GetChildren())
			child.QueueFree();
		foreach (var row in SkillSummary.Rows(_profile.Skills, id => GameText.Get("skill." + id)))
		{
			var line = new HBoxContainer();
			line.AddThemeConstantOverride("separation", 12);
			line.AddChild(new Label { Text = row.Name, CustomMinimumSize = new Vector2(170f, 0f) });
			line.AddChild(new Label { Text = GameText.Get("character.level", row.Level), CustomMinimumSize = new Vector2(70f, 0f) });
			line.AddChild(new ProgressBar
			{
				MinValue = 0, MaxValue = 1, Value = row.Progress, ShowPercentage = false,
				CustomMinimumSize = new Vector2(200f, 18f), SizeFlagsVertical = SizeFlags.ShrinkCenter,
			});
			line.AddChild(new Label { Text = row.XpText, Modulate = new Color(0.8f, 0.8f, 0.75f) });
			_list.AddChild(line);
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
