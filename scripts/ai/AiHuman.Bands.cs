using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Work budget half of <see cref="AiHuman"/> (AI combat v2 spec s9): the AI far from the player thinks less often and
/// sees with fewer rays, and every ray is counted for the AI range.
/// </summary>
public partial class AiHuman
{
	private static readonly AiBandTuning BandTuning = new();

	private AiBand _band = AiBand.Combat;
	private float _bandCheckS;
	private float _thinkAccS;
	private float _wokenS;
	private Player? _player;

	/// <summary>Current work band (dev reporting).</summary>
	public AiBand Band => _band;

	private ulong Frame => Engine.GetPhysicsFrames();

	/// <summary>A counted ray; all AI casts go through here so the range can show the cost.</summary>
	private Godot.Collections.Dictionary Cast(PhysicsRayQueryParameters3D query)
	{
		AiPerf.Ray(Frame);
		return GetWorld3D().DirectSpaceState.IntersectRay(query);
	}

	private static readonly System.Collections.Generic.List<Node> PerceivableCache = new();
	private static ulong _cacheFrame = ulong.MaxValue;

	/// <summary>
	/// Everything that can be seen, shared by all AI and refreshed about four times a second instead of every sense
	/// tick of every bot (asking the tree for a group allocates an array each time).
	/// </summary>
	private System.Collections.Generic.IReadOnlyList<Node> Perceivables()
	{
		if (_cacheFrame == ulong.MaxValue || Frame - _cacheFrame > 15 || Frame < _cacheFrame)
		{
			_cacheFrame = Frame;
			PerceivableCache.Clear();
			foreach (var n in GetTree().GetNodesInGroup("perceivables"))
				PerceivableCache.Add(n);
		}
		return PerceivableCache;
	}

	/// <summary>Spread the first ticks of each bot over time so a squad never casts all its rays in one frame.</summary>
	private void StaggerTicks()
	{
		_tickAcc = (float)_rng.NextDouble() * TickS;
		_thinkAccS = (float)_rng.NextDouble() * 0.1f;
	}

	private void UpdateBand(float delta)
	{
		_wokenS = Mathf.Max(0f, _wokenS - delta);
		_bandCheckS -= delta;
		if (_bandCheckS > 0f)
			return;
		_bandCheckS = 0.25f;
		_player ??= GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault();
		var distance = _player is { } p ? GlobalPosition.DistanceTo(p.GlobalPosition) : 0f;
		var fighting = Brain.State == AiState.Combat || Perception.Awareness > 0.5f;
		_band = AiTickBands.Classify(BandTuning, distance, fighting, _wokenS > 0f);
	}

	/// <summary>A sound that reaches a far-off AI, or a hit on it, wakes it for a few seconds.</summary>
	private void Wake() => _wokenS = BandTuning.WakeS;

	/// <summary>Frames between full thinking steps: gravity and the last velocity carry the body on.</summary>
	private void Coast(float delta)
	{
		var vel = Velocity;
		vel.Y = IsOnFloor() ? 0f : vel.Y - _gravity * delta;
		Velocity = vel;
		MoveAndSlide();
	}
}
