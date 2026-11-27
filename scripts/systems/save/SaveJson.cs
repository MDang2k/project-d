using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ProjectD;

/// <summary>The save's JSON conventions: camelCase properties, indented, nulls left out.</summary>
public static class SaveJson
{
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	/// <summary>Writes a top-level key; a null value leaves the key out.</summary>
	public static void Set<T>(JsonObject root, string key, T value)
	{
		if (value is not null)
			root[key] = JsonSerializer.SerializeToNode(value, Options);
	}

	/// <summary>Reads a top-level key; default when it is absent or null. Throws on the wrong shape.</summary>
	public static T? Get<T>(JsonObject root, string key)
		=> root[key] is { } node ? node.Deserialize<T>(Options) : default;
}
