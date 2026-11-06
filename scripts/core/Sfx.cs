using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Plays sound events from data/audio/sfx.json (audio pass): a pool of 3D players for sounds in the world, a few 2D
/// players for UI and the player's own sounds, the SFX, Ambient and UI buses (created at first use, no project setting
/// changed), and a loader that caches the streams. Static on purpose: it is not an autoload; the pool node hangs under
/// the scene root the first time a sound plays. Picking and settings are the testable <see cref="SfxCatalog"/>.
/// </summary>
public static class Sfx
{
	private const string CatalogPath = "res://data/audio/sfx.json";
	private const string FolderPath = "res://assets/audio/sfx/";
	private const int Players3D = 24;
	private const int Players2D = 6;

	private static SfxCatalog? _catalog;
	private static readonly Dictionary<string, AudioStream?> Streams = new();
	private static readonly System.Random Rng = new();
	private static AudioStreamPlayer3D[]? _pool3D;
	private static AudioStreamPlayer[]? _pool2D;
	private static int _next3D;
	private static int _next2D;
	private static bool _busesReady;

	/// <summary>Dev (--sfxlog): print every sound that plays.</summary>
	private static readonly bool LogPlays = OS.GetCmdlineUserArgs().Contains("--sfxlog");

	public static SfxCatalog Catalog => _catalog ??= LoadCatalog();

	private static SfxCatalog LoadCatalog()
	{
		try
		{
			return SfxCatalog.Parse(FileAccess.GetFileAsString(CatalogPath));
		}
		catch (System.Exception e)
		{
			GD.PushWarning($"[Sfx] could not read {CatalogPath}: {e.Message}");
			return SfxCatalog.Parse("{\"events\":{}}");
		}
	}

	private static AudioStream? Load(string file)
	{
		if (!Streams.TryGetValue(file, out var stream))
		{
			stream = ResourceLoader.Exists(FolderPath + file + ".wav") ? GD.Load<AudioStream>(FolderPath + file + ".wav") : null;
			if (stream is null)
				GD.PushWarning($"[Sfx] missing sound file {file}");
			Streams[file] = stream;
		}
		return stream;
	}

	/// <summary>Creates the SFX, Ambient and UI buses under Master when they do not exist yet.</summary>
	public static void EnsureBuses()
	{
		if (_busesReady)
			return;
		foreach (var name in new[] { "SFX", "Ambient", "UI" })
		{
			if (AudioServer.GetBusIndex(name) >= 0)
				continue;
			AudioServer.AddBus();
			var index = AudioServer.BusCount - 1;
			AudioServer.SetBusName(index, name);
			AudioServer.SetBusSend(index, "Master");
		}
		_busesReady = true;
	}

	/// <summary>Volume of a bus as a 0 to 1 slider (Master, SFX, Ambient or UI).</summary>
	public static void SetBusVolume(string bus, float linear)
	{
		EnsureBuses();
		var index = AudioServer.GetBusIndex(bus);
		if (index >= 0)
			AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Clamp(linear, 0.0001f, 1f)));
	}

	public static float GetBusVolume(string bus)
	{
		EnsureBuses();
		var index = AudioServer.GetBusIndex(bus);
		return index >= 0 ? Mathf.DbToLinear(AudioServer.GetBusVolumeDb(index)) : 1f;
	}

	private static bool EnsurePool(Node context)
	{
		if (_pool3D is not null && GodotObject.IsInstanceValid(_pool3D[0]))
			return true;
		if (context.GetTree()?.Root is not { } root)
			return false;
		EnsureBuses();
		var holder = new Node { Name = "SfxPool" };
		_pool3D = new AudioStreamPlayer3D[Players3D];
		for (var i = 0; i < Players3D; i++)
		{
			_pool3D[i] = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance };
			holder.AddChild(_pool3D[i]);
		}
		_pool2D = new AudioStreamPlayer[Players2D];
		for (var i = 0; i < Players2D; i++)
		{
			_pool2D[i] = new AudioStreamPlayer();
			holder.AddChild(_pool2D[i]);
		}
		root.CallDeferred(Node.MethodName.AddChild, holder);
		return true;
	}

	/// <summary>Plays an event in the world at a point; the oldest sound is cut when all 24 players are busy.</summary>
	public static void Play3D(Node context, string id, Vector3 position, float dbOffset = 0f)
	{
		if (Catalog.Event(id) is not { } e || Catalog.Pick(id, Rng) is not { } pick || Load(pick.File) is not { } stream || !EnsurePool(context))
			return;
		var player = _pool3D![_next3D];
		_next3D = (_next3D + 1) % Players3D;
		if (!player.IsInsideTree())
			return; // the pool joins the tree a frame after its first use
		if (LogPlays)
			GD.Print($"SFX {id} -> {pick.File} at {position.X:0},{position.Z:0}");
		player.Stream = stream;
		player.GlobalPosition = position;
		player.VolumeDb = pick.VolumeDb + dbOffset;
		player.PitchScale = pick.PitchScale;
		player.MaxDistance = e.MaxDistanceM;
		player.UnitSize = e.UnitSizeM;
		player.Bus = e.Bus;
		player.Play();
	}

	/// <summary>Plays an event with no position (UI, the player's own sounds).</summary>
	public static void Play2D(Node context, string id, float dbOffset = 0f)
	{
		if (Catalog.Event(id) is not { } e || Catalog.Pick(id, Rng) is not { } pick || Load(pick.File) is not { } stream || !EnsurePool(context))
			return;
		var player = _pool2D![_next2D];
		_next2D = (_next2D + 1) % Players2D;
		if (!player.IsInsideTree())
			return;
		if (LogPlays)
			GD.Print($"SFX {id} -> {pick.File} (2D)");
		player.Stream = stream;
		player.VolumeDb = pick.VolumeDb + dbOffset;
		player.PitchScale = pick.PitchScale;
		player.Bus = e.Bus;
		player.Play();
	}

	/// <summary>A looping 2D sound (the wind bed) as its own player under <paramref name="parent"/>; null if the file is missing.</summary>
	public static AudioStreamPlayer? Loop2D(Node parent, string id)
	{
		if (Catalog.Event(id) is not { } e || Catalog.Pick(id, Rng) is not { } pick || Load(pick.File) is not AudioStreamWav wav)
			return null;
		EnsureBuses();
		var looped = (AudioStreamWav)wav.Duplicate();
		looped.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		looped.LoopBegin = 0;
		looped.LoopEnd = looped.Data.Length / 2; // 16-bit mono: samples
		var player = new AudioStreamPlayer { Stream = looped, VolumeDb = pick.VolumeDb, Bus = e.Bus, Autoplay = true };
		parent.AddChild(player);
		return player;
	}
}
