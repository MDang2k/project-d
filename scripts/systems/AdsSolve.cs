using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Socket-based aim down sights (animation spec s2.3): the transform that puts a weapon sight socket on the
/// view axis at the eye relief, whatever optic, mount or stock is fitted. Pure logic.
/// </summary>
public static class AdsSolve
{
	/// <summary>
	/// Weapon root transform in camera space with the socket at (0, 0, -eyeRelief), its forward axis along the view.
	/// <paramref name="socket"/> is the sight socket in weapon space (the camera looks down -Z).
	/// </summary>
	public static Transform3D WeaponTransform(Transform3D socket, float eyeRelief)
		=> new Transform3D(Basis.Identity, new Vector3(0f, 0f, -eyeRelief)) * socket.AffineInverse();

	/// <summary>Position-only variant for sockets whose axis already matches the weapon axis.</summary>
	public static Vector3 WeaponOffset(Vector3 socketPosition, float eyeRelief)
		=> new Vector3(0f, 0f, -eyeRelief) - socketPosition;
}

/// <summary>A place the support hand can go (animation spec s2.2).</summary>
public readonly record struct GripCandidate(int Priority, Vector3 Position, string Pose);

/// <summary>Picks the support-hand socket: foregrip over handguard over receiver or magwell over the weapon default.</summary>
public static class SupportGripResolver
{
	public const int Foregrip = 0;
	public const int Handguard = 1;
	public const int Receiver = 2;

	public static int PriorityOf(string slotType) => slotType switch
	{
		"underbarrel" or "foregrip" => Foregrip,
		"handguard" => Handguard,
		_ => Receiver,
	};

	/// <summary>The lowest priority number wins; ties keep the first. Falls back to the weapon default.</summary>
	public static GripCandidate Resolve(IEnumerable<GripCandidate> candidates, GripCandidate weaponDefault)
	{
		var list = candidates.ToList();
		return list.Count == 0 ? weaponDefault : list.OrderBy(c => c.Priority).First();
	}
}
