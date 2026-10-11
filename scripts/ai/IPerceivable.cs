using Godot;

namespace ProjectD;

/// <summary>Anything an AI can see: the player and other AI. Lives in group "perceivables".</summary>
public interface IPerceivable
{
	Faction Faction { get; }
	bool IsAlive { get; }
	Stance PerceivedStance { get; }
	/// <summary>Horizontal speed as a fraction of sprint speed, 0..1.</summary>
	float PerceivedSpeedFraction { get; }
	/// <summary>World points sampled for line of sight: head, chest, legs.</summary>
	Vector3[] BodyPoints();
	Rid BodyRid { get; }
}
