using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Knowledge in a raid (M8d spec s2): plants the documents at their spots (not the ones whose knowledge you already
/// have), notes what the player learns, and at the raid's end unlocks what an extraction carries out and counts a
/// translation down. Thin node over <see cref="KnowledgeBook"/>. Dev: <c>--giveknow=item</c> puts a document in the pockets,
/// <c>--readnow=item</c> starts reading one, (<c>--knowcode=knowledge</c> is handled at boot in GameState).
/// </summary>
public partial class RaidKnowledge : Node
{
	private readonly List<KnowledgeSpec> _learned = new();
	private Player _player = null!;

	private static KnowledgeBook Book => (GameState.Instance?.Profile ?? new GameProfile()).Knowledge;

	public void Start(Player player)
	{
		_player = player;
		Book.Unlocked += OnUnlocked;
		var args = OS.GetCmdlineUserArgs();
		PlantSpots();
		if (args.FirstOrDefault(a => a.StartsWith("--translate="))?["--translate=".Length..] is { } note)
			GD.Print($"KNOWLEDGE dev: start translating {note}: {Book.StartTranslation(note)}");
		if (args.FirstOrDefault(a => a.StartsWith("--giveknow="))?["--giveknow=".Length..] is { } give && ItemDb.Create(give) is { } doc)
		{
			player.Inventory.Inventory.TryAutoStore(doc);
			if (args.FirstOrDefault(a => a.StartsWith("--readnow="))?["--readnow=".Length..] is { } read && read == give)
				player.StartRead(doc);
		}
	}

	public override void _ExitTree() => Book.Unlocked -= OnUnlocked;

	private void OnUnlocked(KnowledgeSpec spec)
	{
		_learned.Add(spec);
		GD.Print($"KNOWLEDGE unlocked: {spec.Id}{(Book.NeedsTranslation(spec) ? " (needs translating)" : "")}");
	}

	private void PlantSpots()
	{
		var planted = 0;
		foreach (var spot in GetTree().GetNodesInGroup("knowledge_spots").OfType<KnowledgeSpot>())
		{
			if (spot.ItemId == "" || !Book.ShouldSpawn(spot.ItemId) || ItemDb.Create(spot.ItemId) is not { } item)
				continue;
			var doc = DroppedItem.Create(item, spot.GlobalPosition + Vector3.Up * 0.3f, Vector3.Zero);
			doc.Persistent = true;
			GetParent().CallDeferred(Node.MethodName.AddChild, doc);
			planted++;
		}
		GD.Print($"KNOWLEDGE planted {planted} documents");
	}

	/// <summary>
	/// The raid is over: an extraction unlocks the knowledge of every document carried out, unread or not, and the running
	/// translation comes one raid closer whatever the outcome. Returns the lines for the result screen.
	/// </summary>
	public IReadOnlyList<string> OnRaidEnded(RaidOutcome outcome, CharacterInventory carried)
	{
		if (outcome == RaidOutcome.Extracted)
			KnowledgeRules.OnExtracted(Book, carried);
		var translated = Book.OnRaidEnded();
		GD.Print($"KNOWLEDGE raid ended {outcome}: translating {Book.ActiveTranslation ?? "-"} ({Book.RaidsLeft(Book.ActiveTranslation ?? "")} left), translated now {translated?.Id ?? "-"}");
		var lines = _learned.Select(k => GameText.Get("knowledge.raid.learned", GameText.Get(k.TitleKey))).ToList();
		if (translated is not null)
			lines.Add(GameText.Get("knowledge.raid.translated", GameText.Get(translated.TitleKey)));
		return lines;
	}
}
