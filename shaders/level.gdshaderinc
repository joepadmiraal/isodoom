// Level shader body (SPEC §7.2, §7.4, §12 T2.5, T3.1), included by
// level.gdshader (floors and solid walls) and level_masked.gdshader (masked
// middles, with MASKED_PASS defined: the only variant with the opacity mask).
// Both discard the fragments the cutaway removes (T3.4).
//
// Each material draws its kinds for every sector of a level. Textures and
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
// Light (SPEC §7.4, §12 T2.8): vanilla's tables (IsoDoom.Map.LightTables,
// the CPU reference) in `light_tables` (R8, 128 x 32): row i = r_main.c
// zlight[i] (floors, by distance >> LIGHTZSHIFT), row 16 + i = scalelight[i]
// (walls, by scale (centerx << 20) / distance, capped at 47). The light
// number is (sector light >> 4) + extralight (+ the wall's fake contrast),
// clamped to 0-15, as r_segs.c / r_plane.c. The distance (fixed_t, clamped to
// MAX_DISTANCE units) depends on `light_mode` (IsoDoom.Map.LightDiminishing):
// 0 none (`light_reference`, map units), 1 player (horizontal distance from
// `light_origin`, map x/y, at least `light_near`: T3.7), 2 camera (view-space
// depth). Only the
// distance is float; the rest is integer, so the CPU can match it exactly.
//
// Vertex attributes (map units; 1 map unit = 1/32 m, x -> +X, y -> -Z):
//   VERTEX.xz  position (VERTEX.y is ignored and set here)
//   CUSTOM0    kind (0 floor, 1 solid wall, 2 masked middle, 3 a masked middle's back face,
//              4 a floor's cutaway cap), texture slot, front sector, back sector (-1: none;
//              for a cap its cut centre, 0 player, 1 cursor)
//   floors:    UV = (map x, -map y): the flat texel is floor(UV) & 63 (r_draw.c R_DrawSpan)
//   walls:     UV.x = texture column (textureoffset + distance from V1), UV.y = 0 bottom, 1 top
//              CUSTOM1 = bottom plane, bottom offset, top plane, top offset
//              CUSTOM2 = texture-top plane, texture-top offset, fake contrast (-1, 0, +1),
//                        direction from end A to B (radians; LevelMesh.PieceAngle)
//              planes are IsoDoom.Map.WallPlane: 0 front floor, 1 front ceiling,
//              2 back floor, 3 back ceiling, 4 higher floor, 5 lower ceiling
//
// Solid walls ignore the opacity mask: a hole in a composite (vanilla draws
// garbage there) shows the stored index, never the background.
//
// Masked middles (kind 2, T3.1; r_segs.c R_RenderMaskedSegRange) keep the
// mask (discard where G < 0.5), are clipped to the texture's rows below the
// texture top as WallSection.Span (drawn once: no vertical tiling; the height
// is the slot's texture_info height) and lit as the other walls, with the fake
// contrast. Columns wrap as solid walls' (TextureWrap.MaskedTexel).
// Kind 3 (T3.1a) is the back face of a masked middle on one side of a line
// only (the same corners and columns, wound the other way, with its sectors
// and plane references swapped, so it is lit from the other side): drawn as a
// masked middle when `masked_backs` is 1, collapsed to nothing when it is 0
// (vanilla, which draws a masked middle only from its own side).
//
// Cutaway (SPEC §7.3, §12 T3.4; the CPU reference is IsoDoom.Render.Cutaway):
// a fragment at map point p is cut when, for a cut centre c (`cut_player`,
// and `cut_cursor` when its w is 1; map x, y, floor height), all of these hold:
//   - p is more than `cut_height` above c's floor (so c's own floor, steps
//     and anything else up to that height are never cut);
//   - c is behind the surface's plane, seen from the camera, by more than
//     CUT_PLANE_MARGIN (the surface can hide it; walls behind the centre, or
//     in line with it, keep their full height). A floor above the cutoff,
//     the top of a block whose walls are cut, is always in front;
//   - p is within `cut_radius` of the line from the anchor a = c + `cut_anchor`
//     up towards the camera (the view direction in orthographic, the camera
//     position in perspective): a disc around the centre on screen.
// `cut_mode` 0 draws everything, 1 discards cut fragments, 2 discards half of
// them in a 4x4 ordered dither. Discarded fragments write no depth, so
// whatever is behind (lower floors, sprites) shows.
//
// Cap (T3.4a; IsoDoom.Render.CutawayCap, `cut_cap` 0 off, 1 flat, 2 dark): a
// block whose top and walls are cut would show the void inside. Kind 4 is a
// copy of each floor per cut centre (CUSTOM0.w: 0 `cut_player`, 1
// `cut_cursor`), placed at that centre's cutoff, c.z + `cut_height`, and drawn
// only where the sector's floor is above it (so the floor is cut there) and
// within the centre's disc: the block's cross-section at the cutoff, as if it
// were cut down to the stub its walls keep. It shows the sector's flat, lit as
// the floor (flat) or CAP_DARKEN light levels darker (dark). It is collapsed
// when the cutaway or the cap is off or the centre is inactive, and is cut by
// the other centre as any floor.

const float MAP_UNITS_PER_METRE = 32.0;
const int SECTOR_DATA_WIDTH = 256;
// r_main.c
const int LIGHTLEVELS = 16;
const int LIGHTSEGSHIFT = 4;
const int MAXLIGHTSCALE = 48;
const int MAXLIGHTZ = 128;
const int LIGHTZSHIFT = 20;
// IsoDoom.Map.LightTables.MaxDistanceUnits
const float MAX_DISTANCE = 4096.0;
// IsoDoom.Render.Cutaway.PlaneMargin (map units).
const float CUT_PLANE_MARGIN = 1.0;
// IsoDoom.Render.Cutaway.CapDarken: light levels (of 16) a dark cap loses.
const int CAP_DARKEN = 4;

uniform sampler2D atlas : filter_nearest, repeat_disable;
uniform sampler2D texture_info : filter_nearest, repeat_disable;
uniform sampler2D sector_data : filter_nearest, repeat_disable;
// PLAYPAL: RGB8, 256 x palettes. COLORMAP: R8, 256 x maps (as palette.gdshader).
uniform sampler2D playpal : filter_nearest, repeat_disable;
uniform sampler2D colormap : filter_nearest, repeat_disable;
uniform int palette_index = 0;
// -1: from the sector light and distance; 0-33: this map everywhere (vanilla's
// fixedcolormap: 0 full bright, 1 light amplification, 32 invulnerability).
uniform int colormap_override = -1;
uniform sampler2D light_tables : filter_nearest, repeat_disable;
// IsoDoom.Map.LightDiminishing: 0 none, 1 player, 2 camera.
uniform int light_mode = 1;
uniform vec2 light_origin = vec2(0.0);
uniform float light_reference = 256.0;
// The shortest distance Player mode uses (T3.7; IsoDoom.Map.LightTables.DefaultNearDistance).
uniform float light_near = 80.0;
// r_main.c centerx (viewwidth / 2): the projection of the wall scale.
uniform int light_centerx = 160;
// p_user.c / r_main.c extralight (the muzzle flash: 0-2).
uniform int extralight = 0;
// IsoDoom.Map.WallTextureTiling: 0 vanilla (width mask, & 127, tutti-frutti), 1 texture size.
uniform int wall_tiling = 0;
// IsoDoom.Render.MaskedBackFaces (T3.1a): 0 off (vanilla), 1 mirrored.
uniform int masked_backs = 1;
// The cutaway (T3.4): 0 off, 1 cut, 2 dither.
uniform int cut_mode = 0;
// Cut centres, map units (x, y, floor height, w = 1 active).
uniform vec4 cut_player = vec4(0.0);
uniform vec4 cut_cursor = vec4(0.0);
uniform float cut_radius = 96.0;
uniform float cut_height = 32.0;
uniform float cut_anchor = 28.0;
// The cap (T3.4a): 0 off, 1 flat, 2 dark.
uniform int cut_cap = 0;

varying flat ivec4 v_rect;
varying flat int v_kind;
varying flat int v_lightnum;
// The cut centre whose cap this is (kind 4, T3.4a): 0 player, 1 cursor; -1 not a cap.
varying flat int v_cap;
varying vec2 v_texcoord;
varying vec2 v_map;
varying float v_height;
// The surface's normal (map axes): towards the front sector for walls, up for floors.
varying flat vec3 v_normal;
varying float v_depth;

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
	bool back_face = kind == 3;
	if (back_face) {
		kind = 2;
	}
	v_cap = -1;
	if (kind == 4) {
		kind = 0;
		v_cap = int(CUSTOM0.w);
	}
	int front_id = int(CUSTOM0.z);
	int back_id = v_cap >= 0 ? -1 : int(CUSTOM0.w);
	vec4 front = data_texel(sector_data, front_id);
	vec4 back = back_id >= 0 ? data_texel(sector_data, back_id) : front;
	ivec4 rect = ivec4(data_texel(texture_info, int(CUSTOM0.y)) + 0.5);
	v_rect = rect;
	v_kind = kind;
	int light = clamp(int(front.b + 0.5), 0, 255);
	v_lightnum = (light >> LIGHTSEGSHIFT) + extralight + (kind == 0 ? 0 : int(CUSTOM2.z));

	float z;
	if (kind == 0) {
		z = front.r;
		v_texcoord = UV;
		if (v_cap >= 0) {
			vec4 c = v_cap == 0 ? cut_player : cut_cursor;
			z = c.z + cut_height;
			if (cut_mode == 0 || cut_cap == 0 || c.w < 0.5 || front.r <= z) {
				// Nothing to cap: collapse the triangle.
				VERTEX.xz = vec2(0.0);
			}
			if (cut_cap == 2) {
				v_lightnum -= CAP_DARKEN;
			}
		}
	} else {
		// WallSection.Span: clipped to the front sector, empty when top <= bottom.
		float bottom = max(plane_height(int(CUSTOM1.x), front, back) + CUSTOM1.y, front.r);
		float top = min(plane_height(int(CUSTOM1.z), front, back) + CUSTOM1.w, front.g);
		float texture_top = plane_height(int(CUSTOM2.x), front, back) + CUSTOM2.y;
		if (kind == 2) {
			// Drawn once: only the texture's rows, texture_top down by its height.
			top = min(top, texture_top);
			bottom = max(bottom, texture_top - float(rect.w));
		}
		top = max(top, bottom);
		if (back_face && masked_backs == 0) {
			top = bottom;
		}
		z = UV.y > 0.5 ? top : bottom;
		v_texcoord = vec2(UV.x, texture_top - z);
	}
	VERTEX.y = z / MAP_UNITS_PER_METRE;
	v_map = vec2(VERTEX.x, -VERTEX.z) * MAP_UNITS_PER_METRE;
	v_height = z;
	v_normal = kind == 0 ? vec3(0.0, 0.0, 1.0) : vec3(sin(CUSTOM2.w), -cos(CUSTOM2.w), 0.0);
	v_depth = -(MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).z * MAP_UNITS_PER_METRE;
}

// IsoDoom.Map.LightTables.WallColormap / PlaneColormap.
int light_colormap() {
	if (colormap_override >= 0) {
		return colormap_override;
	}
	float d = light_mode == 0 ? light_reference : light_mode == 1 ? max(length(v_map - light_origin), light_near) : v_depth;
	int dist = int(clamp(d, 0.0, MAX_DISTANCE) * 65536.0);
	int lightnum = clamp(v_lightnum, 0, LIGHTLEVELS - 1);
	if (v_kind == 0) {
		int z = dist <= 0 ? 0 : min(dist >> LIGHTZSHIFT, MAXLIGHTZ - 1);
		return int(texelFetch(light_tables, ivec2(z, lightnum), 0).r * 255.0 + 0.5);
	}
	int scale = dist <= 0 ? MAXLIGHTSCALE - 1 : min((light_centerx << 20) / dist, MAXLIGHTSCALE - 1);
	return int(texelFetch(light_tables, ivec2(scale, LIGHTLEVELS + lightnum), 0).r * 255.0 + 0.5);
}

int floor_mod(int a, int n) {
	int m = a % n;
	return m < 0 ? m + n : m;
}

// IsoDoom.Render.Cutaway.Hides: whether centre c cuts wall point p (map units).
// n: the surface's normal, turned towards the camera; to_camera: the unit
// direction from the anchor towards the camera, null for perspective (then
// computed from cam, the camera position).
bool cut_hides(vec3 p, vec3 n, vec4 c, bool ortho, vec3 to_camera, vec3 cam) {
	if (c.w < 0.5 || p.z <= c.z + cut_height) {
		return false;
	}
	vec3 a = vec3(c.xy, c.z + cut_anchor);
	if (dot(a - p, n) > -CUT_PLANE_MARGIN) {
		return false;
	}
	vec3 axis = ortho ? to_camera : normalize(cam - a);
	vec3 d = p - a;
	return length(d - axis * dot(d, axis)) < cut_radius;
}

// 4x4 ordered dither threshold (0-1) of a pixel.
float bayer4(ivec2 q) {
	const int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[(q.y & 3) * 4 + (q.x & 3)]) + 0.5) / 16.0;
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	vec3 p = vec3(v_map, v_height);
	if (cut_mode != 0) {
		// Godot space (x, height, -y, metres) to map units (x, y, height).
		vec3 cam = vec3(CAMERA_POSITION_WORLD.x, -CAMERA_POSITION_WORLD.z, CAMERA_POSITION_WORLD.y) * MAP_UNITS_PER_METRE;
		// The camera's +Z axis: from the scene towards the camera.
		vec3 back = normalize(INV_VIEW_MATRIX[2].xyz);
		vec3 to_camera = vec3(back.x, -back.z, back.y);
		bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
		vec3 n = v_normal;
		if (dot(n, ortho ? to_camera : cam - p) < 0.0) {
			n = -n;
		}
		if (cut_hides(p, n, cut_player, ortho, to_camera, cam) || cut_hides(p, n, cut_cursor, ortho, to_camera, cam)) {
			if (cut_mode == 1 || bayer4(ivec2(FRAGCOORD.xy)) < 0.5) {
				discard;
			}
		}
		if (v_cap >= 0) {
			// A cap shows only inside its centre's disc, where the floor above it is cut.
			vec4 c = v_cap == 0 ? cut_player : cut_cursor;
			vec3 a = vec3(c.xy, c.z + cut_anchor);
			vec3 axis = ortho ? to_camera : normalize(cam - a);
			vec3 d = p - a;
			if (length(d - axis * dot(d, axis)) >= cut_radius) {
				discard;
			}
		}
	}
	ivec2 t = ivec2(floor(v_texcoord));
	int w = v_rect.z;
	int h = v_rect.w;
	int col;
	int row;
	if (v_kind == 0) {
		col = t.x & 63;
		row = t.y & 63;
	} else if (v_kind == 2) {
		// IsoDoom.Map.TextureWrap.MaskedTexel: columns as solid walls, rows not repeated.
		col = wall_tiling == 0 ? t.x & ((1 << findMSB(w)) - 1) : floor_mod(t.x, w);
		row = clamp(t.y, 0, h - 1);
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
	vec2 texel = texelFetch(atlas, v_rect.xy + ivec2(col, row), 0).rg;
#ifdef MASKED_PASS
	if (texel.g < 0.5) {
		discard;
	}
#endif
	int index = int(texel.r * 255.0 + 0.5);
	int mapped = int(texelFetch(colormap, ivec2(index, light_colormap()), 0).r * 255.0 + 0.5);
	// PLAYPAL holds sRGB values; ALBEDO is linear (Godot converts back on output).
	ALBEDO = srgb_to_linear(texelFetch(playpal, ivec2(mapped, palette_index), 0).rgb);
}
