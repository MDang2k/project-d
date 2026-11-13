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

	private float _gasLevel;
	private float _coughLeftS;

	/// <summary>Where the camera looks, for effects that depend on facing (the flashbang).</summary>
	public Vector3 ViewForward => -_camera.GlobalBasis.Z;

	/// <summary>Weapon sway multiplier from tear gas (x2.5 in the thick of it).</summary>
	public float GasSwayMultiplier => GasModel.SwayMultiplier(_gasLevel);

	/// <summary>A flashbang reached the player: the white-out (looking away halves it) and the ringing deafness.</summary>
	public void ApplyStun(float strength, float blindS, float deafS)
	{
		if (ScreenEffects.Get(this) is not { } fx)
			return;
		fx.WhiteOut(Mathf.Clamp(strength * 1.4f, 0.3f, 1f), blindS);
		fx.Deafen(deafS);
		GD.Print($"STUN player: strength {strength:0.00}, white-out {blindS:0.0} s, deaf {deafS:0.0} s");
	}

	/// <summary>Tear gas and fire on the player each physics step: sway, blur, stamina, coughing and the burn edge.</summary>
	private void UpdateGrenadeStatus(float delta)
	{
		var gas = SmokeCloud.GasAt(GlobalPosition + Vector3.Up * 1.5f);
		_gasLevel = Mathf.MoveToward(_gasLevel, gas, delta * 2f);
		var fx = ScreenEffects.Get(this);
		fx?.SetGas(_gasLevel);
		fx?.SetBurn(Health.IsBurning ? 0.85f : 0f);
		if (_gasLevel <= 0.05f)
			return;
		Legs.Spend(GasModel.StaminaDrainPerS * _gasLevel * delta);
		if ((_coughLeftS -= delta) <= 0f)
		{
			_coughLeftS = 2.5f;
			NoiseBus.Emit(GlobalPosition, GasModel.CoughNoiseM, "cough", this);
			Sfx.Play3D(this, "cough", GlobalPosition);
		}
	}

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
			if (!_grenadeSpec.ImpactTriggered())
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
		if (Input.IsActionJustPressed("reload") && !_grenadeSpec.ImpactTriggered() && _fuse.Cook())
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

	/// <summary>Dev (--aithrow): the nearest bot throws a frag at the player from 20 m, and a second bot stands by to dodge it.</summary>
	/// <summary>Dev (--thermitedoor): thermite burns beside the first locked door; prints when it opens.</summary>
	private async void DevThermiteDoor()
	{
		await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
		if (GetTree().GetNodesInGroup("locked_doors").OfType<LockedDoor>().FirstOrDefault() is not { } door || ItemDb.Data("thermite_grenade")?.Payload is not GrenadeData data)
			return;
		DebugTeleport(door.GlobalPosition + new Vector3(0f, 0f, 7f));
		FirePatch.Spawn(this, data.ToSpec(), door.GlobalPosition + new Vector3(0f, -1f, 1.2f));
		var waited = 0f;
		while (!door.IsOpen && waited < 30f)
		{
			await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
			waited += 1f;
		}
		GD.Print($"THERMITE dev: door {door.Name} open={door.IsOpen} after {waited:0} s (proof={door.ThermiteProof})");
	}

	private async void DevAiThrow()
	{
		await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
		var bots = GetTree().GetNodesInGroup("perceivables").OfType<AiHuman>().Where(a => a.IsAlive)
			.OrderBy(a => a.GlobalPosition.DistanceTo(GlobalPosition)).Take(2).ToList();
		if (bots.Count < 2)
			return;
		DebugTeleport(bots[0].GlobalPosition + new Vector3(0f, 0f, 20f));
		Health.DamageScale = 0f; // survive it, this is a look at what the bots do
		bots[1].GlobalPosition = GlobalPosition + new Vector3(3f, 0f, 2f);
		bots[0].DebugThrowAt(GlobalPosition);
		await ToSignal(GetTree().CreateTimer(9.0), SceneTreeTimer.SignalName.Timeout);
		GD.Print($"AITHROW dev: player hp {Health.Body.TotalHp:0}, bots left with {bots[0].Gear.AllItems().Count(i => i.Spec.Id.EndsWith("grenade"))} grenades");
	}

	private async void StartGrenadeDev()
	{
		if (OS.GetCmdlineUserArgs().Contains("--thermitedoor"))
		{
			DevThermiteDoor();
			return;
		}
		if (OS.GetCmdlineUserArgs().Contains("--aithrow"))
		{
			DevAiThrow();
			return;
		}
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
