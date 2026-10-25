using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace ProjectD;

/// <summary>One retargeted leg clip: per bone, keys of (time, rotation relative to the clip's reference pose).</summary>
public sealed record RealClip(float Length, IReadOnlyDictionary<string, (float Time, Quaternion Rot)[]> Bones);

/// <summary>
/// Real locomotion clips (animation spec s7): the Quaternius Universal Animation Library (CC0) legs, retargeted
/// to the game rig by tools/retarget_ual.py into data/animation/locomotion_clips.json. The keys hold only the
/// motion around a reference pose; the stance base (crouch bend) is added when the Animation is built, because the
/// game sets hips height itself and has no foot IK yet. Missing file or clip: the generated gait is used.
/// </summary>
public static class LocomotionClips
{
	private const string Path = "res://data/animation/locomotion_clips.json";
	private static System.Collections.Generic.Dictionary<string, RealClip>? _clips;

	public static IReadOnlyDictionary<string, RealClip> All => _clips ??= Load();

	/// <summary>The pack clip for a stance and speed ring (0 idle, 0.5 walk, 1 sprint); null means use the generated gait.</summary>
	public static string? ClipFor(GaitStance stance, float ring) => stance switch
	{
		GaitStance.Stand => ring <= 0f ? "Idle_Loop" : ring < 1f ? "Walk_Loop" : "Sprint_Loop",
		GaitStance.Crouch => ring <= 0f ? "Crouch_Idle_Loop" : "Crouch_Fwd_Loop",
		_ => null,
	};

	/// <summary>
	/// How a direction (0 forward, then clockwise in 45 degree steps) uses the pack: it ships forward clips only, so
	/// the front three directions play forward, the back three play it reversed, and the sides stay generated.
	/// </summary>
	public static (bool Real, bool Reverse) DirectionUse(int dir) => dir switch
	{
		0 or 1 or 7 => (true, false),
		3 or 4 or 5 => (true, true),
		_ => (false, false),
	};

	private static System.Collections.Generic.Dictionary<string, RealClip> Load()
	{
		var result = new System.Collections.Generic.Dictionary<string, RealClip>();
		if (!ResourceLoader.Exists(Path) || GD.Load<Json>(Path) is not { Data.VariantType: Variant.Type.Dictionary } json)
			return result;
		var root = json.Data.AsGodotDictionary();
		if (!root.ContainsKey("clips"))
			return result;
		foreach (var (name, clipValue) in root["clips"].AsGodotDictionary())
		{
			var clip = clipValue.AsGodotDictionary();
			var bones = new System.Collections.Generic.Dictionary<string, (float, Quaternion)[]>();
			foreach (var (bone, keysValue) in clip["bones"].AsGodotDictionary())
			{
				var keys = keysValue.AsGodotArray();
				var list = new (float, Quaternion)[keys.Count];
				for (var i = 0; i < keys.Count; i++)
				{
					var k = keys[i].AsGodotArray();
					list[i] = ((float)k[0], new Quaternion((float)k[1], (float)k[2], (float)k[3], (float)k[4]));
				}
				bones[bone.AsString()] = list;
			}
			result[name.AsString()] = new RealClip((float)clip["length"], bones);
		}
		return result;
	}
}
