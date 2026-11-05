using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// An Ewigstein deposit (M8e spec s3): irradiates whoever stands near it (<see cref="RaidExposure"/> measures the
/// distance) and gives one shard per raid to a player who holds the interact key's action for 10 s. The harvest is
/// loud (AI within 25 m hear it) and a hit breaks it. The crystal mesh is a child in the scene.
/// </summary>
public partial class EwigsteinDeposit : Node3D
{
	[Export] public float HarvestS { get; set; } = 10f;
	[Export] public float NoiseRadiusM { get; set; } = 25f;

	private ReadTimer _timer = null!;
	private Player? _player;
	private float _noiseLeft;

	public bool Harvested { get; private set; }

	public override void _Ready()
	{
		_timer = new ReadTimer(HarvestS);
		_timer.Finished += _ => Finish();
		_timer.Cancelled += () => _player?.NotifyMessage(GameText.Get("deposit.broken"));
		if (Engine.IsEditorHint())
			return;
		AddToGroup("ewigstein_deposits");
	}

	public void TryStartHarvest(Player player)
	{
		if (Harvested || _timer.IsReading || player.IsDead || !_timer.Start("shard"))
			return;
		_player = player;
		_noiseLeft = 0f;
		player.Health.HitTaken += OnHit;
		player.NotifyMessage(GameText.Get("deposit.harvesting"));
	}

	private void OnHit(HitResult _)
	{
		_timer.Cancel();
		End();
	}

	public override void _Process(double delta)
	{
		if (!_timer.IsReading || _player is not { } player)
			return;
		if (!IsInstanceValid(player) || player.IsDead || player.GlobalPosition.DistanceTo(GlobalPosition) > 4f)
		{
			_timer.Cancel(); // died or walked away
			End();
			return;
		}
		_noiseLeft -= (float)delta;
		if (_noiseLeft <= 0f)
		{
			_noiseLeft = 1f;
			NoiseBus.Emit(GlobalPosition, NoiseRadiusM, "harvest", this);
		}
		_timer.Update((float)delta);
		player.HarvestProgress = _timer.IsReading ? _timer.Progress : -1f;
		if (!_timer.IsReading)
			End();
	}

	private void Finish()
	{
		if (_player is not { } player || ItemDb.Create("ewigstein_shard") is not { } shard)
			return;
		shard.FoundInRaid = true;
		if (!EwigsteinRules.StoreShard(player.Inventory.Inventory, shard))
		{
			player.NotifyMessage(GameText.Get("deposit.noroom")); // the deposit keeps its shard
			return;
		}
		Harvested = true;
		player.Inventory.NotifyChanged();
		player.NotifyMessage(GameText.Get("deposit.done"));
		GD.Print($"EWIGSTEIN shard harvested at {Name}");
		if (GetNodeOrNull<Node3D>("Crystal") is { } crystal)
			crystal.Scale = new Vector3(0.6f, 0.4f, 0.6f);
	}

	private void End()
	{
		if (_player is { } p && IsInstanceValid(p))
		{
			p.Health.HitTaken -= OnHit;
			p.HarvestProgress = -1f;
		}
		_player = null;
	}
}
