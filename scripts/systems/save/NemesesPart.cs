using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>One nemesis kill in a nemesis's record (v9).</summary>
public sealed record NemesisKillSave(int Raid, string MapId, float X, float Z, string? Place = null);

/// <summary>A nemesis (v9): who it is, where it waits and the gear it carries, stored like a character.</summary>
public sealed record NemesisSave(
	string Id, string Name, string Nickname, string Faction, string MapId, float X, float Z, int Rank, int RaidCreated,
	bool FriendlyOrigin, List<NemesisKillSave> Kills, CharacterSave Gear);

/// <summary>Nemeses that hold gear they took from the player ("nemeses").</summary>
public sealed class NemesesPart(NemesisRoster roster) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "nemeses", roster.All.Select(n => new NemesisSave(n.Id, n.Name, n.Nickname, n.Faction.ToString(),
			n.MapId, n.X, n.Z, n.Rank, n.RaidCreated, n.FriendlyOrigin,
			n.Kills.Select(k => new NemesisKillSave(k.Raid, k.MapId, k.X, k.Z, k.Place)).ToList(), ItemSaves.ToSave(n.Gear))).ToList());

	public void Read(JsonObject root, LoadContext ctx)
	{
		foreach (var n in SaveJson.Get<List<NemesisSave>>(root, "nemeses") ?? new())
		{
			if (!Enum.TryParse<Faction>(n.Faction, out var faction))
			{
				ctx.Warnings.Add($"nemesis '{n.Name}' has an unknown faction '{n.Faction}' and was dropped");
				continue;
			}
			var nemesis = new Nemesis(n.Id, n.Name, n.Nickname, faction, n.MapId, n.X, n.Z, n.RaidCreated, n.FriendlyOrigin,
				ItemSaves.LoadCharacter(n.Gear, ctx)) { Rank = Math.Clamp(n.Rank, 1, roster.Tuning.MaxRank) };
			nemesis.Kills.AddRange((n.Kills ?? new()).Select(k => new NemesisKill(k.Raid, k.MapId, k.X, k.Z, k.Place ?? "")));
			roster.Add(nemesis);
		}
	}
}
