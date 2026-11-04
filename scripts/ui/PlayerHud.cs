using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Minimal in-raid HUD (GDD s18): stamina, energy and hydration, a small body silhouette, status
/// tags, a stance pictogram, quick-use slots and one line of short messages. Every element fades
/// in only while it matters. There is never an ammo counter and never a crosshair.
/// </summary>
public partial class PlayerHud : CanvasLayer
{
	/// <summary>
	/// Keeps every element on screen (for now). Later this becomes an options entry:
	/// off / auto-hide / always on.
	/// </summary>
	public static bool AlwaysVisible { get; set; } = true;

	private Player _player = null!;
	private HudCanvas _canvas = null!;

	public static PlayerHud Attach(Player player)
	{
		var hud = new PlayerHud { Name = "Hud", Layer = 5, _player = player };
		player.AddChild(hud);
		return hud;
	}

	public override void _Ready()
	{
		_canvas = new HudCanvas { Player = _player };
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_canvas.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_canvas);
	}
}

/// <summary>Draws the whole HUD in one control; it reads the player's state every frame.</summary>
public partial class HudCanvas : Control
{
	private static readonly Color Ink = new(0.92f, 0.92f, 0.88f);
	private static readonly Color Back = new(0f, 0f, 0f, 0.45f);

	public Player Player { get; set; } = null!;

	private readonly HudFade _stamina = new(1.2f);
	private readonly HudFade _vitals = new(1.5f);
	private readonly HudFade _health = new(2.0f);
	private readonly HudFade _stance = new(1.4f);
	private readonly HudFade _quick = new(2.5f);
	private readonly HudFade _message = new(2.2f);
	private readonly HudFade _levelUp = new(3.0f, 0.2f, 0.8f);
	private readonly HudFade _hostile = new(4.0f, 0.2f, 1.0f);
	private readonly HudFade _nemesis = new(4.0f, 0.2f, 1.0f);
	private string _nemesisText = "";
	private string _hostileText = "";
	private string _levelUpText = "";
	private SkillBook? _skills;
	private float _lastStamina = 1f;
	private float _staminaMoving;
	private string _messageText = "";

	public override void _Ready()
	{
		Player.StanceChanged += _ => _stance.Pulse();
		Player.QuickUsePressed += _ => _quick.Pulse();
		Player.WeaponMessage += text =>
		{
			_messageText = text;
			_message.Pulse();
		};
		Player.QuickUse.Changed += _ => _quick.Pulse();
		// a skill went up a level (M8a spec s3): a brief line, shown even when the rest of the HUD hides
		_skills = GameState.Instance?.Profile.Skills;
		if (_skills is not null)
			_skills.LevelUp += OnLevelUp;
		RaidStanding.FactionTurnedHostile += OnFactionHostile;
		RaidNemesis.PlayerKilledNemesis += OnNemesisKilled;
	}

	private void OnNemesisKilled(Nemesis nemesis)
	{
		_nemesisText = NemesisSummary.KilledLine(nemesis, k => GameText.Get(k));
		_nemesis.Pulse();
	}

	private void OnFactionHostile(Faction faction)
	{
		_hostileText = GameText.Get("rep.hostile", GameText.Get("faction." + faction.ToString().ToLowerInvariant()));
		_hostile.Pulse();
	}

	public override void _ExitTree()
	{
		if (_skills is not null)
			_skills.LevelUp -= OnLevelUp;
		RaidStanding.FactionTurnedHostile -= OnFactionHostile;
		RaidNemesis.PlayerKilledNemesis -= OnNemesisKilled;
	}

	private void OnLevelUp(string skill, int from, int to)
	{
		_levelUpText = GameText.Get("skill.levelup", GameText.Get("skill." + skill), to);
		_levelUp.Pulse();
	}

	public override void _Process(double deltaD)
	{
		var delta = (float)deltaD;
		var stamina = Player.Legs.Fraction;
		_staminaMoving = Mathf.Max(0f, _staminaMoving - delta);
		if (Mathf.Abs(stamina - _lastStamina) > 0.0004f)
			_staminaMoving = 0.8f;
		_lastStamina = stamina;
		_stamina.Update(delta, HudRules.StaminaShown(stamina, _staminaMoving > 0f));

		var health = Player.Health;
		var vitals = health.Vitals;
		_vitals.Update(delta, HudRules.VitalsShown(vitals.Energy / 100f, vitals.Hydration / 100f,
			health.Consumables.IsBusy));
		var lowest = 1f;
		foreach (var part in System.Enum.GetValues<BodyPart>())
		{
			var max = health.Body.MaxHp(part);
			if (max > 0f)
				lowest = Mathf.Min(lowest, health.Body.Hp(part) / max);
		}
		var fx = health.Effects;
		_health.Update(delta, HudRules.HealthShown(lowest, fx.BleedingParts.Any(), fx.Fractures.Count > 0,
			fx.Pain > 8f));

		if (PlayerHud.AlwaysVisible)
		{
			_stamina.Pulse();
			_vitals.Pulse();
			_health.Pulse();
			_stance.Pulse();
			_quick.Pulse();
		}
		_stance.Update(delta, false);
		_quick.Update(delta, QuickWantsVisible());
		_message.Update(delta, false);
		_levelUp.Update(delta, false);
		_hostile.Update(delta, false);
		_nemesis.Update(delta, false);

		Visible = !Player.InventoryOpen && !Player.IsDead;
		QueueRedraw();
	}

	/// <summary>The quick-use bar also stays up while an item is being used.</summary>
	private bool QuickWantsVisible()
		=> Player.Health.Medical.IsBusy || Player.Health.Consumables.IsBusy;

	public override void _Draw()
	{
		var size = Size;
		var font = ThemeDB.FallbackFont;
		var left = 28f;
		var bottom = size.Y - 28f;

		// Stamina: a clear bar for the legs (sprinting, jumping) and a thin one for the arms (weapon hold,
		// melee), with a label so it reads at a glance. Turns red while exhausted.
		if (_stamina.Alpha > 0.01f)
		{
			var a = _stamina.Alpha;
			const float w = 180f;
			var legs = new Rect2(left, bottom - 12f, w, 10f);
			DrawRect(legs.Grow(1f), new Color(0f, 0f, 0f, 0.55f * a));
			DrawRect(legs, new Color(0.12f, 0.12f, 0.12f, 0.7f * a));
			var legColor = Player.Legs.IsExhausted ? new Color(0.9f, 0.3f, 0.22f) : new Color(0.9f, 0.88f, 0.55f);
			legColor.A = a;
			DrawRect(new Rect2(legs.Position, new Vector2(w * Player.Legs.Fraction, legs.Size.Y)), legColor);
			var label = Ink;
			label.A = a;
			DrawString(font, new Vector2(left, bottom - 15f), "STAMINA", HorizontalAlignment.Left, -1, 11, label);

			var arms = new Rect2(left, bottom + 2f, w, 4f);
			DrawRect(arms, new Color(0.12f, 0.12f, 0.12f, 0.7f * a));
			var armColor = Player.Arms.IsExhausted ? new Color(0.9f, 0.3f, 0.22f) : new Color(0.6f, 0.75f, 0.9f);
			armColor.A = a;
			DrawRect(new Rect2(arms.Position, new Vector2(w * Player.Arms.Fraction, arms.Size.Y)), armColor);
		}

		// Energy and hydration: two small bars above it.
		if (_vitals.Alpha > 0.01f)
		{
			var v = Player.Health.Vitals;
			Pip(left, bottom - 32f, 85f, v.Energy / 100f, "E", new Color(0.9f, 0.7f, 0.25f), _vitals.Alpha, font);
			Pip(left + 95f, bottom - 32f, 85f, v.Hydration / 100f, "H", new Color(0.35f, 0.65f, 0.95f), _vitals.Alpha, font);
		}

		// Body silhouette, only while injured.
		if (_health.Alpha > 0.01f)
			DrawBody(new Vector2(left, bottom - 146f), _health.Alpha, font);

		DrawTags(new Vector2(left + 60f, bottom - 136f), font);

		// Stance pictogram next to the bars when it just changed.
		if (_stance.Alpha > 0.01f)
			DrawStance(new Vector2(left + 196f, bottom - 28f), _stance.Alpha);

		// Quick-use slots bottom centre.
		if (_quick.Alpha > 0.01f)
			DrawQuickSlots(new Vector2(size.X * 0.5f, size.Y - 74f), _quick.Alpha, font);

		// A level-up, upper middle, in a warmer colour than the other messages.
		if (_levelUp.Alpha > 0.01f && _levelUpText.Length > 0)
		{
			var gold = new Color(1f, 0.85f, 0.4f, _levelUp.Alpha);
			DrawString(font, new Vector2(0f, size.Y * 0.2f), _levelUpText, HorizontalAlignment.Center, size.X, 26, gold);
		}

		// A faction turned on the player: a warning line under the level-up line, in red.
		if (_hostile.Alpha > 0.01f && _hostileText.Length > 0)
		{
			var red = new Color(0.95f, 0.35f, 0.3f, _hostile.Alpha);
			DrawString(font, new Vector2(0f, size.Y * 0.26f), _hostileText, HorizontalAlignment.Center, size.X, 22, red);
		}

		// You killed a nemesis: a gold line, under the other warnings.
		if (_nemesis.Alpha > 0.01f && _nemesisText.Length > 0)
		{
			var gold = new Color(1f, 0.85f, 0.4f, _nemesis.Alpha);
			DrawString(font, new Vector2(0f, size.Y * 0.32f), _nemesisText, HorizontalAlignment.Center, size.X, 24, gold);
		}

		// Reading a document: a bar and the word, low centre.
		if (Player.ReadProgress >= 0f)
		{
			var barBack = new Rect2(size.X * 0.5f - 110f, size.Y - 150f, 220f, 6f);
			DrawRect(barBack, new Color(0f, 0f, 0f, 0.55f));
			DrawRect(new Rect2(barBack.Position, new Vector2(barBack.Size.X * Player.ReadProgress, barBack.Size.Y)), new Color(0.85f, 0.8f, 0.55f));
			DrawString(font, new Vector2(0f, size.Y - 156f), GameText.Get("knowledge.reading"), HorizontalAlignment.Center, size.X, 16, Ink);
		}

		// One short message above them.
		if (_message.Alpha > 0.01f && _messageText.Length > 0)
		{
			var c = Ink;
			c.A = _message.Alpha;
			DrawString(font, new Vector2(0f, size.Y - 100f), _messageText, HorizontalAlignment.Center, size.X, 18, c);
		}
	}

	private void Pip(float x, float y, float w, float fraction, string label, Color color, float alpha, Font font)
	{
		var back = Back;
		back.A *= alpha;
		DrawRect(new Rect2(x, y, w, 4f), back);
		color.A = alpha;
		DrawRect(new Rect2(x, y, w * Mathf.Clamp(fraction, 0f, 1f), 4f), color);
		var ink = Ink;
		ink.A = alpha;
		DrawString(font, new Vector2(x, y - 3f), label, HorizontalAlignment.Left, -1, 11, ink);
	}

	private void DrawBody(Vector2 origin, float alpha, Font font)
	{
		var body = Player.Health.Body;
		void Part(BodyPart part, Rect2 r)
		{
			var max = body.MaxHp(part);
			var f = max > 0f ? body.Hp(part) / max : 0f;
			var c = body.IsDestroyed(part)
				? new Color(0.12f, 0.02f, 0.02f)
				: new Color(0.25f, 0.6f, 0.3f).Lerp(new Color(0.85f, 0.15f, 0.1f), 1f - Mathf.Clamp(f, 0f, 1f));
			c.A = alpha;
			var moved = new Rect2(origin + r.Position, r.Size);
			DrawRect(moved, c);
			DrawRect(moved, new Color(0f, 0f, 0f, 0.6f * alpha), false, 1f);
		}

		Part(BodyPart.Head, new Rect2(18, 0, 14, 14));
		Part(BodyPart.Thorax, new Rect2(12, 16, 26, 22));
		Part(BodyPart.Stomach, new Rect2(14, 40, 22, 14));
		Part(BodyPart.LeftArm, new Rect2(0, 16, 10, 38));
		Part(BodyPart.RightArm, new Rect2(40, 16, 10, 38));
		Part(BodyPart.LeftLeg, new Rect2(14, 56, 10, 42));
		Part(BodyPart.RightLeg, new Rect2(26, 56, 10, 42));
	}

	/// <summary>Active conditions as short words (GDD s18 status icons).</summary>
	private void DrawTags(Vector2 origin, Font font)
	{
		var health = Player.Health;
		var fx = health.Effects;
		var tags = new List<(string Text, Color Color)>();
		if (fx.BleedingParts.Any(p => fx.Bleed(p) == BleedLevel.Heavy))
			tags.Add(("HEAVY BLEEDING", new Color(0.95f, 0.25f, 0.2f)));
		else if (fx.BleedingParts.Any())
			tags.Add(("BLEEDING", new Color(0.9f, 0.4f, 0.3f)));
		if (fx.Fractures.Count > 0)
			tags.Add(("FRACTURE", new Color(0.95f, 0.65f, 0.25f)));
		if (fx.Pain > 8f && !fx.PainSuppressed)
			tags.Add(("PAIN", new Color(0.9f, 0.55f, 0.55f)));
		if (Player.Tier >= WeightTier.Heavy)
			tags.Add(("OVERWEIGHT", new Color(0.85f, 0.8f, 0.4f)));
		if (health.Vitals.Energy / 100f < HudRules.TagShowBelow)
			tags.Add(("HUNGRY", new Color(0.9f, 0.7f, 0.25f)));
		if (health.Vitals.Hydration / 100f < HudRules.TagShowBelow)
			tags.Add(("THIRSTY", new Color(0.4f, 0.7f, 0.95f)));
		for (var i = 0; i < tags.Count; i++)
			DrawString(font, origin + new Vector2(0f, i * 16f), tags[i].Text, HorizontalAlignment.Left, -1, 13, tags[i].Color);
	}

	private void DrawStance(Vector2 pos, float alpha)
	{
		var c = Ink;
		c.A = alpha;
		var dark = new Color(0f, 0f, 0f, 0.5f * alpha);
		DrawRect(new Rect2(pos - new Vector2(4f, 30f), new Vector2(40f, 38f)), dark);
		var level = Player.HeightLevel;
		switch (Player.Stance)
		{
			case Stance.Prone:
				DrawRect(new Rect2(pos + new Vector2(0f, -8f), new Vector2(30f, 6f)), c);
				DrawCircle(pos + new Vector2(2f, -5f), 4f, c);
				break;
			default:
				var h = Mathf.Lerp(12f, 26f, Player.Stance == Stance.Stand ? 1f : Mathf.Clamp(level, 0f, 1f));
				DrawCircle(pos + new Vector2(16f, -h - 6f), 4f, c);
				DrawRect(new Rect2(pos + new Vector2(11f, -h - 2f), new Vector2(10f, h)), c);
				break;
		}
	}

	private void DrawQuickSlots(Vector2 centre, float alpha, Font font)
	{
		const float w = 104f, h = 38f, gap = 8f;
		var total = QuickUseBar.SlotCount * w + (QuickUseBar.SlotCount - 1) * gap;
		var x0 = centre.X - total * 0.5f;
		var inv = Player.Inventory.Inventory;
		for (var i = 0; i < QuickUseBar.SlotCount; i++)
		{
			var rect = new Rect2(x0 + i * (w + gap), centre.Y, w, h);
			var id = Player.QuickUse.IdAt(i);
			var count = Player.QuickUse.CountOf(i, inv);
			var has = id is not null && count > 0;
			var back = Back;
			back.A = 0.55f * alpha;
			DrawRect(rect, back);
			var ink = has ? Ink : new Color(0.6f, 0.6f, 0.6f);
			ink.A = alpha * (has ? 1f : 0.7f);
			DrawString(font, rect.Position + new Vector2(5f, 14f), $"{i + 4}", HorizontalAlignment.Left, -1, 12,
				new Color(0.95f, 0.85f, 0.5f, alpha));
			var name = id is null ? "-" : GameText.ItemName(id);
			DrawString(font, rect.Position + new Vector2(18f, 14f), name, HorizontalAlignment.Left, w - 22f, 12, ink);
			if (id is not null)
				DrawString(font, rect.Position + new Vector2(5f, 31f), has ? $"x{count}" : "none", HorizontalAlignment.Left, -1, 12, ink);
		}
	}
}
