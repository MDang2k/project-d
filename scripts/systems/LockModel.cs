using System;

namespace ProjectD;

public enum LockState { Locked, Open }

/// <summary>Result of trying a key on a lock.</summary>
public enum UnlockResult { Opened, MissingKey, AlreadyOpen }

/// <summary>
/// A locked door or container (GDD s8f): opened with a key item, which a single-use key loses when it works. Pure logic;
/// <c>LockedDoor</c> is the node around it. Code locks (8d) will come with the knowledge system.
/// </summary>
public sealed class LockModel
{
	public LockModel(string keyId, bool singleUse = true)
	{
		KeyId = keyId;
		SingleUse = singleUse;
	}

	public string KeyId { get; }
	public bool SingleUse { get; }
	public LockState State { get; private set; } = LockState.Locked;

	public event Action? Opened;

	/// <summary>
	/// Tries the key. <paramref name="consumeKey"/> takes the key item away and says whether it did; it is only called
	/// for a single-use key, and if it fails the lock stays shut.
	/// </summary>
	public UnlockResult TryOpen(Func<string, bool> hasKey, Func<string, bool> consumeKey)
	{
		if (State == LockState.Open)
			return UnlockResult.AlreadyOpen;
		if (!hasKey(KeyId) || (SingleUse && !consumeKey(KeyId)))
			return UnlockResult.MissingKey;
		State = LockState.Open;
		Opened?.Invoke();
		return UnlockResult.Opened;
	}
}
