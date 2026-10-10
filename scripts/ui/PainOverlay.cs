using Godot;

namespace ProjectD;

/// <summary>
/// Screen-edge blur and red darkening that scale with pain strength (GDD s9: "blurred vision
/// edges"). Hidden at zero strength so it costs nothing normally.
/// </summary>
public partial class PainOverlay : CanvasLayer
{
	private const string ShaderCode = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec2 uv = SCREEN_UV;
	float d = distance(uv, vec2(0.5)) * 1.41421;
	float edge = smoothstep(0.2, 1.0, d) * strength;
	vec3 col = textureLod(screen_tex, uv, edge * 4.0).rgb;
	col = mix(col, col * vec3(0.55, 0.25, 0.25), edge);
	COLOR = vec4(col, 1.0);
}";

	private ColorRect _rect = null!;
	private ShaderMaterial _material = null!;

	public override void _Ready()
	{
		// Below the default HUD layer (1) so text stays sharp; it still blurs the 3D view.
		Layer = 0;
		_material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
		_rect = new ColorRect
		{
			Material = _material,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
		};
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
	}

	/// <summary>0 hides the overlay; 1 is the strongest blur and darkening.</summary>
	public void SetStrength(float strength)
	{
		_rect.Visible = strength > 0.01f;
		_material.SetShaderParameter("strength", Mathf.Clamp(strength, 0f, 1f));
	}
}
