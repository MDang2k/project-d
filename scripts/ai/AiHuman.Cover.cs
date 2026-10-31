using Godot;

namespace ProjectD;

/// <summary>
/// Cover half of <see cref="AiHuman"/> (AI combat v2 spec s5): a radial sweep finds obstacles, measures their height,
/// puts the AI behind them away from the threat and scores the spots; peeking leans around tall cover or rises over
/// low cover.
/// </summary>
public partial class AiHuman
{

	private CoverClass _coverClass = CoverClass.Standing;
	private float _lean;
	private float _leanSide;

	/// <summary>Class of the cover the AI last chose (dev reporting).</summary>
	public CoverClass CoverKind => _coverClass;

	private bool Hidden(Vector3 threatEye, Vector3 spot, CoverClass c)
		=> Blocked(threatEye, spot + Vector3.Up * CoverRules.HiddenEyeHeight(c));

	/// <summary>Cover class a spot gives against the threat, from how low a body can hide there.</summary>
	private CoverClass HiddenClass(Vector3 threatEye, Vector3 spot)
	{
		if (Blocked(threatEye, spot + Vector3.Up * 1.6f))
			return CoverClass.Standing;
		if (Blocked(threatEye, spot + Vector3.Up * 1.0f))
			return CoverClass.Crouch;
		return Blocked(threatEye, spot + Vector3.Up * 0.3f) ? CoverClass.Prone : CoverClass.None;
	}

	/// <summary>Height of the obstacle at a wall hit, measured by a ray dropped onto its top.</summary>
	private float ObstacleHeight(Vector3 hit, Vector3 dir, float floorY)
	{
		var above = hit + dir * 0.15f + Vector3.Up * 2.5f;
		var q = PhysicsRayQueryParameters3D.Create(above, above + Vector3.Down * 3f, 1u);
		var r = Cast(q);
		return r.Count > 0 ? ((Vector3)r["position"]).Y - floorY : 2.5f;
	}

	/// <summary>True when a spot gives a shot at the threat by leaning around it or rising over it.</summary>
	private bool HasFiringAngle(Vector3 spot, Vector3 threatTarget, CoverClass c)
	{
		if (c != CoverClass.Standing)
			return !Blocked(spot + Vector3.Up * CoverRules.PeekEyeHeight(c), threatTarget);
		var toward = new Vector3(threatTarget.X - spot.X, 0f, threatTarget.Z - spot.Z).Normalized();
		var side = toward.Cross(Vector3.Up);
		var lean = S(Stats.Cover.LeanM);
		return !Blocked(spot + side * lean + Vector3.Up * 1.6f, threatTarget)
			|| !Blocked(spot - side * lean + Vector3.Up * 1.6f, threatTarget);
	}

	/// <summary>
	/// Best cover spot against a threat, or null. Sets <see cref="_coverClass"/>. Obstacles come from rays all around
	/// the AI at ankle height; each is measured and the AI is put on its far side from the threat.
	/// </summary>
	private Vector3? FindCover(Vector3 threat, bool retreat)
	{
		var me = GlobalPosition;
		var threatEye = new Vector3(threat.X, Mathf.Max(threat.Y, me.Y + 1.4f), threat.Z);
		var myThreatDist = me.DistanceTo(threat);
		var radius = S(Stats.Cover.SearchM);
		var rays = System.Math.Max(8, AiTickBands.CoverRays(BandTuning, _band));
		Vector3? best = null;
		var bestClass = CoverClass.Standing;
		var bestScore = float.MaxValue;

		void Consider(Vector3 spot, CoverClass known)
		{
			var snapped = SnapToNav(spot);
			if (new Vector2(snapped.X - spot.X, snapped.Z - spot.Z).Length() > 1.5f)
				return;
			var c = known == CoverClass.None ? HiddenClass(threatEye, snapped) : known;
			if (c == CoverClass.None || !Hidden(threatEye, snapped, c))
				return;
			var visible = 0;
			for (var i = 1; i <= 3; i++)
			{
				var p = me.Lerp(snapped, i * 0.25f) + Vector3.Up * 1.0f;
				if (!Blocked(threatEye, p))
					visible++;
			}
			var candidate = new CoverCandidate(c, me.DistanceTo(snapped), visible / 3f,
				HasFiringAngle(snapped, threat + Vector3.Up * 1.1f, c), snapped.DistanceTo(threat));
			var score = CoverRules.Score(candidate, myThreatDist, retreat);
			if (score < bestScore)
			{
				bestScore = score;
				best = snapped;
				bestClass = c;
			}
		}

		Consider(me, CoverClass.None); // standing where it is may already hide it
		for (var i = 0; i < rays; i++)
		{
			var a = Mathf.Tau * i / rays;
			var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
			var from = me + Vector3.Up * CoverRules.ProneMinHeight;
			var hit = Cast(PhysicsRayQueryParameters3D.Create(from, from + dir * radius, 1u));
			if (hit.Count == 0)
				continue;
			var point = (Vector3)hit["position"];
			var height = ObstacleHeight(point, dir, me.Y);
			var klass = CoverRules.Classify(height);
			if (klass == CoverClass.None)
				continue;
			var away = new Vector3(point.X - threat.X, 0f, point.Z - threat.Z).Normalized();
			Consider(point + away * 0.7f, klass);
			Consider(point - dir * 0.7f, CoverClass.None); // the near side, checked as it stands
		}
		_coverClass = bestClass;
		return best;
	}

	/// <summary>Which side to lean to for a clear shot from standing cover: -1, 1, or 0 when neither works.</summary>
	private float FindLeanSide()
	{
		if (_coverPoint is not { } cover || Perception.InterestPoint is not { } target)
			return 0f;
		var toward = new Vector3(target.X - cover.X, 0f, target.Z - cover.Z).Normalized();
		var side = toward.Cross(Vector3.Up);
		var lean = S(Stats.Cover.LeanM);
		foreach (var s in new[] { 1f, -1f })
		{
			if (!Blocked(cover + side * lean * s + Vector3.Up * 1.6f, target))
				return (side * s).Dot(GlobalBasis.X) >= 0f ? 1f : -1f; // the body leans along its own x axis
		}
		return 0f;
	}

	/// <summary>From cover: a lean, a rise, or failing that a sidestep with a clear shot.</summary>
	private Vector3? FindPeekPoint()
	{
		if (_coverPoint is not { } cover || Perception.InterestPoint is not { } target)
			return null;
		_leanSide = _coverClass == CoverClass.Standing ? FindLeanSide() : 0f;
		if (_leanSide != 0f || _coverClass != CoverClass.Standing)
			return cover; // peek in place
		var toTarget = new Vector3(target.X - cover.X, 0f, target.Z - cover.Z).Normalized();
		var side = toTarget.Cross(Vector3.Up);
		foreach (var offset in new[] { 0f, 1f, -1f, 2f, -2f })
		{
			var p = SnapToNav(cover + side * offset);
			if (!Blocked(p + Vector3.Up * 1.6f, target))
				return p;
		}
		return null;
	}

	/// <summary>Moves the body, hitboxes and gun sideways while leaning around cover.</summary>
	private void ApplyLean(float delta, float side)
	{
		_lean = Mathf.MoveToward(_lean, side, 4f * delta);
		var offset = _lean * S(Stats.Cover.LeanM);
		_hitboxRoot.Position = new Vector3(offset, _hitboxRoot.Position.Y, _hitboxRoot.Position.Z);
		if (_bodyMesh is not null)
			_bodyMesh.Position = new Vector3(offset, _bodyMesh.Position.Y, _bodyMesh.Position.Z);
		PlaceWeapon();
	}

	private void PlaceWeapon()
	{
		if (_rig is not null)
			return; // the weapon rides on the animated chest
		if (Weapon is { } w)
			w.Position = new Vector3(0.18f + LeanOffset, 1.2f * _crouchScale, -0.2f);
	}

	private float LeanOffset => _lean * S(Stats.Cover.LeanM);
}
