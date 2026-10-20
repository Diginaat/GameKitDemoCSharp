// Weather wetness for spatial shaders (Sky Creator style "material FX").
//
// As rain starts, droplet splatter spots darken dry ground; then the whole
// surface gets a wet film (darker, glossy), puddles form in low spots on flat
// ground (mirror-smooth, with rain ripples), and water trickles down walls.
// Everything is procedural and keyed to world position, so it works on any
// mesh. Surfaces with something above them (roofs, overhangs, tables) stay dry,
// using the rain occlusion height map from RainOcclusion.
// Requires the global shader parameters weather_wetness, weather_rain,
// weather_rain_occlusion and weather_occlusion_area (declared in project.godot
// under [shader_globals]); WeatherSystem drives them.
//
// Usage in a spatial shader:
//   #include "res://addons/gamekit_weather/shaders/weather_wetness.gdshaderinc"
//   ...in fragment(), with world-space position and normal:
//   vec3 ripple = weather_apply_wetness(world_pos, world_normal, 1.0, TIME, albedo, roughness);
//   NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(ripple, 0.0)).xyz);

global uniform float weather_wetness;
global uniform float weather_rain;
// Height (m) of the highest surface above each point, top-down around the player.
global uniform sampler2D weather_rain_occlusion;
// x, y = map center (world x, z), z = map width in meters, w = 1 when valid.
global uniform vec4 weather_occlusion_area;

// 1 where rain reaches the surface, 0 under cover. Walls sample just outside
// their face, so their outer side still gets wet.
float weather_rain_exposure(vec3 world_pos, vec3 world_normal) {
	if (weather_occlusion_area.w < 0.5) {
		return 1.0;
	}
	vec2 sample_xz = world_pos.xz + world_normal.xz * 0.2;
	vec2 uv = (sample_xz - weather_occlusion_area.xy) / weather_occlusion_area.z + 0.5;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
		return 1.0;
	}
	float cover_height = texture(weather_rain_occlusion, uv).r;
	return 1.0 - smoothstep(0.1, 0.35, cover_height - world_pos.y);
}

vec2 weather_hash22(vec2 p) {
	vec3 a = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	a += dot(a, a.yzx + 33.33);
	return fract((a.xx + a.yz) * a.zy);
}

float weather_hash12(vec2 p) {
	vec3 a = fract(vec3(p.xyx) * 0.1031);
	a += dot(a, a.yzx + 33.33);
	return fract((a.x + a.y) * a.z);
}

float weather_value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(weather_hash12(i), weather_hash12(i + vec2(1.0, 0.0)), f.x),
			mix(weather_hash12(i + vec2(0.0, 1.0)), weather_hash12(i + vec2(1.0, 1.0)), f.x), f.y);
}

// One layer of expanding ripple rings, one per cell. Returns a horizontal
// (world xz) normal offset.
vec2 weather_ripple_layer(vec2 p, float time) {
	vec2 cell = floor(p);
	vec2 h = weather_hash22(cell);
	vec2 to_center = fract(p) - (0.25 + 0.5 * h);
	float t = fract(time + h.x);
	float r = length(to_center);
	float ring = clamp(1.0 - abs(r - t * 0.3) * 30.0, 0.0, 1.0);
	return to_center / max(r, 1e-4) * ring * (1.0 - t);
}

// 0..1 puddle coverage on flat ground; grows from low spots as wetness rises.
float weather_puddle_mask(vec3 world_pos, float flat_ground) {
	float noise = weather_value_noise(world_pos.xz * 0.3) * 0.7 + weather_value_noise(world_pos.xz * 1.3) * 0.3;
	// Heavy rain pools most of the flat ground, leaving raised patches drier.
	float coverage = smoothstep(0.5, 1.0, weather_wetness) * 0.7;
	return flat_ground * smoothstep(1.0 - coverage - 0.04, 1.0 - coverage + 0.04, 1.0 - noise);
}

// One layer of landing drops. Each cell lands a drop at its own moment in a
// repeating cycle: the spot appears at full size instantly (the impact), then
// evaporates. Every cycle picks a new place, size and chance, so spots keep
// popping up across the ground instead of fading in where they are.
float weather_splatter_layer(vec2 p, float time, float density) {
	const float PERIOD = 1.6;
	vec2 cell = floor(p);
	float phase = weather_hash12(cell + 11.0);
	float cycle = floor(time / PERIOD + phase);
	float age = fract(time / PERIOD + phase);
	if (weather_hash12(cell + cycle * 3.7) > density) {
		return 0.0;
	}
	vec2 h = weather_hash22(cell + cycle * 17.31);
	float radius = 0.05 + 0.12 * h.y;
	float d = length(fract(p) - (0.15 + 0.7 * h.yx));
	float evaporate = 1.0 - smoothstep(0.35, 1.0, age);
	return (1.0 - smoothstep(radius, radius + 0.04, d)) * evaporate;
}

// Droplet splatter while rain starts on dry ground: drops landing as dark
// spots. Two overlapping layers at different scales so no grid shows.
float weather_splatter(vec3 world_pos, float time) {
	vec2 p = world_pos.xz;
	// Ramps up smoothly from the first drops.
	float density = 0.75 * smoothstep(0.0, 0.5, weather_rain);
	return max(weather_splatter_layer(p * 4.3, time, density), weather_splatter_layer(p * 9.7 + 3.1, time * 1.3, density));
}

// Water trickling down vertical surfaces. Returns 0..1 rivulet coverage.
float weather_wall_streaks(vec3 world_pos, vec3 world_normal, float time) {
	vec3 tangent = normalize(cross(world_normal, vec3(0.0, 1.0, 0.0)) + vec3(1e-4));
	float u = dot(world_pos, tangent) * 18.0;
	float column = floor(u);
	float seed = weather_hash12(vec2(column, 5.1));
	// Thin wandering line per column with drops sliding down it.
	float wobble = sin(world_pos.y * 3.0 + seed * 20.0) * 0.15;
	float line = 1.0 - smoothstep(0.0, 0.18, abs(fract(u + wobble) - 0.5));
	float flow = fract(world_pos.y * 0.6 + time * (0.3 + 0.5 * seed) + seed * 7.0);
	float drop = smoothstep(0.0, 0.1, flow) * (1.0 - smoothstep(0.1, 0.6, flow));
	return line * mix(0.35, 1.0, drop) * step(0.4, seed);
}

// Modifies albedo and roughness in place; returns a world-space normal offset
// for ripples. porosity 0..1: how much the surface darkens when wet.
vec3 weather_apply_wetness(vec3 world_pos, vec3 world_normal, float porosity, float time, inout vec3 albedo, inout float roughness) {
	if (weather_wetness <= 0.001) {
		return vec3(0.0);
	}
	float exposure = weather_rain_exposure(world_pos, world_normal);
	if (exposure <= 0.001) {
		return vec3(0.0);
	}
	float flat_ground = smoothstep(0.7, 0.95, world_normal.y);
	float wall = 1.0 - smoothstep(0.2, 0.5, abs(world_normal.y));

	// The wet film spreads, and later dries, in irregular patches rather than
	// evenly: each spot has its own wetness threshold from low-frequency noise.
	float patch_noise = weather_value_noise(world_pos.xz * 0.7) * 0.6 + weather_value_noise(world_pos.xz * 2.9) * 0.4;
	float threshold = 0.15 + 0.65 * patch_noise;
	float film = smoothstep(threshold, threshold + 0.12, weather_wetness);
	// Splatter only while drops are falling, and only on dry-ish ground: it
	// fades out quickly as the wet film takes over.
	float splatter_fade = 1.0 - smoothstep(0.12, 0.35, weather_wetness);
	float splatter = (weather_rain > 0.01 && splatter_fade > 0.0) ? weather_splatter(world_pos, time) * flat_ground * splatter_fade : 0.0;
	float wet = max(film, splatter) * mix(0.55, 1.0, flat_ground) * exposure;
	float puddle = weather_puddle_mask(world_pos, flat_ground) * exposure;
	float streaks = wall * weather_wetness * weather_rain * weather_wall_streaks(world_pos, world_normal, time) * exposure;

	albedo *= 1.0 - 0.45 * porosity * max(wet, streaks);
	// Wet ground is satin; puddles are mirrors, so they read as distinct pools.
	albedo *= 1.0 - 0.5 * puddle;
	roughness = mix(roughness, 0.3, wet);
	roughness = mix(roughness, 0.1, streaks);
	roughness = mix(roughness, 0.02, puddle);

	// Rain ripples: strong in puddles, faint on the wet film.
	float ripple_strength = weather_rain * flat_ground * mix(0.15 * film, 1.0, puddle) * exposure;
	if (ripple_strength <= 0.001) {
		return vec3(0.0);
	}
	vec2 p = world_pos.xz * 4.0;
	vec2 ripple = weather_ripple_layer(p, time * 1.4) + weather_ripple_layer(p * 1.3 + 17.0, time * 1.6 + 0.5);
	return vec3(ripple.x, 0.0, ripple.y) * 0.7 * ripple_strength;
}
