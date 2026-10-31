using System;

namespace ProjectD;

/// <summary>Army crawl numbers (animation spec s1.7). Placeholders to tune.</summary>
public sealed record CrawlTuning(
	float RollDeg = 3.5f,
	float PitchDeg = 1.5f,
	/// <summary>Metres crawled per stride (one full camera roll cycle).</summary>
	float StrideM = 0.9f,
	/// <summary>Moving slower than this counts as stopped, m/s.</summary>
	float MovingSpeed = 0.08f,
	/// <summary>After stopping, how long the second hand takes to come back to the weapon.</summary>
	float RegripS = 0.4f,
	/// <summary>How fast the one-handed pose comes on when the crawl starts.</summary>
	float GrabS = 0.15f,
	float HipSpreadMult = 3f,
	float RecoilMult = 1.5f,
	/// <summary>Backward crawling is this share of the forward speed.</summary>
	float BackSpeedShare = 0.7f);

/// <summary>
/// The army crawl as the view and the weapon feel it: a camera roll and pitch in step with the stride, the weapon in
/// one hand while crawling (no ADS, wide hip spread, harder recoil), the second hand coming back after a stop. Pure
/// logic; the player feeds it the prone state and its speed.
/// </summary>
public sealed class CrawlModel
{
	private readonly CrawlTuning _t;
	private float _phase;
	private float _active;

	public CrawlModel(CrawlTuning? tuning = null) => _t = tuning ?? new CrawlTuning();

	public CrawlTuning Tuning => _t;

	/// <summary>0 hands on the weapon .. 1 weapon in one hand.</summary>
	public float OneHanded { get; private set; }

	/// <summary>True while crawling: the body is moving along the ground.</summary>
	public bool Moving { get; private set; }

	/// <summary>Camera roll in degrees for this stride, scaled by how far the crawl has built up.</summary>
	public float RollDeg => MathF.Sin(MathF.Tau * _phase) * _t.RollDeg * _active;

	/// <summary>Camera pitch in degrees, two bobs per stride (each elbow pulls).</summary>
	public float PitchDeg => MathF.Sin(2f * MathF.Tau * _phase) * _t.PitchDeg * _active;

	/// <summary>No sight picture while the weapon is in one hand, and until the second hand is back.</summary>
	public bool BlocksAds => OneHanded > 0.01f;

	public float HipSpreadMult => 1f + (_t.HipSpreadMult - 1f) * OneHanded;

	public float RecoilMult => 1f + (_t.RecoilMult - 1f) * OneHanded;

	public void Update(float delta, bool prone, float groundSpeed)
	{
		Moving = prone && groundSpeed > _t.MovingSpeed;
		if (Moving)
		{
			_phase = (_phase + groundSpeed * delta / Math.Max(0.1f, _t.StrideM)) % 1f;
			_active = Math.Min(1f, _active + delta / 0.2f);
			OneHanded = Math.Min(1f, OneHanded + delta / Math.Max(0.01f, _t.GrabS));
		}
		else
		{
			_active = Math.Max(0f, _active - delta / 0.2f);
			OneHanded = Math.Max(0f, OneHanded - delta / Math.Max(0.01f, _t.RegripS));
			if (!prone)
			{
				_phase = 0f;
				OneHanded = 0f;
			}
		}
	}
}

/// <summary>Prone lean numbers: the roll onto the shoulder and hip (animation spec s1.7). Placeholders to tune.</summary>
public sealed record ProneLeanTuning(
	float MaxRollDeg = 40f,
	float ShiftM = 0.25f,
	/// <summary>The weapon rolls further than the camera: nearly horizontal at full lean.</summary>
	float CantDeg = 75f,
	/// <summary>A lean past this blocks ADS (no normal eye relief on the side).</summary>
	float AdsBlockAt = 0.15f);

/// <summary>Maps the lean amount (-1 left .. 1 right) to the prone lean roll, shift and weapon cant. Pure.</summary>
public static class ProneLean
{
	public static float CameraRollDeg(ProneLeanTuning t, float lean) => t.MaxRollDeg * Math.Clamp(lean, -1f, 1f);

	public static float ShiftM(ProneLeanTuning t, float lean) => t.ShiftM * Math.Clamp(lean, -1f, 1f);

	/// <summary>Roll of the weapon beyond the camera roll, degrees (same sign as the lean).</summary>
	public static float ExtraCantDeg(ProneLeanTuning t, float lean) => (t.CantDeg - t.MaxRollDeg) * Math.Clamp(lean, -1f, 1f);

	public static bool BlocksAds(ProneLeanTuning t, float lean) => Math.Abs(lean) > t.AdsBlockAt;
}
