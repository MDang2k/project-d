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

	public static ActorInstance Create(string id) => Catalog.Create(id);

	public static WeaponData? Weapon(string id) => Find<WeaponData>("weapons", id);
	public static ArmorData? Armor(string id) => Find<ArmorData>("armor", id);
	public static MedicalData? Medical(string id) => Find<MedicalData>("items", id);
	public static LootTable? Pockets(string id) => Find<LootTable>("loot", id);

	private static T? Find<T>(string folder, string id) where T : Resource
	{
		if (id == "")
			return null;
		var path = $"res://data/{folder}/{id}.tres";
		if (!ResourceLoader.Exists(path))
		{
			GD.PushWarning($"[ActorDb] {path} not found");
			return null;
		}
		return GD.Load<T>(path);
	}
}
