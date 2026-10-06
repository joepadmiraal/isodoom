// Level shader: the spatial variant of palette.gdshader (SPEC §7.2, §7.4, §12 T2.5).
//
// One material draws every floor and solid wall of a level. Textures and
// flats sit in one RG8 index atlas (R = palette index, G = opacity), looked
// up with texelFetch at integer texel coordinates that the shader wraps
// itself, so the atlas needs no repeat wrapping (IsoDoom.Render.LevelMesh).
//
// Per-sector data (`sector_data`, RGBA float, SECTOR_DATA_WIDTH texels per
// row, one per sector): R = floor height, G = ceiling height (map units),
// B = light level (0-255). The vertex shader places every vertex from it, so
// changing a sector's heights moves its floor and walls without a rebuild.
// `texture_info` (RGBA float, same layout, one texel per texture slot):
// atlas x, y, width, height.
//
// Vertex attributes (map units; 1 map unit = 1/32 m, x -> +X, y -> -Z):
//   VERTEX.xz  position (VERTEX.y is ignored and set here)
//   CUSTOM0    kind (0 floor, 1 solid wall), texture slot, front sector, back sector (-1: none)
//   floors:    UV = (map x, -map y): the flat texel is floor(UV) & 63 (r_draw.c R_DrawSpan)
//   walls:     UV.x = texture column (textureoffset + distance from V1), UV.y = 0 bottom, 1 top
//              CUSTOM1 = bottom plane, bottom offset, top plane, top offset
//              CUSTOM2 = texture-top plane, texture-top offset, -, -
//              planes are IsoDoom.Map.WallPlane: 0 front floor, 1 front ceiling,
//              2 back floor, 3 back ceiling, 4 higher floor, 5 lower ceiling
//
// Solid walls ignore the opacity mask: a hole in a composite (vanilla draws
// garbage there) shows the stored index, never the background.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, shadows_disabled, fog_disabled;

const float MAP_UNITS_PER_METRE = 32.0;
const int SECTOR_DATA_WIDTH = 256;

uniform sampler2D atlas : filter_nearest, repeat_disable;
uniform sampler2D texture_info : filter_nearest, repeat_disable;
uniform sampler2D sector_data : filter_nearest, repeat_disable;
// PLAYPAL: RGB8, 256 x palettes. COLORMAP: R8, 256 x maps (as palette.gdshader).
uniform sampler2D playpal : filter_nearest, repeat_disable;
uniform sampler2D colormap : filter_nearest, repeat_disable;
uniform int palette_index = 0;
// -1: from the sector light (flat for now: 31 - light / 8); 0-33: this map everywhere.
uniform int colormap_override = -1;
// IsoDoom.Map.WallTextureTiling: 0 vanilla (width mask, & 127, tutti-frutti), 1 texture size.
uniform int wall_tiling = 0;

varying flat ivec4 v_rect;
varying flat int v_kind;
varying flat int v_colormap;
varying vec2 v_texcoord;

vec4 data_texel(sampler2D tex, int id) {
	return texelFetch(tex, ivec2(id % SECTOR_DATA_WIDTH, id / SECTOR_DATA_WIDTH), 0);
}

float plane_height(int plane, vec4 front, vec4 back) {
	switch (plane) {
		case 0: return front.r;
		case 1: return front.g;
		case 2: return back.r;
		case 3: return back.g;
		case 4: return max(front.r, back.r);
		default: return min(front.g, back.g);
	}
}

void vertex() {
	int kind = int(CUSTOM0.x);
	int front_id = int(CUSTOM0.z);
	int back_id = int(CUSTOM0.w);
	vec4 front = data_texel(sector_data, front_id);
	vec4 back = back_id >= 0 ? data_texel(sector_data, back_id) : front;
	v_rect = ivec4(data_texel(texture_info, int(CUSTOM0.y)) + 0.5);
	v_kind = kind;
	int light = clamp(int(front.b + 0.5), 0, 255);
	v_colormap = colormap_override >= 0 ? colormap_override : 31 - (light >> 3);

	float z;
	if (kind == 0) {
		z = front.r;
		v_texcoord = UV;
	} else {
		// WallSection.Span: clipped to the front sector, empty when top <= bottom.
		float bottom = max(plane_height(int(CUSTOM1.x), front, back) + CUSTOM1.y, front.r);
		float top = min(plane_height(int(CUSTOM1.z), front, back) + CUSTOM1.w, front.g);
		top = max(top, bottom);
		z = UV.y > 0.5 ? top : bottom;
		float texture_top = plane_height(int(CUSTOM2.x), front, back) + CUSTOM2.y;
		v_texcoord = vec2(UV.x, texture_top - z);
	}
	VERTEX.y = z / MAP_UNITS_PER_METRE;
}

int floor_mod(int a, int n) {
	int m = a % n;
	return m < 0 ? m + n : m;
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	ivec2 t = ivec2(floor(v_texcoord));
	int w = v_rect.z;
	int h = v_rect.w;
	int col;
	int row;
	if (v_kind == 0) {
		col = t.x & 63;
		row = t.y & 63;
	} else if (wall_tiling == 0) {
		// IsoDoom.Map.TextureWrap.WallTexel (Vanilla)
		col = t.x & ((1 << findMSB(w)) - 1);
		row = t.y & 127;
		if (row >= h) {
			int i = (col * h + row) % (w * h);
			col = i / h;
			row = i % h;
		}
	} else {
		col = floor_mod(t.x, w);
		row = floor_mod(t.y, h);
	}
	int index = int(texelFetch(atlas, v_rect.xy + ivec2(col, row), 0).r * 255.0 + 0.5);
	int mapped = int(texelFetch(colormap, ivec2(index, v_colormap), 0).r * 255.0 + 0.5);
	// PLAYPAL holds sRGB values; ALBEDO is linear (Godot converts back on output).
	ALBEDO = srgb_to_linear(texelFetch(playpal, ivec2(mapped, palette_index), 0).rgb);
}
