using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>The hub stash ("stash"). Its size is a design constant, not save data.</summary>
public sealed class StashPart(GameProfile profile) : ISavePart
{
	public void Write(JsonObject root) => SaveJson.Set(root, "stash", ItemSaves.ToSave(profile.Stash));

	public void Read(JsonObject root, LoadContext ctx)
	{
		if (SaveJson.Get<GridSave>(root, "stash") is { } save)
			ItemSaves.PlaceAll(profile.Stash, save, ctx);
	}
}
