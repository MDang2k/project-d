using System;

namespace ProjectD;

/// <summary>Whether a trader shows its revealed identity (character visuals spec s2.7: Weiss after mission 4).</summary>
public static class TraderReveal
{
	public static bool IsRevealed(string revealQuestId, Func<string, bool> questCompleted)
		=> revealQuestId != "" && questCompleted(revealQuestId);
}
