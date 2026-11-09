// Leaf cluster cards: alpha scissor, double-sided, and cards seen nearly edge-on fade out
// (their smeared streaks are the classic card-tree artifact). The card's real face normal comes
// from screen derivatives, so the mesh can keep its outward "crown" custom normals for lighting.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_disable;
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float alpha_cutoff : hint_range(0.0, 1.0) = 0.5;
// |dot(face normal, view)| below fade_start: card hidden; above fade_end: fully shown.
uniform float fade_start : hint_range(0.0, 1.0) = 0.15;
uniform float fade_end : hint_range(0.0, 1.0) = 0.4;
uniform float roughness : hint_range(0.0, 1.0) = 0.85;

void fragment() {
	vec4 tex = texture(albedo_tex, UV);
	vec3 face_n = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	float facing = abs(dot(face_n, normalize(VERTEX)));
	ALBEDO = tex.rgb;
	ALPHA = tex.a * smoothstep(fade_start, fade_end, facing);
	ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;
	NORMAL_MAP = texture(normal_tex, UV).rgb;
	NORMAL_MAP_DEPTH = normal_strength;
	ROUGHNESS = roughness;
}
