shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// gamekit_core's prototype grid that also gets wet in rain: splatter, wet film, puddles with ripples and
// streaks on walls (weather_wetness.gdshaderinc). Same uniforms as prototype_grid.gdshader, plus
// `receive_weather` and `porosity`. Needs the weather shader globals in project.godot (see README).

#include "res://addons/gamekit_core/shaders/prototype_grid.gdshaderinc"
#include "res://addons/gamekit_weather/shaders/weather_wetness.gdshaderinc"

uniform vec3 color : source_color = vec3(0.55, 0.56, 0.58);
uniform float cell_size = 1.0;
uniform float line_width = 0.02;
uniform float roughness_val : hint_range(0.0, 1.0) = 0.85;
// Gets wet in rain. Turn off for indoor surfaces (covered ones stay dry on their own).
uniform bool receive_weather = true;
// How much the surface darkens when wet.
uniform float porosity : hint_range(0.0, 1.0) = 0.8;

varying vec3 v_world_position;
varying vec3 v_world_normal;

void vertex() {
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

void fragment() {
	vec3 albedo = prototype_grid_albedo(color, v_world_position, v_world_normal, cell_size, line_width);
	float roughness = roughness_val;
	if (receive_weather) {
		vec3 ripple = weather_apply_wetness(v_world_position, v_world_normal, porosity, TIME, albedo, roughness);
		NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(ripple, 0.0)).xyz);
	}
	ALBEDO = albedo;
	ROUGHNESS = roughness;
}
