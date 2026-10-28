using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// One moment of an authored weapon action clip: where the two hands and the weapon pivot are, weapon space. A track
/// the clip does not animate is null and the procedural choreography fills it in.
/// </summary>
public readonly record struct ClipSample(
	Vector3? LeftPos, Quaternion? LeftRot,
	Vector3? RightPos, Quaternion? RightRot,
	Vector3? PivotPos, Quaternion? PivotRot);

/// <summary>
/// Reads an authored weapon action clip (animation spec s1.3, s2.1): Blender empties named LeftHandAction,
/// RightHandAction and WeaponPivot, animated relative to the weapon, arrive as position and rotation tracks named
/// after those nodes. The path prefix does not matter, only the node name.
/// </summary>
public sealed class ClipSampler
{
	public const string Left = "LeftHandAction";
	public const string Right = "RightHandAction";
	public const string Pivot = "WeaponPivot";

	private readonly Animation _clip;
	private readonly Dictionary<string, (int Pos, int Rot)> _tracks = new();

	public ClipSampler(Animation clip)
	{
		_clip = clip;
		for (var i = 0; i < clip.GetTrackCount(); i++)
		{
			var type = clip.TrackGetType(i);
			if (type is not (Animation.TrackType.Position3D or Animation.TrackType.Rotation3D))
				continue;
			var path = clip.TrackGetPath(i).ToString();
			var node = path.Split(':')[0];
			node = node[(node.LastIndexOf('/') + 1)..];
			var known = _tracks.GetValueOrDefault(node, (-1, -1));
			_tracks[node] = type == Animation.TrackType.Position3D ? (i, known.Item2) : (known.Item1, i);
		}
	}

	public float Length => (float)_clip.Length;

	/// <summary>True when the clip animates at least one of the three action nodes.</summary>
	public bool HasAnyTrack => _tracks.ContainsKey(Left) || _tracks.ContainsKey(Right) || _tracks.ContainsKey(Pivot);

	private Vector3? Pos(string node, double t)
		=> _tracks.TryGetValue(node, out var tr) && tr.Pos >= 0 ? _clip.PositionTrackInterpolate(tr.Pos, t) : null;

	private Quaternion? Rot(string node, double t)
		=> _tracks.TryGetValue(node, out var tr) && tr.Rot >= 0 ? _clip.RotationTrackInterpolate(tr.Rot, t) : null;

	public ClipSample Sample(float time)
	{
		var t = Mathf.Clamp((double)time, 0.0, _clip.Length);
		return new ClipSample(Pos(Left, t), Rot(Left, t), Pos(Right, t), Rot(Right, t), Pos(Pivot, t), Rot(Pivot, t));
	}
}
