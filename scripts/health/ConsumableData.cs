using Godot;

namespace ProjectD;

/// <summary>Food or drink item (GDD s9). Placeholder numbers, tune in data/items/*.tres.</summary>
[GlobalClass]
public partial class ConsumableData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public string NameKey { get; set; } = "";
	[Export] public float UseTimeS { get; set; } = 5f;
	[Export] public float WeightKg { get; set; } = 0.4f;
	/// <summary>Energy restored (0-100 meter).</summary>
	[Export] public float Energy { get; set; }
	/// <summary>Hydration restored (0-100 meter).</summary>
	[Export] public float Hydration { get; set; }

	public ConsumableSpec ToSpec() => new(Id, UseTimeS, Energy, Hydration);
}
