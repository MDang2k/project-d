using System;

namespace ProjectD;

/// <summary>When each HUD element is worth showing (GDD s18: minimal, auto-hiding). Pure logic.</summary>
public static class HudRules
{
	public const float StaminaShowBelow = 0.6f;
	public const float VitalsShowBelow = 0.4f;
	public const float TagShowBelow = 0.3f;

	/// <summary>Stamina is shown while it moves or is low.</summary>
	public static bool StaminaShown(float fraction, bool changing) => changing || fraction < StaminaShowBelow;

	/// <summary>Energy and hydration show when low or while eating and drinking.</summary>
	public static bool VitalsShown(float energy01, float hydration01, bool consuming)
		=> consuming || energy01 < VitalsShowBelow || hydration01 < VitalsShowBelow;

	/// <summary>The body silhouette shows only when something is wrong.</summary>
	public static bool HealthShown(float lowestLimb01, bool bleeding, bool fractured, bool pain)
		=> bleeding || fractured || pain || lowestLimb01 < 0.99f;
}

/// <summary>Fades an element in at once and out after a hold time. Pure logic.</summary>
public sealed class HudFade
{
	private readonly float _holdS;
	private readonly float _fadeInS;
	private readonly float _fadeOutS;
	private float _sinceWanted = float.MaxValue;

	public HudFade(float holdS = 1.5f, float fadeInS = 0.15f, float fadeOutS = 0.5f)
	{
		_holdS = holdS;
		_fadeInS = fadeInS;
		_fadeOutS = fadeOutS;
	}

	public float Alpha { get; private set; }

	/// <param name="wanted">The element should be visible this frame; hides again after the hold time.</param>
	public void Update(float delta, bool wanted)
	{
		_sinceWanted = wanted ? 0f : _sinceWanted + delta;
		if (_sinceWanted <= _holdS)
			Alpha = Math.Min(1f, Alpha + delta / Math.Max(0.01f, _fadeInS));
		else
			Alpha = Math.Max(0f, Alpha - delta / Math.Max(0.01f, _fadeOutS));
	}

	/// <summary>Shows it now (event-driven elements such as the stance icon).</summary>
	public void Pulse() => _sinceWanted = 0f;
}
