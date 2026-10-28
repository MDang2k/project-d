using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

public enum WeaponActionKind { None, TacticalReload, FastReload, MagCheck, ChamberCheck }

/// <summary>Result words of a magazine check (GDD s7). None means no magazine is inserted.</summary>
public enum MagEstimate { None, Empty, AlmostEmpty, AboutHalf, Full }

/// <param name="ExactCount">Set only when the check is exact (high Weapon Handling skill).</param>
public readonly record struct MagCheckResult(MagEstimate Estimate, int? ExactCount);

/// <summary>Durations and estimate thresholds for weapon actions (seconds, fractions).</summary>
public sealed record ActionTuning(
	float TacticalReloadS,
	float FastReloadS,
	float MagCheckS,
	float ChamberCheckS,
	/// <summary>Extra time to charge the weapon when the chamber is empty at reload.</summary>
	float ChargeS,
	/// <summary>Fraction of the reload at which the old magazine leaves the weapon.</summary>
	float EjectAt,
	float AlmostEmptyMax,
	float HalfMax)
{
	public static ActionTuning Default => new(
		TacticalReloadS: 2.4f, FastReloadS: 1.6f, MagCheckS: 1.2f, ChamberCheckS: 0.9f, ChargeS: 0.7f,
		EjectAt: 0.4f, AlmostEmptyMax: 0.25f, HalfMax: 0.75f);
}

/// <summary>Where spare magazines come from and go back to (the inventory, or a debug pouch).</summary>
public interface IMagazineStore
{
	/// <summary>Removes and returns the compatible magazine with the most rounds; null if none.</summary>
	Magazine? TakeFullest();

	/// <summary>Gives a magazine back (tactical reload, or an unusable spare).</summary>
	void Return(Magazine magazine);

	/// <summary>Whether a compatible magazine with rounds is available (nothing is taken).</summary>
	bool HasUsable();
}

/// <summary>Debug pouch of spare magazines, used when no inventory is attached.</summary>
public sealed class MagazinePouch : IMagazineStore
{
	private readonly List<Magazine> _mags = new();

	public int Count => _mags.Count;

	public void Clear() => _mags.Clear();

	public void Add(Magazine magazine) => _mags.Add(magazine);

	public void Return(Magazine magazine) => _mags.Add(magazine);

	public bool HasUsable() => _mags.Any(m => !m.IsEmpty);

	/// <summary>Removes and returns the magazine with the most rounds; null when none.</summary>
	public Magazine? TakeFullest()
	{
		if (_mags.Count == 0)
			return null;
		var best = _mags.OrderByDescending(m => m.Count).First();
		_mags.Remove(best);
		return best;
	}
}

/// <summary>
/// Timed weapon actions (GDD s7): tactical reload keeps the old magazine, fast reload drops it,
/// plus magazine and chamber checks. Pure logic; the node plays placeholder poses from Action/Progress.
/// </summary>
public sealed class WeaponController
{
	private readonly ActionTuning _t;
	private ReloadSequence? _seq;
	/// <summary>The old magazine, detached from the weapon but not yet stowed.</summary>
	private Magazine? _hand;
	/// <summary>The new magazine, fetched but not yet inserted.</summary>
	private Magazine? _incoming;
	private float _elapsed;
	private float _duration;

	public WeaponState State { get; }
	public MagazinePouch Pouch { get; } = new();

	/// <summary>Spare magazines source; defaults to the debug pouch, the player swaps in the inventory.</summary>
	public IMagazineStore Store { get; set; } = null!;
	public WeaponActionKind Action { get; private set; }
	public bool IsBusy => Action != WeaponActionKind.None;
	public float Progress => _seq is not null ? _seq.Progress : _duration > 0f ? Math.Min(_elapsed / _duration, 1f) : 0f;
	/// <summary>Current reload phase (None when not reloading); the animation follows it.</summary>
	public ReloadPhase Phase => _seq?.Phase ?? ReloadPhase.None;
	/// <summary>Progress inside the current reload phase, 0..1.</summary>
	public float PhaseProgress => _seq?.PhaseProgress ?? 0f;
	public bool ReloadHasRack => _seq?.HasRack ?? false;
	public bool ReloadIsFast => Action == WeaponActionKind.FastReload;
	/// <summary>Changes whenever a reload starts or turns fast, so the animation knows to restart its time warp.</summary>
	public int ReloadId { get; private set; }
	public float ReloadElapsed => _seq?.Elapsed ?? 0f;
	public ReloadEntry ReloadEntryKind => _seq?.Entry ?? ReloadEntry.Full;
	public IReadOnlyList<(ReloadEvent Event, float Time)> ReloadTimeline => _seq?.Timeline ?? System.Array.Empty<(ReloadEvent, float)>();
	public float ReloadTotal => _seq?.Total ?? 0f;
	/// <summary>True once the Weapon Handling skill gives exact counts (M8).</summary>
	public bool ExactMagCheck { get; set; }

	public event Action<MagCheckResult>? MagChecked;
	public event Action<bool>? ChamberChecked;
	public event Action<Magazine>? MagazineDropped;
	public event Action? ReloadFinished;
	/// <summary>A reload stopped early (sprint, melee, no magazine left): the weapon keeps its physical state.</summary>
	public event Action? ReloadInterrupted;

	public WeaponController(WeaponState state, ActionTuning tuning)
	{
		State = state;
		_t = tuning;
		Store = Pouch;
	}

	/// <summary>
	/// Starts a reload at the phase that matches the weapon's physical state (resumes a stopped one). False if
	/// busy or no usable spare is needed and none exists.
	/// </summary>
	public bool StartReload(bool fast)
	{
		if (IsBusy)
			return false;
		var entry = ReloadSequence.EntryFor(State.Magazine is not null, State.Magazine?.Count ?? 0, State.Chamber is not null);
		if (entry != ReloadEntry.ResumeRack && !Store.HasUsable())
			return false;
		_hand = null;
		_incoming = null;
		_seq = new ReloadSequence(_t, fast, entry, State.Chamber is null, ReloadTimeMultiplier);
		Action = fast ? WeaponActionKind.FastReload : WeaponActionKind.TacticalReload;
		ReloadId++;
		return true;
	}

	/// <summary>Double-tap reload: converts a just-started tactical reload into a fast one.</summary>
	public bool UpgradeToFast()
	{
		if (_seq is not { CanUpgrade: true } seq)
			return false;
		seq.UpgradeToFast(State.Chamber is null);
		Action = WeaponActionKind.FastReload;
		ReloadId++;
		return true;
	}

	/// <summary>
	/// Stops whatever is running (sprint, melee, mantle, jump, item use). Magazines in the hand go back to the
	/// store; the weapon keeps exactly the state it reached, so the next reload resumes from there.
	/// </summary>
	public void Interrupt()
	{
		if (!IsBusy)
			return;
		var wasReload = _seq is not null;
		if (_hand is not null)
			Store.Return(_hand);
		if (_incoming is not null)
			Store.Return(_incoming);
		_hand = null;
		_incoming = null;
		_seq = null;
		Action = WeaponActionKind.None;
		if (wasReload)
			ReloadInterrupted?.Invoke();
	}

	public bool StartMagCheck() => Begin(WeaponActionKind.MagCheck, _t.MagCheckS);

	public bool StartChamberCheck() => Begin(WeaponActionKind.ChamberCheck, _t.ChamberCheckS);

	public void Update(float delta)
	{
		if (!IsBusy)
			return;
		if (_seq is not null)
		{
			foreach (var e in _seq.Advance(delta))
			{
				Handle(e);
				if (_seq is null)
					return;
			}
			if (_seq.IsDone)
			{
				_seq = null;
				Action = WeaponActionKind.None;
			}
			return;
		}
		_elapsed += delta;
		if (_elapsed >= _duration)
			FinishCheck();
	}

	/// <summary>The one place each reload step changes the weapon or the inventory.</summary>
	private void Handle(ReloadEvent e)
	{
		switch (e)
		{
			case ReloadEvent.MagDetached:
				var old = State.EjectMagazine();
				if (old is null)
					break;
				if (Action == WeaponActionKind.FastReload)
					MagazineDropped?.Invoke(old);
				else
					_hand = old;
				break;
			case ReloadEvent.MagStowed:
				if (_hand is not null)
					Store.Return(_hand);
				_hand = null;
				break;
			case ReloadEvent.MagFetched:
				var spare = Store.TakeFullest();
				if (spare is null || spare.IsEmpty)
				{
					if (spare is not null)
						Store.Return(spare);
					Interrupt();
					break;
				}
				_incoming = spare;
				break;
			case ReloadEvent.MagInserted:
				if (_incoming is not null)
					State.InsertMagazine(_incoming);
				_incoming = null;
				break;
			case ReloadEvent.BoltRacked:
				if (State.Chamber is null)
					State.ChamberRound();
				break;
			case ReloadEvent.Finished:
				_seq?.Stop();
				_seq = null;
				Action = WeaponActionKind.None;
				ReloadFinished?.Invoke();
				break;
		}
	}

	/// <summary>Word for a magazine's fill level using the tuning thresholds.</summary>
	public static MagEstimate Estimate(Magazine magazine, ActionTuning t)
	{
		if (magazine.IsEmpty)
			return MagEstimate.Empty;
		var fraction = (float)magazine.Count / magazine.Capacity;
		if (fraction <= t.AlmostEmptyMax)
			return MagEstimate.AlmostEmpty;
		return fraction <= t.HalfMax ? MagEstimate.AboutHalf : MagEstimate.Full;
	}

	/// <summary>Injuries (destroyed arm) make reloads slower: 1 is normal speed.</summary>
	public float ReloadTimeMultiplier { get; set; } = 1f;

	private bool Begin(WeaponActionKind kind, float seconds)
	{
		if (IsBusy)
			return false;
		Action = kind;
		_elapsed = 0f;
		_duration = seconds;
		return true;
	}

	private void FinishCheck()
	{
		var kind = Action;
		Action = WeaponActionKind.None;
		if (kind == WeaponActionKind.MagCheck)
		{
			var mag = State.Magazine;
			var result = mag is null
				? new MagCheckResult(MagEstimate.None, null)
				: new MagCheckResult(Estimate(mag, _t), ExactMagCheck ? mag.Count : null);
			MagChecked?.Invoke(result);
		}
		else
		{
			ChamberChecked?.Invoke(State.Chamber is not null);
		}
	}
}

/// <summary>Placeholder weapon pose offsets while an action plays (until hand-made animations).</summary>
public static class WeaponPoseModel
{
	/// <summary>Raise, hold, lower envelope over action progress (0..1).</summary>
	public static float Envelope(float progress)
	{
		var rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(progress / 0.2f, 0f, 1f));
		var fall = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((progress - 0.8f) / 0.2f, 0f, 1f));
		return rise * (1f - fall);
	}

	/// <summary>Quick thrust forward at the end of the windup, then back. progress is 0..1.</summary>
	public static (Vector3 Position, Vector3 RotationDeg) MeleePose(float progress, float strikeAt)
	{
		var p = Mathf.Clamp(progress, 0f, 1f);
		var e = p < strikeAt ? p / Mathf.Max(0.01f, strikeAt) : 1f - (p - strikeAt) / Mathf.Max(0.01f, 1f - strikeAt);
		return (new Vector3(0f, -0.04f, -0.3f) * e, new Vector3(-8f, 0f, 0f) * e);
	}

	/// <summary>Position offset (m) and rotation (degrees pitch, yaw, roll) of the weapon.</summary>
	public static (Vector3 Position, Vector3 RotationDeg) Pose(WeaponActionKind kind, float progress)
	{
		var e = Envelope(progress);
		return kind switch
		{
			WeaponActionKind.TacticalReload or WeaponActionKind.FastReload
				=> (new Vector3(-0.04f, -0.16f, 0.06f) * e, new Vector3(-28f, 12f, 18f) * e),
			WeaponActionKind.MagCheck
				=> (new Vector3(-0.06f, 0.02f, 0.08f) * e, new Vector3(8f, 25f, 75f) * e),
			WeaponActionKind.ChamberCheck
				=> (new Vector3(-0.04f, 0.04f, 0.06f) * e, new Vector3(18f, -20f, -55f) * e),
			_ => (Vector3.Zero, Vector3.Zero),
		};
	}
}
