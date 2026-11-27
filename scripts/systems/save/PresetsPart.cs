using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace ProjectD;

public sealed record PresetSave(string Name, string WeaponId, Dictionary<string, string> Parts);

/// <summary>Saved weapon builds ("presets").</summary>
public sealed class PresetsPart(GameProfile profile) : ISavePart
{
	public void Write(JsonObject root)
		=> SaveJson.Set(root, "presets", profile.Presets.Select(p => new PresetSave(p.Name, p.WeaponId, p.Parts.ToDictionary(kv => kv.Key, kv => kv.Value))).ToList());

	public void Read(JsonObject root, LoadContext ctx)
	{
		foreach (var preset in SaveJson.Get<List<PresetSave>>(root, "presets") ?? new())
			profile.Presets.Add(new PartPreset(preset.Name, preset.WeaponId, preset.Parts));
	}
}
