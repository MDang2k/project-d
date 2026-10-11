using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class MagazineLoaderTests
{
	private static ItemInstance Mag(int rounds = 0, int cap = 10, string cal = "7.62x39")
	{
		var spec = new ItemSpec("mag", "mag", ItemCategory.Magazine, 1, 2, 0.2f, MagazineCapacity: cap,
			RoundWeightKg: 0.01f, Caliber: cal);
		var m = ItemInstance.Create(spec);
		for (var i = 0; i < rounds; i++)
			m.Rounds.Add("ball");
		return m;
	}

	private static ItemInstance Ammo(int count, string id = "ball", string cal = "7.62x39")
	{
		var spec = new ItemSpec("ammo_" + id, "ammo", ItemCategory.Ammo, 1, 1, 0.01f, 60, Caliber: cal, AmmoId: id);
		return ItemInstance.Create(spec, count);
	}

	[Fact]
	public void LoadsOneRoundPerInterval()
	{
		var mag = Mag();
		var ammo = Ammo(30);
		var l = new MagazineLoader(0.5f);
		Assert.True(l.StartLoad(mag, ammo));
		l.Update(0.4f);
		Assert.Empty(mag.Rounds);
		l.Update(0.2f);
		Assert.Single(mag.Rounds);
		Assert.Equal(29, ammo.Count);
		l.Update(1.0f);
		Assert.Equal(3, mag.Rounds.Count);
	}

	[Fact]
	public void StopsWhenMagazineIsFull()
	{
		var mag = Mag(rounds: 8);
		var ammo = Ammo(30);
		var l = new MagazineLoader(0.1f);
		var finished = false;
		l.Finished += () => finished = true;
		l.StartLoad(mag, ammo);
		l.Update(5f);
		Assert.Equal(10, mag.Rounds.Count);
		Assert.Equal(28, ammo.Count);
		Assert.True(finished);
		Assert.False(l.IsBusy);
	}

	[Fact]
	public void StopsWhenAmmoRunsOut()
	{
		var mag = Mag();
		var ammo = Ammo(3);
		var l = new MagazineLoader(0.1f);
		l.StartLoad(mag, ammo);
		l.Update(5f);
		Assert.Equal(3, mag.Rounds.Count);
		Assert.Equal(0, ammo.Count);
	}

	[Fact]
	public void WrongCaliberCannotLoad()
	{
		var l = new MagazineLoader(0.1f);
		Assert.False(l.StartLoad(Mag(), Ammo(10, "other", "9x19")));
		Assert.False(MagazineLoader.CanLoad(Mag(rounds: 10), Ammo(5)));
	}

	[Fact]
	public void MixedAmmoKeepsLoadOrder()
	{
		var mag = Mag();
		var l = new MagazineLoader(0.1f);
		l.StartLoad(mag, Ammo(2, "ball"));
		l.Update(1f);
		l.StartLoad(mag, Ammo(2, "ap"));
		l.Update(1f);
		Assert.Equal(new[] { "ball", "ball", "ap", "ap" }, mag.Rounds);
	}

	[Fact]
	public void UnloadReturnsRoundsTopFirst()
	{
		var mag = Mag();
		mag.Rounds.AddRange(new[] { "ball", "ball", "ap" });
		var got = new List<string>();
		var l = new MagazineLoader(0.1f);
		l.RoundUnloaded += got.Add;
		Assert.True(l.StartUnload(mag));
		l.Update(1f);
		Assert.Equal(new[] { "ap", "ball", "ball" }, got);
		Assert.Empty(mag.Rounds);
		Assert.False(l.IsBusy);
	}

	[Fact]
	public void CancelKeepsProgress()
	{
		var mag = Mag();
		var ammo = Ammo(10);
		var l = new MagazineLoader(0.1f);
		l.StartLoad(mag, ammo);
		l.Update(0.35f);
		l.Cancel();
		l.Update(5f);
		Assert.Equal(3, mag.Rounds.Count);
		Assert.Equal(7, ammo.Count);
	}

	[Fact]
	public void OneTaskAtATime()
	{
		var l = new MagazineLoader(0.1f);
		var mag = Mag();
		Assert.True(l.StartLoad(mag, Ammo(5)));
		Assert.False(l.StartUnload(Mag(rounds: 3)));
	}
}

public class WeaponItemStateTests
{
	[Fact]
	public void InsertedMagazineCountsInWeaponWeight()
	{
		var rifle = ItemInstance.Create(Items.Rifle);
		var baseWeight = rifle.WeightKg;
		var magSpec = new ItemSpec("m", "m", ItemCategory.Magazine, 1, 2, 0.25f, MagazineCapacity: 30, RoundWeightKg: 0.016f);
		var mag = ItemInstance.Create(magSpec);
		for (var i = 0; i < 30; i++)
			mag.Rounds.Add("x");
		rifle.InsertedMagazine = mag;
		Assert.Equal(baseWeight + 0.25f + 30 * 0.016f, rifle.WeightKg, 3);
	}

	[Fact]
	public void MagazineWithRoundsHasUnloadAction()
	{
		var magSpec = new ItemSpec("m", "m", ItemCategory.Magazine, 1, 2, 0.25f, MagazineCapacity: 30);
		var mag = ItemInstance.Create(magSpec);
		var inv = new CharacterInventory();
		Assert.Equal(QuickActionKind.None, InventoryActions.Quick(inv, mag, null).Kind);
		mag.Rounds.Add("x");
		Assert.Equal(QuickActionKind.Unload, InventoryActions.Quick(inv, mag, null).Kind);
	}

	[Fact]
	public void PouchStillWorksAsDefaultStore()
	{
		var state = new WeaponState(new FireControl(new[] { FireMode.Semi }, 600f));
		var c = new WeaponController(state, ActionTuning.Default);
		Assert.Same(c.Pouch, c.Store);
	}
}
