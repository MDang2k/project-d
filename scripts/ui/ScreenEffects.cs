using Godot;

namespace ProjectD;

/// <summary>
/// Full-screen effects of grenades (throwables spec s4): the white-out of a stun grenade with the deafness that goes
/// with it (a low-pass on the master bus and a ringing), the blur and tint of tear gas, and the red edge of burning.
/// One node under the scene root, created at first use; the player code sets the levels each frame.
/// </summary>
public partial class ScreenEffects : CanvasLayer
{
	private static ScreenEffects? _instance;

	private ColorRect _flash = null!;
	private ColorRect _blur = null!;
	private ColorRect _burn = null!;
	private ColorRect _gas = null!;
	private ShaderMaterial _blurMat = null!;
	private ShaderMaterial _burnMat = null!;
	private float _flashS;
	private float _flashLeftS;
	private float _flashTotalS = 1f;
	private float _deafLeftS;
	private AudioEffectLowPassFilter? _lowPass;
	private int _lowPassIndex = -1;

	public static ScreenEffects? Get(Node context)
	{
		if (_instance is not null && IsInstanceValid(_instance))
			return _instance;
		if (context.GetTree()?.Root is not { } root)
			return null;
		_instance = new ScreenEffects { Name = "ScreenEffects", Layer = 6 };
		root.CallDeferred(Node.MethodName.AddChild, _instance);
		return _instance;
	}

	public override void _Ready()
	{
		_flash = FullRect(new Color(1f, 1f, 1f, 0f));
		_gas = FullRect(new Color(0.7f, 0.8f, 0.3f, 0f));
		_blurMat = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = @"shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float amount = 0.0;
void fragment() {
	vec2 uv = SCREEN_UV + vec2(sin(TIME * 2.0 + SCREEN_UV.y * 9.0), cos(TIME * 1.7 + SCREEN_UV.x * 7.0)) * 0.004 * amount;
	COLOR = vec4(textureLod(screen_tex, uv, amount * 3.5).rgb, 1.0);
}",
			},
		};
		_blur = FullRect(Colors.White);
		_blur.Material = _blurMat;
		_blur.Visible = false;
		_burnMat = new ShaderMaterial
		{
			Shader = new Shader
			{
				Code = @"shader_type canvas_item;
uniform float amount = 0.0;
void fragment() {
	float d = length(UV - vec2(0.5)) * 1.6;
	float pulse = 0.75 + 0.25 * sin(TIME * 9.0);
	COLOR = vec4(1.0, 0.28, 0.05, smoothstep(0.3, 1.0, d) * amount * pulse);
}",
			},
		};
		_burn = FullRect(Colors.White);
		_burn.Material = _burnMat;
		_burn.Visible = false;
	}

	private ColorRect FullRect(Color color)
	{
		var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(rect);
		rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return rect;
	}

	/// <summary>White-out: full white at once, fading over <paramref name="seconds"/>.</summary>
	public void WhiteOut(float strength, float seconds)
	{
		if (!IsNodeReady())
			return; // added to the tree a frame after its first use
		_flashS = Mathf.Clamp(strength, 0f, 1f);
		_flashLeftS = seconds;
		_flashTotalS = Mathf.Max(0.1f, seconds);
		_flash.Color = new Color(1f, 1f, 1f, _flashS);
	}

	/// <summary>Deafness: a low-pass on the master bus and a ringing, for this many seconds.</summary>
	public void Deafen(float seconds)
	{
		if (!IsNodeReady())
			return; // added to the tree a frame after its first use
		if (seconds <= 0f)
			return;
		if (_lowPass is null)
		{
			_lowPass = new AudioEffectLowPassFilter { CutoffHz = 650f };
			AudioServer.AddBusEffect(0, _lowPass);
			_lowPassIndex = AudioServer.GetBusEffectCount(0) - 1;
		}
		_deafLeftS = Mathf.Max(_deafLeftS, seconds);
		AudioServer.SetBusEffectEnabled(0, _lowPassIndex, true);
		Sfx.Play2D(this, "ear_ring");
	}

	/// <summary>Blur and tint for tear gas, 0 none to 1 thick; call every frame.</summary>
	public void SetGas(float level)
	{
		if (!IsNodeReady())
			return; // added to the tree a frame after its first use
		_blur.Visible = level > 0.02f;
		_blurMat.SetShaderParameter("amount", Mathf.Clamp(level, 0f, 1f));
		_gas.Color = new Color(0.7f, 0.8f, 0.3f, Mathf.Clamp(level, 0f, 1f) * 0.2f);
	}

	/// <summary>Red edge for burning, 0 none to 1; call every frame.</summary>
	public void SetBurn(float level)
	{
		if (!IsNodeReady())
			return; // added to the tree a frame after its first use
		_burn.Visible = level > 0.02f;
		_burnMat.SetShaderParameter("amount", Mathf.Clamp(level, 0f, 1f));
	}

	public override void _Process(double delta)
	{
		if (_flashLeftS > 0f)
		{
			_flashLeftS -= (float)delta;
			// stays near full for the first third, then fades out
			_flash.Color = new Color(1f, 1f, 1f, _flashS * Mathf.Clamp(_flashLeftS / (_flashTotalS * 0.66f), 0f, 1f));
			if (_flashLeftS <= 0f)
				_flash.Color = new Color(1f, 1f, 1f, 0f);
		}
		if (_deafLeftS > 0f && (_deafLeftS -= (float)delta) <= 0f && _lowPass is not null)
			AudioServer.SetBusEffectEnabled(0, _lowPassIndex, false);
	}
}
