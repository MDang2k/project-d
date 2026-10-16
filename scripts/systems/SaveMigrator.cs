using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectD;

/// <summary>The file was written by a newer build than this one understands.</summary>
public sealed class SaveTooNewException(int found, int supported)
	: Exception($"save version {found} is newer than supported {supported}");

/// <summary>
/// Brings an older save up to <see cref="SaveFile.CurrentVersion"/> by running one step per
/// version over the raw JSON. Add a step to <see cref="Default"/> whenever the format changes.
/// </summary>
public sealed class SaveMigrator
{
	private readonly int _current;
	private readonly IDictionary<int, Action<JsonObject>> _steps;

	public SaveMigrator(int currentVersion, IDictionary<int, Action<JsonObject>>? steps = null)
	{
		_current = currentVersion;
		_steps = steps ?? new Dictionary<int, Action<JsonObject>>();
	}

	// Each entry N upgrades a version-N object in place to the shape of N+1; the migrator stamps the version.
	public static SaveMigrator Default { get; } = new(SaveFile.CurrentVersion, new Dictionary<int, Action<JsonObject>>
	{
		// 1 -> 2: side and stash arrive with the hub; needsKit is retired (extra properties are ignored on read).
		[1] = root =>
		{
			root["side"] = null;
			root["stash"] = new JsonObject
			{
				["width"] = GameProfile.StashWidth,
				["height"] = GameProfile.StashHeight,
				["items"] = new JsonArray(),
			};
			root.Remove("needsKit");
		},
	});

	public JsonObject Migrate(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			throw new JsonException("save file is empty");
		if (JsonNode.Parse(json) is not JsonObject root)
			throw new JsonException("save is not a JSON object");
		var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var n)
			? n
			: throw new JsonException("save has no version");
		if (version > _current)
			throw new SaveTooNewException(version, _current);
		while (version < _current)
		{
			if (!_steps.TryGetValue(version, out var step))
				throw new JsonException($"no migration from save version {version}");
			step(root);
			version++;
			root["version"] = version;
		}
		return root;
	}
}
