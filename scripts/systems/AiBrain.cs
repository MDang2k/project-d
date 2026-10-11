using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>Timing numbers for the AI state machine (GDD s16). Filled from an <c>AiPreset</c>.</summary>
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
	float PatrolWaitS)
{
	public static BrainTuning Default => new(1.2f, 3f, 4f, 10f, 2f);
}

public enum AiState { Idle, Patrol, Investigate, Combat, Search, Dead }

/// <summary>What the AI currently perceives, fed to the brain once per tick.</summary>
public readonly record struct AiSenses(AwarenessLevel Level, bool SeesTarget, Vector3? InterestPoint,
	float SecondsSinceCue);

/// <summary>
/// Human AI state machine: idle/patrol, investigate, combat, search (GDD s16). Pure logic. It decides
/// the state and where to walk; the node moves the body and, in combat, handles distance and aiming.
/// </summary>
public sealed class AiBrain
{
	private readonly BrainTuning _t;
	private readonly List<Vector3> _patrol = new();
	private int _patrolIndex;
	private float _waitTimer;

	public AiBrain(BrainTuning tuning) => _t = tuning;

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
