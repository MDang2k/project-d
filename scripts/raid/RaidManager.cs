using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Runs one raid in a map scene: the timer, extraction zones and the outcome (GDD s15). Thin node
/// around <see cref="RaidSession"/>; the map scene or test range binds the player and listens to
/// <see cref="Ended"/>.
/// </summary>
public partial class RaidManager : Node
{
	[Export] public RaidConfig? Config { get; set; }

	private RaidSession? _session;
	private Player? _player;

	/// <summary>The slot each lost item was worn in, captured just before a death or MIA took the gear (a nemesis puts it back on).</summary>
	public System.Collections.Generic.IReadOnlyDictionary<ItemInstance, EquipSlot> LostSlots { get; private set; }
		= new System.Collections.Generic.Dictionary<ItemInstance, EquipSlot>();

	public RaidSession Session => _session ?? throw new InvalidOperationException("Raid not started");
	public bool IsRunning => _session?.IsActive ?? false;

	/// <summary>Outcome and the gear lost with it (empty when extracted).</summary>
	public event Action<RaidOutcome, System.Collections.Generic.IReadOnlyList<ItemInstance>>? Ended;

	/// <summary>Starts the countdown for this player. Zones are read from the tree at this point.</summary>
	public void Start(Player player)
	{
		_player = player;
		var cfg = Config ?? new RaidConfig();
		var zones = GetTree().GetNodesInGroup("extraction_zones").OfType<ExtractionZone>().Select(z => z.ToSpec());
		_session = new RaidSession(cfg.DurationS, zones);
		_session.Ended += OnEnded;
		player.Health.Died += OnPlayerDied;
	}

	private void OnPlayerDied(BodyPart _) => _session?.PlayerDied();

	private void OnEnded(RaidOutcome outcome)
	{
		if (_player is null)
			return;
		_player.Health.Died -= OnPlayerDied;
		// A paid extract takes its cost from carried banknotes (not the secure container) before the outcome rules run.
		if (outcome == RaidOutcome.Extracted && _session?.PaidCost > 0)
			MoneyLedger.Remove(_player.Inventory.Inventory.StorageGrids.ToList(), _session.PaidCost);
		LostSlots = _player.Inventory.Inventory.EquippedItems.Where(kv => kv.Key != EquipSlot.SecureContainer)
			.ToDictionary(kv => kv.Value, kv => kv.Key);
		var lost = RaidOutcomeRules.Apply(outcome, _player.Inventory.Inventory);
		_player.Inventory.NotifyChanged();
		Ended?.Invoke(outcome, lost);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_session is not { IsActive: true } session || _player is null)
			return;
		string? zoneId = null;
		var pos = _player.GlobalPosition;
		foreach (var zone in GetTree().GetNodesInGroup("extraction_zones").OfType<ExtractionZone>())
		{
			if (zone.Contains(pos))
			{
				zoneId = zone.ZoneId;
				break;
			}
		}
		var inv = _player.Inventory.Inventory;
		session.Update((float)delta, zoneId, id => inv.AllItems().Any(i => i.Spec.Id == id),
			() => MoneyLedger.Count(inv.StorageGrids));
	}
}
