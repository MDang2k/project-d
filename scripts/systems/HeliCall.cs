using System;

namespace ProjectD;

public enum HeliState { NotCalled, Coming, Landed }

/// <summary>
/// The helicopter of a flare landing zone (Border Strip spec s4): a fired signal flare calls it, it lands after a delay,
/// and only then can the zone be used. One flare per zone per raid. Pure logic.
/// </summary>
public sealed class HeliCall
{
	public HeliCall(float delayS = 60f) => DelayS = delayS;

	public float DelayS { get; }
	public HeliState State { get; private set; }
	public float RemainingS { get; private set; }

	/// <summary>The helicopter touched down.</summary>
	public event Action? Landed;

	/// <summary>Fires the flare. False when one was already fired here.</summary>
	public bool Fire()
	{
		if (State != HeliState.NotCalled)
			return false;
		State = HeliState.Coming;
		RemainingS = DelayS;
		return true;
	}

	public void Update(float delta)
	{
		if (State != HeliState.Coming)
			return;
		RemainingS = Math.Max(0f, RemainingS - delta);
		if (RemainingS > 0f)
			return;
		State = HeliState.Landed;
		Landed?.Invoke();
	}
}
