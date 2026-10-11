using Godot;

namespace ProjectD;

/// <summary>
/// Autoload (CLAUDE.md approved list): loads every item definition in data/items/defs once at
/// startup and creates item instances by id.
/// </summary>
public partial class ItemDb : Node
{
	private const string DefsDir = "res://data/items/defs";

	public static ItemDb? Instance { get; private set; }

	public ItemRegistry Registry { get; } = new();

	public override void _Ready()
	{
		Instance = this;
		using var dir = DirAccess.Open(DefsDir);
		if (dir is null)
		{
			GD.PushWarning($"[ItemDb] {DefsDir} not found");
			return;
		}
		dir.ListDirBegin();
		for (var name = dir.GetNext(); name != ""; name = dir.GetNext())
		{
			if (dir.CurrentIsDir())
				continue;
			// Exported builds list resources with a .remap suffix.
			var file = name.EndsWith(".remap") ? name[..^".remap".Length] : name;
			if (!file.EndsWith(".tres"))
				continue;
			if (GD.Load<ItemData>($"{DefsDir}/{file}") is { } data && data.Id != "")
				Registry.Add(data);
		}
		dir.ListDirEnd();
	}

	public static ItemInstance? Create(string id, int count = 1) => Instance?.Registry.Create(id, count);

	public static ItemData? Data(string id) => Instance?.Registry.Data(id);
}
