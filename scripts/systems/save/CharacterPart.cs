using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>The carried character ("character"; absent for a fresh one).</summary>
public sealed class CharacterPart(RunProgress run, CharacterInventory? carried) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "character", carried is null ? null : ItemSaves.ToSave(carried));

	public void Read(JsonObject root, LoadContext ctx)
	{
		var inventory = SaveJson.Get<CharacterSave>(root, "character") is { } save ? ItemSaves.LoadCharacter(save, ctx) : null;
		run.Restore(inventory, run.Raids, run.Extractions, run.Deaths, run.Mia);
	}
}
