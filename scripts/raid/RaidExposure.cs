using System.Linq;
using Godot;

namespace ProjectD;

/// <summary>
/// Ewigstein exposure in a raid (M8e spec s2): each physics frame feeds the player's <see cref="ExposureModel"/> the
/// unshielded shards carried and the distance to every deposit, applies the HP loss, and drives the restrained
/// presentation: a slight ripple and colour shift from 50 and a faint hum and radio static (generated, no audio assets
/// yet). Sway and the status tag read the model directly. Dev: <c>--giveshard=n</c> puts n shards in the pockets,
/// <c>--givecanister=small|large</c> a canister, <c>--spawndeposit</c> a deposit 3 m ahead (<c>--harvestnow</c> starts harvesting it), <c>--expreport</c> prints the meter each second, <c>--expscale=x</c> runs
/// exposure x times faster.
/// </summary>
public partial class RaidExposure : Node
{
	private const int MixRate = 22050;

	private Player _player = null!;
	private ColorRect _overlay = null!;
	private ShaderMaterial _shader = null!;
	private AudioStreamPlayer? _audio;
	private float _scale = 1f;
	private bool _report;
	private int _frames;
	private float _humPhase;
	private readonly RandomNumberGenerator _rng = new();

	public void Start(Player player)
	{
		_player = player;
		_player.Exposure.Reset();
		var args = OS.GetCmdlineUserArgs();
		if (args.FirstOrDefault(a => a.StartsWith("--expscale="))?["--expscale=".Length..] is { } s && float.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var x))
			_scale = x;
		_report = args.Contains("--expreport");
		BuildPresentation();
		DevArgs(args);
	}

	private void BuildPresentation()
	{
		var layer = new CanvasLayer { Layer = 5 };
		AddChild(layer);
		_shader = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = @"shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform float strength = 0.0;
void fragment() {
	vec2 uv = SCREEN_UV;
	uv.x += sin(uv.y * 40.0 + TIME * 1.6) * 0.0018 * strength;
	uv.y += cos(uv.x * 30.0 + TIME * 1.1) * 0.0012 * strength;
	vec3 c = texture(screen, uv).rgb;
	float grey = dot(c, vec3(0.3, 0.59, 0.11));
	c = mix(c, vec3(grey) * vec3(1.0, 0.96, 0.9), 0.35 * strength);
	COLOR = vec4(c, 1.0);
}",
			},
		};
		_overlay = new ColorRect { Material = _shader, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		layer.AddChild(_overlay);
		_overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_audio = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.25f }, Autoplay = true };
		AddChild(_audio);
	}

	private void DevArgs(string[] args)
	{
		var inv = _player.Inventory.Inventory;
		if (args.FirstOrDefault(a => a.StartsWith("--givecanister="))?["--givecanister=".Length..] is { } size
			&& ItemDb.Create(size == "large" ? "large_ewigstein_canister" : "small_ewigstein_canister") is { } can)
			inv.TryAutoStore(can);
		if (args.FirstOrDefault(a => a.StartsWith("--giveshard="))?["--giveshard=".Length..] is { } n && int.TryParse(n, out var count))
		{
			for (var i = 0; i < count; i++)
				EwigsteinRules.StoreShard(inv, ItemDb.Create("ewigstein_shard")!);
		}
		if (args.Contains("--spawndeposit") && GD.Load<PackedScene>("res://scenes/world/ewigstein_deposit.tscn").Instantiate<EwigsteinDeposit>() is { } dep)
		{
			GetParent().CallDeferred(Node.MethodName.AddChild, dep);
			dep.CallDeferred(Node3D.MethodName.SetGlobalPosition, _player.GlobalPosition + -_player.GlobalBasis.Z * 3f);
			if (args.Contains("--harvestnow"))
				GetTree().CreateTimer(1.5).Timeout += () => dep.TryStartHarvest(_player);
		}
		_player.Inventory.NotifyChanged();
	}

	public override void _PhysicsProcess(double delta)
	{
		var model = _player.Exposure;
		if (_player.IsDead)
			return;
		var dt = (float)delta * _scale;
		var deposits = GetTree().GetNodesInGroup("ewigstein_deposits").OfType<Node3D>()
			.Select(d => d.GlobalPosition.DistanceTo(_player.GlobalPosition));
		model.Update(dt, EwigsteinRules.UnshieldedShards(_player.Inventory.Inventory), deposits);
		// HP loss is not hit damage: straight to the body, so Vitality's scaling never applies (spec s2).
		if (model.HpLossPerS > 0f)
		{
			_player.Health.Body.TakeDamage(BodyPart.Thorax, model.HpLossPerS * dt * 0.5f);
			_player.Health.Body.TakeDamage(BodyPart.Stomach, model.HpLossPerS * dt * 0.5f);
		}
		if (_report && ++_frames % 60 == 0)
			GD.Print($"EXPOSURE level={model.Level:F1} rate={model.SourceRate:F2} sway={model.SwayMultiplier:F2} distorted={model.Distorted} hploss={model.HpLossPerS} hp={_player.Health.Body.TotalHp:F1}");
		var strength = model.Distorted ? Mathf.Clamp((model.Level - 50f) / 50f, 0.2f, 1f) : 0f;
		_overlay.Visible = strength > 0f;
		_shader.SetShaderParameter("strength", strength);
	}

	public override void _Process(double delta)
	{
		if (_audio?.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback)
			return;
		var model = _player.Exposure;
		var hum = Mathf.Clamp(model.SourceRate / 6f, 0f, 1f) * 0.06f;
		var stat = model.Distorted ? 0.02f + (model.Level - 50f) / 50f * 0.05f : 0f;
		var frames = playback.GetFramesAvailable();
		if ((hum <= 0f && stat <= 0f) || frames <= 0)
			return;
		for (var i = 0; i < frames; i++)
		{
			_humPhase += Mathf.Tau * 58f / MixRate;
			var s = Mathf.Sin(_humPhase) * hum + _rng.RandfRange(-1f, 1f) * stat;
			playback.PushFrame(new Vector2(s, s));
		}
	}
}
