using Godot;

namespace ProjectD;

/// <summary>
/// In-raid objective text for the tracked quest (GDD s14, M7e): the title and one line per objective.
/// Fades in for a few seconds at raid start and whenever progress changes, or stays visible while
/// <see cref="PlayerHud.AlwaysVisible"/> is on. No markers, no input.
/// </summary>
public partial class ObjectiveHud : CanvasLayer
{
	private readonly HudFade _fade = new(5f, 0.2f, 0.8f);
	private readonly Label _label = new();
	private string _last = "";

	private static QuestLog Log => (GameState.Instance?.Profile ?? new GameProfile()).Quests;

	public override void _Ready()
	{
		Layer = 4;
		_label.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_label.GrowHorizontal = Control.GrowDirection.Begin;
		_label.OffsetRight = -20f;
		_label.OffsetTop = 18f;
		_label.HorizontalAlignment = HorizontalAlignment.Right;
		_label.MouseFilter = Control.MouseFilterEnum.Ignore;
		_label.AddThemeFontSizeOverride("font_size", 15);
		AddChild(_label);
		Log.Changed += () => _fade.Pulse();
		_fade.Pulse(); // shown for the first seconds of the raid
		Refresh();
	}

	private void Refresh()
	{
		var log = Log;
		var tracked = QuestEngine.Tracked(QuestCatalog.All, log);
		if (tracked is null)
		{
			_label.Text = "";
			return;
		}
		var entry = log.Entry(tracked);
		var lines = new System.Collections.Generic.List<string> { QuestText.Title(tracked) };
		for (var i = 0; i < tracked.Objectives.Count; i++)
			lines.Add("  " + QuestText.Objective(tracked.Objectives[i], entry.Progress[i]));
		var text = string.Join("\n", lines);
		if (text != _last)
		{
			_last = text;
			_label.Text = text;
			_fade.Pulse();
		}
	}

	public override void _Process(double delta)
	{
		Refresh();
		if (PlayerHud.AlwaysVisible)
			_fade.Pulse();
		_fade.Update((float)delta, false);
		_label.Modulate = new Color(1f, 1f, 1f, _fade.Alpha);
	}
}
