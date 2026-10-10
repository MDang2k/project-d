using System.Collections.Generic;

namespace ProjectD;

/// <summary>
/// Single place for player-facing text (CLAUDE.md localization rule). Keys map to English now;
/// swap the lookup for Godot translations when localization starts.
/// </summary>
public static class GameText
{
	private static readonly Dictionary<string, string> English = new()
	{
		["msg.mag.full"] = "Magazine: full",
		["msg.mag.half"] = "Magazine: about half",
		["msg.mag.almost_empty"] = "Magazine: almost empty",
		["msg.mag.empty"] = "Magazine: empty",
		["msg.mag.none"] = "No magazine inserted",
		["msg.mag.exact"] = "Magazine: {0} rounds",
		["msg.chamber.loaded"] = "Round chambered",
		["msg.chamber.empty"] = "Chamber empty",
		["msg.weapon.click"] = "*click*",
		["msg.reload.no_spare"] = "No usable magazine",
		["msg.weapon.rested"] = "Weapon rested",
		["msg.melee.miss"] = "Swing missed",
		["msg.melee.hit"] = "Melee hit: {0} for {1} damage",
		["msg.melee.hit_stopped"] = "Melee hit: {0} for {1} damage (stopped by armor)",
		["msg.melee.hit_armor"] = "Melee hit: {0} for {1} damage (through armor)",
		["msg.you.died"] = "You died",
		["msg.bipod.deployed"] = "Bipod deployed",
		["msg.bipod.folded"] = "Bipod folded",
		["msg.bipod.no_surface"] = "No surface for the bipod",
		["msg.bipod.none"] = "This weapon has no bipod",
	};

	public static string Get(string key) => English.TryGetValue(key, out var text) ? text : key;

	public static string Get(string key, params object[] args) => string.Format(Get(key), args);

	public static string ForMagCheck(MagCheckResult r)
	{
		if (r.ExactCount is { } n)
			return Get("msg.mag.exact", n);
		return r.Estimate switch
		{
			MagEstimate.Full => Get("msg.mag.full"),
			MagEstimate.AboutHalf => Get("msg.mag.half"),
			MagEstimate.AlmostEmpty => Get("msg.mag.almost_empty"),
			MagEstimate.Empty => Get("msg.mag.empty"),
			_ => Get("msg.mag.none"),
		};
	}
}
