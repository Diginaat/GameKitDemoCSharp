// Rain impact, drawn for each impact particle (one per raindrop hitting a
// surface). The particle never moves; all motion is computed here from its
// lifetime, so an impact costs one particle.
//
// mode 0 (ring):  a thin ring that expands on the surface. Only on wet
//                 surfaces, like a real puddle; dry ground just gets darker.
// mode 1 (crown): droplets thrown up and pulled back by gravity, each drawn
//                 stretched along its velocity (motion blur). The mesh has one
//                 quad per droplet; COLOR.r holds the droplet index (0..1).
//                 Small drops make no crown; crown size varies per impact.
// mode 2 (mist):  a faint puff of spray that rises and spreads, so heavy rain
//                 builds a low ground mist.
// mode 3 (sheet): the crown itself: a thin translucent cup of water that rises
//                 from the impact, flares out with a jagged rim and collapses,
//                 refracting the scene behind it. The mesh is an open cylinder;
//                 UV.x is the angle around it (0..1), UV.y the height (0..1).
// Ring and crown are lit, so they pick up sky reflections and lightning.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform, diffuse_lambert, specular_schlick_ggx;

global uniform float weather_wetness;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform int mode = 0;
uniform vec3 water_color : source_color = vec3(0.62, 0.68, 0.75);
uniform float opacity : hint_range(0.0, 1.0) = 0.02;
// Small self-lit term for light scattered by the water, so impacts read in dark storms.
uniform float glint : hint_range(0.0, 1.0) = 0.15;
uniform float roughness : hint_range(0.0, 1.0) = 0.02;
// Particle lifetime in seconds; must match the emitter.
uniform float lifetime = 0.42;
uniform float ring_max_radius = 0.05;
uniform float droplet_size = 0.004;
uniform float droplet_speed = 1.8;
uniform float droplet_rise = 1.3;
// Fraction of impacts that throw up a crown (the rest are small drops: ring only).
uniform float crown_chance : hint_range(0.0, 1.0) = 0.4;
// Simulated camera shutter for the droplet motion blur, in seconds.
uniform float motion_blur = 0.02;
uniform float crown_radius = 0.04;
uniform float crown_height = 0.06;
uniform float crown_refraction = 0.2;
uniform float mist_size = 0.20;
uniform float mist_opacity : hint_range(0.0, 1.0) = 0.05;

varying float life;
varying float fade;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void vertex() {
	// ParticleProcessMaterial stores lifetime progress (0..1) in INSTANCE_CUSTOM.y.
	life = INSTANCE_CUSTOM.y;
	vec3 origin = MODEL_MATRIX[3].xyz;
	float seed = hash(origin.xz);
	vec3 world = origin;
	vec3 normal = vec3(0.0, 1.0, 0.0);
	fade = 1.0;

	if (mode == 0) {
		float grow = 1.0 - (1.0 - life) * (1.0 - life);
		float radius = ring_max_radius * (0.5 + 0.5 * seed) * grow;
		world = origin + vec3(VERTEX.x * radius * 2.0, 0.003, VERTEX.z * radius * 2.0);
		fade = clamp(weather_wetness * 1.5 - 0.2, 0.0, 1.0);
	} else if (mode == 1) {
		float index = COLOR.r;
		// Small drops don't splash a crown, and crowns vary in droplet count.
		bool has_crown = seed < crown_chance;
		bool has_droplet = hash(vec2(index, seed)) < 0.4 + 0.6 * fract(seed * 7.3);
		// Droplets are thrown up from the crown's rim, mostly upward.
		float angle = (index + seed) * TAU;
		float speed = droplet_speed * (0.25 + 0.45 * hash(vec2(index, seed + 1.0)));
		float rise = droplet_rise * (0.7 + 0.5 * hash(vec2(seed, index)));
		float t = life * lifetime;
		vec3 velocity = vec3(cos(angle) * speed, rise - 9.8 * t, sin(angle) * speed);
		vec3 rim = vec3(cos(angle), 0.0, sin(angle)) * crown_radius;
		vec3 offset = rim + vec3(cos(angle) * speed * t, crown_height * 0.6 + rise * t - 4.9 * t * t, sin(angle) * speed * t);
		fade = (has_crown && has_droplet && offset.y > -0.005) ? 1.0 : 0.0;
		offset.y = max(offset.y, 0.0);

		// Quad stretched along the velocity and turned to face the camera.
		vec3 center = origin + offset;
		vec3 along = normalize(velocity + vec3(0.0, 1e-4, 0.0));
		vec3 to_camera = normalize(CAMERA_POSITION_WORLD - center);
		vec3 across = normalize(cross(along, to_camera));
		float width = droplet_size * (0.6 + 0.6 * hash(vec2(seed, index + 2.0)));
		float stretch = width + length(velocity) * motion_blur;
		world = center + across * VERTEX.x * width + along * VERTEX.y * stretch;
		normal = to_camera;
	} else if (mode == 3) {
		// Crown sheet: an open cup that grows, flares and collapses.
		float size = 0.7 + 0.6 * hash(vec2(seed, 9.1));
		float grow = 1.0 - (1.0 - life) * (1.0 - life);
		float angle = UV.x * TAU;
		// Jagged rim: each column of the mesh gets its own height.
		float spike = 0.55 + 0.45 * hash(vec2(floor(UV.x * 64.0 + 0.5), seed));
		float height = crown_height * size * sin(PI * min(life * 1.3, 1.0)) * mix(1.0, spike, UV.y);
		float radius = crown_radius * size * (0.35 + 0.65 * grow) * mix(1.0, 1.6, UV.y);
		vec3 out_dir = vec3(cos(angle), 0.0, sin(angle));
		world = origin + out_dir * radius + vec3(0.0, UV.y * height, 0.0);
		normal = normalize(out_dir + vec3(0.0, 0.4, 0.0));
		fade = (seed < crown_chance) ? clamp(weather_wetness * 2.0, 0.0, 1.0) : 0.0;
	} else {
		// Mist puff: camera-facing, rises a little and spreads.
		float size = mist_size * (0.3 + 0.7 * life) * (0.6 + 0.6 * seed);
		vec3 right = INV_VIEW_MATRIX[0].xyz;
		vec3 up = INV_VIEW_MATRIX[1].xyz;
		world = origin + vec3(0.0, 0.05 + 0.15 * life, 0.0) + (right * VERTEX.x + up * VERTEX.y) * size;
		normal = INV_VIEW_MATRIX[2].xyz;
	}

	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	NORMAL = (VIEW_MATRIX * vec4(normal, 0.0)).xyz;
}

void fragment() {
	ALBEDO = water_color;
	EMISSION = water_color * glint;
	ROUGHNESS = roughness;
	SPECULAR = 0.5;
	float r = length(UV * 2.0 - 1.0);
	if (mode == 0) {
		float ring = smoothstep(0.8, 0.92, r) * (1.0 - smoothstep(0.92, 1.0, r));
		ALPHA = opacity * ring * (1.0 - life) * fade;
	} else if (mode == 1) {
		ALPHA = opacity * (1.0 - smoothstep(0.35, 1.0, r)) * (1.0 - life * life) * fade;
	} else if (mode == 3) {
		// Thin water: refracts the scene behind it, brightest along the rim.
		vec3 behind = textureLod(screen_texture, SCREEN_UV - NORMAL.xy * crown_refraction, 0.0).rgb;
		EMISSION = behind * 1.1 + water_color * glint;
		ALBEDO = water_color * 0.2;
		float rim = smoothstep(0.6, 1.0, UV.y);
		ALPHA = opacity * mix(0.25, 0.9, rim) * (1.0 - life * life) * fade;
	} else {
		ALPHA = mist_opacity * (1.0 - smoothstep(0.0, 1.0, r)) * sin(life * PI);
	}
}
