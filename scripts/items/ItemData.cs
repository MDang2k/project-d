using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Inventory definition of an item (GDD s10): grid size, weight, category and a payload that
/// points at the gameplay data (WeaponData, ArmorData, MedicalData...). Authored as .tres in
/// data/items/defs.
/// </summary>
[GlobalClass]
public partial class ItemData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	[Export] public ItemCategory Category { get; set; } = ItemCategory.Misc;
	[Export] public int Width { get; set; } = 1;
	[Export] public int Height { get; set; } = 1;
	[Export] public float WeightKg { get; set; } = 0.1f;
	[Export] public int MaxStack { get; set; } = 1;
	[Export] public WeaponClass WeaponClass { get; set; } = WeaponClass.None;
	/// <summary>Gameplay resource: WeaponData, ArmorData, MedicalData, ConsumableData, MeleeData or AmmoData.</summary>
	[Export] public Resource? Payload { get; set; }
	/// <summary>Placeholder icon colour until real art exists.</summary>
	[Export] public Color IconColor { get; set; } = new(0.45f, 0.5f, 0.4f);
	[Export] public string IconLabel { get; set; } = "";

	[ExportGroup("Provided grids (rigs, backpacks, secure container)")]
	[Export] public Godot.Collections.Array<Vector2I> Grids { get; set; } = new();

	[ExportGroup("Trade")]
	/// <summary>Value in currency units (marks); 0 means traders will not buy it.</summary>
	[Export] public int BaseValue { get; set; }

	/// <summary>False for money, ammo, medical, food, drink and the secure container.</summary>
	[Export] public bool Insurable { get; set; } = true;

	[ExportGroup("Magazines and ammo")]
	[Export] public int MagazineCapacity { get; set; }
	[Export] public float RoundWeightKg { get; set; }
	[Export] public string Caliber { get; set; } = "";
	[Export] public string AmmoId { get; set; } = "";

	public ItemSpec ToSpec() => new(
		Id, NameKey, Category, Width, Height, WeightKg, MaxStack, WeaponClass,
		Grids.Count > 0 ? Grids.Select(g => new GridSize(g.X, g.Y)).ToList() : null,
		MagazineCapacity, RoundWeightKg,
		string.IsNullOrEmpty(Caliber) ? null : Caliber,
		string.IsNullOrEmpty(AmmoId) ? null : AmmoId,
		BaseValue,
		Payload is ArmorData armor ? armor.MaxDurability : 0f,
		Insurable);
}

/// <summary>Registry of every ItemData by id (plain class so it can be unit tested).</summary>
public sealed class ItemRegistry
{
	private readonly Dictionary<string, ItemData> _byId = new();
	private readonly Dictionary<string, ItemSpec> _specs = new();

	public int Count => _byId.Count;

	public void Add(ItemData data)
	{
		_byId[data.Id] = data;
		_specs[data.Id] = data.ToSpec();
	}

	public bool Has(string id) => _byId.ContainsKey(id);
	public ItemData? Data(string id) => _byId.GetValueOrDefault(id);
	public ItemSpec? Spec(string id) => _specs.GetValueOrDefault(id);

	public ItemInstance? Create(string id, int count = 1)
		=> _specs.TryGetValue(id, out var spec) ? ItemInstance.Create(spec, count) : null;

	public IEnumerable<string> Ids => _byId.Keys;
}
