using System;
using System.Collections.Generic;

namespace ProjectD;

public enum ReloadPhase { None, Enter, MagDetach, MagStow, MagFetch, MagInsert, BoltRack, Exit }

/// <summary>An event at the end of a phase; every inventory or weapon-state change happens at exactly one of these.</summary>
public enum ReloadEvent { MagDetached, MagStowed, MagFetched, MagInserted, BoltRacked, Finished }

/// <summary>Where a reload starts, from the weapon's physical state (animation spec s5.2).</summary>
public enum ReloadEntry
{
	/// <summary>Magazine in and chambered, or magazine in but empty: the whole sequence.</summary>
	Full,
	/// <summary>Magazine in with rounds but nothing chambered: only the bolt rack.</summary>
	ResumeRack,
	/// <summary>No magazine, chambered: fetch and insert.</summary>
	ResumeInsert,
	/// <summary>No magazine, nothing chambered: fetch, insert, rack.</summary>
	ResumeEmpty,
}

/// <summary>
/// Phased reload timeline (animation spec s5.1, s5.6). The weapon logic owns time: this advances through phases
/// by seconds and reports events; the controller does the state changes and the animation follows. Pure logic.
/// </summary>
public sealed class ReloadSequence
{
	private readonly record struct Step(ReloadPhase Phase, float Seconds, ReloadEvent? EndEvent);

	private readonly ActionTuning _t;
	private readonly float _timeMultiplier;
	private List<Step> _steps = new();
	private int _index;
	private float _inStep;

	public bool Fast { get; private set; }
	public ReloadEntry Entry { get; }
	public bool IsDone { get; private set; }
	public ReloadPhase Phase => IsDone || _index >= _steps.Count ? ReloadPhase.None : _steps[_index].Phase;
	public float Total { get; private set; }
	public float Elapsed { get; private set; }
	public float Progress => Total > 0f ? Math.Min(Elapsed / Total, 1f) : 1f;

	/// <param name="needsRack">Chamber was empty when the reload began (full reloads add a rack phase).</param>
	public ReloadSequence(ActionTuning tuning, bool fast, ReloadEntry entry, bool needsRack, float timeMultiplier = 1f)
	{
		_t = tuning;
		_timeMultiplier = timeMultiplier;
		Entry = entry;
		Fast = fast;
		_steps = Build(fast, entry, needsRack);
		Total = TotalOf(_steps);
	}

	/// <summary>True until the magazine has left the weapon (a double tap may still make it a fast reload).</summary>
	public bool CanUpgrade => !IsDone && !Fast && Entry == ReloadEntry.Full && _index < 2;

	public void UpgradeToFast(bool needsRack)
	{
		if (!CanUpgrade)
			return;
		Fast = true;
		_steps = Build(true, Entry, needsRack);
		Total = TotalOf(_steps);
		_index = Math.Min(_index, _steps.Count - 1);
		_inStep = 0f;
		Elapsed = Math.Min(Elapsed, Total);
	}

	private static float TotalOf(List<Step> steps)
	{
		var total = 0f;
		foreach (var s in steps)
			total += s.Seconds;
		return total;
	}

	private List<Step> Build(bool fast, ReloadEntry entry, bool needsRack)
	{
		var b = (fast ? _t.FastReloadS : _t.TacticalReloadS) * _timeMultiplier;
		var rack = _t.ChargeS * _timeMultiplier;
		var after = 1f - _t.EjectAt; // share of the base time after the magazine leaves
		var steps = new List<Step>();
		void Add(ReloadPhase p, float seconds, ReloadEvent? e) => steps.Add(new Step(p, seconds, e));
		switch (entry)
		{
			case ReloadEntry.Full:
				Add(ReloadPhase.Enter, b * 0.08f, null);
				Add(ReloadPhase.MagDetach, b * (_t.EjectAt - 0.08f), ReloadEvent.MagDetached);
				if (!fast)
					Add(ReloadPhase.MagStow, b * after * 0.25f, ReloadEvent.MagStowed);
				Add(ReloadPhase.MagFetch, b * after * (fast ? 0.38f : 0.28f), ReloadEvent.MagFetched);
				Add(ReloadPhase.MagInsert, b * after * (fast ? 0.42f : 0.33f), ReloadEvent.MagInserted);
				if (needsRack)
					Add(ReloadPhase.BoltRack, rack, ReloadEvent.BoltRacked);
				Add(ReloadPhase.Exit, b * after * (fast ? 0.2f : 0.14f), ReloadEvent.Finished);
				break;
			case ReloadEntry.ResumeRack:
				Add(ReloadPhase.BoltRack, rack, ReloadEvent.BoltRacked);
				Add(ReloadPhase.Exit, b * after * 0.14f, ReloadEvent.Finished);
				break;
			default:
				Add(ReloadPhase.MagFetch, b * after * 0.3f, ReloadEvent.MagFetched);
				Add(ReloadPhase.MagInsert, b * after * 0.35f, ReloadEvent.MagInserted);
				if (entry == ReloadEntry.ResumeEmpty)
					Add(ReloadPhase.BoltRack, rack, ReloadEvent.BoltRacked);
				Add(ReloadPhase.Exit, b * after * 0.15f, ReloadEvent.Finished);
				break;
		}
		return steps;
	}

	/// <summary>Advances time and returns the events whose phases ended, in order.</summary>
	public List<ReloadEvent> Advance(float delta)
	{
		var events = new List<ReloadEvent>();
		if (IsDone)
			return events;
		Elapsed += delta;
		_inStep += delta;
		while (_index < _steps.Count && _inStep >= _steps[_index].Seconds)
		{
			var step = _steps[_index];
			_inStep -= step.Seconds;
			if (step.EndEvent is { } e)
				events.Add(e);
			_index++;
		}
		if (_index >= _steps.Count)
			IsDone = true;
		return events;
	}

	/// <summary>Ends the sequence without further events.</summary>
	public void Stop() => IsDone = true;

	/// <summary>Which phase to start from for the weapon's physical state.</summary>
	public static ReloadEntry EntryFor(bool hasMagazine, int magazineRounds, bool chambered)
	{
		if (hasMagazine)
			return !chambered && magazineRounds > 0 ? ReloadEntry.ResumeRack : ReloadEntry.Full;
		return chambered ? ReloadEntry.ResumeInsert : ReloadEntry.ResumeEmpty;
	}
}
