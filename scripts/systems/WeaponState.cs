using System.Collections.Generic;

namespace ProjectD;

/// <summary>
/// Firing state of one weapon: fire control, magazine and chambered round. Semi/auto actions
/// feed the next round from the magazine after each shot. Reload actions arrive in M2 step 3.
/// </summary>
public sealed class WeaponState
{
	public FireControl Fire { get; }
	public Magazine? Magazine { get; private set; }
	public AmmoType? Chamber { get; private set; }

	public WeaponState(FireControl fire) => Fire = fire;

	/// <summary>Rounds that can still be fired: chamber plus magazine.</summary>
	public int RoundsAvailable => (Chamber is null ? 0 : 1) + (Magazine?.Count ?? 0);

	public void InsertMagazine(Magazine magazine) => Magazine = magazine;

	/// <summary>Restores a chambered round when a weapon item is drawn again.</summary>
	public void SetChamber(AmmoType? round) => Chamber = round;

	public Magazine? EjectMagazine()
	{
		var m = Magazine;
		Magazine = null;
		return m;
	}

	/// <summary>Moves the top magazine round into an empty chamber (charging handle).</summary>
	public bool ChamberRound()
	{
		if (Chamber is not null || Magazine is null)
			return false;
		Chamber = Magazine.Pop();
		return Chamber is not null;
	}

	/// <summary>Advances fire control; returns the rounds fired this frame, oldest first.</summary>
	public List<AmmoType> Update(float delta, bool pressed, bool held)
	{
		var fired = new List<AmmoType>();
		var shots = Fire.Update(delta, pressed, held, RoundsAvailable);
		for (var i = 0; i < shots; i++)
		{
			if (Chamber is null && !ChamberRound())
				break;
			fired.Add(Chamber!);
			Chamber = null;
			ChamberRound();
		}
		return fired;
	}
}
