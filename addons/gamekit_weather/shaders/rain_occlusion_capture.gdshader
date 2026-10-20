// Full-screen pass for the rain occlusion camera (RainOcclusion). Converts the
// top-down depth buffer into world-space height (meters), written to a float
// viewport texture: the height of the highest surface above each point.
//
// The quad only draws for orthographic cameras, so the player's perspective
// camera never sees it.
shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled, skip_vertex_transform;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

void vertex() {
	// Orthographic projections have [3][3] == 1; perspective ones have 0.
	if (PROJECTION_MATRIX[3][3] < 0.5) {
		POSITION = vec4(2.0, 2.0, 2.0, 1.0);
	} else {
		POSITION = vec4(VERTEX.xy * 2.0, 1.0, 1.0);
	}
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
	ALBEDO = vec3(world.y);
}
