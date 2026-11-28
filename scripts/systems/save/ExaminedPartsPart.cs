using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>Weapon part ids the player has seen ("examined").</summary>
public sealed class ExaminedPartsPart(GameProfile profile) : ISavePart
{
	public void Write(JsonObject root) => SaveJson.Set(root, "examined", profile.ExaminedParts.ToList());

	public void Read(JsonObject root, LoadContext ctx)
		=> profile.ExaminedParts.UnionWith(SaveJson.Get<List<string>>(root, "examined") ?? new());
}
