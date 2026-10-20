// Rain streak for GPUParticles3D draw passes (QuadMesh 1x1).
// Each quad is stretched along the particle's Y axis (aligned to velocity by
// the process material) and turned toward the camera around that axis.
//
// The streak is lit like a thin water cylinder: a curved normal across its
// width picks up sun highlights, sky reflections and lightning flashes, and
// it refracts the scene behind it like a real drop (screen texture sampled
// with an offset). Shadows are skipped to stay cheap.
// Streaks are never drawn thinner than min_pixel_width; when widened, their
// opacity drops to match, so distant rain stays visible and doesn't shimmer.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec3 rain_color : source_color = vec3(0.6, 0.66, 0.72);
// How strongly drops bend the background (0 = no refraction).
uniform float refraction : hint_range(0.0, 0.05) = 0.016;
uniform float opacity : hint_range(0.0, 1.0) = 0.26;
uniform float roughness : hint_range(0.0, 1.0) = 0.12;
// Small self-lit term for light scattered by the water, so rain reads in dark storms.
uniform float glint : hint_range(0.0, 1.0) = 0.08;
// Light passing through the drop from behind.
uniform float translucency : hint_range(0.0, 1.0) = 0.40;
uniform float streak_length = 0.55;
uniform float streak_width = 0.008;
uniform float min_pixel_width = 1.5;
uniform float near_fade = 1.5;
uniform float far_fade = 32.0;

varying float fade;

float hash(float n) {
	return fract(sin(n * 12.9898) * 43758.5453);
}

void vertex() {
	vec3 center = MODEL_MATRIX[3].xyz;
	vec3 axis_y = normalize(MODEL_MATRIX[1].xyz);
	vec3 to_camera = CAMERA_POSITION_WORLD - center;
	float distance_to_camera = length(to_camera);
	vec3 view_dir = to_camera / max(distance_to_camera, 1e-4);
	vec3 axis_x = normalize(cross(axis_y, view_dir));
	vec3 facing = normalize(cross(axis_x, axis_y));

	// Size of one pixel in meters at this distance.
	float pixel_size = 2.0 * distance_to_camera / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float width = max(streak_width, pixel_size * min_pixel_width);
	// Real drops vary in size: per-drop length, width and brightness.
	float seed = hash(float(INSTANCE_ID));
	float drop_scale = length(MODEL_MATRIX[1].xyz);
	width *= mix(0.7, 1.4, seed);
	float length_m = streak_length * drop_scale * mix(0.6, 1.3, hash(seed * 7.1));
	vec3 world = center + axis_x * VERTEX.x * width + axis_y * VERTEX.y * length_m;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	NORMAL = (VIEW_MATRIX * vec4(facing, 0.0)).xyz;
	TANGENT = (VIEW_MATRIX * vec4(axis_x, 0.0)).xyz;
	BINORMAL = (VIEW_MATRIX * vec4(axis_y, 0.0)).xyz;

	fade = smoothstep(near_fade * 0.5, near_fade, distance_to_camera)
			* (1.0 - smoothstep(far_fade * 0.6, far_fade, distance_to_camera))
			* pow(streak_width / width, 0.3)
			* mix(0.45, 1.0, hash(seed * 3.3));
}

void fragment() {
	// Curved normal across the width, like a cylinder of water.
	float x = UV.x * 2.0 - 1.0;
	NORMAL = normalize(NORMAL * sqrt(max(1.0 - x * x, 0.0)) + TANGENT * x);
	// Refraction: the drop shows the scene behind it, shifted by its curved
	// surface and slightly brightened (a drop focuses the light behind it).
	vec2 bend = NORMAL.xy * refraction;
	vec3 behind = textureLod(screen_texture, SCREEN_UV - bend, 0.0).rgb;
	ALBEDO = rain_color * 0.15;
	ROUGHNESS = roughness;
	SPECULAR = 0.8;
	BACKLIGHT = rain_color * translucency;
	EMISSION = behind * 1.0 + rain_color * glint;
	// Motion-blurred drop: brightest at the leading end, fading along the tail.
	// The quad's +Y points along the velocity, so the leading end is UV.y = 0.
	float tail = 1.0 - UV.y;
	float along = smoothstep(0.0, 0.85, tail) * (1.0 - smoothstep(0.92, 1.0, tail));
	ALPHA = opacity * (1.0 - abs(x)) * along * fade;
}
