using Godot;

namespace ProjectD;

/// <summary>
/// The minefield map drawn in the journal (Border Strip spec s5): the map outline with north up, the border line, the
/// minefields in red and the safe lanes in green. A plain top-down sketch; the mines on the lanes stay.
/// </summary>
public partial class MinefieldMapView : Control
{
	private const float PixelsPerMetre = 1.1f; // pixels per metre

	private readonly MinefieldMap _map;

	public MinefieldMapView(MinefieldMap map)
	{
		_map = map;
		CustomMinimumSize = new Vector2(map.MapSize.X * PixelsPerMetre, map.MapSize.Y * PixelsPerMetre);
	}

	private Vector2 P(Vector2 metres) => metres * PixelsPerMetre;

	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, _map.MapSize * PixelsPerMetre), new Color(0.12f, 0.13f, 0.11f));
		DrawRect(new Rect2(Vector2.Zero, _map.MapSize * PixelsPerMetre), new Color(0.5f, 0.5f, 0.45f), false, 1.5f);
		DrawDashedLine(P(new Vector2(_map.MapSize.X / 2f, 0f)), P(new Vector2(_map.MapSize.X / 2f, _map.MapSize.Y)), new Color(0.6f, 0.6f, 0.55f, 0.6f), 1f, 6f);
		foreach (var field in _map.Minefields)
		{
			var r = new Rect2(P(field.Position), field.Size * PixelsPerMetre);
			DrawRect(r, new Color(0.8f, 0.15f, 0.12f, 0.35f));
			DrawRect(r, new Color(0.9f, 0.25f, 0.2f), false, 1.5f);
		}
		foreach (var lane in _map.Lanes)
		{
			for (var i = 1; i < lane.Points.Count; i++)
				DrawLine(P(lane.Points[i - 1]), P(lane.Points[i]), new Color(0.4f, 1f, 0.5f), 3f);
			foreach (var p in lane.Points)
				DrawCircle(P(p), 3.5f, new Color(0.4f, 1f, 0.5f));
		}
		var font = ThemeDB.FallbackFont;
		DrawString(font, new Vector2(8f, 18f), "N", HorizontalAlignment.Left, -1, 14, new Color(0.9f, 0.9f, 0.9f));
		DrawString(font, new Vector2(P(new Vector2(_map.MapSize.X / 2f, 0f)).X + 6f, 18f), GameText.Get("journal.minefield.border"), HorizontalAlignment.Left, -1, 12, new Color(0.7f, 0.7f, 0.65f));
	}
}
