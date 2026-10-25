using System;
using Godot;

namespace ProjectD;

/// <summary>
/// 1D spring-damper (animation spec s4.1): unit mass, stiffness k, damping given as a ratio (1 = critically
/// damped, below 1 bounces). Stepped in small fixed substeps so high stiffness stays stable. Pure logic.
/// </summary>
public sealed class SpringDamper
{
	private const float MaxStep = 1f / 240f;

	public float Value { get; set; }
	public float Velocity { get; set; }

	/// <summary>Adds an instant velocity (a kick).</summary>
	public void Impulse(float velocity) => Velocity += velocity;

	public void Update(float delta, float stiffness, float dampingRatio)
	{
		var damping = 2f * dampingRatio * MathF.Sqrt(MathF.Max(0f, stiffness));
		while (delta > 0f)
		{
			var h = MathF.Min(delta, MaxStep);
			Velocity += (-stiffness * Value - damping * Velocity) * h;
			Value += Velocity * h;
			delta -= h;
		}
	}

	public void Reset()
	{
		Value = 0f;
		Velocity = 0f;
	}
}

/// <summary>Three independent springs sharing one stiffness and damping (pitch/yaw/roll or x/y/z).</summary>
public sealed class Spring3
{
	public SpringDamper X { get; } = new();
	public SpringDamper Y { get; } = new();
	public SpringDamper Z { get; } = new();

	public Vector3 Value => new(X.Value, Y.Value, Z.Value);

	public void Impulse(Vector3 velocity)
	{
		X.Impulse(velocity.X);
		Y.Impulse(velocity.Y);
		Z.Impulse(velocity.Z);
	}

	public void Update(float delta, float stiffness, float dampingRatio)
	{
		X.Update(delta, stiffness, dampingRatio);
		Y.Update(delta, stiffness, dampingRatio);
		Z.Update(delta, stiffness, dampingRatio);
	}

	public void Reset()
	{
		X.Reset();
		Y.Reset();
		Z.Reset();
	}
}
