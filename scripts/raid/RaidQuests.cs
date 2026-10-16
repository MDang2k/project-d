using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Feeds raid events into the quest engine (GDD s14): kills made by the player, quest zones entered,
/// and the extraction. Thin node; the rules live in <see cref="QuestEngine"/>.
/// </summary>
public partial class RaidQuests : Node
{
	private readonly HashSet<string> _reached = new();
	private Player _player = null!;

	public string MapId { get; set; } = "map1";

	private static GameProfile Profile => GameState.Instance?.Profile ?? new GameProfile();

	public void Start(Player player, ProjectileManager projectiles)
	{
		_player = player;
		projectiles.Killed += OnBulletKill;
		player.MeleeKilled += victim => OnKill(victim, WeaponClass.Melee);
	}

	private void OnBulletKill(HealthComponent victim, Rid shooter)
	{
		if (shooter == _player.GetRid())
			OnKill(victim, _player.Weapon?.Item?.Spec.WeaponClass ?? WeaponClass.None);
	}

	private void OnKill(HealthComponent victim, WeaponClass weapon)
	{
		if (victim.GetParent() is not AiHuman ai)
			return;
		Raise(new KilledEvent(ai.Faction, weapon, MapId));
	}

	/// <summary>Dev: raises a kill or a zone event by hand (--questkill=Looters:3, --questzone=tower_zone).</summary>
	public void DebugKills(Faction faction, int count)
	{
		for (var i = 0; i < count; i++)
			Raise(new KilledEvent(faction, WeaponClass.Primary, MapId));
	}

	public void DebugZone(string zoneId) => Raise(new ZoneReachedEvent(zoneId, MapId));

	public void OnExtracted(string zoneId)
	{
		var carried = _player.Inventory.Inventory.AllItems().Select(i => i.Spec.Id).Distinct().ToList();
		Raise(new ExtractedEvent(zoneId, carried, MapId));
	}

	private void Raise(QuestEvent ev) => QuestEngine.OnEvent(QuestCatalog.All, Profile.Quests, ev);

	public override void _PhysicsProcess(double delta)
	{
		if (_player is null || _player.IsDead)
			return;
		foreach (var zone in GetTree().GetNodesInGroup("quest_zones").OfType<QuestZone>())
		{
			if (zone.MapId != "" && zone.MapId != MapId)
				continue;
			if (!_reached.Contains(zone.ZoneId) && zone.Contains(_player.GlobalPosition))
			{
				_reached.Add(zone.ZoneId);
				Raise(new ZoneReachedEvent(zone.ZoneId, MapId));
			}
		}
	}
}
