using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>Placeholder tuning for Ewigstein exposure (M8e spec s2). All rates are per second.</summary>
public sealed record ExposureTuning(
	float ShardRate = 1.5f,
	float DepositRadiusM = 12f,
	float DepositMaxRate = 3f,
	float DecayRate = 0.5f,
	float SwayStart = 25f,
	float SwayMultiplierStart = 1.2f,
	float SwayMultiplierMax = 1.5f,
	float DistortionStart = 50f,
	float DamageStart = 75f,
	float DamagePerS = 0.3f,
	float Max = 100f);

/// <summary>
/// The exposure meter of a raid (M8e spec s2): 0 to 100, rises near deposits and with unshielded shards carried, decays
/// when no source applies. Never saved; a new raid starts at 0. Pure logic.
/// </summary>
public sealed class ExposureModel
{
	private readonly ExposureTuning _t;

	public ExposureModel(ExposureTuning? tuning = null) => _t = tuning ?? new ExposureTuning();

	public float Level { get; private set; }

	/// <summary>Gain per second from sources at the last update (0 when none); the hum follows it.</summary>
	public float SourceRate { get; private set; }

	/// <summary>Gain per second from one deposit at a distance in metres: full at the deposit, none at the radius.</summary>
	public float DepositRate(float distanceM)
		=> distanceM >= _t.DepositRadiusM ? 0f : _t.DepositMaxRate * (1f - Math.Max(0f, distanceM) / _t.DepositRadiusM);

	public void Update(float delta, int unshieldedShards, IEnumerable<float> depositDistancesM)
	{
		SourceRate = unshieldedShards * _t.ShardRate + depositDistancesM.Sum(DepositRate);
		Level = Math.Clamp(Level + (SourceRate > 0f ? SourceRate : -_t.DecayRate) * delta, 0f, _t.Max);
	}

	public void Reset()
	{
		Level = 0f;
		SourceRate = 0f;
	}

	/// <summary>Weapon sway multiplier: 1 below the start, 1.2 at 25 rising to 1.5 at 100.</summary>
	public float SwayMultiplier => Level < _t.SwayStart
		? 1f
		: _t.SwayMultiplierStart + (_t.SwayMultiplierMax - _t.SwayMultiplierStart) * (Level - _t.SwayStart) / (_t.Max - _t.SwayStart);

	/// <summary>Vision distortion and radio static from 50.</summary>
	public bool Distorted => Level >= _t.DistortionStart;

	/// <summary>HP lost per second from 75 (split between thorax and stomach by the caller); never an instant death.</summary>
	public float HpLossPerS => Level >= _t.DamageStart ? _t.DamagePerS : 0f;
}

/// <summary>Shielding rule of M8e: only shards outside a canister expose their carrier.</summary>
public static class EwigsteinRules
{
	/// <summary>Shards in pockets, vest, rig and backpack that are not inside a canister.</summary>
	public static int UnshieldedShards(CharacterInventory character)
		=> character.StorageGrids.Sum(g => g.Items.Sum(CountUnshielded));

	private static int CountUnshielded(ItemInstance item)
		=> item.Spec.Category switch
		{
			ItemCategory.Ewigstein => item.Count,
			ItemCategory.Canister => 0,
			_ => item.Grids.Sum(g => g.Items.Sum(CountUnshielded)),
		};

	/// <summary>
	/// Stores a harvested shard (M8e spec s3): into a free canister slot if the player carries one, else into the carried
	/// grids unshielded. False when there is no room anywhere.
	/// </summary>
	public static bool StoreShard(CharacterInventory character, ItemInstance shard)
		=> character.StorageGrids.SelectMany(g => g.Items).Where(i => i.Spec.Category == ItemCategory.Canister)
			.SelectMany(c => c.Grids).Any(g => g.TryPlaceAnywhere(shard, false))
			|| character.TryAutoStore(shard);
}
