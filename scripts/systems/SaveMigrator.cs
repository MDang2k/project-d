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
		// 2 -> 3: per-trader level and spent money.
		[2] = root => root["traders"] = new JsonObject(),
		// 3 -> 4: insurance returns queue (item flags default to not insured).
		[3] = root => root["insuranceReturns"] = new JsonArray(),
		// 9 -> 10: knowledge (a missing map means nothing known yet).
		[9] = root => root["knowledge"] = new JsonObject(),
		// 8 -> 9: nemeses (a missing list means none).
		[8] = root => root["nemeses"] = new JsonArray(),
		// 7 -> 8: skill XP (a missing map means no XP yet).
		[7] = root => root["skills"] = new JsonObject(),
		// 6 -> 7: examined parts and saved presets (missing lists mean none).
		[6] = _ => { },
		// 5 -> 6: attached weapon parts (a missing "parts" map means no parts; defaults fill required slots on load).
		[5] = _ => { },
		// 4 -> 5: quests, tracking, unlocked traders, pending rewards (item flag foundInRaid defaults to false).
		[4] = root =>
		{
			root["quests"] = new JsonObject();
			root["tracked"] = null;
			root["unlockedTraders"] = new JsonArray();
			root["pendingRewards"] = new JsonArray();
			root["reputation"] = new JsonObject();
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
