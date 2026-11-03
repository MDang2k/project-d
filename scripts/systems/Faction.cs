namespace ProjectD;

/// <summary>Human factions of the slice (GDD s3).</summary>
public enum Faction { Looters, Bandits, AlliedPmc, SovietPmc, Deserters }

/// <summary>Hostility matrix (GDD s16): everyone is hostile to everyone else; PMCs are friendly to their own.</summary>
public static class FactionRelations
{
	public static bool IsHostile(Faction a, Faction b) => a switch
	{
		Faction.Looters => b != Faction.Looters,
		Faction.Bandits => b != Faction.Bandits,
		Faction.Deserters => b != Faction.Deserters,
		_ => a != b,
	};
}
