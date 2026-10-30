using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>Timing numbers for the AI state machine (GDD s16). Filled from the actor stats (<c>ActorTuning</c>).</summary>
public sealed record BrainTuning(
	/// <summary>Distance at which a destination counts as reached, metres.</summary>
	float ArrivalRadius,
	/// <summary>Seconds spent looking around after reaching an investigated spot.</summary>
	float LookAroundS,
	/// <summary>Seconds without any cue before combat gives way to a search.</summary>
	float LostTargetS,
	/// <summary>Seconds spent searching the last known position before giving up.</summary>
	float SearchS,
	/// <summary>Seconds waited at each patrol point.</summary>
	float PatrolWaitS,
	/// <summary>Give up on a body after this many seconds (blocked path, interrupted).</summary>
	float LootTimeoutS = 40f)
{
	public static BrainTuning Default => new(1.2f, 3f, 4f, 10f, 2f);
}

public enum AiState { Idle, Patrol, Investigate, Combat, Search, Loot, Dead }

/// <summary>What the AI currently perceives, fed to the brain once per tick.</summary>
public readonly record struct AiSenses(AwarenessLevel Level, bool SeesTarget, Vector3? InterestPoint,
	float SecondsSinceCue, Vector3? LootPoint = null);

/// <summary>
/// Human AI state machine: idle/patrol, investigate, combat, search (GDD s16). Pure logic. It decides
/// the state and where to walk; the node moves the body and, in combat, handles distance and aiming.
/// </summary>
public sealed class AiBrain
{
	private BrainTuning _t;
	private readonly List<Vector3> _patrol = new();
	private int _patrolIndex;
	private float _waitTimer;
	private float _lootCooldown;

	public AiBrain(BrainTuning tuning) => _t = tuning;

	/// <summary>Replaced when the actor's stats change.</summary>
	public BrainTuning Tuning { get => _t; set => _t = value; }

	public AiState State { get; private set; } = AiState.Idle;

	/// <summary>Where to walk, or null when standing (looking around, combat positioning is the node's job).</summary>
	public Vector3? Destination { get; private set; }

	public float StateTimeS { get; private set; }

	/// <summary>True while standing at a spot and looking around.</summary>
	public bool Looking { get; private set; }

	public event Action<AiState, AiState>? StateChanged;

	public void SetPatrol(IEnumerable<Vector3> points)
	{
		_patrol.Clear();
		_patrol.AddRange(points);
		_patrolIndex = 0;
		if (State is AiState.Idle or AiState.Patrol)
			GoIdleOrPatrol();
	}

	private AiState RestingState => _patrol.Count > 0 ? AiState.Patrol : AiState.Idle;

	private void Enter(AiState next)
	{
		if (next == State)
			return;
		var previous = State;
		State = next;
		StateTimeS = 0f;
		_waitTimer = 0f;
		Looking = false;
		StateChanged?.Invoke(previous, next);
	}

	private void GoIdleOrPatrol()
	{
		Enter(RestingState);
		Destination = State == AiState.Patrol ? _patrol[_patrolIndex] : null;
		Looking = State == AiState.Idle;
	}

	/// <param name="arrived">The body reached <see cref="Destination"/> (or is within arrival radius).</param>
	public void Update(float delta, in AiSenses s, bool arrived, bool dead)
	{
		if (dead)
		{
			Enter(AiState.Dead);
			Destination = null;
			return;
		}
		if (State == AiState.Dead)
			GoIdleOrPatrol();
		StateTimeS += delta;
		_lootCooldown = Math.Max(0f, _lootCooldown - delta);

		var engaged = s.Level == AwarenessLevel.Alert && s.SeesTarget;
		if (engaged)
		{
			Enter(AiState.Combat);
			Destination = null;
			return;
		}

		switch (State)
		{
			case AiState.Idle:
			case AiState.Patrol:
				if (s.Level != AwarenessLevel.Unaware && s.InterestPoint is { } first)
				{
					Enter(AiState.Investigate);
					Destination = first;
				}
				else if (s.LootPoint is { } body && _lootCooldown <= 0f)
				{
					Enter(AiState.Loot);
					Destination = body;
				}
				else
				{
					UpdatePatrol(delta, arrived);
				}
				break;

			case AiState.Investigate:
				if (s.InterestPoint is { } point && s.SecondsSinceCue < 0.5f)
					Destination = point; // fresh cue: keep following it
				if (Destination is null || arrived)
				{
					Looking = true;
					Destination = null;
					_waitTimer += delta;
					if (_waitTimer >= _t.LookAroundS)
						GoIdleOrPatrol();
				}
				break;

			case AiState.Loot:
				if (s.Level != AwarenessLevel.Unaware && s.InterestPoint is { } alarm)
				{
					Enter(AiState.Investigate);
					Destination = alarm;
				}
				else if (s.LootPoint is null || StateTimeS >= _t.LootTimeoutS)
				{
					if (s.LootPoint is not null)
						_lootCooldown = 15f; // stuck on this body: leave it alone for a while
					GoIdleOrPatrol();
				}
				else
				{
					Destination = s.LootPoint;
					Looking = arrived;
				}
				break;

			case AiState.Combat:
				// Lost sight: after a short grace period go to where the target was last known.
				if (s.SecondsSinceCue >= _t.LostTargetS && s.InterestPoint is { } last)
				{
					Enter(AiState.Search);
					Destination = last;
				}
				else if (s.SecondsSinceCue >= _t.LostTargetS)
				{
					GoIdleOrPatrol();
				}
				break;

			case AiState.Search:
				if (s.InterestPoint is { } fresh && s.SecondsSinceCue < 0.5f)
					Destination = fresh;
				if (Destination is null || arrived)
				{
					Looking = true;
					Destination = null;
					_waitTimer += delta;
				}
				if (StateTimeS >= _t.SearchS)
					GoIdleOrPatrol();
				break;
		}
	}

	private void UpdatePatrol(float delta, bool arrived)
	{
		if (State == AiState.Idle)
		{
			Looking = true;
			if (_patrol.Count > 0)
				GoIdleOrPatrol();
			return;
		}
		if (_patrol.Count == 0)
		{
			GoIdleOrPatrol();
			return;
		}
		Destination ??= _patrol[_patrolIndex];
		if (!arrived)
		{
			Looking = false;
			return;
		}
		Looking = true;
		_waitTimer += delta;
		if (_waitTimer >= _t.PatrolWaitS)
		{
			_waitTimer = 0f;
			_patrolIndex = (_patrolIndex + 1) % _patrol.Count;
			Destination = _patrol[_patrolIndex];
			Looking = false;
		}
	}
}
