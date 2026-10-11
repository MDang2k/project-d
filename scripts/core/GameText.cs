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
		["item.ak_type"] = "AK-type rifle",
		["item.fal_type"] = "FAL-type rifle",
		["item.em2"] = "EM-2 bullpup",
		["item.mg_type"] = "MG42-type machine gun",
		["item.pistol"] = "Pistol",
		["item.ak_mag"] = "AK magazine",
		["item.fal_mag"] = "FAL magazine",
		["item.em2_mag"] = "EM-2 magazine",
		["item.mg_belt"] = "MG ammo box",
		["item.pistol_mag"] = "Pistol magazine",
		["item.ammo_9x19"] = "9x19 rounds",
		["item.ammo_7_62x39"] = "7.62x39 rounds",
		["item.ammo_7_62x51"] = "7.62x51 rounds",
		["item.ammo_280"] = ".280 British rounds",
		["item.ammo_7_92x57"] = "7.92x57 rounds",
		["item.flak_vest_t1"] = "Flak vest (T1)",
		["item.flak_vest_t2"] = "Flak vest (T2)",
		["item.prototype_vest_t4"] = "Prototype vest (T4)",
		["item.steel_helmet_t1"] = "Steel helmet (T1)",
		["item.steel_helmet_t2"] = "Steel helmet (T2)",
		["item.light_rig"] = "Light chest rig",
		["item.field_rig"] = "Field chest rig",
		["item.satchel"] = "Satchel",
		["item.field_pack"] = "Field pack",
		["item.secure_pouch"] = "Secure pouch",
		["item.bandage"] = "Bandage",
		["item.tourniquet"] = "Tourniquet",
		["item.splint"] = "Splint",
		["item.morphine"] = "Morphine",
		["item.field_medkit"] = "Field medkit",
		["item.surgery_kit"] = "Surgery kit",
		["item.canned_food"] = "Canned food",
		["item.ration_pack"] = "Ration pack",
		["item.water_canteen"] = "Water canteen",
		["item.combat_knife"] = "Combat knife",
		["item.entrenching_tool"] = "Entrenching tool",
		["msg.use.cannot"] = "Cannot use that now",
		["container.crate"] = "Wooden crate",
		["container.locker"] = "Metal locker",
		["container.weapons_crate"] = "Weapons crate",
		["container.body"] = "Body",
		["msg.search.prompt"] = "F: search {0}",
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

	/// <summary>Display name of an item id; falls back to a tidied id.</summary>
	public static string ItemName(string id)
	{
		var key = "item." + id;
		if (English.TryGetValue(key, out var name))
			return name;
		return id.Replace('_', ' ');
	}

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
