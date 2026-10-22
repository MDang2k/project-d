using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

public enum BoxState { Populated, Open, MissingVital, Conflict }

/// <summary>What a slot box shows; supplied by the screen for each slot of the tree.</summary>
public sealed record BoxInfo(string Title, string Part, BoxState State);

/// <summary>
/// The spatial part of the gunsmith (weapon modding v2 UI): a placeholder weapon body with anchor pins, and a
/// floating box per slot tethered by leader lines. Children of a part branch outward from its box. Pure layout
/// plus drawing; the screen decides what each box says and what a click does.
/// </summary>
public partial class GunsmithBoard : Control
{
	private const float BoxW = 124f;
	private const float BoxH = 46f;
	private const float BodyHalfW = 130f;
	private const float BodyHalfH = 28f;

	private enum Side { Left, Right, Top, Bottom }

	private sealed record Placed(SlotEntry Entry, Vector2 Center, Vector2 From, Button Box);

	private readonly List<Placed> _placed = new();
	private readonly Dictionary<string, Button> _byId = new();

	/// <summary>Raised when a box is clicked, with the box so the flyout can anchor to it.</summary>
	public event Action<SlotEntry, Control>? BoxClicked;

	private WeaponViewport _view = null!;
	private ItemInstance? _weapon;

	public override void _Ready()
	{
		Resized += QueueRedraw;
		_view = new WeaponViewport();
		AddChild(_view);
		MoveChild(_view, 0);
		var hint = new Label { Text = "Drag: rotate   Wheel: zoom   Double-click: reset", Modulate = new Color(1f, 1f, 1f, 0.5f), MouseFilter = MouseFilterEnum.Ignore };
		hint.SetAnchorsPreset(LayoutPreset.BottomLeft);
		hint.OffsetTop = -26f;
		hint.OffsetLeft = 6f;
		AddChild(hint);
	}

	public override void _Process(double delta) => QueueRedraw();

	/// <summary>Draws a hovered candidate on the 3D model (null restores the real build).</summary>
	public void SetPreview(string slotId, ItemInstance? part)
	{
		if (_weapon is null)
			return;
		if (part is null)
		{
			_view.ShowWeapon(_weapon);
			return;
		}
		var copy = WeaponWorkshop.Copy(_weapon);
		WeaponBuild.Detach(copy, slotId);
		WeaponBuild.Attach(copy, slotId, part);
		_view.ShowWeapon(copy);
	}

	public GunsmithBoard()
	{
		CustomMinimumSize = new Vector2(860f, 520f);
		ClipContents = true;
	}

	private Vector2 Area => Size.X > 10f && Size.Y > 10f ? Size : CustomMinimumSize;

	private Vector2 BodyCenter => Area / 2f;

	private static Side SideOf(string type) => type switch
	{
		"barrel" or "muzzle" or "gas" or "handguard" or "underbarrel" or "light" => Side.Left,
		"stock" or "bolt" or "receiver_cover" => Side.Right,
		"mount" or "optic" => Side.Top,
		_ => Side.Bottom,
	};

	/// <summary>Fraction of the body (x along the barrel, y top to bottom) where a slot's anchor pin sits.</summary>
	internal static Vector2 PinFraction(WeaponSlotSpec s) => s.Marker switch
	{
		"Muzzle" => new Vector2(0f, 0.5f),
		"Stock" => new Vector2(1f, 0.5f),
		"Handguard" => new Vector2(0.22f, 1f),
		"OpticMount" => new Vector2(0.45f, 0f),
		"BipodMount" => new Vector2(0.12f, 1f),
		"Light" => new Vector2(0.3f, 1f),
		_ => s.Type switch
		{
			"barrel" => new Vector2(0.08f, 0.35f),
			"gas" => new Vector2(0.3f, 0f),
			"bolt" => new Vector2(0.72f, 0.2f),
			"grip" => new Vector2(0.65f, 1f),
			"mount" => new Vector2(0.45f, 0f),
			_ => new Vector2(0.5f, 0.5f),
		},
	};

	public void Rebuild(IEnumerable<SlotEntry> slots, ItemInstance weapon, Func<SlotEntry, BoxInfo?> info)
	{
		foreach (var p in _placed)
			p.Box.QueueFree();
		_placed.Clear();
		_weapon = weapon;
		_view.ShowWeapon(weapon);
		_byId.Clear();

		// first pass: side, depth and row of every visible slot (parents come before their children)
		var rows = new Dictionary<(Side, int), int>();
		var sideOf = new Dictionary<ItemInstance, (Side Side, int Depth, int Row)> { [weapon] = (Side.Left, -1, 0) };
		var list = new List<(SlotEntry E, BoxInfo I, Side Side, int Depth, int Row)>();
		foreach (var e in slots)
		{
			if (info(e) is not { } i)
				continue;
			var parent = sideOf.TryGetValue(e.Owner, out var found) ? found : (Side: Side.Left, Depth: -1, Row: 0);
			var isRoot = ReferenceEquals(e.Owner, weapon);
			var side = isRoot ? SideOf(e.Spec.Type) : parent.Side;
			var depth = isRoot ? 0 : parent.Depth + 1;
			var row = Math.Max(isRoot ? 0 : parent.Row, rows.GetValueOrDefault((side, depth)));
			rows[(side, depth)] = row + 1;
			if (e.Part is not null)
				sideOf[e.Part] = (side, depth, row);
			list.Add((e, i, side, depth, row));
		}
		var maxRows = new Dictionary<Side, int>();
		foreach (var kv in rows)
			maxRows[kv.Key.Item1] = Math.Max(maxRows.GetValueOrDefault(kv.Key.Item1), kv.Value);

		var centers = new Dictionary<ItemInstance, Vector2>();
		foreach (var (e, i, side, depth, row) in list)
		{
			var n = maxRows[side];
			var c = BodyCenter;
			var center = side switch
			{
				Side.Left => new Vector2(c.X - BodyHalfW - 54f - BoxW / 2f - depth * (BoxW + 26f), c.Y + (row - (n - 1) / 2f) * (BoxH + 12f)),
				Side.Right => new Vector2(c.X + BodyHalfW + 54f + BoxW / 2f + depth * (BoxW + 26f), c.Y + (row - (n - 1) / 2f) * (BoxH + 12f)),
				Side.Top => new Vector2(c.X + (row - (n - 1) / 2f) * (BoxW + 14f), c.Y - BodyHalfH - 44f - BoxH / 2f - depth * (BoxH + 34f)),
				_ => new Vector2(c.X + (row - (n - 1) / 2f) * (BoxW + 14f), c.Y + BodyHalfH + 44f + BoxH / 2f + depth * (BoxH + 34f)),
			};
			center = new Vector2(Mathf.Clamp(center.X, BoxW / 2f, Area.X - BoxW / 2f), Mathf.Clamp(center.Y, BoxH / 2f, Area.Y - BoxH / 2f));
			var from = ReferenceEquals(e.Owner, weapon)
				? BodyCenter + (PinFraction(e.Spec) - new Vector2(0.5f, 0.5f)) * new Vector2(BodyHalfW * 2f, BodyHalfH * 2f)
				: centers.GetValueOrDefault(e.Owner, BodyCenter);
			if (e.Part is not null)
				centers[e.Part] = center;
			var box = MakeBox(e, i);
			box.Position = center - new Vector2(BoxW, BoxH) / 2f;
			AddChild(box);
			_placed.Add(new Placed(e, center, from, box));
			_byId[e.Spec.Id] = box;
		}
		QueueRedraw();
	}

	/// <summary>Shows a translucent candidate in a slot's box while it is hovered in the flyout.</summary>
	public void SetGhost(string slotId, string? partName)
	{
		if (!_byId.TryGetValue(slotId, out var box))
			return;
		var placed = _placed.Find(p => p.Entry.Spec.Id == slotId);
		if (placed is null)
			return;
		box.Modulate = partName is null ? Colors.White : new Color(1f, 1f, 1f, 0.55f);
		box.Text = partName is null ? BoxText(placed.Entry, null) : $"{Title(placed.Entry)}\n{Trim(partName)}";
	}

	private static string Title(SlotEntry e) => GameText.Get("slot." + e.Spec.Id);

	private static string Trim(string s) => s.Length > 18 ? s[..17] + "." : s;

	private string BoxText(SlotEntry e, BoxInfo? i)
		=> i is null ? Title(e) : $"{i.Title}\n{i.Part}";

	private Button MakeBox(SlotEntry e, BoxInfo i)
	{
		var box = new Button
		{
			Text = BoxText(e, i),
			CustomMinimumSize = new Vector2(BoxW, BoxH),
			Size = new Vector2(BoxW, BoxH),
			ClipText = true,
			TooltipText = e.Spec.Required ? "Vital part" : "",
		};
		box.AddThemeFontSizeOverride("font_size", 12);
		var (border, bg) = i.State switch
		{
			BoxState.MissingVital => (new Color(0.9f, 0.2f, 0.2f), new Color(0.25f, 0.1f, 0.1f)),
			BoxState.Conflict => (new Color(0.95f, 0.7f, 0.2f), new Color(0.25f, 0.2f, 0.08f)),
			BoxState.Open => (new Color(0.5f, 0.5f, 0.55f), new Color(0.12f, 0.13f, 0.15f, 0.6f)),
			_ => (new Color(0.75f, 0.78f, 0.85f), new Color(0.17f, 0.19f, 0.23f)),
		};
		foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
		{
			var style = new StyleBoxFlat
			{
				BgColor = state == "hover" ? bg.Lightened(0.12f) : bg,
				BorderColor = border,
				BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
				ContentMarginLeft = 4, ContentMarginRight = 4,
			};
			box.AddThemeStyleboxOverride(state, style);
		}
		if (i.State == BoxState.MissingVital)
		{
			var tween = box.CreateTween().SetLoops();
			tween.TweenProperty(box, "modulate:a", 0.55f, 0.6f);
			tween.TweenProperty(box, "modulate:a", 1f, 0.6f);
		}
		box.Pressed += () => BoxClicked?.Invoke(e, box);
		return box;
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.2f, 0.22f, 0.27f));
		foreach (var p in _placed)
		{
			var root = ReferenceEquals(p.Entry.Owner, _weapon);
			var from = p.From;
			if (root && _view.Project(_view.PinLocal(p.Entry.Spec, PinFraction(p.Entry.Spec))) is { } pin)
				from = pin;
			DrawLine(from, p.Center, new Color(0.55f, 0.58f, 0.65f, 0.8f), 1.5f);
			if (root)
				DrawCircle(from, 4f, new Color(0.95f, 0.85f, 0.4f));
		}
	}
}
