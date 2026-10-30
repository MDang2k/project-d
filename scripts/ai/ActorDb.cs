using Godot;

namespace ProjectD;

/// <summary>
/// Loads the exported actor sheets (data/actors/actors.json) once and finds the item resources they name by id.
/// Not an autoload: a static cache is enough and the file is small.
/// </summary>
public static class ActorDb
{
	private const string Path = "res://data/actors/actors.json";

	private static ActorCatalog? _catalog;

	public static ActorCatalog Catalog => _catalog ??= Load();

	private static ActorCatalog Load()
	{
		using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
		if (file is null)
			throw new System.InvalidOperationException($"{Path} missing; run python tools/export_actors.py");
		return ActorCatalog.Parse(file.GetAsText());
	}

	/// <summary>A live actor for a spawn: tier and act sets added and an archetype rolled from the sheet's weights.</summary>
	public static ActorInstance Create(string id, int? tier = null, int? act = null)
		=> Catalog.Create(id, tier, act, new System.Random());

	public static WeaponData? Weapon(string id) => Find<WeaponData>("weapons", id);
	public static ArmorData? Armor(string id) => Find<ArmorData>("armor", id);
	public static MedicalData? Medical(string id) => Find<MedicalData>("items", id);
	public static LootTable? Pockets(string id) => Find<LootTable>("loot", id);

	// Held for the life of the game: when nothing referenced a loaded resource its C# wrapper could be finalized just
	// as the next spawn loaded the same file again, which aborts in Godot's script bridge.
	private static readonly System.Collections.Generic.Dictionary<string, Resource?> Loaded = new();

	private static T? Find<T>(string folder, string id) where T : Resource
	{
		if (id == "")
			return null;
		var path = $"res://data/{folder}/{id}.tres";
		if (Loaded.TryGetValue(path, out var cached))
			return cached as T;
		Resource? found = null;
		if (ResourceLoader.Exists(path))
			found = GD.Load<T>(path);
		else
			GD.PushWarning($"[ActorDb] {path} not found");
		Loaded[path] = found;
		return found as T;
	}
}
