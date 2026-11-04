using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>One journal entry (M8d spec s5). <c>Body</c> is empty for an untranslated note; <c>Status</c> may be empty.</summary>
public sealed record JournalEntry(string Id, KnowledgeKind Kind, string Title, string Body, string Status);

/// <summary>Journal contents as plain data: entries grouped by kind, in catalog order. Pure logic.</summary>
public static class KnowledgeSummary
{
	public static IReadOnlyList<JournalEntry> Entries(KnowledgeBook book, Func<string, string> text, Func<string, object[], string> format)
	{
		var list = new List<JournalEntry>();
		foreach (var spec in book.Specs.Where(s => book.IsUnlocked(s.Id)).OrderBy(s => s.Kind))
		{
			var state = book.Entry(spec.Id)!.Value.State;
			var body = text(spec.TextKey);
			var status = "";
			if (book.NeedsTranslation(spec) && state != KnowledgeState.Translated)
			{
				body = "";
				status = state == KnowledgeState.Translating
					? format("journal.translating", new object[] { book.RaidsLeft(spec.Id) })
					: text("journal.untranslated");
			}
			else if (book.NeedsTranslation(spec))
				status = text("journal.translated");
			if (spec.Kind == KnowledgeKind.StashLocation && spec.LandmarkKey != "")
				status = format("journal.near", new object[] { text(spec.LandmarkKey) });
			list.Add(new JournalEntry(spec.Id, spec.Kind, text(spec.TitleKey), body, status));
		}
		return list;
	}

	/// <summary>Lab notes read and waiting for the Scientist (unlocked, translation not started).</summary>
	public static IReadOnlyList<KnowledgeSpec> Untranslated(KnowledgeBook book)
		=> book.Specs.Where(s => book.NeedsTranslation(s) && book.Entry(s.Id)?.State == KnowledgeState.Unlocked).ToList();
}
