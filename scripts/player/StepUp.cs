using Godot;

namespace ProjectD;

/// <summary>
/// Walking up stairs and curbs: a capsule body stops dead against a 0.18 m step, so before the move the body is
/// lifted onto the step when something low blocks the way, there is room above, and it has a flat top to stand on.
/// Slopes are left to the normal slide. Shared by the player and the AI.
/// </summary>
public static class StepUp
{
	/// <summary>True when the body was lifted. Call before MoveAndSlide, with the horizontal velocity about to be used.</summary>
	public static bool Try(CharacterBody3D body, Vector3 horizontalVelocity, float delta, float stepHeight)
	{
		horizontalVelocity.Y = 0f;
		if (!body.IsOnFloor() || body.Velocity.Y > 0.5f || horizontalVelocity.LengthSquared() < 0.01f)
			return false;
		var dir = horizontalVelocity.Normalized();
		var motion = dir * Mathf.Max(horizontalVelocity.Length() * delta, 0.06f);
		var from = body.GlobalTransform;
		if (!body.TestMove(from, motion))
			return false; // nothing in the way

		// the top of whatever blocks us, just beyond the front of the capsule
		var probe = from.Origin + dir * (Radius(body) + 0.04f);
		var space = body.GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(probe + Vector3.Up * stepHeight, probe + Vector3.Down * 0.05f,
			body.CollisionMask, new Godot.Collections.Array<Rid> { body.GetRid() });
		var hit = space.IntersectRay(query);
		// only a flat top counts: a slope (even a walkable one) is left to the normal slide
		if (hit.Count == 0 || hit["normal"].AsVector3().AngleTo(Vector3.Up) > Mathf.DegToRad(8f))
			return false; // no flat top within a step's height
		var rise = hit["position"].AsVector3().Y - from.Origin.Y;
		if (rise < 0.02f || rise > stepHeight)
			return false;

		var up = Vector3.Up * (rise + 0.02f);
		if (body.TestMove(from, up))
			return false; // no room above
		if (body.TestMove(from.Translated(up), motion))
			return false; // still blocked up there
		body.GlobalPosition = from.Origin + Vector3.Up * rise;
		return true;
	}

	/// <summary>
	/// Going down a stair the body walks off the edge and would free-fall step by step. When it was on the floor last
	/// frame, is not rising and now has none, put it back on the floor up to a step's height below. Call after MoveAndSlide.
	/// </summary>
	public static bool Down(CharacterBody3D body, float stepHeight, bool wasOnFloor)
	{
		if (!wasOnFloor || body.IsOnFloor() || body.Velocity.Y > 0.1f)
			return false;
		var landing = new KinematicCollision3D();
		// the capsule meets a step edge with its rounded bottom, so the contact normal is slanted: only reject walls
		if (!body.TestMove(body.GlobalTransform, Vector3.Down * stepHeight, landing) || landing.GetNormal().Y < 0.3f)
			return false;
		body.GlobalPosition += landing.GetTravel();
		var velocity = body.Velocity;
		velocity.Y = 0f;
		body.Velocity = velocity;
		return true;
	}

	/// <summary>Radius of the body's capsule collision shape (0.3 when it has none).</summary>
	private static float Radius(CharacterBody3D body)
	{
		foreach (var child in body.GetChildren())
		{
			if (child is CollisionShape3D { Shape: CapsuleShape3D capsule })
				return capsule.Radius;
		}
		return 0.3f;
	}
}
