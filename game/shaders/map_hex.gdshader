// Map hexes (stage 5): biome tile, relief and feature tiles, rivers on edges, then the tint of the cell layers.
// Per instance: COLOR = tint (rgb + coverage in alpha); INSTANCE_CUSTOM = (biome tile + 1000 * relief,
// relief tile or -1, feature tile or -1, river bits: 2 bits per edge N, NE, SE, S, SW, NW; 1 stream, 2 river).
shader_type canvas_item;

uniform sampler2D biome_atlas : filter_nearest;
uniform sampler2D biome_flat : filter_nearest;
uniform sampler2D terrain_atlas : filter_nearest;
uniform vec2 biome_grid = vec2(3.0, 14.0);
uniform vec2 terrain_grid = vec2(8.0, 3.0);
uniform float river_tile = 7.0;
uniform float biome_opacity = 1.0;
uniform float river_opacity = 1.0;
uniform bool textured = true;
uniform bool has_art = true;

varying vec4 custom;
varying vec4 tint;

vec2 cell_uv(float index, vec2 grid, vec2 uv) {
	float col = mod(index, grid.x);
	float row = floor(index / grid.x);
	return (vec2(col, row) + uv) / grid;
}

vec3 over(vec3 below, vec4 tile, float alpha) {
	return mix(below, tile.rgb, tile.a * alpha);
}

void vertex() {
	custom = INSTANCE_CUSTOM;
	tint = COLOR;
}

void fragment() {
	float relief = floor(custom.x / 1000.0 + 0.001);
	float biome = custom.x - relief * 1000.0;
	vec3 col = vec3(0.16, 0.16, 0.17);
	if (has_art) {
		vec3 base;
		if (textured) {
			base = texture(biome_atlas, cell_uv(biome, biome_grid, UV)).rgb;
		} else {
			// Far away single pixels would shimmer: use the tile's average colour, and shade relief instead of drawing it.
			base = texture(biome_flat, cell_uv(biome, biome_grid, vec2(0.5))).rgb;
			if (relief > 1.5) {
				base = mix(base, vec3(0.35, 0.31, 0.27), 0.6);
			} else if (relief > 0.5) {
				base *= 0.82;
			}
		}
		col = mix(col, base, biome_opacity);
		if (textured) {
			if (custom.y >= 0.0) {
				col = over(col, texture(terrain_atlas, cell_uv(custom.y, terrain_grid, UV)), biome_opacity);
			}
			if (custom.z >= 0.0) {
				col = over(col, texture(terrain_atlas, cell_uv(custom.z, terrain_grid, UV)), biome_opacity);
			}
			int rivers = int(custom.w + 0.5);
			for (int e = 0; e < 6; e++) {
				int size = (rivers >> (e * 2)) & 3;
				if (size > 0) {
					float tile = river_tile + float(e * 2 + min(size, 2) - 1);
					col = over(col, texture(terrain_atlas, cell_uv(tile, terrain_grid, UV)), river_opacity);
				}
			}
		}
	}
	col = mix(col, tint.rgb, tint.a);
	COLOR = vec4(col, 1.0);
}
