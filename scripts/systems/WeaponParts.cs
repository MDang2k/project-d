using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>
/// One attachment point on a weapon or on a part (GDD s11, weapon modding spec). A part fits when one of
/// its interface tags is in <see cref="Accepts"/>.
/// </summary>
/// <param name="Id">Unique within one weapon's whole tree (weapon and attached parts together).</param>
/// <param name="Type">optic, mount, muzzle, underbarrel, light, stock, barrel, handguard, grip, receiver_cover, trigger.</param>
/// <param name="Required">A weapon missing a required part cannot be equipped or fired.</param>
/// <param name="Accessory">Can be swapped inside a raid; structural slots are hub-only.</param>
/// <param name="Marker">Model marker the part is attached at (Muzzle, OpticMount, BipodMount, ...).</param>
/// <param name="Rail">Rail surface the part sits on (v2 clearance); empty when the slot is not on a rail.</param>
/// <param name="RailStart">First 10 mm step of the part on that rail.</param>
public sealed record WeaponSlotSpec(
	string Id,
	string Type,
	IReadOnlyList<string> Accepts,
	bool Required = false,
	bool Accessory = false,
	string Marker = "",
	string Rail = "",
	int RailStart = 0);

/// <summary>Weapon base ratings, 0-100 and positive (weapon modding v2 s4).</summary>
public sealed record WeaponRatings(
	float VerticalRecoil = 50f,
	float HorizontalRecoil = 50f,
	float Stability = 50f,
	float HipStability = 50f);

/// <summary>The modding side of a weapon item: its slot tree, default parts and base ergonomics.</summary>
public sealed record WeaponBuildSpec(
	IReadOnlyList<WeaponSlotSpec> Slots,
	IReadOnlyDictionary<string, string> DefaultParts,
	/// <summary>0-100 before parts; final ergonomics scales ADS time, sway and arm-stamina drain.</summary>
	float Ergonomics = 50f,
	/// <summary>Integrated bipod (MG); clamp-on bipods are parts.</summary>
	bool IntegratedBipod = false,
	WeaponRatings? Ratings = null,
	/// <summary>Rail surfaces of the receiver: id to length in 10 mm steps.</summary>
	IReadOnlyDictionary<string, int>? Rails = null);

/// <summary>What a part is and what it does. Rating points add to the weapon's 0-100 ratings; percent modifiers are fractions.</summary>
public sealed record PartSpec(
	/// <summary>Interface tags this part fits (any one matches an accepting slot).</summary>
	IReadOnlyList<string> Interfaces,
	IReadOnlyList<WeaponSlotSpec> AddsSlots,
	/// <summary>Slot ids this part makes unusable while attached.</summary>
	IReadOnlyList<string> Blocks,
	float VerticalRecoilPts = 0f,
	float HorizontalRecoilPts = 0f,
	float StabilityPts = 0f,
	float HipStabilityPts = 0f,
	float ErgonomicsPts = 0f,
	float VelocityPct = 0f,
	float NoisePct = 0f,
	/// <summary>Optics: magnification, 0 for none.</summary>
	float Zoom = 0f,
	int GridWidthAdd = 0,
	/// <summary>A clamp-on bipod: enables the bipod logic while attached.</summary>
	bool Bipod = false,
	/// <summary>A weapon light (arrives with M9 flashlights).</summary>
	bool Light = false,
	/// <summary>Length on a rail, in 10 mm steps (0 = not rail-bound).</summary>
	int Steps = 0,
	/// <summary>Named zones this part occupies (sight_line, fore_barrel...); two parts in one zone conflict.</summary>
	IReadOnlyList<string>? Zones = null,
	/// <summary>Rail surfaces this part provides (a handguard): id to length in steps.</summary>
	IReadOnlyDictionary<string, int>? Rails = null,
	/// <summary>Whether and how the part can be swapped in a raid (weapon modding v2 s7).</summary>
	SwapKind Swap = SwapKind.Tooled);

/// <summary>
/// Final ratings and handling numbers of a weapon with its parts. Ratings are 0-100; the multipliers come
/// from <see cref="RatingCurves"/>.
/// </summary>
public sealed record WeaponStats(
	float VerticalRecoil,
	float HorizontalRecoil,
	float Stability,
	float HipStability,
	float Ergonomics,
	float VelocityMult,
	float NoiseMult,
	float Zoom,
	float WeightKg,
	int Value,
	int GridWidth,
	bool HasBipod,
	bool HasLight)
{
	public float VerticalRecoilMult => RatingCurves.RecoilMult(VerticalRecoil);
	public float HorizontalRecoilMult => RatingCurves.RecoilMult(HorizontalRecoil);
	public float SwayMult => RatingCurves.SwayMult(Stability);
	public float DispersionMult => RatingCurves.SwayMult(HipStability);
}

/// <summary>
/// Ratings to handling numbers (tune). Rating 50 is neutral (x1); the recoil curve never drops below
/// <see cref="RecoilFloor"/> of base kick, even at 100.
/// </summary>
public static class RatingCurves
{
	public const float RecoilFloor = 0.25f;

	private static float Curve(float rating, float atZero, float atFifty, float atHundred)
	{
		var r = Math.Clamp(rating, 0f, 100f);
		return r <= 50f
			? atZero + (atFifty - atZero) * (r / 50f)
			: atFifty + (atHundred - atFifty) * ((r - 50f) / 50f);
	}

	public static float RecoilMult(float rating) => Curve(rating, 1.75f, 1f, RecoilFloor);
	public static float SwayMult(float rating) => Curve(rating, 1.5f, 1f, 0.4f);
}

/// <summary>How final ergonomics scales handling, normalised so ergonomics 50 changes nothing (tune).</summary>
public sealed record ErgonomicsTuning(float AdsTimeSwing = 0.3f, float SwaySwing = 0.2f, float StaminaSwing = 0.3f)
{
	public static ErgonomicsTuning Default { get; } = new();

	private static float Scale(float ergonomics, float swing) => 1f + (50f - Math.Clamp(ergonomics, 0f, 100f)) / 50f * swing;

	public float AdsTimeMult(float ergonomics) => Scale(ergonomics, AdsTimeSwing);
	public float SwayMult(float ergonomics) => Scale(ergonomics, SwaySwing);
	public float StaminaDrainMult(float ergonomics) => Scale(ergonomics, StaminaSwing);
}
