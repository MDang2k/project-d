using System;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class KnowledgeSummaryTests
{
	private static KnowledgeBook Book()
	{
		var b = new KnowledgeBook();
		b.Register(new("lore", KnowledgeKind.Lore, "lore.t", "lore.x", "lore_i", 0, Array.Empty<string>()));
		b.Register(new("stash", KnowledgeKind.StashLocation, "stash.t", "stash.x", "stash_i", 0, new[] { "stash.a" }, "landmark.tower"));
		b.Register(new("note", KnowledgeKind.LabNote, "note.t", "note.x", "note_i", 2, Array.Empty<string>()));
		return b;
	}

	private static System.Collections.Generic.IReadOnlyList<JournalEntry> Rows(KnowledgeBook b)
		=> KnowledgeSummary.Entries(b, k => k, (k, a) => k + ":" + string.Join(",", a));

	[Fact]
	public void OnlyUnlockedEntriesShowGroupedByKind()
	{
		var b = Book();
		Assert.Empty(Rows(b));
		b.Unlock("note"); b.Unlock("lore"); b.Unlock("stash");
		Assert.Equal(new[] { KnowledgeKind.Lore, KnowledgeKind.StashLocation, KnowledgeKind.LabNote }, Rows(b).Select(r => r.Kind));
	}

	[Fact]
	public void UntranslatedNoteHidesItsTextUntilTranslated()
	{
		var b = Book();
		b.Unlock("note");
		var row = Rows(b).Single();
		Assert.Equal("", row.Body);
		Assert.Equal("journal.untranslated", row.Status);
		Assert.Single(KnowledgeSummary.Untranslated(b));

		b.StartTranslation("note");
		row = Rows(b).Single();
		Assert.Equal("journal.translating:2", row.Status);
		Assert.Empty(KnowledgeSummary.Untranslated(b));

		b.OnRaidEnded(); b.OnRaidEnded();
		row = Rows(b).Single();
		Assert.Equal("note.x", row.Body);
		Assert.Equal("journal.translated", row.Status);
	}

	[Fact]
	public void StashEntryNamesItsLandmark()
	{
		var b = Book();
		b.Unlock("stash");
		var row = Rows(b).Single();
		Assert.Equal("stash.x", row.Body);
		Assert.Equal("journal.near:landmark.tower", row.Status);
	}
}
