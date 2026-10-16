using System.Linq;

namespace ProjectD;

/// <summary>
/// Hub-side upkeep for quests: unlock by prerequisites, recount hand-in items, deliver pending rewards.
/// Called on hub entry and after every stash change or trade.
/// </summary>
public static class QuestSync
{
	/// <returns>A note for the player when rewards were delivered, else null.</returns>
	public static string? Run(GameProfile profile, CharacterInventory? character)
	{
		var log = profile.Quests;
		var catalog = QuestCatalog.All;
		var delivered = QuestEngine.DeliverPending(log, profile, (id, count) => ItemDb.Create(id, count));
		QuestEngine.Refresh(catalog, log, profile);
		QuestEngine.Recount(catalog, log, profile, character ?? new CharacterInventory());
		if (delivered.Count == 0)
			return null;
		return "Quest reward delivered to your stash: " + string.Join(", ", delivered.Select(d => d.ItemId == MoneyLedger.MoneyId
			? $"{d.Count:N0} {GameText.Get("currency.unit")}"
			: $"{GameText.ItemName(d.ItemId)}{(d.Count > 1 ? " x" + d.Count : "")}"));
	}
}
