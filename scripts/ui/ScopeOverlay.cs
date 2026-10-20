using Godot;

namespace ProjectD;

/// <summary>
/// Dark ring around the view while aiming through a magnifying optic (weapon modding spec: scopes zoom
/// by narrowing the field of view, with an overlay). Amount 0 hides it, 1 is fully aimed.
/// </summary>
public partial class ScopeOverlay : CanvasLayer
{
	private const string ShaderCode = @"
shader_type canvas_item;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float aspect = 1.78;
void fragment() {
	vec2 p = (UV - vec2(0.5)) * vec2(aspect, 1.0);
	float d = length(p);
	float edge = smoothstep(0.30, 0.34, d);
	COLOR = vec4(0.0, 0.0, 0.0, edge * amount);
}";

	private ColorRect _rect = null!;
	private ShaderMaterial _material = null!;

	public override void _Ready()
	{
		Layer = 3;
		_material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
		_rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _material, Visible = false };
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
	}

	public void SetAmount(float amount)
	{
		_rect.Visible = amount > 0.01f;
		_material.SetShaderParameter("amount", Mathf.Clamp(amount, 0f, 1f));
		var size = GetViewport().GetVisibleRect().Size;
		_material.SetShaderParameter("aspect", size.Y > 0f ? size.X / size.Y : 1.78f);
	}
}
