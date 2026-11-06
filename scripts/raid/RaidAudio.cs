using Godot;

namespace ProjectD;

/// <summary>
/// The sounds of a raid that no single node owns (audio pass): the wind bed, bullet impacts by what they hit (flesh,
/// armor, metal, concrete, dirt), the crack of a bullet passing close, the player's hurt grunt, and the extraction
/// ticks and chime. Weapon, footstep, door and mine sounds are played where they happen. Thin node over <see cref="Sfx"/>.
/// </summary>
public partial class RaidAudio : Node
{
	private Player _player = null!;
	private RaidManager _raid = null!;
	private float _tickLeft;
	private float _whizCooldown;

	public void Start(Player player, ProjectileManager projectiles, RaidManager raid)
	{
		_player = player;
		_raid = raid;
		Sfx.EnsureBuses();
		Sfx.Loop2D(this, "ambient_wind");
		projectiles.Impact += OnImpact;
		projectiles.Passed += OnPassed;
		player.Health.HitTaken += _ => Sfx.Play2D(this, "hurt");
		raid.Ended += (outcome, _) =>
		{
			if (outcome == RaidOutcome.Extracted)
				Sfx.Play2D(this, "extract_done");
		};
	}

	private void OnImpact(BulletImpact impact)
	{
		string id;
		if (impact.Collider is HitboxArea)
			id = impact.Hit is { ArmorHit: true, Penetrated: false } ? "impact_armor" : "impact_flesh";
		else
			id = SurfaceSound(impact.Collider as Node, impact.Normal);
		Sfx.Play3D(this, id, impact.Position);
	}

	/// <summary>Picks the impact sound from what was hit: vehicles and masts ring, floors puff, everything else cracks.</summary>
	public static string SurfaceSound(Node? collider, Vector3 normal)
	{
		var name = collider?.Name.ToString() ?? "";
		foreach (var metal in new[] { "queue", "convoy", "boxcar", "heli", "pylon", "relay", "fence", "gate" })
		{
			if (name.StartsWith(metal))
				return "impact_metal";
		}
		var path = collider?.GetPath().ToString() ?? "";
		if (normal.Y > 0.7f || path.Contains("/Ground/") || path.Contains("/Terrain/"))
			return "impact_dirt";
		return "impact_concrete";
	}

	private void OnPassed(Vector3 from, Vector3 to, Rid shooter)
	{
		if (shooter == _player.GetRid() || _whizCooldown > 0f)
			return;
		var head = _player.GlobalPosition + Vector3.Up * 1.5f;
		var segment = to - from;
		var length = segment.LengthSquared();
		if (length < 0.0001f)
			return;
		var closest = from + segment * Mathf.Clamp((head - from).Dot(segment) / length, 0f, 1f);
		if (closest.DistanceTo(head) > 3.5f)
			return;
		_whizCooldown = 0.12f;
		Sfx.Play3D(this, "bullet_whiz", closest);
	}

	public override void _Process(double delta)
	{
		_whizCooldown = Mathf.Max(0f, _whizCooldown - (float)delta);
		if (!_raid.IsRunning || _raid.Session.ExtractingZoneId is null)
		{
			_tickLeft = 0f;
			return;
		}
		if ((_tickLeft -= (float)delta) <= 0f)
		{
			_tickLeft = 1f;
			Sfx.Play2D(this, "extract_tick");
		}
	}
}
