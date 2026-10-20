// Photographic lens pass for gamekit_lens (CameraLens): distortion, chromatic aberration, depth of
// field, halation, highlight bloom, grading, LUT, vignette and sensor grain.
// Based on EpicCameraX real_camera_post_fx.gdshader (MIT, Diginaat / DevKine; see LICENSE-EpicCameraX.txt).
// Changes: distortion applied when enabled (the original test was inverted), unused lens flare code
// removed (flares are lens_flare.gdshader), reversed-Z depth, linear-light grading, full-screen in
// clip space. Drawn first among transparents (see CameraLens), so transparents get no lens effects.
shader_type spatial;
render_mode unshaded, fog_disabled, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform sampler2D lut_texture : source_color, repeat_disable, filter_linear;

// -------------------------
// TOGGLES
// -------------------------
uniform bool enable_vignette = true;
uniform bool enable_chromatic_aberration = true;
uniform bool enable_distortion = true;
uniform bool enable_grain = true;
uniform bool enable_color_grading = true;
uniform bool enable_highlight_bloom = true;
uniform bool enable_lut = false;
uniform bool enable_halation = true;

// -------------------------
// VIGNETTE
// size = where darkening starts
// smoothness = feather amount
// -------------------------
uniform float vignette_intensity : hint_range(0.0, 2.0, 0.01) = 0.35;
uniform float vignette_size : hint_range(0.0, 2.5, 0.01) = 0.75;
uniform float vignette_smoothness : hint_range(0.001, 2.0, 0.01) = 0.35;
uniform float vignette_roundness : hint_range(0.25, 4.0, 0.01) = 1.0;

// -------------------------
// CHROMATIC ABERRATION
// Real lenses mostly show lateral CA toward the frame edges,
// with green staying comparatively anchored and stronger fringing
// only on high-contrast edges / bright transitions.
// -------------------------
uniform float chromatic_aberration : hint_range(0.0, 8.0, 0.01) = 1.6;
uniform float ca_edge_start : hint_range(0.0, 1.0, 0.01) = 0.62;
uniform float ca_blur : hint_range(0.0, 4.0, 0.01) = 0.85;
uniform float ca_radial_power : hint_range(0.5, 4.0, 0.01) = 1.85;
uniform float ca_edge_response : hint_range(0.25, 4.0, 0.01) = 1.35;
uniform float ca_highlight_fringe : hint_range(0.0, 1.0, 0.01) = 0.22;

// -------------------------
// DISTORTION
// positive = barrel, negative = pincushion
// -------------------------
uniform float distortion : hint_range(-1.0, 1.0, 0.001) = 0.035;
uniform float distortion_cubic : hint_range(-1.0, 1.0, 0.001) = 0.015;
uniform float distortion_overscan : hint_range(1.0, 2.0, 0.001) = 1.08;
uniform vec2 distortion_center = vec2(0.5, 0.5);


// -------------------------
// FILM GRAIN
// overall grain_strength is global multiplier
// shadows / mids / highlights let you bias by luma
// -------------------------
uniform bool enable_rgb_digital_noise = false;
uniform float grain_strength : hint_range(0.0, 1.0, 0.001) = 0.02;
uniform float grain_size : hint_range(0.25, 8.0, 0.01) = 1.5;
uniform float grain_speed : hint_range(0.0, 20.0, 0.01) = 6.0;
uniform float grain_shadows : hint_range(0.0, 4.0, 0.01) = 1.2;
uniform float grain_mids : hint_range(0.0, 4.0, 0.01) = 1.0;
uniform float grain_highlights : hint_range(0.0, 4.0, 0.01) = 0.6;

// -------------------------
// BASIC COLOR GRADING
// -------------------------
uniform float exposure : hint_range(0.0, 3.0, 0.01) = 1.0;
uniform float contrast : hint_range(0.0, 2.0, 0.01) = 1.05;
uniform float saturation : hint_range(0.0, 2.0, 0.01) = 1.0;
uniform float temperature : hint_range(-1.0, 1.0, 0.001) = 0.0;
uniform float tint : hint_range(-1.0, 1.0, 0.001) = 0.0;
uniform vec3 lift : source_color = vec3(0.0, 0.0, 0.0);
uniform vec3 gamma : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3 gain : source_color = vec3(1.0, 1.0, 1.0);

// -------------------------
// HIGHLIGHT BLOOM
// separate from base grading
// -------------------------
uniform float bloom_intensity : hint_range(0.0, 3.0, 0.01) = 0.35;
uniform float bloom_threshold : hint_range(0.0, 2.0, 0.01) = 1.0;
uniform float bloom_softness : hint_range(0.001, 2.0, 0.01) = 0.35;
uniform float bloom_radius : hint_range(0.0, 12.0, 0.01) = 4.0;
uniform vec3 bloom_tint : source_color = vec3(1.0, 1.0, 1.0);

// -------------------------
// LUT GRADING
// expects a 2D strip LUT:
// width = lut_size * lut_size
// height = lut_size
// -------------------------
uniform float lut_size : hint_range(2.0, 65.0, 1.0) = 33.0;
uniform float lut_strength : hint_range(0.0, 1.0, 0.01) = 1.0;

// -------------------------
// HALATION
// red/orange highlight bleed
// -------------------------
uniform float halation_intensity : hint_range(0.0, 3.0, 0.01) = 0.22;
uniform float halation_threshold : hint_range(0.0, 2.0, 0.01) = 1.0;
uniform float halation_softness : hint_range(0.001, 2.0, 0.01) = 0.35;
uniform float halation_radius : hint_range(0.0, 16.0, 0.01) = 5.5;
uniform vec3 halation_color : source_color = vec3(1.0, 0.35, 0.12);
uniform bool halation_depth_aware = true;
uniform float halation_depth_reference : hint_range(0.1, 500.0, 0.01) = 8.0;
uniform float halation_depth_curve : hint_range(0.1, 4.0, 0.01) = 1.0;
uniform float halation_depth_min_scale : hint_range(0.05, 4.0, 0.01) = 0.45;
uniform float halation_depth_max_scale : hint_range(0.05, 8.0, 0.01) = 2.2;
uniform float halation_depth_rejection : hint_range(0.01, 10.0, 0.01) = 1.25;

uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_linear_mipmap;

uniform bool enable_post_dof = false;
uniform float post_dof_amount : hint_range(0.0, 1.0, 0.01) = 0.1;
uniform bool post_dof_near_enabled = false;
uniform float post_dof_near_distance : hint_range(0.01, 100.0, 0.01) = 2.0;
uniform float post_dof_near_transition : hint_range(0.001, 50.0, 0.01) = 1.0;
uniform bool post_dof_far_enabled = true;
uniform float post_dof_far_distance : hint_range(0.01, 500.0, 0.01) = 10.0;
uniform float post_dof_far_transition : hint_range(0.001, 100.0, 0.01) = 5.0;
uniform float post_dof_near_clip : hint_range(0.001, 10.0, 0.001) = 0.05;
uniform float post_dof_far_clip : hint_range(1.0, 100000.0, 0.1) = 4000.0;



float luma(vec3 c) {
	return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

float max3(vec3 c) {
	return max(c.r, max(c.g, c.b));
}

vec2 get_screen_pixel_size() {
	return 1.0 / vec2(textureSize(screen_texture, 0));
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float hash12_seed(vec2 p, float seed) {
	return hash12(p + vec2(seed * 0.123, seed * 0.456));
}

float signed_noise(vec2 p, float seed) {
	return hash12_seed(p, seed) * 2.0 - 1.0;
}

float gaussianish_noise(vec2 p, float seed) {
	float n = 0.0;
	n += signed_noise(p, seed + 0.0);
	n += signed_noise(p, seed + 11.7);
	n += signed_noise(p, seed + 23.4);
	n += signed_noise(p, seed + 37.1);
	return n * 0.25;
}


float approx_gauss(vec2 p, float seed) {
	float n = 0.0;
	n += hash12_seed(p, seed + 0.0);
	n += hash12_seed(p, seed + 1.0);
	n += hash12_seed(p, seed + 2.0);
	n += hash12_seed(p, seed + 3.0);
	return (n * 0.25 - 0.5) * 2.0;
}

vec3 digital_camera_rgb_noise(vec2 p, vec3 color_srgb) {
	// Approximate linear signal from display color
	vec3 signal = pow(clamp(color_srgb, vec3(0.0), vec3(1.0)), vec3(2.2));
	float lum = luma(signal);

	// Frame index changes the value, not the position
	float frame = floor(TIME * max(grain_speed, 0.001) * 12.0);

	// Stable fixed-pattern sensor texture
	vec3 fixed_pattern = vec3(
		gaussianish_noise(p, 101.0),
		gaussianish_noise(p, 211.0),
		gaussianish_noise(p, 307.0)
	);

	// Temporal luminance noise
	float luma_temporal = gaussianish_noise(p, frame + 1.0);

	// Temporal RGB channel noise
	vec3 chroma_temporal = vec3(
		gaussianish_noise(p, frame + 13.0),
		gaussianish_noise(p, frame + 29.0),
		gaussianish_noise(p, frame + 47.0)
	);

	// Bayer-ish channel bias:
	// green cleaner, blue a bit noisier
	chroma_temporal *= vec3(1.00, 0.72, 1.18);
	fixed_pattern *= vec3(0.28, 0.18, 0.34);

	// Mix mostly luma noise with some RGB separation
	vec3 noise = vec3(luma_temporal) * 0.62
		+ chroma_temporal * 0.38
		+ fixed_pattern;

	// Stronger in darker regions, softer in highlights
	float shadow_boost = mix(1.55, 0.72, smoothstep(0.0, 1.0, lum));

	// Relative noise feel:
	// darker = more visible noise
	// brighter = still present, but cleaner
	vec3 signal_response = mix(
		vec3(1.10, 1.00, 1.15),
		vec3(0.72, 0.62, 0.78),
		clamp(signal, vec3(0.0), vec3(1.0))
	);

	return noise * signal_response * shadow_boost;
}

vec2 distort_uv(vec2 uv) {
	if (!enable_distortion) {
		return uv;
	}
	
	uv = (uv - distortion_center) / distortion_overscan + distortion_center;

	vec2 p = (uv - distortion_center) * 2.0;
	float r2 = dot(p, p);

	float k = 1.0 + distortion * r2 + distortion_cubic * r2 * r2;
	p *= k;

	return distortion_center + p * 0.5;
}

float ca_radial_factor(vec2 uv) {
	vec2 size_px = vec2(textureSize(screen_texture, 0));
	float aspect = size_px.x / max(size_px.y, 0.0001);
	vec2 p = uv - distortion_center;
	p.x *= aspect;
	float r = length(p) * 2.0;
	float mask = smoothstep(ca_edge_start, 1.0, r);
	return pow(clamp(mask, 0.0, 1.0), max(ca_radial_power, 0.0001));
}

vec3 sample_screen(vec2 uv) {
	return textureLod(screen_texture, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).rgb;
}

float ca_edge_mask(vec2 duv, vec2 radial_dir, vec2 tangent_dir) {
	vec2 px = get_screen_pixel_size();
	vec2 radial_step = radial_dir * px * 1.35;
	vec2 tangent_step = tangent_dir * px * 1.10;

	float lr_p = luma(sample_screen(duv + radial_step));
	float lr_n = luma(sample_screen(duv - radial_step));
	float lt_p = luma(sample_screen(duv + tangent_step));
	float lt_n = luma(sample_screen(duv - tangent_step));

	float grad = max(abs(lr_p - lr_n), abs(lt_p - lt_n));
	float boosted = grad * (1.2 + ca_blur * 0.35) * ca_edge_response;
	return smoothstep(0.02, 0.18, boosted);
}



vec3 sample_scene_base(vec2 uv) {
	return sample_screen(distort_uv(uv));
}

vec3 sample_scene_ca(vec2 uv) {
	vec2 duv = clamp(distort_uv(uv), vec2(0.0), vec2(1.0));
	vec3 base = sample_screen(duv);

	if (!enable_chromatic_aberration || chromatic_aberration <= 0.0001) {
		return base;
	}

	float radial = ca_radial_factor(uv);
	if (radial <= 0.0001) {
		return base;
	}

	vec2 size_px = vec2(textureSize(screen_texture, 0));
	float aspect = size_px.x / max(size_px.y, 0.0001);
	vec2 to_center = uv - distortion_center;
	vec2 radial_metric = vec2(to_center.x * aspect, to_center.y);
	float radial_len = length(radial_metric);
	vec2 radial_dir = vec2(0.0);
	if (radial_len > 0.00001) {
		radial_dir = normalize(vec2(radial_metric.x / max(aspect, 0.0001), radial_metric.y));
	}
	vec2 tangent_dir = vec2(-radial_dir.y, radial_dir.x);

	float edge_mask = ca_edge_mask(duv, radial_dir, tangent_dir);
	float mask = radial * edge_mask;
	if (mask <= 0.0001) {
		return base;
	}

	vec2 px = get_screen_pixel_size();
	vec2 radial_offset = radial_dir * px * chromatic_aberration * mask;
	vec2 tangential_smear = tangent_dir * px * ca_blur * mask * 0.22;

	float highlight_mask = smoothstep(0.65, 1.15, max3(base)) * ca_highlight_fringe;
	vec2 axial_offset = radial_dir * px * chromatic_aberration * highlight_mask * mask * 0.40;

	vec2 red_uv = duv + radial_offset * 0.55 + axial_offset * 0.40 + tangential_smear;
	vec2 green_uv = duv - axial_offset * 0.22;
	vec2 blue_uv = duv - radial_offset * 0.90 + axial_offset * 0.52 - tangential_smear;

	vec3 ca_col = vec3(
		sample_screen(red_uv).r,
		sample_screen(green_uv).g,
		sample_screen(blue_uv).b
	);

	return mix(base, ca_col, clamp(mask, 0.0, 1.0));
}

float get_view_depth(vec2 uv) {
	vec2 duv = clamp(distort_uv(uv), vec2(0.0), vec2(1.0));
	float depth = textureLod(depth_texture, duv, 0.0).r;
	// Godot 4 (Vulkan) uses reversed Z: depth 1 = near plane, 0 = far plane. The original OpenGL-style
	// formula (depth * 2 - 1) gave wrong distances for DOF and depth-aware halation.
	float n = post_dof_near_clip;
	float f = post_dof_far_clip;
	return n * f / max(depth * (f - n) + n, 0.0001);
}

float halation_highlight_weight(vec2 uv) {
	vec3 c = sample_scene_base(uv);
	float bright = max3(c);
	float w = smoothstep(
		halation_threshold,
		halation_threshold + max(halation_softness, 0.0001),
		bright
	);

	// Bias toward the brightest core so the radius follows the actual source
	return w * w;
}

float get_halation_source_depth(vec2 uv) {
	vec2 px = get_screen_pixel_size() * 1.5;

	vec2 o0 = vec2(0.0, 0.0);
	vec2 o1 = vec2( 1.0,  0.0) * px;
	vec2 o2 = vec2(-1.0,  0.0) * px;
	vec2 o3 = vec2( 0.0,  1.0) * px;
	vec2 o4 = vec2( 0.0, -1.0) * px;
	vec2 o5 = vec2( 0.7071,  0.7071) * px;
	vec2 o6 = vec2(-0.7071,  0.7071) * px;
	vec2 o7 = vec2( 0.7071, -0.7071) * px;
	vec2 o8 = vec2(-0.7071, -0.7071) * px;

	float best_w = 0.0;
	float best_d = get_view_depth(uv);

	#define HALATION_PICK(O, BOOST) { \
		float w = halation_highlight_weight(uv + O) * BOOST; \
		if (w > best_w) { \
			best_w = w; \
			best_d = get_view_depth(uv + O); \
		} \
	}

	HALATION_PICK(o0, 2.0)
	HALATION_PICK(o1, 1.0)
	HALATION_PICK(o2, 1.0)
	HALATION_PICK(o3, 1.0)
	HALATION_PICK(o4, 1.0)
	HALATION_PICK(o5, 0.85)
	HALATION_PICK(o6, 0.85)
	HALATION_PICK(o7, 0.85)
	HALATION_PICK(o8, 0.85)

	#undef HALATION_PICK

	return best_d;
}

float get_halation_radius_px(vec2 uv) {
	float radius_px = halation_radius;

	if (!halation_depth_aware || radius_px <= 0.0001) {
		return radius_px;
	}

	float source_depth = max(get_halation_source_depth(uv), 0.0001);
	float ref_depth = max(halation_depth_reference, 0.0001);

	// Inverse depth scaling: farther source = smaller screen-space bleed,
	// closer source = larger screen-space bleed.
	float scale = pow(ref_depth / source_depth, halation_depth_curve);
	scale = clamp(scale, halation_depth_min_scale, halation_depth_max_scale);

	return radius_px * scale;
}

float get_post_dof_mask(float view_depth) {
	float coc = 0.0;

	if (post_dof_near_enabled) {
		float near_delta = post_dof_near_distance - view_depth;
		coc = max(coc, smoothstep(0.0, max(post_dof_near_transition, 0.0001), near_delta));
	}

	if (post_dof_far_enabled) {
		float far_delta = view_depth - post_dof_far_distance;
		coc = max(coc, smoothstep(0.0, max(post_dof_far_transition, 0.0001), far_delta));
	}

	return clamp(coc * post_dof_amount, 0.0, 1.0);
}

vec3 apply_post_dof(vec2 uv, vec3 center_color) {
	if (!enable_post_dof || post_dof_amount <= 0.0001) {
		return center_color;
	}

	if (!post_dof_near_enabled && !post_dof_far_enabled) {
		return center_color;
	}

	float view_depth = get_view_depth(uv);
	float blur_mask = get_post_dof_mask(view_depth);

	if (blur_mask <= 0.001) {
		return center_color;
	}

	vec2 px = get_screen_pixel_size();
	float radius_px = mix(0.0, 8.0, blur_mask);

	vec2 o1 = vec2( 1.0,  0.0) * px * radius_px;
	vec2 o2 = vec2(-1.0,  0.0) * px * radius_px;
	vec2 o3 = vec2( 0.0,  1.0) * px * radius_px;
	vec2 o4 = vec2( 0.0, -1.0) * px * radius_px;
	vec2 o5 = vec2( 0.7071,  0.7071) * px * radius_px;
	vec2 o6 = vec2(-0.7071,  0.7071) * px * radius_px;
	vec2 o7 = vec2( 0.7071, -0.7071) * px * radius_px;
	vec2 o8 = vec2(-0.7071, -0.7071) * px * radius_px;

	vec3 acc = center_color * 2.0;
	float w = 2.0;

	acc += sample_scene_ca(uv + o1); w += 1.0;
	acc += sample_scene_ca(uv + o2); w += 1.0;
	acc += sample_scene_ca(uv + o3); w += 1.0;
	acc += sample_scene_ca(uv + o4); w += 1.0;
	acc += sample_scene_ca(uv + o5); w += 0.85;
	acc += sample_scene_ca(uv + o6); w += 0.85;
	acc += sample_scene_ca(uv + o7); w += 0.85;
	acc += sample_scene_ca(uv + o8); w += 0.85;

	return mix(center_color, acc / max(w, 0.0001), blur_mask);
}

vec3 threshold_color(vec3 c, float threshold, float softness) {
	float m = smoothstep(threshold, threshold + max(softness, 0.0001), luma(c));
	return c * m;
}

vec3 blur_highlights(vec2 uv, float radius_px, float threshold, float softness) {
	vec2 px = get_screen_pixel_size() * radius_px;

	vec3 acc = vec3(0.0);
	float w = 0.0;

	vec2 o0  = vec2(0.0, 0.0);
	vec2 o1  = vec2( 1.0,  0.0) * px;
	vec2 o2  = vec2(-1.0,  0.0) * px;
	vec2 o3  = vec2( 0.0,  1.0) * px;
	vec2 o4  = vec2( 0.0, -1.0) * px;
	vec2 o5  = vec2( 0.7071,  0.7071) * px * 1.4;
	vec2 o6  = vec2(-0.7071,  0.7071) * px * 1.4;
	vec2 o7  = vec2( 0.7071, -0.7071) * px * 1.4;
	vec2 o8  = vec2(-0.7071, -0.7071) * px * 1.4;
	vec2 o9  = vec2( 2.0,  0.0) * px;
	vec2 o10 = vec2(-2.0,  0.0) * px;
	vec2 o11 = vec2( 0.0,  2.0) * px;
	vec2 o12 = vec2( 0.0, -2.0) * px;

	acc += threshold_color(sample_scene_base(uv + o0), threshold, softness) * 2.0; w += 2.0;
	acc += threshold_color(sample_scene_base(uv + o1), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o2), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o3), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o4), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o5), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o6), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o7), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o8), threshold, softness); w += 1.0;
	acc += threshold_color(sample_scene_base(uv + o9), threshold, softness) * 0.75; w += 0.75;
	acc += threshold_color(sample_scene_base(uv + o10), threshold, softness) * 0.75; w += 0.75;
	acc += threshold_color(sample_scene_base(uv + o11), threshold, softness) * 0.75; w += 0.75;
	acc += threshold_color(sample_scene_base(uv + o12), threshold, softness) * 0.75; w += 0.75;

	return acc / max(w, 0.0001);
}

vec3 blur_highlights_depth_aware(
	vec2 uv,
	float radius_px,
	float threshold,
	float softness,
	float source_depth
) {
	vec2 px = get_screen_pixel_size() * radius_px;

	vec3 acc = vec3(0.0);
	float w = 0.0;

	vec2 o0  = vec2(0.0, 0.0);
	vec2 o1  = vec2( 1.0,  0.0) * px;
	vec2 o2  = vec2(-1.0,  0.0) * px;
	vec2 o3  = vec2( 0.0,  1.0) * px;
	vec2 o4  = vec2( 0.0, -1.0) * px;
	vec2 o5  = vec2( 0.7071,  0.7071) * px * 1.4;
	vec2 o6  = vec2(-0.7071,  0.7071) * px * 1.4;
	vec2 o7  = vec2( 0.7071, -0.7071) * px * 1.4;
	vec2 o8  = vec2(-0.7071, -0.7071) * px * 1.4;
	vec2 o9  = vec2( 2.0,  0.0) * px;
	vec2 o10 = vec2(-2.0,  0.0) * px;
	vec2 o11 = vec2( 0.0,  2.0) * px;
	vec2 o12 = vec2( 0.0, -2.0) * px;

	#define HALATION_ACCUM(O, BASE_W) { \
		float d = get_view_depth(uv + O); \
		float dw = 1.0 - smoothstep(0.0, max(halation_depth_rejection, 0.0001), abs(d - source_depth)); \
		float ww = BASE_W * dw; \
		acc += threshold_color(sample_scene_base(uv + O), threshold, softness) * ww; \
		w += ww; \
	}

	HALATION_ACCUM(o0,  2.0)
	HALATION_ACCUM(o1,  1.0)
	HALATION_ACCUM(o2,  1.0)
	HALATION_ACCUM(o3,  1.0)
	HALATION_ACCUM(o4,  1.0)
	HALATION_ACCUM(o5,  1.0)
	HALATION_ACCUM(o6,  1.0)
	HALATION_ACCUM(o7,  1.0)
	HALATION_ACCUM(o8,  1.0)
	HALATION_ACCUM(o9,  0.75)
	HALATION_ACCUM(o10, 0.75)
	HALATION_ACCUM(o11, 0.75)
	HALATION_ACCUM(o12, 0.75)

	#undef HALATION_ACCUM

	return acc / max(w, 0.0001);
}

vec3 highlight_bloom_pass(vec2 uv) {
	if (!enable_highlight_bloom || bloom_intensity <= 0.0001 || bloom_radius <= 0.0001) {
		return vec3(0.0);
	}

	vec3 b0 = blur_highlights(uv, bloom_radius * 0.65, bloom_threshold, bloom_softness);
	vec3 b1 = blur_highlights(uv, bloom_radius, bloom_threshold, bloom_softness);
	vec3 b2 = blur_highlights(uv, bloom_radius * 1.65, bloom_threshold, bloom_softness);

	vec3 bloom = (b0 + b1 + b2) / 3.0;
	return bloom * bloom_tint * bloom_intensity;
}

vec3 halation_pass(vec2 uv) {
	if (!enable_halation || halation_intensity <= 0.0001 || halation_radius <= 0.0001) {
		return vec3(0.0);
	}

	float source_depth = get_halation_source_depth(uv);
	float radius_px = max(1.0, get_halation_radius_px(uv));

	vec3 inner = blur_highlights_depth_aware(
		uv,
		max(1.0, radius_px * 0.45),
		halation_threshold,
		halation_softness,
		source_depth
	);

	vec3 outer = blur_highlights_depth_aware(
		uv,
		radius_px,
		halation_threshold,
		halation_softness,
		source_depth
	);

	vec3 ring = max(outer - inner * 0.7, vec3(0.0));
	return ring * halation_color * halation_intensity;
}


vec3 apply_basic_grade(vec3 color) {
	if (!enable_color_grading) {
		return color;
	}

	color *= exposure;

	// The scene is linear HDR here (before the environment's tonemapper), so white balance multiplies
	// (an added offset would tint the blacks) and contrast pivots around photographic middle grey.
	color *= vec3(
		1.0 + temperature * 0.10 + tint * 0.025,
		1.0 + tint * 0.05,
		1.0 - temperature * 0.10 - tint * 0.025);

	const float MIDDLE_GREY = 0.18;
	color = MIDDLE_GREY * pow(max(color, vec3(0.0)) / MIDDLE_GREY, vec3(contrast));

	float y = luma(color);
	color = mix(vec3(y), color, saturation);

	color += lift;
	color = max(color, vec3(0.0));
	color = pow(color, vec3(1.0) / max(gamma, vec3(0.001)));
	color *= gain;

	return color;
}

vec3 apply_lut_strip(vec3 color) {
	float size = max(lut_size, 2.0);
	vec3 c = clamp(color, vec3(0.0), vec3(1.0)) * (size - 1.0);

	float blue0 = floor(c.b);
	float blue1 = min(blue0 + 1.0, size - 1.0);
	float bluef = fract(c.b);

	vec2 uv0 = vec2(
		(c.r + blue0 * size + 0.5) / (size * size),
		(c.g + 0.5) / size
	);

	vec2 uv1 = vec2(
		(c.r + blue1 * size + 0.5) / (size * size),
		(c.g + 0.5) / size
	);

	vec3 lut0 = texture(lut_texture, uv0).rgb;
	vec3 lut1 = texture(lut_texture, uv1).rgb;

	return mix(lut0, lut1, bluef);
}

float grain_tone_weight(float lum) {
	lum = pow(max(lum, 0.0), 1.0 / 2.2); // classify tones perceptually
	float shadows = 1.0 - smoothstep(0.0, 0.45, lum);
	float highlights = smoothstep(0.55, 1.0, lum);
	float mids = max(0.0, 1.0 - shadows - highlights);

	return shadows * grain_shadows
		+ mids * grain_mids
		+ highlights * grain_highlights;
}

float vignette_amount(vec2 uv) {
	vec2 size_px = vec2(textureSize(screen_texture, 0));
	float aspect = size_px.x / max(size_px.y, 0.0001);

	vec2 p = uv * 2.0 - 1.0;
	p.x *= aspect;

	p = sign(p) * pow(abs(p), vec2(vignette_roundness));
	float r = length(p);

	return smoothstep(vignette_size, vignette_size + vignette_smoothness, r);
}


void vertex() {
	// The quad mesh is 2 x 2: its vertices are the screen corners in clip space, at the near plane.
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	vec2 uv = SCREEN_UV;

	vec3 color = sample_scene_ca(uv);
	color = apply_post_dof(uv, color);

	vec3 bloom = highlight_bloom_pass(uv);
	vec3 halation = halation_pass(uv);

	color += bloom;
	color += halation;

	color = apply_basic_grade(color);

	if (enable_lut && lut_strength > 0.0001) {
		vec3 lut_color = apply_lut_strip(color);
		color = mix(color, lut_color, lut_strength);
	}

	if (enable_vignette && vignette_intensity > 0.0001) {
		float v = vignette_amount(uv);
		color *= 1.0 - (v * vignette_intensity);
	}

	// Grain scales with the pixel (relative noise). The image here is linear and not yet exposed, so an
	// added offset would explode in dark scenes once auto exposure brightens them.
	if (enable_grain && grain_strength > 0.0001) {
		const float GRAIN_RELATIVE = 4.0; // strength 0.01 = about 4 % noise at mid weight
		vec2 grain_uv = floor(FRAGCOORD.xy / max(grain_size, 0.001));
		float lum = luma(color);
		float tone_weight = grain_tone_weight(lum);

		if (enable_rgb_digital_noise) {
			vec3 rgb_noise = digital_camera_rgb_noise(grain_uv, color);
			color *= max(vec3(0.0), 1.0 + rgb_noise * grain_strength * tone_weight * GRAIN_RELATIVE);
		} else {
			float n = hash12(grain_uv + vec2(TIME * grain_speed, TIME * grain_speed * 0.37));
			color *= max(0.0, 1.0 + (n - 0.5) * grain_strength * tone_weight * GRAIN_RELATIVE);
		}
	}

	ALBEDO = clamp(color, vec3(0.0), vec3(32.0));
	ALPHA = 1.0;
}