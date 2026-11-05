namespace ProjectD;

/// <summary>
/// The "let them pass" rule of an ambusher (Border Strip spec s5, factions spec): the AI ignores what it sees and stays
/// quiet until the trap springs, then fights normally. It springs when the player gets within the trigger range in sight,
/// when the player stumbles onto it, or as soon as the AI is hit or a squadmate is fighting. Once sprung it stays sprung.
/// Pure logic.
/// </summary>
public sealed class AmbushGate
{
	public AmbushGate(float triggerM = 16f, float stumbleM = 6f)
	{
		TriggerM = triggerM;
		StumbleM = stumbleM;
	}

	public float TriggerM { get; }
	public float StumbleM { get; }
	public bool Sprung { get; private set; }

	/// <summary>Feeds one tick; returns whether the trap is sprung (now or earlier).</summary>
	public bool Update(float distanceToPlayerM, bool seesPlayer, bool squadEngaged, bool wasHit)
	{
		if (!Sprung && (wasHit || squadEngaged || distanceToPlayerM <= StumbleM || (seesPlayer && distanceToPlayerM <= TriggerM)))
			Sprung = true;
		return Sprung;
	}
}
