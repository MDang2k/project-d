using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// A minefield (Border Strip spec s2). On entering the raid it lays mines over its ground rectangle, keeping out of
/// the safe lanes that run through it, and stands warning signs round the edge with some missing. Each mine goes off
/// once when the player steps within reach: heavy leg damage, a likely fracture and heavy bleed, armor wear and a bang
/// the AI hear. The AI ignore mines for now. Placement and damage are in <see cref="MineFieldPlanner"/> and
/// <see cref="MineBlast"/>. Dev: <c>--showmines</c> draws every mine.
/// </summary>
public partial class MineField : Node3D
{
	/// <summary>Width (x) and depth (z) of the field; the height is unused.</summary>
	[Export] public Vector3 Size { get; set; } = new(38f, 2f, 80f);

	public MineTuning Tuning { get; set; } = new();

	private readonly List<Vector2> _mines = new();
	private readonly List<MeshInstance3D?> _marks = new();
	private Random _rng = new();
	private Rect2 _area;
	private Player? _player;

	public int MineCount => _mines.Count;

	public override async void _Ready()
	{
		if (Engine.IsEditorHint())
			return;
		// wait a frame so every safe lane point has joined its group
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		_rng = new Random((int)GD.Randi());
		_area = new Rect2(GlobalPosition.X - Size.X / 2f, GlobalPosition.Z - Size.Z / 2f, Size.X, Size.Z);
		var lanes = GetTree().GetNodesInGroup("safe_lanes").OfType<SafeLanePoint>()
			.Where(p => _area.Grow(Tuning.LaneHalfWidthM).HasPoint(new Vector2(p.GlobalPosition.X, p.GlobalPosition.Z)))
			.GroupBy(p => p.Lane)
			.Select(g => (IReadOnlyList<Vector2>)g.OrderBy(p => p.Order).Select(p => new Vector2(p.GlobalPosition.X, p.GlobalPosition.Z)).ToList())
			.ToList();
		_mines.AddRange(MineFieldPlanner.Place(_area, lanes, Tuning, _rng));
		var show = OS.GetCmdlineUserArgs().Contains("--showmines");
		foreach (var mine in _mines)
			_marks.Add(show ? AddMark(mine) : null);
		foreach (var (pos, yaw) in MineFieldPlanner.Signs(_area, Tuning, _rng))
			AddSign(pos, yaw);
		GD.Print($"MINES {Name}: {_mines.Count} mines, {lanes.Count} safe lanes");
		if (OS.GetCmdlineUserArgs().Contains("--minestep"))
			StepOnAMine();
	}

	/// <summary>Dev: after a second, puts the player on a mine of this field so the blast can be checked without walking.</summary>
	private async void StepOnAMine()
	{
		await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
		_player ??= GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault();
		if (_player is null || _mines.Count == 0)
			return;
		var m = _mines[0];
		_player.GlobalPosition = new Vector3(m.X, GlobalPosition.Y + 0.2f, m.Y);
	}

	public override void _PhysicsProcess(double delta)
	{
		_player ??= GetTree().GetNodesInGroup("perceivables").OfType<Player>().FirstOrDefault();
		if (_player is null || !_player.IsAlive)
			return;
		var foot = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z);
		// a jump clears a mine; anything within knee height of the ground sets it off
		if (!_area.Grow(2f).HasPoint(foot) || _player.GlobalPosition.Y - GlobalPosition.Y > 0.6f)
			return;
		var reach = Tuning.TriggerRadiusM * Tuning.TriggerRadiusM;
		for (var i = _mines.Count - 1; i >= 0; i--)
		{
			if (_mines[i].DistanceSquaredTo(foot) > reach)
				continue;
			Detonate(i);
			return;
		}
	}

	/// <summary>Sets off the mine under the player: damage, noise and a puff of dust.</summary>
	private void Detonate(int index)
	{
		var at = new Vector3(_mines[index].X, GlobalPosition.Y, _mines[index].Y);
		Sfx.Play3D(this, "mine_blast", _player!.GlobalPosition);
		var plan = MineBlast.Plan(Tuning, _rng);
		_player!.Health.ReceiveBlast(plan);
		NoiseBus.Emit(at, Tuning.NoiseRadiusM, "explosion", this);
		GroundDust.Puff(this, at, 2f);
		var health = _player.Health;
		GD.Print($"MINE detonated at {at.X:0},{at.Z:0}: {plan.SteppedLeg} hp={health.Body.Hp(plan.SteppedLeg):0}/{health.Body.MaxHp(plan.SteppedLeg):0} " +
			$"fractured={health.Effects.IsFractured(plan.SteppedLeg)} bleed={health.Effects.Bleed(plan.SteppedLeg)} thorax={health.Body.Hp(BodyPart.Thorax):0} dead={health.IsDead}");
		_marks[index]?.QueueFree();
		_marks.RemoveAt(index);
		_mines.RemoveAt(index);
	}

	private void AddSign(Vector2 pos, float yawDeg)
	{
		var sign = new Node3D { Position = new Vector3(pos.X, 0f, pos.Y) - GlobalPosition, RotationDegrees = new Vector3(0f, yawDeg, 0f) };
		sign.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.08f, 1.2f, 0.08f) },
			Position = new Vector3(0f, 0.6f, 0f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.25f, 0.2f) },
		});
		sign.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.35f, 0.04f) },
			Position = new Vector3(0f, 1.05f, -0.05f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.12f, 0.1f) },
		});
		AddChild(sign);
	}

	private MeshInstance3D AddMark(Vector2 pos)
	{
		var mark = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.05f },
			Position = new Vector3(pos.X, 0.03f, pos.Y) - GlobalPosition,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.1f, 0.1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
		};
		AddChild(mark);
		return mark;
	}
}
