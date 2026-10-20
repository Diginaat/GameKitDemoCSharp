shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Opaque pool tile (procedural, world-space) with procedural sunlight caustics below the water line.
// Each point is projected along the sun direction onto the water surface; the caustic net is sampled
// there, so floor and walls line up the way real caustics do. Absorption by the water above is done
// by the water shader, not here.
//
// PhysXWaterSurface3D can also render a caustics map, but on this engine build its light channel stays
// empty (also in the example project's round_pool scene), so it is not used.

uniform vec3 tile_color : source_color = vec3(0.78, 0.88, 0.9);
uniform vec3 grout_color : source_color = vec3(0.55, 0.6, 0.6);
uniform float tile_size = 0.25;
uniform float grout_width = 0.012;
uniform float roughness_val : hint_range(0.0, 1.0) = 0.35;

// World Y of the still water and its XZ footprint (min x, min z, max x, max z).
uniform float water_surface_y = 0.0;
uniform vec4 water_rect = vec4(-1e6, -1e6, 1e6, 1e6);
// Direction sunlight travels (towards the ground).
uniform vec3 sun_direction = vec3(-0.35, -1.0, -0.25);
uniform float caustic_strength = 1.2;
// Caustic cells per metre at the surface.
uniform float caustic_scale = 1.6;
uniform float caustic_speed = 0.6;
// Caustics fade in over this depth below the surface, and blur with depth.
uniform float caustic_fade_depth = 0.4;

varying vec3 v_world_position;
varying vec3 v_world_normal;
// Caustic light for this pixel; applied in light() so the sun's shadow masks it.
varying float v_caustic;

void vertex() {
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// Distance between the nearest and second-nearest animated cell points: near 0 on cell borders.
float cell_border(vec2 p, float t) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float d1 = 8.0;
	float d2 = 8.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 g = vec2(float(x), float(y));
			vec2 o = 0.5 + 0.4 * sin(t + 6.2831 * hash2(i + g));
			float d = length(g + o - f);
			if (d < d1) {
				d2 = d1;
				d1 = d;
			} else if (d < d2) {
				d2 = d;
			}
		}
	}
	return d2 - d1;
}

// Bright caustic net: two drifting layers of focused lines, softer with depth.
float caustics(vec2 p, float t, float depth) {
	float width = 0.08 + 0.05 * depth;
	float a = 1.0 - smoothstep(0.0, width, cell_border(p * caustic_scale + vec2(t * 0.11, t * 0.05), t * caustic_speed));
	float b = 1.0 - smoothstep(0.0, width * 1.3, cell_border(p * caustic_scale * 1.37 - vec2(t * 0.07, -t * 0.09) + 17.0, t * caustic_speed * 1.2));
	return a * a + b * b * 0.6;
}

// Triplanar tile grid: 1 inside a tile, 0 in the grout.
float tile_mask(vec3 p, vec3 n) {
	vec3 w = abs(n);
	vec2 uv = w.y > max(w.x, w.z) ? p.xz : (w.x > w.z ? p.zy : p.xy);
	vec2 g = abs(fract(uv / tile_size + 0.5) - 0.5) * tile_size;
	vec2 aa = fwidth(uv) + 1e-4;
	vec2 t = smoothstep(vec2(grout_width * 0.5), vec2(grout_width * 0.5) + aa, g);
	return t.x * t.y;
}

void fragment() {
	vec3 n = normalize(v_world_normal);
	float tile = tile_mask(v_world_position, n);
	ALBEDO = mix(grout_color, tile_color, tile);
	ROUGHNESS = mix(0.8, roughness_val, tile);
	METALLIC = 0.0;

	v_caustic = 0.0;
	float depth = water_surface_y - v_world_position.y;
	vec3 sun = normalize(sun_direction);
	if (depth > 0.0 && sun.y < -0.05) {
		// Where the sun ray reaching this point entered the water.
		vec3 entry = v_world_position - sun * (depth / -sun.y);
		bool through_water = all(greaterThanEqual(entry.xz, water_rect.xy)) && all(lessThanEqual(entry.xz, water_rect.zw));
		float incidence = max(dot(n, -sun), 0.0);
		if (through_water && incidence > 0.0) {
			float fade = smoothstep(0.0, caustic_fade_depth, depth);
			v_caustic = caustics(entry.xz, TIME, depth) * incidence * fade * caustic_strength;
		}
	}
}

void light() {
	DIFFUSE_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI;
	if (LIGHT_IS_DIRECTIONAL) {
		// Caustics are focused sunlight: shadowed like it.
		DIFFUSE_LIGHT += v_caustic * ATTENUATION * LIGHT_COLOR / PI;
	}
}
