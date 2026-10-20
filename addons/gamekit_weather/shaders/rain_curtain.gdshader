// Distant rain curtains: an open cylinder around the camera (inside the
// RainEmitter) showing far-off rain as moving sheets and streaks, like the
// rain walls in Ultra Dynamic Sky. Geometry closer than the cylinder hides it
// (depth test); geometry just behind it fades it out softly (depth texture),
// so it never draws a hard band across distant walls. Noise is sampled from
// the direction vector, not the angle, so there's no seam.
//
// Unshaded and procedural (no textures): its color is set by WeatherSystem
// to the fog/horizon color, so it always matches the scene's lighting.
// Cost: about one screen of cheap transparent pixels while it rains.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec3 curtain_color : source_color = vec3(0.6, 0.63, 0.68);
// Rain shows only where the scene is at least this far behind the curtain.
uniform float soft_distance = 12.0;
uniform float intensity : hint_range(0.0, 1.0) = 0.0;
// Horizontal wind in m/s (x, z), to slant the streaks and move the sheets.
uniform vec2 wind = vec2(0.0);
uniform float fall_speed = 11.0;
uniform float streak_opacity : hint_range(0.0, 1.0) = 0.22;
uniform float sheet_opacity : hint_range(0.0, 1.0) = 0.28;
// Streak columns around the full circle.
uniform float streak_columns = 900.0;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
			mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	if (intensity <= 0.001) {
		discard;
	}
	vec3 center = MODEL_MATRIX[3].xyz;
	vec3 offset = world_position - center;
	// u: position around the circle (0..1), v: height in meters.
	float u = atan(offset.x, offset.z) / TAU + 0.5;
	float v = offset.y;

	// Slant with the wind (component across the view direction).
	vec2 around = normalize(vec2(offset.z, -offset.x) + vec2(1e-4));
	float slant = dot(wind, around) / fall_speed;
	float column_u = u * streak_columns + v * slant * streak_columns / (TAU * length(offset.xz));

	// Streaks: each column has its own phase and speed; short bright dashes fall through it.
	float column = floor(column_u);
	float column_seed = hash(vec2(column, 3.7));
	float phase = v * 0.35 + TIME * fall_speed * 0.35 * (0.8 + 0.4 * column_seed) + column_seed * 10.0;
	float dash = smoothstep(0.0, 0.08, fract(phase)) * (1.0 - smoothstep(0.08, 0.35, fract(phase)));
	float thin = 1.0 - abs(fract(column_u) - 0.5) * 2.0;
	float streaks = dash * thin * thin * step(0.45, column_seed);

	// Sheets: large soft bands of heavier rain drifting with the wind. Sampled
	// on the unit direction (seamless all the way around).
	vec2 around_dir = normalize(offset.xz + vec2(1e-4)) * 6.0 - wind * TIME * 0.01;
	vec2 sheet_uv = around_dir + vec2(v * 0.04 + TIME * 0.25);
	float sheets = smoothstep(0.35, 0.85, value_noise(sheet_uv) * 0.7 + value_noise(sheet_uv * 3.1 + 7.7) * 0.3);

	// Soft fade against geometry behind the curtain.
	float scene_depth = texture(depth_texture, SCREEN_UV).r;
	vec4 scene_view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	float behind = (-scene_view.z / scene_view.w) - (-VERTEX.z);
	float soft = smoothstep(0.0, soft_distance, behind);

	// Gentle fade toward the top and bottom of the cylinder.
	float height_fade = smoothstep(-10.0, 0.0, v) * (1.0 - smoothstep(4.0, 18.0, v));
	ALBEDO = curtain_color;
	ALPHA = intensity * height_fade * soft * (streaks * streak_opacity * (0.5 + sheets) + sheets * sheet_opacity * intensity);
}
