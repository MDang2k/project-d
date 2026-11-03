using System;
using System.Collections.Generic;

namespace ProjectD;

public enum LoaderMode { Idle, Loading, Unloading }

/// <summary>
/// Loads rounds into a magazine from an ammo stack, or unloads them, one round at a time
/// (GDD s7: "loading rounds into magazines takes time and is done from the inventory").
/// Rounds go on top, so load order matters. Pure logic.
/// </summary>
public sealed class MagazineLoader
{
	private float _timer;
	private ItemInstance? _magazine;
	private ItemInstance? _ammo;

	public LoaderMode Mode { get; private set; }
	public bool IsBusy => Mode != LoaderMode.Idle;
	public float SecondsPerRound { get; }

	public MagazineLoader(float secondsPerRound) => SecondsPerRound = secondsPerRound;

	/// <summary>Fired for every round unloaded, with its ammo id.</summary>
	public event Action<string>? RoundUnloaded;
	/// <summary>Fired for every round loaded into the magazine.</summary>
	public event Action? RoundLoaded;
	/// <summary>Fired when a task ends (finished or cancelled).</summary>
	public event Action? Finished;

	public ItemInstance? Magazine => _magazine;

	/// <summary>Whether this ammo can go into this magazine (same caliber, room left).</summary>
	public static bool CanLoad(ItemInstance magazine, ItemInstance ammo)
		=> magazine.Spec.Category == ItemCategory.Magazine && ammo.Spec.Category == ItemCategory.Ammo
			&& magazine.Spec.Caliber is { } c && c == ammo.Spec.Caliber
			&& ammo.Spec.AmmoId is not null && magazine.Rounds.Count < magazine.Spec.MagazineCapacity
			&& ammo.Count > 0;

	public bool StartLoad(ItemInstance magazine, ItemInstance ammo)
	{
		if (IsBusy || !CanLoad(magazine, ammo))
			return false;
		_magazine = magazine;
		_ammo = ammo;
		Mode = LoaderMode.Loading;
		_timer = 0f;
		return true;
	}

	public bool StartUnload(ItemInstance magazine)
	{
		if (IsBusy || magazine.Rounds.Count == 0)
			return false;
		_magazine = magazine;
		_ammo = null;
		Mode = LoaderMode.Unloading;
		_timer = 0f;
		return true;
	}

	public void Cancel()
	{
		if (!IsBusy)
			return;
		End();
	}

	public void Update(float delta)
	{
		if (!IsBusy || _magazine is null)
			return;
		_timer += delta;
		while (_timer >= SecondsPerRound && IsBusy)
		{
			_timer -= SecondsPerRound;
			Step();
		}
	}

	private void Step()
	{
		var mag = _magazine!;
		if (Mode == LoaderMode.Loading)
		{
			var ammo = _ammo!;
			if (!CanLoad(mag, ammo))
			{
				End();
				return;
			}
			mag.Rounds.Add(ammo.Spec.AmmoId!);
			ammo.Count--;
			RoundLoaded?.Invoke();
			if (ammo.Count <= 0 || mag.Rounds.Count >= mag.Spec.MagazineCapacity)
				End();
		}
		else
		{
			if (mag.Rounds.Count == 0)
			{
				End();
				return;
			}
			var id = mag.Rounds[^1];
			mag.Rounds.RemoveAt(mag.Rounds.Count - 1);
			RoundUnloaded?.Invoke(id);
			if (mag.Rounds.Count == 0)
				End();
		}
	}

	private void End()
	{
		Mode = LoaderMode.Idle;
		_magazine = null;
		_ammo = null;
		_timer = 0f;
		Finished?.Invoke();
	}
}
