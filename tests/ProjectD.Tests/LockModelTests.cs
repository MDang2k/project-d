using System.Collections.Generic;
using Xunit;

namespace ProjectD.Tests;

public class LockModelTests
{
	private static (LockModel Lock, HashSet<string> Carried, List<string> Consumed) Make(bool singleUse = true, params string[] carried)
	{
		var set = new HashSet<string>(carried);
		return (new LockModel("radio_room_key", singleUse), set, new List<string>());
	}

	private static UnlockResult Try(LockModel l, HashSet<string> carried, List<string> consumed)
		=> l.TryOpen(carried.Contains, id => { consumed.Add(id); return carried.Remove(id); });

	[Fact]
	public void WithoutTheKeyTheLockStaysShut()
	{
		var (l, carried, consumed) = Make(true, "bandage");
		Assert.Equal(UnlockResult.MissingKey, Try(l, carried, consumed));
		Assert.Equal(LockState.Locked, l.State);
		Assert.Empty(consumed);
	}

	[Fact]
	public void ASingleUseKeyOpensTheLockAndIsUsedUp()
	{
		var (l, carried, consumed) = Make(true, "radio_room_key");
		var opened = 0;
		l.Opened += () => opened++;
		Assert.Equal(UnlockResult.Opened, Try(l, carried, consumed));
		Assert.Equal(LockState.Open, l.State);
		Assert.Equal(new[] { "radio_room_key" }, consumed);
		Assert.DoesNotContain("radio_room_key", carried);
		Assert.Equal(1, opened);
	}

	[Fact]
	public void AnOpenLockOpensOnlyOnce()
	{
		var (l, carried, consumed) = Make(true, "radio_room_key", "radio_room_key_2");
		Try(l, carried, consumed);
		var opened = 0;
		l.Opened += () => opened++;
		Assert.Equal(UnlockResult.AlreadyOpen, Try(l, carried, consumed));
		Assert.Equal(0, opened);
		Assert.Single(consumed);
	}

	[Fact]
	public void AReusableKeyIsKept()
	{
		var (l, carried, consumed) = Make(false, "radio_room_key");
		Assert.Equal(UnlockResult.Opened, Try(l, carried, consumed));
		Assert.Contains("radio_room_key", carried);
		Assert.Empty(consumed);
	}

	[Fact]
	public void IfTheKeyCannotBeTakenTheLockStaysShut()
	{
		var l = new LockModel("k");
		Assert.Equal(UnlockResult.MissingKey, l.TryOpen(_ => true, _ => false));
		Assert.Equal(LockState.Locked, l.State);
	}
}
