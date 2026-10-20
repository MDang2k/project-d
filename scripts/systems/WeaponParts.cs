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
public sealed record WeaponSlotSpec(
	string Id,
	string Type,
	IReadOnlyList<string> Accepts,
	bool Required = false,
	bool Accessory = false,
	string Marker = "");

/// <summary>The modding side of a weapon item: its slot tree, default parts and base ergonomics.</summary>
public sealed record WeaponBuildSpec(
	IReadOnlyList<WeaponSlotSpec> Slots,
	IReadOnlyDictionary<string, string> DefaultParts,
	/// <summary>0-100 before parts; final ergonomics scales ADS time, sway and arm-stamina drain.</summary>
	float Ergonomics = 50f,
	/// <summary>Integrated bipod (MG); clamp-on bipods are parts.</summary>
	bool IntegratedBipod = false);

/// <summary>What a part is and what it does. Percent modifiers are fractions (0.05 = +5%) and stack additively.</summary>
public sealed record PartSpec(
	/// <summary>Interface tags this part fits (any one matches an accepting slot).</summary>
	IReadOnlyList<string> Interfaces,
	IReadOnlyList<WeaponSlotSpec> AddsSlots,
	/// <summary>Slot ids this part makes unusable while attached.</summary>
	IReadOnlyList<string> Blocks,
	float RecoilPct = 0f,
	float ErgonomicsPts = 0f,
	float SwayPct = 0f,
	float DispersionPct = 0f,
	float VelocityPct = 0f,
	float NoisePct = 0f,
	/// <summary>Optics: magnification, 0 for none.</summary>
	float Zoom = 0f,
	int GridWidthAdd = 0,
	/// <summary>A clamp-on bipod: enables the bipod logic while attached.</summary>
	bool Bipod = false,
	/// <summary>A weapon light (arrives with M9 flashlights).</summary>
	bool Light = false);

/// <summary>Final handling numbers of a weapon with its parts (feeds recoil, sway, ADS, noise, zoom).</summary>
public sealed record WeaponStats(
	float RecoilMult,
	float Ergonomics,
	float SwayMult,
	float DispersionMult,
	float VelocityMult,
	float NoiseMult,
	float Zoom,
	float WeightKg,
	int Value,
	int GridWidth,
	bool HasBipod,
	bool HasLight);

/// <summary>How final ergonomics scales handling, normalised so ergonomics 50 changes nothing (tune).</summary>
public sealed record ErgonomicsTuning(float AdsTimeSwing = 0.3f, float SwaySwing = 0.2f, float StaminaSwing = 0.3f)
{
	public static ErgonomicsTuning Default { get; } = new();

	private static float Scale(float ergonomics, float swing) => 1f + (50f - Math.Clamp(ergonomics, 0f, 100f)) / 50f * swing;

	public float AdsTimeMult(float ergonomics) => Scale(ergonomics, AdsTimeSwing);
	public float SwayMult(float ergonomics) => Scale(ergonomics, SwaySwing);
	public float StaminaDrainMult(float ergonomics) => Scale(ergonomics, StaminaSwing);
}
