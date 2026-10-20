shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

// Water surface material for PhysXWaterSurface3D (assigned as its water_material). The node binds the
// simulation textures and transform uniforms below; the rest are look settings.
//
// Shading model, per pixel:
// - Normal: ripple layer slope + FFT ocean slope + small procedural capillary waves.
// - Reflection: the engine's GGX/Schlick specular with F0 = 0.02 (water, n = 1.33) reflects sky and sun.
// - Transmission: the opaque scene behind the surface, refracted by the normal, attenuated per channel
//   by Beer-Lambert absorption over the underwater path length, plus in-scattered water colour.
//   Weighted by (1 - Fresnel) so grazing views turn into reflections.
// - From below: Snell's window -- refraction inside the critical angle, total internal reflection outside.
// Underwater fog for the rest of the scene is the camera's job (see PlayerHud).

// --- Bound by PhysXWaterSurface3D ---
uniform sampler2D ripple_height_tex : hint_default_black, filter_linear, repeat_disable;
uniform sampler2D ocean_height_tex : hint_default_black, filter_linear, repeat_enable;
uniform sampler2D ocean_disp_tex : hint_default_black, filter_linear, repeat_enable;
uniform sampler2D ocean_deriv_tex : hint_default_black, filter_linear, repeat_enable;
uniform float choppiness = 1.0;
uniform vec2 ripple_domain_size = vec2(10.0, 10.0);
uniform vec2 ocean_domain_size = vec2(10.0, 10.0);
uniform vec2 grid_center = vec2(0.0);
uniform vec2 mesh_offset = vec2(0.0);
uniform float water_level = 0.0;
// Animation clock set by the node while the game runs (0 in the editor, so the editor does not redraw
// every frame).
uniform float water_time = 0.0;
// Set by WaterVolume: still-water level minus the simulation's measured mean. The ripple solver does not
// conserve volume (proxies and splashes shift its mean), so the drawn surface is re-anchored to gameplay.
uniform float level_correction = 0.0;

// --- Look ---
// Absorption coefficients per metre (red is absorbed fastest, which is why deep water turns blue-green).
uniform vec3 absorption = vec3(0.46, 0.09, 0.06);
// Colour light scatters back toward the viewer from inside the water, reached once it is optically deep.
uniform vec3 scatter_color : source_color = vec3(0.015, 0.11, 0.14);
// Colour seen from below outside Snell's window (total internal reflection of the deep water).
// Seen from below outside Snell's window the surface mirrors the pool, which is mostly water far away:
// the underwater colour (underwater_view.gdshader water_color), lit a little by the waves.
uniform vec3 underside_color : source_color = vec3(0.1, 0.42, 0.47);
// Extra capillary detail on the underside, which breaks up the edge of Snell's window as in real water.
uniform float underside_detail = 1.0;
// Refractive index used only for the size of Snell's window. Water is 1.333 (window 48.6 degrees from
// straight up); lower widens it for gameplay visibility of the world above (1.1: about 65 degrees).
uniform float window_ior : hint_range(1.0, 1.333) = 1.1;
// Planar reflection of the pool for the underside, rendered by PlanarReflection (mirrored camera).
uniform sampler2D reflection_tex : source_color, hint_default_black, filter_linear, repeat_disable;
uniform mat4 reflection_view_projection;
// Set by PlanarReflection from its PlanarReflectionSettings resource.
uniform float reflection_strength : hint_range(0.0, 1.0) = 1.0;
uniform float reflection_distortion = 0.04;
uniform float reflection_edge_fade = 0.05;
// How much of the wave slope bends the edge of Snell's window (0 = perfect circle).
uniform float window_wave : hint_range(0.0, 1.0) = 0.3;
uniform float roughness_val : hint_range(0.0, 1.0) = 0.03;
uniform float refraction_strength : hint_range(0.0, 0.2) = 0.05;
// Small wind-driven capillary waves on top of the simulation, for sparkle on otherwise calm water.
uniform float detail_slope : hint_range(0.0, 0.5) = 0.08;
uniform float detail_scale = 1.0;
// Fades the water out over this thickness (m) where it meets walls and objects, instead of a hard line.
uniform float edge_softness : hint_range(0.001, 0.2) = 0.03;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

varying vec2 v_rest_xz;
varying vec3 v_world_position;

void vertex() {
	VERTEX += (inverse(MODEL_MATRIX) * vec4(mesh_offset.x, 0.0, mesh_offset.y, 0.0)).xyz;
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_rest_xz = world_pos.xz;
	vec2 cuv_r = clamp((world_pos.xz - grid_center) / ripple_domain_size + 0.5, vec2(0.0), vec2(1.0));
	vec2 cuv_o = world_pos.xz / ocean_domain_size + 0.5;
	// Without a running simulation (editor, no compute support) the ripple texture is the tiny default:
	// rest at the still-water level instead of the node's origin.
	float ripple = textureSize(ripple_height_tex, 0).x > 4 ? textureLod(ripple_height_tex, cuv_r, 0.0).r + level_correction : water_level;
	// A diverged simulation must not tear the mesh apart or make it vanish: fall back to still water.
	if (isnan(ripple) || isinf(ripple) || abs(ripple - water_level) > 1.0) {
		ripple = water_level;
	}
	// Horizontal chop fades out at the domain edge, so the surface stays against the pool walls.
	vec2 edge = min(cuv_r, 1.0 - cuv_r) * ripple_domain_size;
	float wall_fade = smoothstep(0.0, 0.3, min(edge.x, edge.y));
	vec2 d = -choppiness * wall_fade * textureLod(ocean_disp_tex, cuv_o, 0.0).rg;
	VERTEX += vec3(d.x, ripple + textureLod(ocean_height_tex, cuv_o, 0.0).r, d.y);
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Slope (dh/dx, dh/dz) of a few short directional waves.
vec2 detail_waves(vec2 p, float t) {
	const vec4 dir_x = vec4(0.8, -0.6, 0.3, -0.95);
	const vec4 dir_z = vec4(0.6, 0.8, -0.95, -0.3);
	const vec4 k = vec4(9.0, 13.0, 21.0, 31.0);
	const vec4 amp = vec4(0.5, 0.3, 0.15, 0.08);
	vec4 phase = (p.x * dir_x + p.y * dir_z) * k * detail_scale - t * sqrt(9.81 * k);
	vec4 c = cos(phase) * amp;
	return vec2(dot(c, dir_x), dot(c, dir_z)) * detail_slope;
}

vec3 surface_normal(vec2 world_xz, float detail_gain) {
	vec2 cuv_r = clamp((world_xz - grid_center) / ripple_domain_size + 0.5, vec2(0.0), vec2(1.0));
	vec2 cuv_o = world_xz / ocean_domain_size + 0.5;
	const float e = 0.1;
	vec2 du = vec2(e) / ripple_domain_size;
	vec2 slope = vec2(
		texture(ripple_height_tex, cuv_r + vec2(du.x, 0.0)).r - texture(ripple_height_tex, cuv_r - vec2(du.x, 0.0)).r,
		texture(ripple_height_tex, cuv_r + vec2(0.0, du.y)).r - texture(ripple_height_tex, cuv_r - vec2(0.0, du.y)).r) / (2.0 * e);
	slope += texture(ocean_deriv_tex, cuv_o).xy;
	slope += detail_waves(world_xz, water_time) * detail_gain;
	return normalize(vec3(-slope.x, 1.0, -slope.y));
}

// The pool mirrored in the underside at world point p; water colour where the reflection has nothing.
vec3 underside_reflection(vec3 p, vec3 n_world) {
	vec3 fallback = underside_color * (0.8 + 2.0 * length(n_world.xz));
	if (reflection_strength <= 0.0 || textureSize(reflection_tex, 0).x <= 4) {
		return fallback;
	}
	vec4 clip = reflection_view_projection * vec4(p, 1.0);
	if (clip.w <= 0.0) {
		return fallback;
	}
	vec2 uv = clip.xy / clip.w * vec2(0.5, -0.5) + 0.5;
	uv += n_world.xz * reflection_distortion;
	vec2 inside = min(uv, 1.0 - uv);
	float fade = smoothstep(0.0, reflection_edge_fade, min(inside.x, inside.y)) * reflection_strength;
	return mix(fallback, texture(reflection_tex, clamp(uv, vec2(0.001), vec2(0.999))).rgb, fade);
}

float linear_depth(vec2 uv, float raw, mat4 inv_projection) {
	vec4 view = inv_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
	return -view.z / view.w;
}

void fragment() {
	// Which side is decided per fragment against the displaced surface's own (upward) face normal, not
	// a flat level or FRONT_FACING: with the eye within a wave height of the surface, a nearby trough is
	// seen from above while a crest is seen from below. The node's generated mesh does not guarantee a winding.
	vec3 face = cross(dFdy(v_world_position), dFdx(v_world_position));
	face *= sign(face.y) + step(0.0, -abs(face.y));
	bool from_below = dot(CAMERA_POSITION_WORLD - v_world_position, face) < 0.0;
	vec3 n_world = surface_normal(v_rest_xz, from_below ? underside_detail : 1.0);
	if (from_below) {
		n_world = -n_world;
	}
	vec3 n_view = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);
	NORMAL = n_view;
	ROUGHNESS = roughness_val;
	METALLIC = 0.0;
	// F0 = 0.16 * specular^2 = 0.02 for water.
	SPECULAR = 0.354;
	ALBEDO = vec3(0.0);

	vec3 view_dir = normalize(VIEW);
	float surface_depth = -VERTEX.z;
	// Refraction: offset the background lookup by the waves' tilt away from flat water (flat water must
	// not shift the image), less with distance, and none at the screen edges where the lookup would clamp.
	vec3 flat_view = normalize((VIEW_MATRIX * vec4(0.0, from_below ? -1.0 : 1.0, 0.0, 0.0)).xyz);
	vec2 offset = (n_view.xy - flat_view.xy) * refraction_strength / max(surface_depth * 0.25, 1.0);
	vec2 screen_edge = min(SCREEN_UV, 1.0 - SCREEN_UV);
	offset *= smoothstep(0.0, 0.08, min(screen_edge.x, screen_edge.y));
	vec2 uv = clamp(SCREEN_UV + offset, vec2(0.001), vec2(0.999));
	float scene_depth = linear_depth(uv, texture(depth_tex, uv).r, INV_PROJECTION_MATRIX);
	// Anything in front of the water must not be smeared into it: fall back to the straight lookup.
	if (scene_depth < surface_depth) {
		uv = SCREEN_UV;
		scene_depth = linear_depth(uv, texture(depth_tex, uv).r, INV_PROJECTION_MATRIX);
	}
	vec3 background = textureLod(screen_tex, uv, 0.0).rgb;
	// Unrefracted thickness for the soft contact, so the edge does not wobble with the refraction.
	float contact = linear_depth(SCREEN_UV, texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX) - surface_depth;

	float cos_i = clamp(dot(n_view, view_dir), 0.0, 1.0);
	vec3 transmitted;
	float fresnel;
	if (!from_below) {
		// Path length through the water along the view ray (view depth converted to ray length).
		float path = max(scene_depth - surface_depth, 0.0) / max(-normalize(VERTEX).z, 1e-3);
		vec3 transmittance = exp(-absorption * path);
		float optical = 1.0 - dot(transmittance, vec3(0.333));
		transmitted = background * transmittance + scatter_color * optical;
		fresnel = 0.02 + 0.98 * pow(1.0 - cos_i, 5.0);
	} else {
		// Looking up: light only gets through inside Snell's window (critical angle ~48.6 degrees); outside
		// it the surface mirrors the pool. Both are emitted here: the engine's specular would reflect the
		// sky, which is wrong from below. Absorption between the eye and the surface is applied by
		// underwater_view.gdshader, so what is above the water stays visible through the window.
		vec3 mirror = underside_reflection(v_world_position, n_world);
		// The window's edge uses a damped normal: the full wave slope swung it into large lobes, while in
		// real water it stays a near-circular disc with a rippled rim.
		vec3 n_window = normalize(mix(flat_view, n_view, window_wave));
		vec3 t = refract(-view_dir, n_window, window_ior);
		float reflectance = 1.0;
		if (dot(t, t) > 1e-6) {
			// Schlick from the denser side uses the transmitted angle.
			reflectance = 0.02 + 0.98 * pow(1.0 - clamp(dot(-t, n_window), 0.0, 1.0), 5.0);
		}
		transmitted = mix(background, mirror, reflectance);
		fresnel = 0.0;
		SPECULAR = 0.0;
	}
	// Reflections come from the engine's specular term; transmission is emitted directly.
	EMISSION = transmitted * (1.0 - fresnel);
	ALPHA = smoothstep(0.0, edge_softness, contact);
}
