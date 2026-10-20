// Lens flare overlay for gamekit_lens (CameraLens): source glow, octagonal aperture ghosts along the
// optical axis, optional rays. CameraLens fills flare_data / flare_tint each frame.
// From EpicCameraX ue_like_lens_flare.gdshader (MIT, Diginaat / DevKine; see LICENSE-EpicCameraX.txt);
// unused bokeh texture uniforms removed.
shader_type canvas_item;
render_mode unshaded, blend_add;

const int MAX_FLARES = 12;

uniform int flare_count = 0;
uniform vec4 flare_data[MAX_FLARES]; // x=uv.x, y=uv.y, z=size, w=intensity
uniform vec4 flare_tint[MAX_FLARES]; // rgb=tint, a=screen-space visibility gate strength

uniform sampler2D scene_color_tex : hint_screen_texture, repeat_disable, filter_linear_mipmap;

// screen-space source gating
uniform bool enable_source_visibility_gating = true;
uniform float source_visibility_threshold : hint_range(0.0, 2.0, 0.01) = 0.65;
uniform float source_visibility_softness : hint_range(0.001, 1.0, 0.01) = 0.18;
uniform float source_visibility_sample_radius : hint_range(0.0, 0.05, 0.0005) = 0.006;

// central safe area for ghost flares: allow a ring around the optical center,
// but avoid landing perfectly centered unless the user disables it.
uniform bool enable_center_safe_zone = true;
uniform float allowed_flare_area_radius_scale : hint_range(0.0, 0.8, 0.001) = 0.26;
uniform float center_dead_zone_radius_scale : hint_range(0.0, 0.6, 0.001) = 0.10;
uniform float allowed_flare_area_feather : hint_range(0.0, 0.2, 0.001) = 0.025;

// existing script-driven controls
uniform float master_intensity : hint_range(0.0, 8.0, 0.01) = 1.0;
uniform float blur_amount : hint_range(0.0, 8.0, 0.01) = 1.2;
uniform float chroma_amount : hint_range(0.0, 0.05, 0.0001) = 0.0015;
uniform float halo_intensity : hint_range(0.0, 4.0, 0.01) = 0.45;
uniform float ghost_intensity : hint_range(0.0, 4.0, 0.01) = 0.22;
uniform float ray_intensity : hint_range(0.0, 4.0, 0.01) = 0.06;
uniform float imperfection : hint_range(0.0, 1.0, 0.01) = 0.20;
uniform float ray_count : hint_range(4.0, 16.0, 1.0) = 6.0;
uniform float ray_length : hint_range(0.2, 4.0, 0.01) = 1.35;
uniform float ghost_spread : hint_range(0.1, 3.0, 0.01) = 1.0;
uniform float source_bloom_scale : hint_range(0.1, 4.0, 0.01) = 0.9;

// look tuning
uniform float flare_saturation : hint_range(0.0, 2.0, 0.01) = 0.82;
uniform float ghost_opacity_falloff : hint_range(0.1, 4.0, 0.01) = 1.35;
uniform float ghost_softness : hint_range(0.0, 2.0, 0.01) = 0.65;
uniform float source_core_intensity : hint_range(0.0, 4.0, 0.01) = 0.35;
uniform float ghost_nearness : hint_range(0.1, 2.0, 0.01) = 1.0;
uniform float ghost_color_boost : hint_range(0.0, 2.0, 0.01) = 1.25;

mat2 rot2(float a) {
	float s = sin(a);
	float c = cos(a);
	return mat2(vec2(c, -s), vec2(s, c));
}

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 345.45));
	p += dot(p, p + 34.345);
	return fract(p.x * p.y);
}

vec3 set_saturation(vec3 c, float s) {
	float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
	return mix(vec3(l), c, s);
}

float sample_scene_brightness(vec2 uv) {
	vec3 c = textureLod(scene_color_tex, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).rgb;
	return max(max(c.r, c.g), c.b);
}

float screen_aspect() {
	ivec2 tex_size_i = textureSize(scene_color_tex, 0);
	vec2 tex_size = vec2(tex_size_i);
	return tex_size.y > 0.0 ? (tex_size.x / tex_size.y) : 1.0;
}

vec2 uv_to_center_space(vec2 uv) {
	vec2 p = uv - vec2(0.5);
	p.x *= screen_aspect();
	return p;
}

vec2 center_space_to_uv(vec2 p) {
	p.x /= max(screen_aspect(), 0.0001);
	return p + vec2(0.5);
}

float flare_allowed_area_visibility(vec2 flare_uv) {
	if (!enable_center_safe_zone) {
		return 1.0;
	}

	float outer_radius = max(allowed_flare_area_radius_scale, 0.0);
	float inner_radius = clamp(center_dead_zone_radius_scale, 0.0, outer_radius);
	vec2 p = uv_to_center_space(flare_uv);
	float d = length(p);
	float feather = max(allowed_flare_area_feather, 0.0001);

	float outer_vis = 1.0;
	if (outer_radius > 0.0001) {
		outer_vis = 1.0 - smoothstep(outer_radius, outer_radius + feather, d);
	}

	float inner_vis = 1.0;
	if (inner_radius > 0.0001) {
		inner_vis = smoothstep(max(inner_radius - feather, 0.0), inner_radius, d);
	}

	return inner_vis * outer_vis;
}

float source_visibility(vec2 src_uv, float size, float gate_strength) {
	if (!enable_source_visibility_gating || gate_strength <= 0.0001) {
		return 1.0;
	}

	float radius = max(source_visibility_sample_radius, size * 0.22);
	vec2 ox = vec2(radius, 0.0);
	vec2 oy = vec2(0.0, radius);
	vec2 od = vec2(radius * 0.70710678, radius * 0.70710678);

	float center = sample_scene_brightness(src_uv);
	float ring = 0.0;
	ring += sample_scene_brightness(src_uv + ox);
	ring += sample_scene_brightness(src_uv - ox);
	ring += sample_scene_brightness(src_uv + oy);
	ring += sample_scene_brightness(src_uv - oy);
	ring += sample_scene_brightness(src_uv + od);
	ring += sample_scene_brightness(src_uv - od);
	ring += sample_scene_brightness(src_uv + vec2( od.x, -od.y));
	ring += sample_scene_brightness(src_uv + vec2(-od.x,  od.y));
	ring *= 0.125;

	float core = smoothstep(
		max(0.0, source_visibility_threshold - source_visibility_softness),
		source_visibility_threshold + source_visibility_softness,
		center
	);

	float support = smoothstep(
		max(0.0, source_visibility_threshold * 0.75 - source_visibility_softness),
		source_visibility_threshold * 0.75 + source_visibility_softness,
		ring
	);

	float vis = core * mix(0.75, 1.0, support);
	vis = pow(clamp(vis, 0.0, 1.0), mix(1.0, 2.4, clamp(gate_strength, 0.0, 1.0)));

	return mix(1.0, vis, clamp(gate_strength, 0.0, 1.0));
}

// soft octagon aperture mask
float octagon_mask(vec2 p, float radius, float softness, float rotation) {
	p = rot2(rotation) * p;
	p /= max(radius, 0.0001);
	p = abs(p);

	float d = max(max(p.x, p.y), (p.x + p.y) * 0.70710678);
	return 1.0 - smoothstep(1.0 - softness, 1.0 + softness, d);
}

float blurred_octagon(vec2 p, float blur, float rotation) {
	float s = 0.10 + blur * 0.06 + ghost_softness * 0.04;
	vec2 o = vec2(0.08 + blur * 0.02, 0.0);

	float a0 = octagon_mask(p, 1.0, s, rotation);
	float a1 = octagon_mask(p + vec2( o.x, 0.0), 1.0, s, rotation);
	float a2 = octagon_mask(p + vec2(-o.x, 0.0), 1.0, s, rotation);
	float a3 = octagon_mask(p + vec2(0.0,  o.x), 1.0, s, rotation);
	float a4 = octagon_mask(p + vec2(0.0, -o.x), 1.0, s, rotation);

	return (a0 * 2.0 + a1 + a2 + a3 + a4) / 6.0;
}

float dirt_mask(vec2 uv01, float amt) {
	vec2 cell = floor(uv01 * 36.0);
	float n1 = hash21(cell);
	float n2 = hash21(cell + vec2(7.0, 19.0));
	float n = mix(n1, n2, 0.5);
	return mix(1.0, 0.90 + n * 0.10, amt);
}

vec3 octo_bokeh_sprite(
	vec2 screen_uv,
	vec2 center_uv,
	float size,
	vec2 axis,
	vec3 tint,
	float blur,
	float chroma,
	float rotation,
	float opacity
) {
	vec2 p = (screen_uv - center_uv) / max(size, 0.0001);

	float r = length(p);
	if (r > 1.4) {
		return vec3(0.0);
	}

	vec2 tang = vec2(-axis.y, axis.x);
	vec2 ca = axis * chroma * 8.0 + tang * chroma * 3.5;

	float mr = blurred_octagon(p + ca, blur, rotation + 0.01);
	float mg = blurred_octagon(p, blur, rotation);
	float mb = blurred_octagon(p - ca, blur, rotation - 0.01);

	vec3 shape = vec3(mr, mg, mb);

	float core = exp(-r * r * 4.8) * source_core_intensity;
	float ring_outer = octagon_mask(p, 0.72, 0.10 + blur * 0.04, rotation) * 0.06;
	float edge_fade = 1.0 - smoothstep(0.88, 1.20 + blur * 0.08, r);

	vec2 uv01 = p * 0.5 + 0.5;
	float dirt = dirt_mask(uv01, imperfection);

	vec3 col = shape * tint;
	col += tint * (core + ring_outer);

	return col * edge_fade * dirt * opacity;
}

vec3 source_glow(vec2 screen_uv, vec2 center_uv, float size, vec3 tint, float opacity) {
	vec2 p = (screen_uv - center_uv) / max(size, 0.0001);
	float r = length(p);

	float disc = exp(-r * r * 2.0);
	float veil = exp(-r * 1.8) * 0.35;

	return tint * (disc + veil) * opacity;
}

vec3 flare_rays_pass(vec2 screen_uv, vec2 src_uv, float size, vec3 tint, float intensity, float rays, float length_scale) {
	vec2 p = (screen_uv - src_uv) / max(size * length_scale, 0.0001);
	float r = length(p);

	if (r > 1.35) {
		return vec3(0.0);
	}

	float a = atan(p.y, p.x);

	float spokes_a = pow(max(0.0, cos(a * rays)), 22.0);
	float spokes_b = pow(max(0.0, cos(a * (rays * 0.5) + 0.4)), 30.0) * 0.35;
	float radial = exp(-r * 2.1);

	float breakup = 1.0 + sin(a * 13.0 + r * 9.0) * 0.08 * imperfection;

	return tint * (spokes_a + spokes_b) * radial * intensity * breakup;
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 screen_center = vec2(0.5, 0.5);
	vec3 out_col = vec3(0.0);

	for (int i = 0; i < MAX_FLARES; i++) {
		if (i >= flare_count) {
			break;
		}

		vec4 fd = flare_data[i];
		vec4 ft = flare_tint[i];

		vec2 src = fd.xy;
		float size = fd.z;
		float intensity = fd.w * master_intensity;

		if (size <= 0.0 || intensity <= 0.0) {
			continue;
		}

		float source_vis = source_visibility(src, size, ft.a);
		if (source_vis <= 0.001) {
			continue;
		}

		intensity *= source_vis;

		vec3 tint = set_saturation(ft.rgb, flare_saturation);
		vec2 axis_vec = screen_center - src;
		float axis_len = length(axis_vec);
		vec2 axis = axis_len > 0.00001 ? axis_vec / axis_len : vec2(1.0, 0.0);

		float edge_boost = mix(0.82, 1.15, smoothstep(0.08, 0.95, axis_len));
		float e = intensity * edge_boost;

		float seed = hash21(src * 97.13);
		float rot = (seed - 0.5) * 0.18;

		// main source flare
		out_col += source_glow(uv, src, size * source_bloom_scale * 1.55, tint, halo_intensity * e * 0.55);
		out_col += octo_bokeh_sprite(
			uv,
			src,
			size * source_bloom_scale,
			axis,
			tint,
			blur_amount * 1.1,
			chroma_amount,
			rot,
			halo_intensity * e
		);

		// subtle rays
		out_col += flare_rays_pass(
			uv,
			src,
			size * 1.8,
			tint,
			ray_intensity * e,
			ray_count,
			ray_length
		);

		// unreal-style ghost chain: soft, translucent, octagonal
		vec2 g1_raw = src + (screen_center - src) * (0.24 * ghost_spread * ghost_nearness);
		vec2 g2_raw = src + (screen_center - src) * (0.44 * ghost_spread * ghost_nearness);
		vec2 g3_raw = src + (screen_center - src) * (0.66 * ghost_spread * ghost_nearness);
		vec2 g4_raw = src + (screen_center - src) * (0.90 * ghost_spread * ghost_nearness);

		vec2 g1 = g1_raw;
		vec2 g2 = g2_raw;
		vec2 g3 = g3_raw;
		vec2 g4 = g4_raw;

		float g1_vis = flare_allowed_area_visibility(g1);
		float g2_vis = flare_allowed_area_visibility(g2);
		float g3_vis = flare_allowed_area_visibility(g3);
		float g4_vis = flare_allowed_area_visibility(g4);

		vec3 ghost_tint_1 = set_saturation(tint * vec3(1.18, 0.92, 0.82), flare_saturation * ghost_color_boost);
		vec3 ghost_tint_2 = set_saturation(tint * vec3(0.76, 1.18, 0.94), flare_saturation * ghost_color_boost);
		vec3 ghost_tint_3 = set_saturation(tint * vec3(1.18, 0.78, 1.12), flare_saturation * ghost_color_boost);
		vec3 ghost_tint_4 = set_saturation(tint * vec3(1.08, 0.98, 0.82), flare_saturation * ghost_color_boost);

		out_col += octo_bokeh_sprite(
			uv, g1, size * 0.19, axis,
			ghost_tint_1,
			blur_amount * 1.05, chroma_amount * 1.15, rot + 0.02,
			ghost_intensity * e * 0.82 * g1_vis / ghost_opacity_falloff
		);

		out_col += octo_bokeh_sprite(
			uv, g2, size * 0.13, axis,
			ghost_tint_2,
			blur_amount * 1.00, chroma_amount * 1.20, rot - 0.01,
			ghost_intensity * e * 0.58 * g2_vis / ghost_opacity_falloff
		);

		out_col += octo_bokeh_sprite(
			uv, g3, size * 0.10, axis,
			ghost_tint_3,
			blur_amount * 0.95, chroma_amount * 1.25, rot + 0.03,
			ghost_intensity * e * 0.38 * g3_vis / ghost_opacity_falloff
		);

		out_col += octo_bokeh_sprite(
			uv, g4, size * 0.14, axis,
			ghost_tint_4,
			blur_amount * 1.08, chroma_amount * 1.15, rot - 0.02,
			ghost_intensity * e * 0.24 * g4_vis / ghost_opacity_falloff
		);
	}

	COLOR = vec4(out_col, 1.0);
}
