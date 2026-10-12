using System.Linq;
using Xunit;

namespace ProjectD.Tests;

public class HudRulesTests
{
	[Fact]
	public void StaminaShowsWhenMovingOrLow()
	{
		Assert.False(HudRules.StaminaShown(1f, false));
		Assert.True(HudRules.StaminaShown(1f, true));
		Assert.True(HudRules.StaminaShown(0.4f, false));
	}

	[Fact]
	public void VitalsShowWhenLowOrConsuming()
	{
		Assert.False(HudRules.VitalsShown(0.9f, 0.9f, false));
		Assert.True(HudRules.VitalsShown(0.3f, 0.9f, false));
		Assert.True(HudRules.VitalsShown(0.9f, 0.2f, false));
		Assert.True(HudRules.VitalsShown(0.9f, 0.9f, true));
	}

	[Fact]
	public void HealthShowsOnlyWhenSomethingIsWrong()
	{
		Assert.False(HudRules.HealthShown(1f, false, false, false));
		Assert.True(HudRules.HealthShown(0.8f, false, false, false));
		Assert.True(HudRules.HealthShown(1f, true, false, false));
		Assert.True(HudRules.HealthShown(1f, false, true, false));
		Assert.True(HudRules.HealthShown(1f, false, false, true));
	}
}

public class HudFadeTests
{
	[Fact]
	public void AppearsQuicklyAndStaysForTheHoldTime()
	{
		var f = new HudFade(1.5f, 0.1f, 0.5f);
		f.Update(0.1f, true);
		Assert.Equal(1f, f.Alpha, 3);
		f.Update(1.0f, false);
		Assert.Equal(1f, f.Alpha, 3);
	}

	[Fact]
	public void FadesOutAfterTheHoldTime()
	{
		var f = new HudFade(1.0f, 0.1f, 0.5f);
		f.Update(0.1f, true);
		for (var i = 0; i < 12; i++)
			f.Update(0.1f, false); // 1.2 s: hold ended at 1.0
		Assert.True(f.Alpha < 1f);
		for (var i = 0; i < 10; i++)
			f.Update(0.1f, false);
		Assert.Equal(0f, f.Alpha, 3);
	}

	[Fact]
	public void PulseShowsWithoutWantedFlag()
	{
		var f = new HudFade(1.0f, 0.1f, 0.5f);
		f.Pulse();
		f.Update(0.2f, false);
		Assert.True(f.Alpha > 0f);
	}
}

public class QuickUseBarTests
{
	private static CharacterInventory InvWith(params (ItemSpec Spec, int Count)[] items)
	{
		var inv = new CharacterInventory();
		foreach (var (spec, count) in items)
			inv.TryAutoStore(ItemInstance.Create(spec, count));
		return inv;
	}

	[Fact]
	public void BindAndResolveFindsCarriedItem()
	{
		var bar = new QuickUseBar();
		var inv = InvWith((Items.Bandage, 1));
		bar.Bind(0, Items.Bandage.Id);
		Assert.Equal(Items.Bandage.Id, bar.Resolve(0, inv)?.Spec.Id);
		Assert.Null(bar.Resolve(1, inv));
	}

	[Fact]
	public void BindingTheSameItemMovesIt()
	{
		var bar = new QuickUseBar();
		bar.Bind(0, "bandage");
		bar.Bind(2, "bandage");
		Assert.Null(bar.IdAt(0));
		Assert.Equal("bandage", bar.IdAt(2));
	}

	[Fact]
	public void MissingItemResolvesToNull()
	{
		var bar = new QuickUseBar();
		bar.Bind(0, Items.Bandage.Id);
		Assert.Null(bar.Resolve(0, new CharacterInventory()));
		Assert.Equal(0, bar.CountOf(0, new CharacterInventory()));
	}

	[Fact]
	public void SecureContainerIsNotUsedFromQuickSlots()
	{
		var bar = new QuickUseBar();
		bar.Bind(0, Items.Bandage.Id);
		var inv = new CharacterInventory();
		inv.TryEquip(ItemInstance.Create(Items.Secure), EquipSlot.SecureContainer);
		inv.SecureGrids.First().TryPlace(ItemInstance.Create(Items.Bandage), 0, 0);
		Assert.Null(bar.Resolve(0, inv));
	}

	[Fact]
	public void ChangedFiresForBindAndClear()
	{
		var bar = new QuickUseBar();
		var seen = 0;
		bar.Changed += _ => seen++;
		bar.Bind(1, "x");
		bar.Clear(1);
		bar.Clear(1);
		Assert.Equal(2, seen);
	}
}
