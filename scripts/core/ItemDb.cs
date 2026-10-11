using System.Linq;
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

	private readonly System.Collections.Generic.Dictionary<string, AmmoType?> _ammoCache = new();

	/// <summary>Runtime ammo type for an AmmoData id (as stored in magazines and chambers).</summary>
	public static AmmoType? AmmoTypeById(string ammoId) => Instance?.LookupAmmo(ammoId);

	private AmmoType? LookupAmmo(string ammoId)
	{
		if (_ammoCache.TryGetValue(ammoId, out var cached))
			return cached;
		AmmoType? found = null;
		foreach (var id in Registry.Ids)
		{
			if (Registry.Data(id)?.Payload is AmmoData a && a.Id == ammoId)
			{
				found = a.ToType();
				break;
			}
		}
		_ammoCache[ammoId] = found;
		return found;
	}

	/// <summary>Item id of the ammo stack that holds rounds with this AmmoData id.</summary>
	public static string? AmmoItemId(string ammoId)
	{
		var r = Instance?.Registry;
		return r?.Ids.FirstOrDefault(id => r.Data(id) is { Category: ItemCategory.Ammo } d && d.AmmoId == ammoId);
	}

	/// <summary>A magazine item definition for a caliber (used to rebuild debug magazines).</summary>
	public static ItemData? MagazineFor(string caliber)
	{
		var r = Instance?.Registry;
		if (r is null)
			return null;
		foreach (var id in r.Ids)
		{
			if (r.Data(id) is { Category: ItemCategory.Magazine } d && d.Caliber == caliber)
				return d;
		}
		return null;
	}
}
