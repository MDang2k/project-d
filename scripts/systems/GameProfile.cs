using System.Collections.Generic;

namespace ProjectD;

/// <summary>The side the player works for (GDD s5). Chosen once, at the first hub visit.</summary>
public enum PlayerSide { Allied, Soviet }

public static class PlayerSideExtensions
{
	public static Faction ToFaction(this PlayerSide side)
		=> side == PlayerSide.Allied ? Faction.AlliedPmc : Faction.SovietPmc;
}

/// <summary>
/// Long-lived player data that lives in the hub (GDD s10, s15): the chosen side and the stash. The
/// carried character lives in <see cref="RunProgress"/>. Pure logic.
/// </summary>
public sealed class GameProfile
{
	public const int StashWidth = 10;
	public const int StashHeight = 30;

	public PlayerSide? Side { get; set; }

	public InventoryGrid Stash { get; private set; } = new(StashWidth, StashHeight);

	public bool HasChosenSide => Side is not null;

	/// <summary>Insured items waiting to come back after the raid that follows the loss (GDD s15).</summary>
	public InsuranceQueue InsuranceReturns { get; } = new();

	/// <summary>Quest states, progress, tracking and pending rewards (GDD s14).</summary>
	public QuestLog Quests { get; } = new();

	/// <summary>Part item ids the player has owned or seen; the workshop sandbox may use them (weapon modding v2 s6).</summary>
	public HashSet<string> ExaminedParts { get; } = new();

	/// <summary>Skill XP and levels (GDD s12); the numbers are registered at boot by GameState.</summary>
	public SkillBook Skills { get; }

	/// <summary>What the skill levels do (stamina, weight, handling, recoil, medicine, searching, health).</summary>
	public SkillEffects SkillEffects { get; }

	public GameProfile()
	{
		Skills = new SkillBook();
		SkillEffects = new SkillEffects(Skills);
	}

	/// <summary>Saved named builds.</summary>
	public List<PartPreset> Presets { get; } = new();

	private readonly Dictionary<string, TraderState> _traders = new();
	private readonly HashSet<string> _unlockedTraders = new();

	/// <summary>Per-trader level and spent money; created on first use at level 1.</summary>
	public TraderState Trader(string id)
	{
		if (!_traders.TryGetValue(id, out var state))
			_traders[id] = state = new TraderState();
		return state;
	}

	public IReadOnlyDictionary<string, TraderState> Traders => _traders;

	/// <summary>Traders opened by quest rewards (the opposite quartermaster).</summary>
	public IReadOnlySet<string> UnlockedTraders => _unlockedTraders;

	public void UnlockTrader(string id) => _unlockedTraders.Add(id);

	/// <summary>Whether this trader may be used now: always, your side's quartermaster, or unlocked by a quest.</summary>
	public bool IsTraderAvailable(TraderSpec trader) => trader.Unlock switch
	{
		TraderUnlock.Always => true,
		TraderUnlock.ForSide => (Side is not null && trader.UnlockSide == Side) || _unlockedTraders.Contains(trader.Id),
		_ => _unlockedTraders.Contains(trader.Id),
	};

	/// <summary>Why a trader is unavailable, as a text key (shown in the trader screen); null when available.</summary>
	public string? LockReasonKey(TraderSpec trader)
		=> IsTraderAvailable(trader) ? null : trader.Unlock == TraderUnlock.ForSide ? "trader.locked.side" : "trader.locked.quest";

	public void ReplaceTraders(IEnumerable<KeyValuePair<string, TraderState>> traders)
	{
		_traders.Clear();
		foreach (var (id, state) in traders)
			_traders[id] = state;
	}

	/// <summary>Replaces the stash (loading a save).</summary>
	public void ReplaceStash(InventoryGrid stash) => Stash = stash;

	public void Reset()
	{
		Side = null;
		Stash = new InventoryGrid(StashWidth, StashHeight);
		_traders.Clear();
		_unlockedTraders.Clear();
		InsuranceReturns.Clear();
		Quests.Clear();
		ExaminedParts.Clear();
		Presets.Clear();
	}

	/// <summary>Every item in the stash, nested grids included.</summary>
	public IEnumerable<ItemInstance> AllStashItems()
	{
		foreach (var item in Stash.Items)
		{
			yield return item;
			foreach (var nested in StarterKit.Flatten(item))
				yield return nested;
		}
	}
}
