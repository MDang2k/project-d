using System;
using System.Collections.Generic;

namespace ProjectD;

/// <summary>Stun (prototype flashbang) strength by distance and facing (throwables spec s4). Pure logic.</summary>
public static class StunModel
{
	public const float DeafS = 4f;

	/// <summary>0 outside the radius, up to 1 at the burst; looking away from the burst halves it.</summary>
	public static float Strength(float distanceM, float radiusM, bool facingBurst, float scale = 1f)
	{
		if (radiusM <= 0f || distanceM >= radiusM)
			return 0f;
		var s = 1f - Math.Max(0f, distanceM) / radiusM;
		return s * (facingBurst ? 1f : 0.5f) * scale;
	}

	/// <summary>Seconds the white-out lasts: 3 s at the edge of the effect to 5 s at the burst.</summary>
	public static float BlindS(float strength) => strength <= 0f ? 0f : 3f + 2f * Math.Clamp(strength, 0f, 1f);

	/// <summary>Seconds of deafness: 4 s whenever the burst reached the character at all.</summary>
	public static float DeafnessS(float strength) => strength <= 0.05f ? 0f : DeafS;
}

/// <summary>
/// The burning status (throwables spec s4): 6 HP per second while in fire and for 3 seconds after, spread over the legs
/// (the feet in the fire) and the body part touching it; leaving the fire is the cure. No bleed. Pure logic.
/// </summary>
public sealed class BurnStatus
{
	public const float DamagePerS = 6f;
	public const float LingerS = 3f;

	private static readonly (BodyPart Part, float Share)[] Shares =
	{
		(BodyPart.LeftLeg, 0.25f), (BodyPart.RightLeg, 0.25f), (BodyPart.Thorax, 0.25f), (BodyPart.Stomach, 0.25f),
	};

	private float _leftS;

	public bool IsBurning => _leftS > 0f;
	public float LeftS => _leftS;

	/// <summary>The character is in fire this step: the burn runs for another 3 s from now.</summary>
	public void Ignite() => _leftS = LingerS;

	/// <summary>Damage to deal this step, by body part (empty when not burning).</summary>
	public List<(BodyPart Part, float Damage)> Update(float delta)
	{
		var list = new List<(BodyPart, float)>();
		if (_leftS <= 0f)
			return list;
		var step = Math.Min(delta, _leftS);
		_leftS -= delta;
		foreach (var (part, share) in Shares)
			list.Add((part, DamagePerS * share * step));
		return list;
	}

	public void Extinguish() => _leftS = 0f;
}

/// <summary>Tear gas on a character (throwables spec s4): how thick it is where they stand and what it does. Pure logic.</summary>
public static class GasModel
{
	public const float SwayAtFull = 2.5f;
	public const float StaminaDrainPerS = 4f;
	public const float AimErrorMultiplier = 2f;
	public const float CoughNoiseM = 12f;

	/// <summary>Sway multiplier at a gas concentration (0 none to 1 centre).</summary>
	public static float SwayMultiplier(float concentration) => 1f + (SwayAtFull - 1f) * Math.Clamp(concentration, 0f, 1f);
}

/// <summary>
/// Thermite on a lock (throwables spec s5): the door burns open after about 20 seconds, loud, unless it is proof. Pure
/// logic; the door node feeds it time and plays the noise.
/// </summary>
public sealed class ThermiteLock
{
	public const float BurnS = 20f;
	public const float NoiseEveryS = 5f;

	private float _burned;
	private float _sinceNoise;

	public ThermiteLock(bool proof) => Proof = proof;

	public bool Proof { get; }
	public bool Burning { get; private set; }
	public bool Burnt { get; private set; }

	/// <summary>A noise event is due (every 5 s while it burns).</summary>
	public event Action? Noise;

	/// <summary>The lock burned through.</summary>
	public event Action? BurnedThrough;

	/// <summary>Starts the burn. False for a proof or an already burning or burnt lock.</summary>
	public bool Start()
	{
		if (Proof || Burning || Burnt)
			return false;
		Burning = true;
		return true;
	}

	public void Update(float delta)
	{
		if (!Burning)
			return;
		_burned += delta;
		_sinceNoise += delta;
		if (_sinceNoise >= NoiseEveryS)
		{
			_sinceNoise = 0f;
			Noise?.Invoke();
		}
		if (_burned < BurnS)
			return;
		Burning = false;
		Burnt = true;
		BurnedThrough?.Invoke();
	}
}

public static class GrenadeSpecExtensions
{
	/// <summary>Items that go off on first contact and have no pin to pull: bottle, rock, molotov and the thrown flare.</summary>
	public static bool ImpactTriggered(this GrenadeSpec spec) => spec.Kind is GrenadeKind.Distraction or GrenadeKind.Molotov or GrenadeKind.Flare;
}
