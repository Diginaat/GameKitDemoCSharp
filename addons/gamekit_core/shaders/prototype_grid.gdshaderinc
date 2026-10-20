// Prototype grid pattern, shared by prototype_grid.gdshader and shaders that add effects on top of it
// (for example gamekit_weather's wet_prototype_grid.gdshader). World-space, projected triplanar.

// Albedo of the grid at a world position: 1 m cells (`cell`), darker lines, a faint checker.
vec3 prototype_grid_albedo(vec3 color, vec3 world_position, vec3 world_normal, float cell, float line_width) {
	vec3 w = abs(world_normal);
	vec2 uv = w.y > max(w.x, w.z) ? world_position.xz : (w.x > w.z ? world_position.zy : world_position.xy);
	vec2 g = abs(fract(uv / cell + 0.5) - 0.5) * cell;
	vec2 aa = fwidth(uv) + 1e-4;
	vec2 t = smoothstep(vec2(line_width * 0.5), vec2(line_width * 0.5) + aa, g);
	float on_line = 1.0 - t.x * t.y;
	vec2 c = floor(uv / cell);
	float checker = mod(c.x + c.y, 2.0);
	return color * (0.92 + 0.08 * checker) * (1.0 - 0.35 * on_line);
}
