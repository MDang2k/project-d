using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ProjectD;

/// <summary>One complete look: the outfit pieces (file names after "outfit_") a bot wears over the box body.</summary>
public sealed record Outfit(string Name, float Weight, IReadOnlyList<string> Pieces);

/// <summary>
/// What each kind of AI body can wear (data/actors/wardrobes.json, faction looks spec section 2). A PMC's outfit follows
/// its behaviour type, so a player can read who will flank and who will rush. Pure logic.
/// </summary>
public sealed class OutfitWardrobes
{
	private const string DefaultArchetype = "operator";

	private sealed record Data(Dictionary<string, string> Textures, List<string> HandCovers,
		Dictionary<string, Dictionary<string, List<Outfit>>> Wardrobes);

	private readonly Data _data;

	private OutfitWardrobes(Data data) => _data = data;

	public static OutfitWardrobes Parse(string json)
		=> new(JsonSerializer.Deserialize<Data>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new FormatException("wardrobes: empty file"));

	/// <summary>Material name on a piece to the texture file it shares (the .glb files carry no image).</summary>
	public IReadOnlyDictionary<string, string> Textures => _data.Textures;

	public IEnumerable<string> AllPieces
		=> _data.Wardrobes.Values.SelectMany(w => w.Values).SelectMany(o => o).SelectMany(o => o.Pieces).Distinct();

	/// <summary>The looks an actor of this archetype can have; empty when the actor has no wardrobe.</summary>
	public IReadOnlyList<Outfit> Options(string actorId, string archetype)
	{
		if (!_data.Wardrobes.TryGetValue(actorId, out var byArchetype))
			return Array.Empty<Outfit>();
		return byArchetype.TryGetValue(archetype, out var options) || byArchetype.TryGetValue(DefaultArchetype, out options)
			? options
			: Array.Empty<Outfit>();
	}

	/// <summary>A weighted pick among the options; null when the actor has no wardrobe.</summary>
	public Outfit? Roll(string actorId, string archetype, Random rng)
	{
		var options = Options(actorId, archetype);
		if (options.Count == 0)
			return null;
		var at = rng.NextDouble() * options.Sum(o => o.Weight);
		foreach (var option in options)
		{
			at -= option.Weight;
			if (at < 0)
				return option;
		}
		return options[^1];
	}

	/// <summary>Whether a piece of this outfit replaces the body's own hands (gloves shorter than the hand).</summary>
	public bool HidesHands(Outfit outfit) => outfit.Pieces.Any(_data.HandCovers.Contains);
}
