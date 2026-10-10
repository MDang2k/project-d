using System.Collections.Generic;
using Godot;

namespace ProjectD;

public struct BulletState
{
	public Vector3 Position;
	public Vector3 Velocity;
	public float Age;
}

/// <summary>Projectile integration with gravity drop (GDD s8). No wind in the slice.</summary>
public static class Ballistics
{
	/// <summary>
	/// Predicted path (same integrator as <see cref="Step"/>) for debug drawing: points from
	/// the start until maxTime elapses. Ignores collisions.
	/// </summary>
	public static List<Vector3> Predict(Vector3 position, Vector3 velocity, float gravity,
		float delta, float maxTime)
	{
		var s = new BulletState { Position = position, Velocity = velocity };
		var points = new List<Vector3> { position };
		while (s.Age < maxTime)
		{
			Step(ref s, gravity, delta);
			points.Add(s.Position);
		}
		return points;
	}

	/// <summary>Semi-implicit Euler step; returns the segment travelled for the raycast.</summary>
	public static (Vector3 From, Vector3 To) Step(ref BulletState s, float gravity, float delta)
	{
		var from = s.Position;
		s.Velocity += Vector3.Down * gravity * delta;
		s.Position += s.Velocity * delta;
		s.Age += delta;
		return (from, s.Position);
	}
}
