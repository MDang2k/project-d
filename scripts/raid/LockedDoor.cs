using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// A door that stays shut until the player uses its key (raid map standard s5 lock marker, GDD s8f). Aim at it and press
/// the interact key: with the key in storage it is used up and the door slides up out of the way. The box shape and mesh
/// are children in the scene. Dev: <c>--givekey=id</c> adds that item, <c>--opendoor=id</c> tries the door after a second.
/// </summary>
public partial class LockedDoor : StaticBody3D
{
	/// <summary>Item id of the key.</summary>
	[Export] public string KeyId { get; set; } = "";
	/// <summary>Height of the door, which is how far it slides up when opened.</summary>
	[Export] public float OpenLift { get; set; } = 2.6f;
	[Export] public bool SingleUse { get; set; } = true;
	/// <summary>Name shown in the prompt, for example "radio room".</summary>
	[Export] public string TitleKey { get; set; } = "lock.door";

	private LockModel _lock = null!;

	public bool IsOpen => _lock.State == LockState.Open;

	public string Title => GameText.Get(TitleKey);

	public override void _Ready()
	{
		_lock = new LockModel(KeyId, SingleUse);
		_lock.Opened += OnOpened;
		if (Engine.IsEditorHint())
			return;
		AddToGroup("locked_doors");
		var args = OS.GetCmdlineUserArgs();
		if (args.Any(a => a.StartsWith("--opendoor=" + Name)))
			DevOpen(args);
	}

	/// <summary>Tries the player's key; the player gets the outcome as a message.</summary>
	public UnlockResult TryOpen(Player player)
	{
		var inv = player.Inventory.Inventory;
		var result = _lock.TryOpen(
			id => inv.AllItems().Any(i => i.Spec.Id == id),
			id => inv.AllItems().FirstOrDefault(i => i.Spec.Id == id) is { } key && inv.Remove(key));
		player.Inventory.NotifyChanged();
		var message = result switch
		{
			UnlockResult.Opened => GameText.Get("lock.opened", Title),
			UnlockResult.MissingKey => GameText.Get("lock.needs", GameText.ItemName(KeyId)),
			_ => null,
		};
		if (message is not null)
			player.NotifyMessage(message);
		GD.Print($"LOCK {Name}: {result}");
		return result;
	}

	private void OnOpened()
	{
		if (!IsInsideTree())
			return;
		var tween = CreateTween();
		tween.TweenProperty(this, "position:y", Position.Y + OpenLift, 0.9).SetTrans(Tween.TransitionType.Sine);
		tween.TweenCallback(Callable.From(() => CollisionLayer = 0));
	}

	private async void DevOpen(string[] args)
	{
		await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
		var player = GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault();
		if (player is null)
			return;
		if (args.FirstOrDefault(a => a.StartsWith("--givekey="))?["--givekey=".Length..] is { } id && ItemDb.Create(id, 1) is { } key)
			player.Inventory.Inventory.TryAutoStore(key);
		TryOpen(player);
		TryOpen(player); // a second try finds it open and says nothing
	}
}
