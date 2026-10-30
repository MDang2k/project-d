using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectD;

/// <summary>Light a target stands in. Authored as zones on the map; there is no engine light sampling (AI combat v2 spec s2).</summary>
public enum LightLevel { Dark, Dim, Lit }

/// <summary>Environment rules for the AI senses. Pure logic; the AI node gathers the raycast results.</summary>
public static class PerceptionEnvironment
{
	/// <summary>Opacity of a foliage collider that does not say otherwise.</summary>
	public const float DefaultFoliageOpacity = 0.35f;

	/// <summary>Share of a body point still visible after the foliage between: 1 minus the summed opacities, never below 0.</summary>
	public static float FoliageVisibility(IEnumerable<float> opacities)
		=> 1f - Math.Min(1f, opacities.Sum(o => Math.Clamp(o, 0f, 1f)));

	/// <summary>Sight multiplier for a light level and the time of day (1 noon .. 0 midnight; nights come with M9).</summary>
	public static float LightFactor(LightLevel level, float timeOfDay = 1f)
		=> (level switch { LightLevel.Dark => 0.35f, LightLevel.Dim => 0.7f, _ => 1f }) * Math.Clamp(timeOfDay, 0.1f, 1f);

	// --- hearing ---------------------------------------------------------------------------------

	/// <summary>Loss in dB for one wall of a material tag; untagged or unknown walls count as concrete.</summary>
	public static float WallLossDb(string? material) => material?.ToLowerInvariant() switch
	{
		"brick" => 15f,
		"wood" => 8f,
		"drywall" => 6f,
		"glass" => 3f,
		"metal_sheet" or "metal" => 10f,
		"foliage" => 1f,
		_ => 18f,
	};

	/// <summary>Radius a noise reaches after the walls on its way: <c>radius x 10^(-loss/20)</c>.</summary>
	public static float EffectiveRadius(float radius, IEnumerable<string?> wallMaterials)
		=> radius * MathF.Pow(10f, -wallMaterials.Sum(WallLossDb) / 20f);

	// --- cues ------------------------------------------------------------------------------------

	/// <summary>
	/// How much a cue kind counts for a listener of this aggression (0 careful .. 1 bold): bold bots read a dry click
	/// or a reload as an opening and come for it, careful ones only get suspicious and hold their angle.
	/// </summary>
	public static float CueMultiplier(string kind, float aggression)
	{
		var (careful, bold) = kind switch
		{
			"dry_click" => (0.6f, 1.8f),
			"reload_empty" => (0.6f, 1.5f),
			"surgery_start" => (0.7f, 2.0f),
			"heal" => (0.8f, 1.3f),
			_ => (1f, 1f),
		};
		return careful + (bold - careful) * Math.Clamp(aggression, 0f, 1f);
	}

	/// <summary>Kinds that are tactical cues rather than plain sounds.</summary>
	public static bool IsCue(string kind) => kind is "dry_click" or "reload_empty" or "surgery_start" or "heal";

	/// <summary>Noise radius in metres each cue carries.</summary>
	public static float CueRadius(string kind) => kind switch
	{
		"dry_click" => 14f,
		"reload_empty" => 10f,
		"surgery_start" => 22f,
		"heal" => 9f,
		_ => 0f,
	};
}
