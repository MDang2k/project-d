using System;
using System.Collections.Generic;
using Godot;

namespace ProjectD;

/// <summary>
/// M0 renderer benchmark. Builds a compound-like test scene in code, flies a fixed camera
/// path and reports fps. Keys: N night/day, L extra lights, S shadows, Tab free-cam
/// (WASD + mouse), Space restart run, Esc quit.
/// </summary>
public partial class Benchmark : Node3D
{
	private const float Size = 120f;
	private const int BuildingCount = 90;
	private const int LightCount = 12;
	private const float RunSeconds = 30f;
	private const float PathRadius = 40f;
	private const float FreeSpeed = 12f;
	private const float MouseSens = 0.002f;

	private Camera3D _cam = null!;
	private DirectionalLight3D _sun = null!;
	private Godot.Environment _env = null!;
	private readonly List<Light3D> _lights = new();
	private Label _label = null!;
	private bool _night;
	private bool _free;
	private float _t;
	private readonly List<float> _frameMs = new();
	private string _result = "";

	public override void _Ready()
	{
		ApplyLowestSettings();
		BuildWorld();
		BuildUi();
		SetNight(false);
	}

	private void ApplyLowestSettings()
	{
		var vp = GetViewport();
		vp.Msaa3D = Viewport.Msaa.Disabled;
		vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
		vp.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
		vp.Scaling3DScale = 1f;
		RenderingServer.DirectionalShadowAtlasSetSize(2048, false);
		RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.Hard);
		RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.Hard);
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
	}

	private void BuildWorld()
	{
		var rng = new RandomNumberGenerator { Seed = 1970 };
		AddChild(new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = new Vector2(Size * 2f, Size * 2f) },
			MaterialOverride = Mat(new Color(0.25f, 0.3f, 0.2f)),
		});

		var boxMat = Mat(new Color(0.55f, 0.55f, 0.5f));
		for (var i = 0; i < BuildingCount; i++)
		{
			var b = new BoxMesh
			{
				Size = new Vector3(rng.RandfRange(4f, 14f), rng.RandfRange(3f, 12f), rng.RandfRange(4f, 14f)),
			};
			AddChild(new MeshInstance3D
			{
				Mesh = b,
				MaterialOverride = boxMat,
				Position = new Vector3(rng.RandfRange(-Size, Size), b.Size.Y * 0.5f, rng.RandfRange(-Size, Size)),
			});
		}

		_sun = new DirectionalLight3D
		{
			ShadowEnabled = true,
			DirectionalShadowMaxDistance = 80f,
			RotationDegrees = new Vector3(-50f, 30f, 0f),
		};
		AddChild(_sun);

		for (var i = 0; i < LightCount; i++)
		{
			var l = new OmniLight3D
			{
				OmniRange = 18f,
				LightEnergy = 3f,
				LightColor = new Color(1f, 0.8f, 0.5f),
				ShadowEnabled = true,
				Position = new Vector3(rng.RandfRange(-PathRadius, PathRadius), 4f, rng.RandfRange(-PathRadius, PathRadius)),
			};
			AddChild(l);
			_lights.Add(l);
		}

		_env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
		};
		AddChild(new WorldEnvironment { Environment = _env });

		_cam = new Camera3D { Current = true, Far = 300f };
		AddChild(_cam);
	}

	private void BuildUi()
	{
		var layer = new CanvasLayer();
		_label = new Label { Position = new Vector2(12f, 12f) };
		layer.AddChild(_label);
		AddChild(layer);
	}

	private static StandardMaterial3D Mat(Color c) => new() { AlbedoColor = c };

	private void SetNight(bool on)
	{
		_night = on;
		_sun.LightEnergy = on ? 0.05f : 1f;
		_sun.LightColor = on ? new Color(0.5f, 0.6f, 1f) : new Color(1f, 0.95f, 0.85f);
		_env.BackgroundColor = on ? new Color(0.01f, 0.015f, 0.03f) : new Color(0.55f, 0.7f, 0.9f);
		_env.AmbientLightColor = on ? new Color(0.03f, 0.04f, 0.08f) : new Color(0.6f, 0.65f, 0.7f);
		_env.AmbientLightEnergy = on ? 0.3f : 0.8f;
		foreach (var l in _lights)
			l.Visible = on;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventKey { Pressed: true, Echo: false } k:
				switch (k.PhysicalKeycode)
				{
					case Key.N:
						SetNight(!_night);
						break;
					case Key.L:
						foreach (var l in _lights)
							l.Visible = !l.Visible;
						break;
					case Key.S:
						_sun.ShadowEnabled = !_sun.ShadowEnabled;
						break;
					case Key.Tab:
						_free = !_free;
						Input.MouseMode = _free ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
						break;
					case Key.Space:
						Restart();
						break;
					case Key.Escape:
						GetTree().Quit();
						break;
				}
				break;
			case InputEventMouseMotion mm when _free:
				var rot = _cam.Rotation;
				rot.Y -= mm.Relative.X * MouseSens;
				rot.X = Mathf.Clamp(rot.X - mm.Relative.Y * MouseSens, -1.5f, 1.5f);
				_cam.Rotation = rot;
				break;
		}
	}

	private void Restart()
	{
		_t = 0f;
		_frameMs.Clear();
		_result = "";
	}

	public override void _Process(double deltaD)
	{
		var delta = (float)deltaD;
		if (_free)
		{
			MoveFree(delta);
		}
		else
		{
			FlyPath(delta);
			// Finish once per run: the first frame whose _t reaches RunSeconds.
			if (_result == "")
			{
				_frameMs.Add(delta * 1000f);
				if (_t >= RunSeconds)
					Finish();
			}
		}
		UpdateLabel();
	}

	private void FlyPath(float delta)
	{
		_t += delta;
		var a = _t * 0.15f;
		_cam.Position = new Vector3(Mathf.Cos(a) * PathRadius, 6f, Mathf.Sin(a) * PathRadius);
		_cam.LookAt(new Vector3(Mathf.Cos(a + 0.6f) * PathRadius * 0.5f, 2f, Mathf.Sin(a + 0.6f) * PathRadius * 0.5f));
	}

	private void MoveFree(float delta)
	{
		var dir = Vector3.Zero;
		var basis = _cam.GlobalBasis;
		if (Input.IsPhysicalKeyPressed(Key.W)) dir -= basis.Z;
		if (Input.IsPhysicalKeyPressed(Key.S)) dir += basis.Z;
		if (Input.IsPhysicalKeyPressed(Key.A)) dir -= basis.X;
		if (Input.IsPhysicalKeyPressed(Key.D)) dir += basis.X;
		_cam.Position += dir.Normalized() * FreeSpeed * delta;
	}

	private void Finish()
	{
		var sorted = _frameMs.ToArray();
		Array.Sort(sorted);
		var sum = 0f;
		foreach (var v in sorted)
			sum += v;
		var avgMs = sum / sorted.Length;
		var n = Math.Max(1, (int)(sorted.Length * 0.01f));
		var worst = 0f;
		for (var i = 0; i < n; i++)
			worst += sorted[sorted.Length - 1 - i];
		worst /= n;
		_result = $"RESULT {RenderingServer.GetVideoAdapterName()} | {GetWindow().Size} | night={_night} | " +
			$"avg {1000f / avgMs:0.0} fps | 1% low {1000f / worst:0.0} fps | max {sorted[^1]:0.0} ms";
		GD.Print(_result);
		using var f = FileAccess.Open("user://benchmark_results.txt", FileAccess.ModeFlags.ReadWrite)
			?? FileAccess.Open("user://benchmark_results.txt", FileAccess.ModeFlags.Write);
		f.SeekEnd();
		f.StoreLine(_result);
	}

	private void UpdateLabel()
	{
		var calls = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
		var tris = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
		var fps = Engine.GetFramesPerSecond();
		var run = _free ? "" : $"run {Mathf.Min(_t, RunSeconds):0}/{RunSeconds:0}s";
		_label.Text =
			$"{fps:0} fps  {1000.0 / Math.Max(1.0, fps):0.0} ms  calls {calls}  tris {tris}\n" +
			$"{RenderingServer.GetVideoAdapterName()}  {GetWindow().Size}\n" +
			$"night={_night} shadows={_sun.ShadowEnabled} {run}\n" +
			"[N]ight [L]ights [S]hadows [Tab]free-cam [Space]restart\n" + _result;
	}
}
