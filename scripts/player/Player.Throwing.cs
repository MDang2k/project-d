using System;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Throwing half of the player (throwables spec s1 and s2): a quick-use key puts a grenade in the hand and the gun
/// away; hold left mouse to pull the pin and throw overhand, hold right mouse for an underhand lob, press reload to cook
/// it; switching back to the gun puts the pin back (1.2 s). Pin out or spoon gone and you get hit or die: it drops live.
/// The fuse, speed and direction rules are the pure <see cref="GrenadeFuse"/> and <see cref="ThrowModel"/>.
/// Dev: <c>--throwgrenade=cookSeconds</c> throws a frag at the nearest bot from 7 m away; a negative number sets one off 2 m from it instead.
/// </summary>
public partial class Player
{
	private GrenadeFuse? _fuse;
	private GrenadeSpec? _grenadeSpec;
	private ItemInstance? _grenadeItem;
	private Node3D? _grenadeModel;
	private bool _winding;
	private bool _windUnderhand;
	private float _windS;
	private float _pinBackLeft;

	/// <summary>A grenade is in the hand (the gun is put away).</summary>
	public bool GrenadeInHand => _fuse is not null;

	/// <summary>Seconds the cooked grenade has left, or -1 when the spoon is still on.</summary>
	public float GrenadeFuseLeftS => _fuse is { Stage: GrenadeStage.Cooking } f ? f.RemainingS : -1f;

	/// <summary>Takes a grenade from the inventory into the hand. False when something else is going on.</summary>
	private bool TakeGrenade(ItemInstance item, GrenadeSpec spec)
	{
		if (Health.IsDead || _transition.BlocksWeaponActions || _pendingUse is not null)
			return false;
		if (GrenadeInHand)
		{
			TryPutGrenadeAway(); // the same key again puts it back
			return true;
		}
		Weapon?.Controller.Interrupt();
		_fuse = new GrenadeFuse(spec.FuseS, spec.FuseSpreadS, Random.Shared);
		_fuse.ExplodedInHand += OnGrenadeExplodedInHand;
		_grenadeSpec = spec;
		_grenadeItem = item;
		_winding = false;
		_pinBackLeft = 0f;
		if (Weapon is { } gun)
			gun.Visible = false;
		_grenadeModel = GrenadeModel.Build(item.Spec.Id);
		_grenadeModel.Position = new Vector3(0.2f, -0.16f, -0.42f);
		_swayNode.AddChild(_grenadeModel);
		WeaponMessage?.Invoke(GameText.Get("msg.grenade.ready"));
		return true;
	}

	/// <summary>Puts the grenade away. With the pin out it takes the pin-back time; with the spoon gone it cannot be done.</summary>
	public bool TryPutGrenadeAway()
	{
		if (_fuse is null)
			return true;
		if (!_fuse.CanPutAway)
		{
			WeaponMessage?.Invoke(GameText.Get("msg.grenade.cooked"));
			return false;
		}
		if (_fuse.Stage == GrenadeStage.PinOut)
		{
			if (_pinBackLeft <= 0f)
			{
				_pinBackLeft = _cfg.GrenadePinBackS;
				_winding = false;
				WeaponMessage?.Invoke(GameText.Get("msg.grenade.pinback"));
			}
			return false; // done when the timer runs out
		}
		ClearGrenadeHand();
		return true;
	}

	private void ClearGrenadeHand()
	{
		_grenadeModel?.QueueFree();
		_grenadeModel = null;
		_fuse = null;
		_grenadeSpec = null;
		_grenadeItem = null;
		_winding = false;
		_pinBackLeft = 0f;
		if (Weapon is { } gun)
			gun.Visible = true;
	}

	/// <summary>One physics step of the grenade in hand: fuse, pin back, pin and throw input.</summary>
	private void UpdateGrenadeInHand(float delta)
	{
		if (_fuse is null || _grenadeSpec is null)
			return;
		_fuse.Update(delta);
		if (_fuse is null)
			return; // it went off in the hand
		if (_pinBackLeft > 0f)
		{
			_pinBackLeft -= delta;
			if (_pinBackLeft <= 0f && _fuse.PutPinBack())
				ClearGrenadeHand();
			return;
		}
		if (Input.MouseMode != Input.MouseModeEnum.Captured || _screen.IsOpen || Health.IsDead)
			return;
		var firePressed = Input.IsActionJustPressed("fire");
		var aimPressed = Input.IsActionJustPressed("aim");
		if (_fuse.Stage == GrenadeStage.Safe && (firePressed || aimPressed) && _fuse.PullPin())
		{
			_winding = true;
			_windUnderhand = aimPressed && !firePressed;
			_windS = 0f;
			if (_grenadeSpec.Kind != GrenadeKind.Distraction)
			{
				NoiseBus.Emit(GlobalPosition, _cfg.GrenadePinNoiseM, "grenade_pin", this);
				Sfx.Play3D(this, "grenade_pin", GlobalPosition);
				WeaponMessage?.Invoke(GameText.Get("msg.grenade.pin"));
			}
			return;
		}
		if (!_winding)
			return;
		_windS += delta;
		if (Input.IsActionJustPressed("reload") && _grenadeSpec.Kind != GrenadeKind.Distraction && _fuse.Cook())
		{
			NoiseBus.Emit(GlobalPosition, _cfg.GrenadeSpoonNoiseM, "grenade_spoon", this);
			Sfx.Play3D(this, "grenade_spoon", GlobalPosition);
			WeaponMessage?.Invoke(GameText.Get("msg.grenade.cooking"));
		}
		var held = _windUnderhand ? Input.IsActionPressed("aim") : Input.IsActionPressed("fire");
		if (!held)
			ThrowGrenade();
	}

	private Vector3 GrenadeHandPoint() => _camera.GlobalPosition + (-_camera.GlobalBasis.Z) * 0.35f + Vector3.Down * 0.15f;

	private void ThrowGrenade()
	{
		if (_fuse is null || _grenadeSpec is not { } spec)
			return;
		var tuning = _cfg.ToThrowTuning();
		var speed = ThrowModel.Speed(Stance, _windUnderhand, ThrowModel.WindUpFraction(_windS, tuning),
			GameState.Instance?.Profile.SkillEffects.ThrowSpeedBonus ?? 0f, spec.ThrowSpeedMult, tuning);
		var dir = ThrowModel.Direction(-_camera.GlobalBasis.Z, _windUnderhand, tuning);
		var left = _fuse.Release();
		ReleaseGrenade(spec, left, dir * speed + Velocity * 0.5f);
		_skills.Threw();
		FinishThrownGrenade();
	}

	/// <summary>The thrown grenade is spent: one fewer in the pockets, and the gun comes back.</summary>
	private void FinishThrownGrenade()
	{
		if (_grenadeItem is { } item)
		{
			item.Count--;
			if (item.Count <= 0)
				Inventory.Inventory.Remove(item);
			Inventory.NotifyChanged();
		}
		ClearGrenadeHand();
	}

	/// <summary>Puts a live grenade into the world at the hand with a velocity (a throw, or a drop).</summary>
	private void ReleaseGrenade(GrenadeSpec spec, float fuseLeftS, Vector3 velocity)
	{
		var from = _camera.GlobalPosition;
		var at = GrenadeHandPoint();
		var query = PhysicsRayQueryParameters3D.Create(from, at, CollisionMask, new Godot.Collections.Array<Rid> { GetRid() });
		if (GetWorld3D().DirectSpaceState.IntersectRay(query) is { Count: > 0 } hit)
			at = hit["position"].AsVector3() - velocity.Normalized() * 0.1f; // a wall right in front: start before it
		var grenade = ThrownGrenade.Create(spec, Math.Max(0.05f, fuseLeftS), GetRid(), velocity, this);
		GetParent().AddChild(grenade);
		grenade.GlobalPosition = at;
		GD.Print($"GRENADE thrown {spec.Id} v={velocity.Length():0.0} m/s fuse left {fuseLeftS:0.0} s from {at.X:0},{at.Z:0}");
	}

	/// <summary>A cooked grenade ran out in the hand: it goes off where you stand.</summary>
	private void OnGrenadeExplodedInHand()
	{
		if (_grenadeSpec is not { } spec)
			return;
		GD.Print("GRENADE exploded in hand");
		GrenadeDetonation.Detonate(this, spec, GrenadeHandPoint(), GetRid());
		FinishThrownGrenade();
	}

	/// <summary>Dying, or losing the throwing arm, with the pin out: the grenade falls at your feet live (spec s2).</summary>
	private void DropLiveGrenade()
	{
		if (_fuse is null || _grenadeSpec is not { } spec || _fuse.Stage is GrenadeStage.Safe)
			return;
		var left = _fuse.Release();
		ReleaseGrenade(spec, left, Vector3.Down * 0.5f);
		FinishThrownGrenade();
	}

	private void HookGrenadeEvents()
	{
		// any hit after which the throwing arm is gone (a blast reports one combined hit on its heaviest part)
		Health.HitTaken += _ =>
		{
			if (Health.Body.IsDestroyed(BodyPart.RightArm))
				DropLiveGrenade();
		};
		Health.Died += _ => DropLiveGrenade();
	}

	/// <summary>Dev: <c>--grenadeid=egg_grenade</c> picks the grenade the dev throw uses (default frag_grenade).</summary>
	private static string DevGrenadeId() => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--grenadeid="))?["--grenadeid=".Length..] ?? "frag_grenade";

	private static void Freeze(AiHuman bot) => AiHuman.Paused = true;

	private async void StartGrenadeDev()
	{
		var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--throwgrenade="));
		if (arg is null || !float.TryParse(arg["--throwgrenade=".Length..], System.Globalization.CultureInfo.InvariantCulture, out var cookS))
			return;
		await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
		var bot = GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Where(a => a.IsAlive)
			.OrderBy(a => a.GlobalPosition.DistanceTo(GlobalPosition)).FirstOrDefault();
		if (bot is null || ItemDb.Create(DevGrenadeId()) is not { } grenade)
			return;
		Inventory.Inventory.TryAutoStore(grenade);
		DebugTeleport(bot.GlobalPosition + new Vector3(0f, 0f, 7f));
		DebugTurnTo(0f);
		DebugSetPitchDeg(6f);
		var before = bot.Health.Body.TotalHp;
		if (cookS < 0f && ItemDb.Data(DevGrenadeId())?.Payload is GrenadeData test)
		{
			// dev: set it off 2 m from the bot with nothing in between, to read blast and fragment damage
			Freeze(bot);
			var testSpec = test.ToSpec();
			var at = testSpec.Kind == GrenadeKind.Smoke ? (bot.GlobalPosition + GlobalPosition) * 0.5f : bot.GlobalPosition + new Vector3(2f, 0.1f, 0f);
			GrenadeDetonation.Detonate(this, testSpec, at, GetRid());
			await ToSignal(GetTree().CreateTimer(testSpec.Kind == GrenadeKind.Smoke ? 5.0 : 1.0), SceneTreeTimer.SignalName.Timeout);
			if (testSpec.Kind == GrenadeKind.Smoke)
				GD.Print($"SMOKE dev: bot sees player through the cloud? blocked={SmokeCloud.Blocks(bot.GlobalPosition + Vector3.Up * 1.6f, GlobalPosition + Vector3.Up * 1.3f)}, side line blocked={SmokeCloud.Blocks(bot.GlobalPosition + new Vector3(9f, 1.6f, 0f), GlobalPosition + new Vector3(9f, 1.3f, 0f))}");
			GD.Print($"GRENADE dev: bot {bot.Name} hp {before:0} -> {bot.Health.Body.TotalHp:0}, dead={!bot.IsAlive}");
			return;
		}
		if (ItemDb.Data(DevGrenadeId())?.Payload is not GrenadeData data || !TakeGrenade(grenade, data.ToSpec()))
			return;
		_fuse!.PullPin();
		_winding = true;
		_windS = _cfg.ThrowWindUpS;
		if (cookS > 0f)
		{
			_fuse.Cook();
			await ToSignal(GetTree().CreateTimer(cookS), SceneTreeTimer.SignalName.Timeout);
		}
		if (_fuse is not null)
			ThrowGrenade();
		await ToSignal(GetTree().CreateTimer(8.0), SceneTreeTimer.SignalName.Timeout);
		GD.Print($"GRENADE dev: bot {bot.Name} hp {before:0} -> {bot.Health.Body.TotalHp:0}, dead={!bot.IsAlive}; player hp {Health.Body.TotalHp:0}");
	}
}
