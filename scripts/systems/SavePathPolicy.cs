using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// Decides where (or whether) the save lives for this launch, so dev and headless runs never
/// touch the player's real save. Pure logic; the node passes in the command line and engine facts.
/// </summary>
public static class SavePathPolicy
{
	/// <returns>The file to use, or null when saving is disabled for this launch.</returns>
	/// <param name="globalize">Turns a <c>user://</c> or <c>res://</c> path into a filesystem path.</param>
	public static string? Resolve(IReadOnlyCollection<string> args, bool headless, string defaultPath, Func<string, string> globalize)
	{
		if (args.Contains("--nosave"))
			return null;
		var explicitPath = args.FirstOrDefault(a => a.StartsWith("--savepath="))?["--savepath=".Length..];
		if (!string.IsNullOrWhiteSpace(explicitPath))
			return explicitPath.StartsWith("user://") || explicitPath.StartsWith("res://") ? globalize(explicitPath) : explicitPath;
		if (headless || args.Contains("--autonew") || args.Any(a => a.StartsWith("--shot=")))
			return null;
		return defaultPath;
	}
}
