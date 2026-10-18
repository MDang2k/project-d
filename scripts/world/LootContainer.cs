using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Crate, locker or body in the world. Items are rolled from a table and revealed one by one
/// while the player searches; searching makes noise (GDD s10, s16). Thin wrapper around
/// <see cref="LootContainerModel"/>.
/// </summary>
public partial class LootContainer : Node3D
{
	private const float NoiseIntervalS = 1.0f;

	private Random _rng = new();
	private float _noiseTimer;

	[Export] public string TitleKey { get; set; } = "Crate";
	[Export] public LootTable? Table { get; set; }
	[Export] public Vector2I GridSize { get; set; } = new(5, 4);
	[Export] public float SecondsPerItem { get; set; } = 1.2f;
	/// <summary>How far a search can be heard, metres (metal lockers are louder than crates).</summary>
	[Export] public float NoiseRadius { get; set; } = 10f;
	/// <summary>0 uses a random seed; any other value makes the roll repeatable.</summary>
	[Export] public int Seed { get; set; }
	[Export] public Vector3 BoxSize { get; set; } = new(0.9f, 0.6f, 0.6f);
	[Export] public Color BoxColor { get; set; } = new(0.45f, 0.33f, 0.2f);

	public LootContainerModel Model { get; private set; } = null!;

	public bool IsCorpse => TitleKey == "container.body";

	/// <summary>A dead character's gear (equipment slots, pockets, rig, backpack); null for ordinary containers.</summary>
	public CharacterInventory? Body { get; set; }

	/// <summary>An AI already emptied this one.</summary>
	public bool Taken { get; private set; }

	/// <summary>An AI strips everything; returns how many items it took.</summary>
	public int TakeAll()
	{
		Taken = true;
		Model.StopSearch();
		if (Body is { } body)
		{
			var count = System.Linq.Enumerable.Count(body.AllItems());
			Body = new CharacterInventory();
			return count;
		}
		var items = System.Linq.Enumerable.ToList(Model.Grid.Items);
		foreach (var item in items)
			Model.Grid.Remove(item);
		return items.Count;
	}

	public string Title => GameText.Get(TitleKey);

	public override void _Ready()
	{
		_rng = Seed == 0 ? new Random() : new Random(Seed);
		Model = Body is { } dead
			? CorpseLoot.Build(dead, SecondsPerItem, NoiseRadius)
			: new LootContainerModel(GridSize.X, GridSize.Y, SecondsPerItem, NoiseRadius);
		AddToGroup("loot_containers");

		var body = new StaticBody3D { Position = new Vector3(0f, BoxSize.Y * 0.5f, 0f) };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = BoxSize } });
		body.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = BoxSize },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = BoxColor },
		});
		AddChild(body);

		if (Table is { } table)
			Roll(table);
	}

	private void Roll(LootTable table)
	{
		var rolls = _rng.Next(table.MinRolls, Math.Max(table.MinRolls, table.MaxRolls) + 1);
		foreach (var (id, count) in LootRoller.Roll(table.ToEntries(), rolls, _rng.NextDouble))
		{
			var item = ItemDb.Create(id, count);
			if (item is null)
				continue;
			if (item.Spec.MagazineCapacity > 0)
				FillMagazine(item);
			Model.AddHidden(item);
		}
	}

	/// <summary>Loose magazines found in the world hold between 40% and 100% of their capacity.</summary>
	private void FillMagazine(ItemInstance mag)
	{
		var registry = ItemDb.Instance?.Registry;
		var ammoData = registry?.Ids.Select(registry.Data)
			.FirstOrDefault(d => d is { Category: ItemCategory.Ammo } && d.Caliber == mag.Spec.Caliber);
		if (ammoData is null)
			return;
		var cap = mag.Spec.MagazineCapacity;
		var rounds = (int)Math.Round(cap * (0.4 + 0.6 * _rng.NextDouble()));
		for (var i = 0; i < rounds; i++)
			mag.Rounds.Add(ammoData.AmmoId);
	}

	/// <summary>Adds worn gear that is visible immediately (a looted body).</summary>
	public void AddVisible(ItemInstance item) => Model.AddRevealed(item);

	public override void _PhysicsProcess(double delta)
	{
		Model.Update((float)delta);
		if (!Model.IsSearching)
		{
			_noiseTimer = 0f;
			return;
		}
		_noiseTimer -= (float)delta;
		if (_noiseTimer <= 0f)
		{
			_noiseTimer = NoiseIntervalS;
			NoiseBus.Emit(GlobalPosition, Model.NoiseRadius, "search", this);
		}
	}
}
