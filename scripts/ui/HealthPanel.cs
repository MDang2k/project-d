using System.Text;
using Godot;

namespace ProjectD;

/// <summary>
/// Body silhouette with per-limb HP and active effects, opened from the inventory (GDD s18).
/// Placeholder drawing until real UI art exists.
/// </summary>
public partial class HealthPanel : Control
{
	private HealthComponent? _health;

	public HealthPanel()
	{
		CustomMinimumSize = new Vector2(190f, 330f);
		MouseFilter = MouseFilterEnum.Pass;
	}

	public void Bind(HealthComponent health) => _health = health;

	private static Color Tint(float fraction, bool destroyed)
	{
		if (destroyed)
			return new Color(0.12f, 0.02f, 0.02f);
		return new Color(0.2f, 0.55f, 0.25f).Lerp(new Color(0.75f, 0.15f, 0.1f), 1f - Mathf.Clamp(fraction, 0f, 1f));
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.1f, 0.1f, 0.12f, 0.95f));
		if (_health is null)
			return;
		var font = ThemeDB.FallbackFont;
		var body = _health.Body;

		void Part(BodyPart part, Rect2 rect, string label)
		{
			var fraction = body.MaxHp(part) > 0f ? body.Hp(part) / body.MaxHp(part) : 0f;
			DrawRect(rect, Tint(fraction, body.IsDestroyed(part)));
			DrawRect(rect, new Color(0f, 0f, 0f, 0.7f), false, 1.5f);
			DrawString(font, rect.Position + new Vector2(0f, rect.Size.Y * 0.5f + 5f),
				$"{label} {body.Hp(part):0}", HorizontalAlignment.Center, rect.Size.X, 12, Colors.White);
		}

		Part(BodyPart.Head, new Rect2(75, 8, 40, 36), "H");
		Part(BodyPart.Thorax, new Rect2(60, 48, 70, 54), "T");
		Part(BodyPart.Stomach, new Rect2(65, 106, 60, 40), "S");
		Part(BodyPart.LeftArm, new Rect2(10, 48, 46, 96), "LA");
		Part(BodyPart.RightArm, new Rect2(134, 48, 46, 96), "RA");
		Part(BodyPart.LeftLeg, new Rect2(62, 150, 32, 110), "LL");
		Part(BodyPart.RightLeg, new Rect2(96, 150, 32, 110), "RL");

		var fx = _health.Effects;
		var sb = new StringBuilder();
		foreach (var p in fx.BleedingParts)
			sb.Append($"{fx.Bleed(p)} bleed: {p}\n");
		foreach (var p in fx.Fractures)
			sb.Append($"Fracture: {p}\n");
		if (fx.Pain > 1f)
			sb.Append($"Pain {fx.Pain:0}{(fx.PainSuppressed ? " (suppressed)" : "")}\n");
		var v = _health.Vitals;
		sb.Append($"Energy {v.Energy:0}  Hydration {v.Hydration:0}");
		DrawMultilineString(font, new Vector2(6f, 276f), sb.ToString(), HorizontalAlignment.Left, Size.X - 8f, 12);
	}
}
