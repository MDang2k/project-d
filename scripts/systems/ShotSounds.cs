namespace ProjectD;

/// <summary>Which gunshot sound a weapon makes (audio pass): by its caliber, with the generic rifle and pistol shots as fallbacks. Pure logic.</summary>
public static class ShotSounds
{
	/// <summary>
	/// The sound event for a shot: 7.62x39 has its own, 5.56 and .280 British use the 5.56 shot, 7.62x51 its own .308 shot,
	/// 7.92x57 the 7.62x54R, .22 LR its own quiet one; pistols and anything unknown use the generated shots.
	/// </summary>
	public static string EventFor(string? caliber, bool pistolClass)
	{
		if (pistolClass)
			return "shot_pistol";
		return (caliber ?? "").Trim().ToLowerInvariant() switch
		{
			"7.62x39" => "shot_762x39",
			"5.56x45" or "5.56" or "280 british" => "shot_556",
			"7.62x51" => "shot_762x51",
			"7.92x57" or "7.62x54r" => "shot_762x54r",
			".22lr" or "22lr" => "shot_22lr",
			_ => "shot_rifle",
		};
	}
}
