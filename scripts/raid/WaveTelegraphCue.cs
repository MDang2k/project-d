using Godot;

namespace ProjectD;

/// <summary>
/// The warning before a wave (Border Strip spec s6), played at the wave's entry point from the moment it is telegraphed
/// until it arrives. PMC patrol: truck engines and radio chatter. Bandits: shouting and a red flare over the entry point.
/// There are no audio assets yet, so the sounds are generated in code (rough placeholders). The sound is positional
/// (AudioStreamPlayer3D), so it carries the direction. Dev: <c>--waveclock=secs</c> skips the wave clock ahead to reach a telegraph.
/// </summary>
public partial class WaveTelegraphCue : Node3D
{
	private const int MixRate = 22050;

	private AudioStreamPlayer3D _audio = null!;
	private readonly RandomNumberGenerator _rng = new();
	private bool _bandit;
	private float _timeLeft;
	private float _t;
	private float _burstLeft;
	private float _burstTone;
	private float _burstLength;
	private float _phase;
	private float _enginePhase;
	private float _noiseLow;

	public void Start(bool bandit, float durationS)
	{
		_bandit = bandit;
		_timeLeft = durationS;
		_audio = new AudioStreamPlayer3D
		{
			Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.3f },
			MaxDistance = 220f,
			UnitSize = 25f,
			Autoplay = true,
		};
		AddChild(_audio);
		if (bandit)
		{
			AddChild(new OmniLight3D { LightColor = new Color(1f, 0.25f, 0.2f), LightEnergy = 6f, OmniRange = 60f, Position = new Vector3(0f, 28f, 0f) });
			AddChild(new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = 0.4f, Height = 0.8f },
				Position = new Vector3(0f, 28f, 0f),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.4f, 0.3f) },
			});
		}
	}

	public override void _Process(double delta)
	{
		_timeLeft -= (float)delta;
		if (_timeLeft <= 0f)
		{
			QueueFree();
			return;
		}
		if (_audio.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback)
			return;
		var frames = playback.GetFramesAvailable();
		var fade = Mathf.Clamp(_timeLeft / 2f, 0f, 1f) * Mathf.Clamp(_t / 1.5f, 0f, 1f);
		for (var i = 0; i < frames; i++)
		{
			var dt = 1f / MixRate;
			_t += dt;
			var s = _bandit ? Shout(dt) : Engine3(dt) + Radio(dt);
			s *= fade;
			playback.PushFrame(new Vector2(s, s));
		}
	}

	/// <summary>A truck: a low saw wobbling at about 7 Hz plus rumble noise.</summary>
	private float Engine3(float dt)
	{
		_enginePhase = (_enginePhase + 42f * dt) % 1f;
		var saw = _enginePhase * 2f - 1f;
		_noiseLow += (_rng.RandfRange(-1f, 1f) - _noiseLow) * 0.04f;
		var wobble = 0.6f + 0.4f * Mathf.Sin(Mathf.Tau * 7f * _t);
		return (saw * 0.16f + _noiseLow * 0.5f) * wobble;
	}

	/// <summary>Radio chatter: short bursts of band-limited noise with a tone, every second or two.</summary>
	private float Radio(float dt)
	{
		if ((_burstLeft -= dt) <= 0f)
		{
			_burstLeft = _rng.RandfRange(0.8f, 2.2f);
			_burstLength = _rng.RandfRange(0.25f, 0.6f);
			_burstTone = _rng.RandfRange(900f, 1500f);
		}
		if (_burstLeft > _burstLength)
			return 0f;
		_phase = (_phase + _burstTone * dt) % 1f;
		var gate = Mathf.Sin(Mathf.Tau * 14f * _t) > 0f ? 1f : 0.3f; // choppy, like speech through a handset
		return (Mathf.Sin(Mathf.Tau * _phase) * 0.08f + _rng.RandfRange(-1f, 1f) * 0.07f) * gate;
	}

	/// <summary>Shouting: buzzy bursts with a falling pitch, every one to two and a half seconds.</summary>
	private float Shout(float dt)
	{
		if ((_burstLeft -= dt) <= 0f)
		{
			_burstLeft = _rng.RandfRange(1f, 2.5f);
			_burstLength = _rng.RandfRange(0.3f, 0.7f);
			_burstTone = _rng.RandfRange(170f, 300f);
		}
		if (_burstLeft > _burstLength)
			return 0f;
		var progress = 1f - _burstLeft / _burstLength;
		_phase = (_phase + _burstTone * (1.1f - 0.35f * progress) * dt) % 1f;
		var saw = _phase * 2f - 1f;
		var envelope = Mathf.Sin(Mathf.Pi * progress);
		return (saw * 0.14f + _rng.RandfRange(-1f, 1f) * 0.03f) * envelope;
	}
}
