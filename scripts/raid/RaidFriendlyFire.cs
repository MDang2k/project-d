using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Friendly fire in a raid (M8b spec s2 and s3): hits and kills the player deals on AI of their own side's faction
/// cost reputation, three of them turn that faction hostile for the rest of the raid, and a faction the player is on
/// bad terms with starts the raid hostile. Thin node over <see cref="FriendlyFireTracker"/> and <see cref="RaidStanding"/>.
/// </summary>
public partial class RaidFriendlyFire : Node
{
	private Player _player = null!;
	private FriendlyFireTracker? _tracker;

	private static GameProfile Profile => GameState.Instance?.Profile ?? new GameProfile();

	public void Start(Player player, ProjectileManager projectiles)
	{
		_player = player;
		var args = OS.GetCmdlineUserArgs();
		// dev: --ffside=allied|soviet picks a side for a headless raid, --ffhits=N[:kill] lands N friendly hits after a few seconds
		if (Profile.Side is null && args.FirstOrDefault(x => x.StartsWith("--ffside="))?["--ffside=".Length..] is { } devSide)
			Profile.Side = devSide == "soviet" ? PlayerSide.Soviet : PlayerSide.Allied;
		if (args.FirstOrDefault(x => x.StartsWith("--ffhits="))?["--ffhits=".Length..] is { } hits)
			DevHits(hits);
		if (Profile.Side is not { } side)
			return; // a raid opened from the editor without a hub side has no friends to shoot
		var own = side.ToFaction();
		_tracker = new FriendlyFireTracker(own, ReputationBook.QuartermasterOf(side));
		_tracker.BecameHostile += OnBecameHostile;
		RaidStanding.Begin(_tracker);
		if (Profile.Reputation.OwnFactionHostileAtStart(own))
			_tracker.StartHostile(own);
		projectiles.Hit += OnBulletHit;
		player.MeleeHitOn += OnMeleeHit;
	}

	public override void _ExitTree() => RaidStanding.End();

	private async void DevHits(string spec)
	{
		await ToSignal(GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
		var parts = spec.Split(':');
		var killed = parts.Length > 1 && parts[1] == "kill";
		var own = Profile.Side?.ToFaction();
		var victim = GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().FirstOrDefault(a => a.Faction == own);
		if (victim is null || !int.TryParse(parts[0], out var n))
		{
			GD.Print("REP dev: no friendly AI to hit");
			return;
		}
		for (var i = 0; i < n; i++)
			Register(victim.Health, killed);
		GD.Print($"REP dev: {n} hits on {victim.Name}; hostile={RaidStanding.IsHostileToPlayer(victim.Faction)} " +
			$"rep={Profile.Reputation.Hundredths(ReputationBook.FactionId(victim.Faction)!)}/{Profile.Reputation.Hundredths(ReputationBook.QuartermasterOf(Profile.Side!.Value))}");
	}

	private void OnBulletHit(HealthComponent victim, bool killed, Rid shooter)
	{
		if (shooter == _player.GetRid())
			Register(victim, killed);
	}

	private void OnMeleeHit(HealthComponent victim, bool killed) => Register(victim, killed);

	private void Register(HealthComponent victim, bool killed)
	{
		if (_tracker is null || victim.GetParent() is not AiHuman ai)
			return;
		var result = _tracker.RegisterHit(ai.Faction, killed);
		if (result.Changes.Count == 0)
			return;
		FriendlyFireTracker.Apply(result, Profile.Reputation);
		RaidStanding.Record(result);
		GD.Print($"REP friendly {(killed ? "kill" : "hit")} on {ai.Faction}: " +
			string.Join(", ", System.Linq.Enumerable.Select(result.Changes, c => $"{c.Target} {c.Delta}")));
	}

	private void OnBecameHostile(Faction faction)
	{
		GD.Print($"REP {faction} is hostile for the rest of the raid"); // the HUD draws the line from RaidStanding.FactionTurnedHostile
	}
}
