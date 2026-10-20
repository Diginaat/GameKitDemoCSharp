shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// World-space prototype grid for blocking out levels: 1 m cells, darker lines, a faint checker, projected
// triplanar so it never stretches on scaled boxes. Set `color` per material.

#include "res://addons/gamekit_core/shaders/prototype_grid.gdshaderinc"

uniform vec3 color : source_color = vec3(0.55, 0.56, 0.58);
uniform float cell_size = 1.0;
uniform float line_width = 0.02;
uniform float roughness_val : hint_range(0.0, 1.0) = 0.85;

varying vec3 v_world_position;
varying vec3 v_world_normal;

void vertex() {
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

void fragment() {
	ALBEDO = prototype_grid_albedo(color, v_world_position, v_world_normal, cell_size, line_width);
	ROUGHNESS = roughness_val;
}
