shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// Full-screen underwater pass for one water box (drawn by a 2x2 QuadMesh owned by the water volume).
// Per pixel: if the view ray starts below the surface inside the box, the part of the ray that runs
// through water (up to the opaque scene, or up to the surface when it leaves the water) is attenuated
// toward the water colour. Things above the water seen through the surface are fogged only for the
// underwater stretch, so they stay visible. The test is per pixel at the near plane, so the screen splits
// exactly at the waterline while the camera crosses it, with a thin meniscus line.

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float surface_y = 0.0;
// The simulated surface, copied from the water material by WaterVolume, so the waterline follows the
// drawn waves (and the dent a swimmer makes) instead of the still level.
uniform sampler2D ripple_height_tex : hint_default_black, filter_linear, repeat_disable;
uniform sampler2D ocean_height_tex : hint_default_black, filter_linear, repeat_enable;
uniform vec2 ripple_domain_size = vec2(1.0);
uniform vec2 ocean_domain_size = vec2(1.0);
uniform vec2 grid_center = vec2(0.0);
uniform float level_correction = 0.0;
uniform vec3 box_min = vec3(0.0);
uniform vec3 box_max = vec3(0.0);
// Sunlit in-scattered colour of the water (what distant water looks like).
uniform vec3 water_color : source_color = vec3(0.1, 0.42, 0.47);
// Extinction per metre (clear pool water: ~5 m to two thirds).
uniform float density = 0.2;
// Thickness in pixels of the darker meniscus line where the near plane crosses the surface. Measured in
// pixels, not metres: the near plane is only a few centimetres tall, so any world thickness would fill
// a large part of the screen when the eye sits at the surface.
uniform float meniscus_pixels = 2.0;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

vec3 world_at(vec2 uv, float raw, mat4 inv_projection, mat4 inv_view) {
	vec4 view = inv_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
	view /= view.w;
	return (inv_view * view).xyz;
}

// Height of the drawn surface at world xz (pool_water.gdshader's vertex height, without horizontal chop).
float surface_at(vec2 xz) {
	if (textureSize(ripple_height_tex, 0).x <= 4) {
		return surface_y;
	}
	vec2 cuv = clamp((xz - grid_center) / ripple_domain_size + 0.5, vec2(0.0), vec2(1.0));
	float h = texture(ripple_height_tex, cuv).r + level_correction + texture(ocean_height_tex, xz / ocean_domain_size + 0.5).r;
	return (isnan(h) || isinf(h) || abs(h - surface_y) > 1.0) ? surface_y : h;
}

void fragment() {
	// Reverse Z: 1 is the near plane.
	vec3 start = world_at(SCREEN_UV, 1.0, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
	bool in_box = all(greaterThan(start.xz, box_min.xz)) && all(lessThan(start.xz, box_max.xz)) && start.y > box_min.y;
	float level = surface_at(start.xz);
	float below = level - start.y;
	float pixel = max(fwidth(below), 1e-6);
	float meniscus = pixel * meniscus_pixels;
	if (!in_box || below < -meniscus) {
		discard;
	}
	vec3 end = world_at(SCREEN_UV, texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
	vec3 ray = end - start;
	float length_total = length(ray);
	vec3 dir = ray / max(length_total, 1e-4);
	float underwater = length_total;
	if (dir.y > 1e-4) {
		underwater = min(underwater, max(below, 0.0) / dir.y);
	}
	float fog = (1.0 - exp(-density * underwater)) * smoothstep(-meniscus, meniscus, below);
	float line = 1.0 - smoothstep(0.0, meniscus, abs(below));
	ALBEDO = mix(water_color, water_color * 0.3, line);
	ALPHA = max(fog, line * 0.7);
}
