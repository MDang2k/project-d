using System.Linq;

namespace ProjectD;

/// <summary>Player-facing quest strings built from <see cref="GameText"/> keys (CLAUDE.md: all text in one place).</summary>
public static class QuestText
{
	public static string Title(QuestSpec q) => GameText.Get(q.TitleKey);

	public static string Description(QuestSpec q) => GameText.Get(q.DescriptionKey);

	private static string FactionName(Faction f) => GameText.Get("faction." + f.ToString().ToLowerInvariant());

	private static string ZoneName(string? id) => id is null ? "?" : GameText.Get("zone." + id);

	/// <summary>One objective line with progress, for the Tasks tab and the HUD.</summary>
	public static string Objective(ObjectiveSpec o, int progress)
	{
		switch (o.Kind)
		{
			case ObjectiveKind.Eliminate:
				var target = o.Faction is { } f ? FactionName(f) : GameText.Get("quest.anyone");
				var how = o.WeaponClass is { } w ? " " + GameText.Get("quest.with." + w.ToString().ToLowerInvariant()) : "";
				return GameText.Get("quest.obj.eliminate", target, progress, o.Count) + how;
			case ObjectiveKind.Locate:
				return GameText.Get(progress >= 1 ? "quest.obj.locate.done" : "quest.obj.locate", ZoneName(o.ZoneId));
			case ObjectiveKind.Extract:
				var carry = o.ItemId is null ? "" : " " + GameText.Get("quest.carrying", GameText.ItemName(o.ItemId));
				return GameText.Get(progress >= 1 ? "quest.obj.extract.done" : "quest.obj.extract", ZoneName(o.ZoneId)) + carry;
			case ObjectiveKind.FindInRaid:
				return GameText.Get("quest.obj.find", GameText.ItemName(o.ItemId ?? "?"), progress, o.Count);
			default:
				return GameText.Get("quest.obj.fetch", GameText.ItemName(o.ItemId ?? "?"), progress, o.Count);
		}
	}

	public static string Reward(RewardSpec r) => r.Kind switch
	{
		RewardKind.Money => $"{r.Amount:N0} {GameText.Get("currency.unit")}",
		RewardKind.Item => r.Amount > 1 ? $"{GameText.ItemName(r.ItemId ?? "?")} x{r.Amount}" : GameText.ItemName(r.ItemId ?? "?"),
		RewardKind.TraderLevel => GameText.Get("quest.reward.level", TraderName(r.TraderId), r.Level),
		RewardKind.UnlockTrader => GameText.Get("quest.reward.unlock", TraderName(r.TraderId)),
		_ => GameText.Get("quest.reward.reputation", r.Target ?? "?", r.Amount),
	};

	private static string TraderName(string? id) => id switch
	{
		QuestEngine.SideQuartermaster => GameText.Get("quest.your_qm"),
		QuestEngine.OppositeQuartermaster => GameText.Get("quest.other_qm"),
		null => "?",
		_ => GameText.Get("trader." + id),
	};

	public static string Rewards(QuestSpec q) => string.Join(", ", q.Rewards.Select(Reward));
}
