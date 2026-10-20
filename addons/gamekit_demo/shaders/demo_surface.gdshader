shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// gamekit_demo's all-purpose surface: a colour with a procedural pattern (plain, tiles, planks, stone
// blocks, grass), world-space so it never stretches, and wet in rain (gamekit_weather). No textures.
// Every material in addons/gamekit_demo/materials/ is this shader with different values.
// Needs the weather [shader_globals] in project.godot.

#include "res://addons/gamekit_weather/shaders/weather_wetness.gdshaderinc"

uniform vec3 surface_color : source_color = vec3(0.6, 0.6, 0.6);
// 0 Plain, 1 Tiles, 2 Planks, 3 Stone blocks, 4 Grass
uniform int surface_pattern : hint_enum("Plain", "Tiles", "Planks", "Stone blocks", "Grass") = 0;
// Size of one tile / plank width / block height in metres.
uniform float surface_scale = 1.0;
// Width of the grout or gaps between tiles, planks and blocks (metres).
uniform float surface_gap = 0.015;
uniform vec3 surface_gap_color : source_color = vec3(0.3, 0.3, 0.3);
// How much each tile / plank / block differs in brightness (0 = all the same).
uniform float surface_variation : hint_range(0.0, 1.0) = 0.12;
uniform float surface_roughness : hint_range(0.0, 1.0) = 0.8;
uniform float surface_metallic : hint_range(0.0, 1.0) = 0.0;
// Gets wet in rain. Turn off for indoor-only surfaces (covered ones stay dry on their own).
uniform bool surface_receive_weather = true;
// How much the surface darkens when wet.
uniform float surface_porosity : hint_range(0.0, 1.0) = 0.7;

varying vec3 v_world_position;
varying vec3 v_world_normal;

void vertex() {
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

// World-space 2D coordinates on the face's dominant plane (floors: XZ, walls: XY or ZY).
vec2 surface_plane_uv(vec3 p, vec3 n) {
	vec3 a = abs(n);
	if (a.y >= a.x && a.y >= a.z) {
		return p.xz;
	}
	return a.x > a.z ? vec2(p.z, p.y) : vec2(p.x, p.y);
}

// 1 inside a cell, 0 on its gap. cell_uv is 0..1 across the cell, cell_size in metres.
float surface_gap_mask(vec2 cell_uv, vec2 cell_size) {
	vec2 edge = min(cell_uv, 1.0 - cell_uv) * cell_size;
	float d = min(edge.x, edge.y);
	return smoothstep(surface_gap * 0.5, surface_gap * 0.5 + 0.006, d);
}

vec3 surface_albedo(vec3 p, vec3 n) {
	vec2 uv = surface_plane_uv(p, n);
	float s = max(surface_scale, 0.01);
	float fine = weather_value_noise(uv * 6.0) * 0.5 + weather_value_noise(uv * 23.0) * 0.5;
	vec3 col = surface_color;

	if (surface_pattern == 1) {
		// Square tiles.
		vec2 cell = floor(uv / s);
		float inside = surface_gap_mask(fract(uv / s), vec2(s));
		col *= 1.0 + (weather_hash12(cell) - 0.5) * surface_variation * 2.0;
		col *= 0.96 + fine * 0.08;
		return mix(surface_gap_color, col, inside);
	}
	if (surface_pattern == 2) {
		// Planks: width = scale along one axis, 6 x longer along the other, staggered rows.
		vec2 size = vec2(s * 6.0, s);
		float row = floor(uv.y / size.y);
		vec2 puv = vec2(uv.x / size.x + weather_hash12(vec2(row, 7.0)), uv.y / size.y);
		vec2 cell = floor(puv);
		float inside = surface_gap_mask(fract(puv), size);
		float grain = weather_value_noise(vec2(uv.x * 0.8, uv.y * 40.0 / s));
		col *= 1.0 + (weather_hash12(cell) - 0.5) * surface_variation * 2.0;
		col *= 0.88 + grain * 0.24;
		return mix(surface_gap_color, col, inside);
	}
	if (surface_pattern == 3) {
		// Stone blocks in running bond (2:1), slightly uneven.
		vec2 size = vec2(s * 2.0, s);
		float row = floor(uv.y / size.y);
		vec2 buv = vec2(uv.x / size.x + mod(row, 2.0) * 0.5, uv.y / size.y);
		vec2 cell = floor(buv);
		float inside = surface_gap_mask(fract(buv), size);
		col *= 1.0 + (weather_hash12(cell) - 0.5) * surface_variation * 2.0;
		col *= 0.85 + fine * 0.3;
		return mix(surface_gap_color, col, inside);
	}
	if (surface_pattern == 4) {
		// Grass: soft patches of lighter and darker green, fine blades.
		float patches = weather_value_noise(uv * 0.35) * 0.6 + weather_value_noise(uv * 1.3) * 0.4;
		float blades = weather_value_noise(uv * 17.0) * 0.6 + weather_value_noise(uv * 41.0) * 0.4;
		col *= mix(0.75, 1.2, patches);
		col = mix(col, col * vec3(1.15, 1.05, 0.7), smoothstep(0.55, 0.9, patches) * surface_variation * 3.0);
		return col * (0.9 + blades * 0.2);
	}
	// Plain: soft low-frequency variation so large areas don't look flat.
	float broad = weather_value_noise(uv * 0.5);
	return col * (1.0 + (broad - 0.5) * surface_variation) * (0.97 + fine * 0.06);
}

void fragment() {
	vec3 albedo = surface_albedo(v_world_position, v_world_normal);
	float roughness = surface_roughness;
	if (surface_receive_weather) {
		vec3 ripple = weather_apply_wetness(v_world_position, v_world_normal, surface_porosity, TIME, albedo, roughness);
		NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(ripple, 0.0)).xyz);
	}
	ALBEDO = albedo;
	ROUGHNESS = roughness;
	METALLIC = surface_metallic;
}
