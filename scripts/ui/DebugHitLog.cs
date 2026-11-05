using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Debug hit log, bottom right (dev aid): one line per shot the player fires that lands, with the target and body part,
/// range, damage, armor result, HP left and bleeds or fractures; shots into the world show what they hit and how far.
/// Lines fade after a few seconds. Toggle in the F1 debug menu.
/// </summary>
public partial class DebugHitLog : Control
{
	private readonly ShotLog _log = new();
	private Player _player = null!;
	private bool _print;

	public void Start(Player player, ProjectileManager projectiles)
	{
		_player = player;
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
		projectiles.Impact += OnImpact;
		_print = OS.GetCmdlineUserArgs().Contains("--debugshot");
		if (_print)
			DevShots();
	}

	/// <summary>Dev (--debugshot): fires at the nearest bot's chest, then at the ground, and prints the lines.</summary>
	private async void DevShots()
	{
		await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
		var bot = GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Where(a => a.IsAlive)
			.OrderBy(a => a.GlobalPosition.DistanceTo(_player.GlobalPosition)).FirstOrDefault();
		if (bot is not null)
		{
			_player.DebugTeleport(bot.GlobalPosition + new Vector3(0f, 0f, 12f));
			for (var i = 0; i < 4; i++)
			{
				_player.DebugShootAt(bot.GlobalPosition + Vector3.Up * 1.3f, "7_62x51");
				await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
			}
		}
		_player.DebugShootAt(_player.GlobalPosition + new Vector3(10f, -1f, 30f), "7_62x51");
	}

	private void OnImpact(BulletImpact impact)
	{
		if (impact.Shooter != _player.GetRid())
			return;
		var range = impact.TravelDistance;
		if (impact.Collider is HitboxArea { Health: { } victim } area && victim.GetParent() is { } owner)
		{
			var name = owner is AiHuman ai ? ai.ActorId : owner.Name.ToString();
			var hit = impact.Hit;
			var after = victim.Body.TotalHp;
			var dmg = hit?.Damage ?? 0f;
			var effects = ShotLog.EffectsText(
				victim.Effects.BleedingParts.Select(p => (p, victim.Effects.Bleed(p))), victim.Effects.Fractures);
			Added(new ShotReport(range, name, true, area.Part, dmg, hit?.ArmorHit ?? false, hit?.Penetrated ?? true,
				hit?.DurabilityLoss ?? 0f, after + dmg, after, victim.IsDead, effects));
		}
		else
		{
			Added(new ShotReport(range, impact.Collider is Node n ? n.Name.ToString() : "world", false));
		}
	}

	private void Added(ShotReport report)
	{
		_log.Add(report);
		if (_print)
			GD.Print("SHOTLOG " + ShotLog.Format(report));
	}

	public override void _Process(double delta)
	{
		_log.Update((float)delta);
		QueueRedraw();
	}

	public override void _Draw()
	{
		var font = ThemeDB.FallbackFont;
		var size = GetViewportRect().Size;
		var y = size.Y - 24f;
		for (var i = _log.Lines.Count - 1; i >= 0; i--)
		{
			var (text, age, hit) = _log.Lines[i];
			var color = hit ? new Color(0.65f, 1f, 0.65f) : new Color(0.75f, 0.75f, 0.78f);
			color.A = _log.Alpha(age);
			var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X;
			DrawRect(new Rect2(size.X - width - 24f, y - 14f, width + 16f, 18f), new Color(0f, 0f, 0f, 0.45f * color.A));
			DrawString(font, new Vector2(size.X - width - 16f, y), text, HorizontalAlignment.Left, -1, 13, color);
			y -= 20f;
		}
	}
}
