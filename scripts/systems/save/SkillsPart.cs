using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>Total XP per skill ("skills", v8); levels come from the skill data.</summary>
public sealed class SkillsPart(SkillBook book) : ISavePart
{
	public void Write(JsonObject root) => SaveJson.Set(root, "skills", book.Snapshot());

	public void Read(JsonObject root, LoadContext ctx)
		=> book.Load(SaveJson.Get<Dictionary<string, float>>(root, "skills") ?? new());
}
