using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>A piece of knowledge as authored in data/knowledge/*.tres (M8d spec s1). Text goes through GameText keys.</summary>
[GlobalClass]
public partial class KnowledgeData : Resource
{
	[Export] public string Id { get; set; } = "";
	[Export] public KnowledgeKind Kind { get; set; } = KnowledgeKind.Lore;
	[Export] public string TitleKey { get; set; } = "";
	[Export] public string TextKey { get; set; } = "";
	/// <summary>The item that carries it (read it in a raid, or extract with it).</summary>
	[Export] public string ItemId { get; set; } = "";
	/// <summary>Lab notes: completed raids the Scientist needs to translate it; 0 for none.</summary>
	[Export] public int TranslationRaids { get; set; }
	/// <summary>Effect ids it opens: lock.&lt;id&gt;, stash.&lt;id&gt;, quest.&lt;id&gt;.</summary>
	[Export] public string[] Unlocks { get; set; } = Array.Empty<string>();

	/// <summary>Stash locations: GameText key of the nearest map landmark, named in the journal ("landmark.east_tower").</summary>
	[Export] public string LandmarkKey { get; set; } = "";

	public KnowledgeSpec ToSpec() => new(Id, Kind, TitleKey, TextKey, ItemId, TranslationRaids, Unlocks.ToList(), LandmarkKey);
}
