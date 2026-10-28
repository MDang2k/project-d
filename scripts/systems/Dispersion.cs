using System;
using Godot;

namespace ProjectD;

/// <summary>
/// Bullet dispersion in MOA (minutes of angle): the accuracy of the gun itself, separate from recoil and from the
/// hip-fire cone. A weapon has a base value, parts change it by a percentage, and the ammo changes it by a
/// percentage. A group of N MOA is N/60 degrees wide. Pure logic.
/// </summary>
public static class Dispersion
{
	public const float DegPerMoa = 1f / 60f;

	public static float MoaToDeg(float moa) => moa * DegPerMoa;

	/// <summary>Final dispersion: base MOA times (1 + parts + ammo), never below a tenth of the base.</summary>
	public static float FinalMoa(float baseMoa, float partsFraction, float ammoFraction)
		=> baseMoa * Math.Max(0.1f, 1f + partsFraction + ammoFraction);

	/// <summary>
	/// The shot direction with the bullet scatter applied: uniform over a disc whose diameter is the group size
	/// (so the edge of the disc is half of it from the aim point).
	/// </summary>
	public static Vector3 Apply(Vector3 direction, float moa, Func<float> random01)
		=> AiAim.Jitter(direction, MoaToDeg(moa) * 0.5f, random01);
}
