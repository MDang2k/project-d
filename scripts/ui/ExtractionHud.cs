using Godot;

namespace ProjectD;

/// <summary>
/// Extraction countdown (GDD s15): while you stand in an extraction zone it shows how many seconds
/// are left; if the zone refuses you it says why (missing item, not enough money). Hidden otherwise.
/// </summary>
public partial class ExtractionHud : CanvasLayer
{
	private readonly RaidManager _raid;
	private Label _title = null!;
	private Label _time = null!;
	private ColorRect _barBack = null!;
	private ColorRect _bar = null!;

	public ExtractionHud(RaidManager raid) => _raid = raid;

	public override void _Ready()
	{
		Layer = 4;
		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		box.GrowHorizontal = Control.GrowDirection.Both;
		box.GrowVertical = Control.GrowDirection.Begin;
		box.OffsetTop = -190f;
		box.OffsetBottom = -130f;
		box.OffsetLeft = -150f;
		box.OffsetRight = 150f;
		box.AddThemeConstantOverride("separation", 2);
		AddChild(box);

		_title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_title.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(_title);
		_time = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_time.AddThemeFontSizeOverride("font_size", 40);
		box.AddChild(_time);
		_barBack = new ColorRect { CustomMinimumSize = new Vector2(300f, 6f), Color = new Color(0f, 0f, 0f, 0.5f) };
		box.AddChild(_barBack);
		_bar = new ColorRect { Color = new Color(0.45f, 0.95f, 0.5f) };
		_barBack.AddChild(_bar);
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!_raid.IsRunning)
		{
			Visible = false;
			return;
		}
		var s = _raid.Session;
		if (s.ExtractingZoneId is not null)
		{
			Visible = true;
			_title.Text = "EXTRACTING";
			_title.Modulate = new Color(0.55f, 1f, 0.6f);
			_time.Text = $"{s.ExtractRemainingS:0.0} s";
			_time.Modulate = Colors.White;
			_time.AddThemeFontSizeOverride("font_size", 40);
			_barBack.Visible = true;
			_bar.Size = new Vector2(300f * s.ExtractProgress, 6f);
		}
		else if (s.Blocked != ExtractBlock.None && s.BlockedSpec is { } spec)
		{
			Visible = true;
			_title.Text = "CANNOT EXTRACT HERE";
			_title.Modulate = new Color(1f, 0.55f, 0.4f);
			_time.Text = s.Blocked == ExtractBlock.NeedsFlare
				? GameText.Get("flare.needs")
				: s.Blocked == ExtractBlock.HelicopterComing
				? GameText.Get("flare.coming")
				: s.Blocked == ExtractBlock.MissingItem
				? $"needs {GameText.ItemName(spec.RequiredItemId ?? "?")}"
				: $"needs {spec.Cost:N0} {GameText.Get("currency.unit")} on you";
			_time.Modulate = new Color(1f, 0.85f, 0.7f);
			_time.AddThemeFontSizeOverride("font_size", 22);
			_barBack.Visible = false;
		}
		else
		{
			Visible = false;
		}
	}
}
