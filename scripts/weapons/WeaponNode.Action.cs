using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// The weapon side of weapon actions (animation spec s1.3): the points the hands follow (they leave the grips during
/// reloads), the magazine shown in the support hand, and the authored reload clips of the weapon when it has any.
/// Which phase is playing comes from the controller.
/// </summary>
public partial class WeaponNode
{
	private Node3D? _heldMag;
	private string _heldMagId = "";
	private AnimationPlayer? _libraryPlayer;
	private bool _libraryLoaded;
	private Animation? _debugClip;
	private readonly Dictionary<string, ClipSampler> _samplers = new();

	/// <summary>The support hand target. Sits on <see cref="GripLeft"/> while idle; the player moves it during actions.</summary>
	public Marker3D LeftHandAction { get; private set; } = null!;

	/// <summary>The firing hand target. Sits on <see cref="GripRight"/> unless an authored clip moves it.</summary>
	public Marker3D RightHandAction { get; private set; } = null!;

	/// <summary>Where the weapon model has its magazine well and charging handle (placeholders get a guess), weapon space.</summary>
	public Vector3 MagWellPoint => WeaponMarker("MagWell");
	public Vector3 ChargingPoint => WeaponMarker("ChargingHandle");

	private void CreateActionNodes()
	{
		LeftHandAction = new Marker3D { Name = "LeftHandAction", Position = GripLeft.Position, Rotation = GripLeft.Rotation };
		RightHandAction = new Marker3D { Name = "RightHandAction", Position = GripRight.Position, Rotation = GripRight.Rotation };
		AddChild(LeftHandAction);
		AddChild(RightHandAction);
	}

	// --- authored reload clips ------------------------------------------------------------------

	/// <summary>Dev: plays this clip for every reload instead of the weapon clips (pipeline check without a model).</summary>
	public void DebugSetReloadClip(Animation? clip)
	{
		_debugClip = clip;
		_samplers.Clear();
	}

	/// <summary>The sampler of the weapon authored reload clip, or null when it has none (the procedural reload plays).</summary>
	public ClipSampler? ReloadClip(bool fast)
	{
		var name = fast ? Data.AnimSet?.FastReloadClip ?? "Reload_Fast" : Data.AnimSet?.TacticalReloadClip ?? "Reload_Tactical";
		var key = (_debugClip is not null ? "debug" : "") + name;
		if (_samplers.TryGetValue(key, out var cached))
			return cached;
		var clip = _debugClip ?? FindClip(name);
		var sampler = clip is not null && new ClipSampler(clip) is { HasAnyTrack: true } s ? s : null;
		if (sampler is not null)
			_samplers[key] = sampler;
		return sampler;
	}

	private Animation? FindClip(string name)
	{
		if (name == "")
			return null;
		if (_model?.FindChild("AnimationPlayer", true, false) is AnimationPlayer own && own.HasAnimation(name))
			return own.GetAnimation(name);
		if (!_libraryLoaded)
		{
			_libraryLoaded = true;
			if (Data.AnimSet is { LibraryPath: { Length: > 0 } path } && LoadLibrary(path) is { } library)
				_libraryPlayer = library;
		}
		return _libraryPlayer is not null && _libraryPlayer.HasAnimation(name) ? _libraryPlayer.GetAnimation(name) : null;
	}

	private AnimationPlayer? LoadLibrary(string path)
	{
		if (!ResourceLoader.Exists(path) || GD.Load<PackedScene>(path) is not { } scene)
			return null;
		var root = scene.Instantiate<Node3D>();
		var player = root.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
		if (player is null)
			root.Free();
		else
			AddChild(root); // keeps the player (and its animations) alive; the library has no visible meshes of its own
		return player;
	}

	/// <summary>
	/// Where the events of the clip fall: the clip markers of that name, the AnimationSet dictionary, and the usual
	/// proportions for anything else.
	/// </summary>
	public ClipEvents ReloadClipEvents(ActionTuning tuning, ClipSampler clip, bool fast, bool needsRack)
	{
		var named = new Dictionary<ReloadEvent, float>();
		var listed = fast ? Data.AnimSet?.FastEvents : Data.AnimSet?.TacticalEvents;
		if (listed is not null)
		{
			foreach (var (key, time) in listed)
			{
				if (System.Enum.TryParse<ReloadEvent>(key, out var e))
					named[e] = time;
			}
		}
		return ReloadClipWarp.WithNominalDefaults(new ClipEvents(clip.Length, named), tuning, fast, needsRack);
	}

	// --- the magazine in the hand ---------------------------------------------------------------

	/// <summary>Keeps a magazine model in the support hand, matching the current magazine item of the weapon.</summary>
	private void EnsureHeldMagazine()
	{
		var id = Item?.InsertedMagazine?.Spec.Id ?? ItemDb.MagazineFor(Data.Ammo?.Caliber ?? "")?.Id ?? "";
		if (id == _heldMagId && _heldMag is not null)
			return;
		_heldMag?.QueueFree();
		_heldMag = null;
		_heldMagId = id;
		if (id == "" || LoadModel(id, "parts") is not { } model)
			return;
		_heldMag = new Node3D { Name = "HeldMagazine", Visible = false };
		model.Position = new Vector3(0f, 0.09f, 0f); // the hand holds the body, the top (Attach) points up
		_heldMag.AddChild(model);
		LeftHandAction.AddChild(_heldMag);
	}

	/// <summary>Shows the magazine in the hand and hides the one in the weapon while the reload has it out.</summary>
	public void SetReloadVisual(ReloadPhase phase, float progress)
	{
		var inHand = phase switch
		{
			ReloadPhase.MagDetach => progress > 0.5f,
			ReloadPhase.MagStow or ReloadPhase.MagFetch or ReloadPhase.MagInsert => true,
			_ => false,
		};
		if (_heldMag is not null)
			_heldMag.Visible = inHand;
		if (_magModel is not null)
			_magModel.Visible = !inHand;
	}
}
