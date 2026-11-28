using System;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>The chosen side ("side"): "Allied", "Soviet" or absent until chosen.</summary>
public sealed class SidePart(GameProfile profile) : ISavePart
{
	public void Write(JsonObject root) => SaveJson.Set(root, "side", profile.Side?.ToString());

	public void Read(JsonObject root, LoadContext ctx)
		=> profile.Side = Enum.TryParse<PlayerSide>(SaveJson.Get<string>(root, "side"), out var side) ? side : null;
}
