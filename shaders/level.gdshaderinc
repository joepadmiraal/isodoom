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
// B = light level (0-255), A = the floor flat's texture slot. The vertex
// shader places every vertex from it, so changing a sector's heights moves
// its floor and walls without a rebuild (T5.1: the sim's, interpolated).
// `side_textures` (RG float, same layout, three texels per sidedef: upper,
// lower, middle; IsoDoom.Render.LevelMesh.SidePartId): R the texture slot of
// each sidedef texture, -1 for none, so a switch changes a wall's texture
// without a rebuild (T5.1); G how far the sidedef's textureoffset moved since
// the build (map units, the same in all three), added to the texture column,
// so a scrolling wall (linedef special 48) scrolls without a rebuild (T5.7).
// `texture_info` (RGBA float, same layout, one texel per texture slot): atlas
// x, y, width, height; an animation re-points a slot to another frame's
// rectangle (vanilla's texturetranslation / flattranslation, T5.7).
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
//              4 a floor's cutaway cap, 5 a lid, 6 a wall cap, 7 a wall cap's cutaway cap, 8 an
//              island's wall above its ceiling, 9 a switch marker, 10 a ledge edge, 11 a lid's end), side
//              part (walls and wall caps: the side_textures texel; floors 0, their slot is the
//              sector's), front sector, back sector (-1: none; for a cap its cut centre, 0
//              player, 1 cursor; for kind 8 its island; for kind 10 the sector across its line)
//   floors:    UV = (map x, -map y): the flat texel is floor(UV) & 63 (r_draw.c R_DrawSpan)
//   wall caps: UV = (texture column, distance behind the wall); CUSTOM1.x the wall's island
//              (-1: none); CUSTOM2.w the wall's direction
//   ledge edges: UV = (distance along the line, distance in from it)
//   switch markers: UV = (texture column, texture row); CUSTOM1 the switch section's planes
//              as walls'; CUSTOM2.w its direction
//   walls:     UV.x = texture column (textureoffset at the build + distance from V1; the
//              side's scroll is added, T5.7), UV.y = 0 bottom, 1 top
//              CUSTOM1 = bottom plane, bottom offset, top plane, top offset
//              CUSTOM2 = texture-top plane (+ 8: add the slot's texture height, for a texture
//                        whose bottom is pegged; T5.1), texture-top offset, fake contrast
//                        (-1, 0, +1), direction from end A to B (radians; LevelMesh.PieceAngle)
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
//     in line with it, keep their full height). For a floor above the
//     cutoff (the top of a block whose walls are cut, a lid) that always
//     holds, so instead p must be on the camera's side of the anchor,
//     horizontally, by more than CUT_PLANE_MARGIN: a floor or lid behind the
//     centre (a door the player stands in front of) cannot hide it and stays;
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
// the other centre as any floor. With the lids on, it also caps a lid's solid
// (below): where the sector's ceiling is at or below the cutoff and its lid
// above it, showing the lid's flat.
//
// Lids (T6.13b; IsoDoom.Render.DoorLids, `lids` 0 off, 1 on): kind 5 is a
// copy of a lid sector's floor (a door, a lintel), placed at its lid height
// from `sector_lids` (RGBA float, one texel per sector: R the lid height, the
// lowest neighbouring ceiling as drawn, very low for none; G the ceiling
// flat's texture slot; B 1 for a door, T6.13d; A 1 when its ceiling moves: a
// door or a crusher, below) and drawn while the sector's
// ceiling is below it: the top of the solid between the upper walls around
// it. It shows the ceiling flat, lit as a floor of the sector, and is cut as
// any floor.
//
// Upper walls (IsoDoom.Render.DoorLids.KeepsUppers, `upper_walls` 0 all, 1
// doors): with 1, an upper wall (part 0 of a side, kind 1) collapses unless
// the sector across its line keeps it: its ceiling moves (`sector_lids` A: a
// door, a crusher) or its lid shows (R above its ceiling, the lids on or not:
// a lintel, a window, a closed door). Over a room with a lower ceiling, which
// is not drawn, it would hang in the air as a band.
//
// Doors (T6.13d; IsoDoom.Render.DoorLids.IsDoor, `cut_doors` 0 keep, 1 cut):
// with 0, nothing of a door sector is cut: its floor, its lid, and every wall
// on a line with the door on either side; its caps collapse.
//
// Wall caps (T6.13e; IsoDoom.Render.WallCaps, `wall_caps` 0 off, 1 on): kind 6
// is a piece of the strip of void behind a one-sided wall, placed at the
// wall's top (its sector's ceiling, or its lid when the lids are on and it has
// one higher: a door's jambs) and drawn while the wall faces the camera (the
// view direction; a wall facing away is culled, and its cap would float over
// its room) and has a height. It shows the wall's texture, its rows going
// back from the wall's face (v_flat's kind 3: textured as a wall, lit as a floor of
// the sector), and is cut as any floor, but only where its wall would be: the
// centre behind the wall's plane by more than CUT_PLANE_MARGIN (a player in
// front of a wall keeps its top whole, as its face). Kind 7 is the same piece per cut
// centre (CUSTOM0.w), placed at its cutoff as the floors' caps where the wall
// reaches above it: a cut wall reads as a block cut down to its stub.
// An island (a void enclosed by one loop of walls: a pillar; `wall_islands`,
// R float, one texel per island: its top, its walls' highest ceiling as drawn)
// makes one column: its walls' caps go up to its top, and kind 8, a quad of
// each of its walls again (as kind 1, its sectors' plane references unused),
// draws the wall on from its sector's ceiling up to that top, its texture
// going on upwards (collapsed with the caps off, or where the ceiling is the top).
//
// Lid ends (T6.13f; IsoDoom.Render.DoorLids.HasEnd): kind 11 is a quad of
// a lid sector's one-sided wall (a door's jamb) again, as kind 1 (its plane
// references unused), from the sector's ceiling up to its lid while the lid
// shows (the lids on), wound the other way: the end of the lid's block,
// facing the void. It shows when the wall faces away from the camera (then
// it has no cap), lit and textured as the wall going on upwards (its columns
// mirrored, as a back face's), and is cut, kept with the doors and fogged as
// its sector's walls.
//
// Switch markers (T6.13p; IsoDoom.Render.SwitchMarkers, `switch_markers` 0 off,
// 1 on): kind 9 is a piece of the decal on the floor in front of a switch side,
// placed SWITCH_MARKER_LIFT above its sector's floor and drawn while the wall
// does not face the camera (facing away, its quad culled; edge-on; or seen
// straight down) and the switch's section has a height (WallSection.Span, as
// the walls'). It shows the side's texture of that part (its current one, so a
// pressed switch shows its other texture), UV its column and row (the wall laid
// back onto the floor, mirrored so that it reads from behind the wall), lit as
// a floor of the sector (v_flat's kind 3, not a wall cap: no wall normal).
//
// Height cues (T6.13s; IsoDoom.Render.LedgeEdges, `height_cues` a bit mask: 1
// edges, 2 shade): kind 10 is a piece of the strip along one side of a
// two-sided line, placed LEDGE_LIFT above its sector's floor and drawn while
// that floor is more than LEDGE_MIN_DROP above the floor across the line (a
// ledge the player could not step back up). It shows palette index
// LEDGE_INDEX lit at least LEDGE_BRIGHTEN light levels above its floor and at
// least LEDGE_MIN_LIGHT, its innermost LEDGE_RIM units index 0 (a dark rim, so
// it reads on a bright floor too); v_flat's kind 4, lit as a floor and cut as one.
// With shade, every surface lit as a floor (floors, their caps, lids, wall
// caps, switch markers, ledge edges) gains a light level per SHADE_STEP units
// it is above `height_reference` (the player's feet), and loses one per
// SHADE_STEP below, at most SHADE_MAX either way (none while
// `height_reference_on` is 0); the darkening stops at SHADE_MIN_LIGHT (a
// surface already below it is not darkened), so a deep drop never goes black.
//
// Fog of war (T6.13l; IsoDoom.Render.FogOfWar, `fog_mode` 0 off, 1 dim, 2
// hide): `sector_fog` (R8, one texel per sector, the same layout) holds each
// sector's IsoDoom.Render.SectorSight (0 unseen, 1 discovered, 2 visible).
// Every kind takes its front sector's (CUSTOM0.z: a wall its own side's
// sector, a cap or lid its sector's); a two-sided wall the more seen of its
// two sectors' (CUSTOM0.w its back), as it is the face of the solid across
// it too (a shut door's face on the unseen side). An unseen surface is collapsed with
// hide; with dim its texel is drawn as `fog_dim` maps it (R8, 256 x 1: a
// fixed dark colormap row towards grey, FogOfWar.DimMap), whatever its light,
// and with `fog_dither` 1 half its pixels discard in the 4x4 dither, so the
// void shows through.

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
// IsoDoom.Render.SwitchMarkers.Lift: a switch marker's height above its floor (map units).
const float SWITCH_MARKER_LIFT = 1.0;
// IsoDoom.Render.LedgeEdges (T6.13s): MinDrop, Lift, Width, ShadeStep, ShadeMax, ShadeMinLight, ColourIndex, Brighten, MinLight, Rim.
const float LEDGE_MIN_DROP = 24.0;
const float LEDGE_LIFT = 0.5;
const float LEDGE_WIDTH = 3.0;
const float SHADE_STEP = 24.0;
const int SHADE_MAX = 5;
const int SHADE_MIN_LIGHT = 8;
const int LEDGE_INDEX = 80;
const int LEDGE_BRIGHTEN = 4;
const int LEDGE_MIN_LIGHT = 10;
const float LEDGE_RIM = 1.0;

uniform sampler2D atlas : filter_nearest, repeat_disable;
uniform sampler2D texture_info : filter_nearest, repeat_disable;
uniform sampler2D sector_data : filter_nearest, repeat_disable;
uniform sampler2D sector_lids : filter_nearest, repeat_disable;
uniform sampler2D wall_islands : filter_nearest, repeat_disable;
uniform sampler2D side_textures : filter_nearest, repeat_disable;
// PLAYPAL's rows (and the blended flash's, LevelMesh.BlendRow) in linear
// colour, RGBA float, what ALBEDO takes: no sRGB conversion per fragment
// (T8.4h; IsoDoom.Render.IndexedTextures.ToLinearRgbaf). COLORMAP: R8, 256 x maps (as palette.gdshader).
uniform sampler2D playpal_linear : filter_nearest, repeat_disable;
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
// The lids (T6.13b): 0 off, 1 on.
uniform int lids = 1;
// Doors (T6.13d): 0 kept whole, 1 cut as the rest.
uniform int cut_doors = 0;
// The wall caps (T6.13e): 0 off, 1 on.
uniform int wall_caps = 1;
// The switch markers (T6.13p): 0 off, 1 on.
uniform int switch_markers = 1;
// The upper walls: 0 all, 1 only over the sectors that keep them.
uniform int upper_walls = 1;
// T6.13s: the height cues (IsoDoom.Render.HeightCueMode, a bit mask: 1 ledge edges, 2 height shading) and the shading's reference height.
uniform int height_cues = 1;
uniform float height_reference = 0.0;
uniform int height_reference_on = 0;
// The fog of war (T6.13l): 0 off, 1 dim, 2 hide.
uniform int fog_mode = 0;
uniform sampler2D sector_fog : filter_nearest, repeat_disable;
uniform sampler2D fog_dim : filter_nearest, repeat_disable;
uniform int fog_dither = 0;

// One varying of our own (T8.4d, SPEC §12: a Raspberry Pi's V3D has room for
// one beside Godot's built-ins; ShaderVaryingTests), flat:
//   x  the texture slot (texture_info; -1 none), or with FLAT_RECT (kind 0: a
//      flat, 64 x 64) its atlas rectangle's corner, x | y << 16 (T8.4h: no
//      texture_info fetch per fragment)
//   y  bits 0-7 the light number + LIGHTNUM_BIAS; 8-11 the kind (0 floor, 1 solid
//      wall, 2 masked middle, 3 wall cap: a wall's texture, a floor's light, 4
//      ledge edge, T6.13s); 12-13 the cut centre whose cap this is + 1 (a cutaway cap,
//      T3.4a: 0 player, 1 cursor; -1 not a cap); FLAT_DOOR the cutaway leaves it
//      whole (a door's while doors are kept, T6.13d); FLAT_FOG the fog of war
//      dims it (its sector unseen, T6.13l); FLAT_WALL_CAP a wall cap's wall normal
//      is set (kind 3; not a switch marker's, T6.13p); FLAT_RECT x is the
//      flat's atlas corner
//   z  the wall's direction (CUSTOM2.w, float bits): the normal (map axes)
//      towards the front sector of a wall (kinds 1, 2), and of a wall cap's
//      wall (kind 3, whose texture row is how far behind it); others face up
//   w  float bits: the height of a surface lit as a floor (kinds 0, 3, 4); a
//      wall's texture top, its height that less its texture row
// The rest rides on Godot's built-ins: UV the texture coordinates, UV2 the map
// point, and the view depth is the fragment's VERTEX.
varying flat ivec4 v_flat;
const int LIGHTNUM_BIAS = 128;
const int FLAT_DOOR = 1 << 14;
const int FLAT_FOG = 1 << 15;
const int FLAT_WALL_CAP = 1 << 16;
const int FLAT_RECT = 1 << 17;

// (id >= 0 and SECTOR_DATA_WIDTH 256: a mask and a shift, cheaper than % and / on V3D, T8.4h)
vec4 data_texel(sampler2D tex, int id) {
	return texelFetch(tex, ivec2(id & (SECTOR_DATA_WIDTH - 1), id >> 8), 0);
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

// T6.13s: the light levels the height shading adds to a surface lit as a floor at height z (IsoDoom.Render.LedgeEdges.ShadeLevels).
int height_shade(float z) {
	if ((height_cues & 2) == 0 || height_reference_on == 0) {
		return 0;
	}
	return clamp(int(floor((z - height_reference) / SHADE_STEP + 0.5)), -SHADE_MAX, SHADE_MAX);
}

// T6.13s: light number lightnum shaded for a surface lit as a floor at height z, darkened no lower than SHADE_MIN_LIGHT (IsoDoom.Render.LedgeEdges.ShadeLight).
int shade_light(int lightnum, float z) {
	int shade = height_shade(z);
	return shade < 0 ? max(lightnum + shade, min(lightnum, SHADE_MIN_LIGHT)) : lightnum + shade;
}

// T6.13l: the sector's IsoDoom.Render.SectorSight (2, visible, with the fog off).
int fog_state(int sector) {
	return fog_mode == 0 ? 2 : int(data_texel(sector_fog, sector).r * 255.0 + 0.5);
}

void vertex() {
	// What the fragment shader reads (v_flat, UV, UV2).
	int o_slot = -1;
	int o_kind = 0;
	int o_lightnum = 0;
	int o_cap = -1;
	int o_door = 0;
	int o_fog = 0;
	bool o_wall_cap = false;
	vec2 o_texcoord = vec2(0.0);
	float o_height = 0.0;
	int kind = int(CUSTOM0.x);
	bool back_face = kind == 3;
	if (back_face) {
		kind = 2;
	}
	bool lid = kind == 5;
	if (kind == 4) {
		kind = 0;
		o_cap = int(CUSTOM0.w);
	} else if (lid) {
		kind = 0;
	}
	int front_id = int(CUSTOM0.z);
	// A two-sided wall (an upper or lower wall, a masked middle) is the face of the solid across it too:
	// seen when either sector is (a shut door's far face, the edge of a seen floor).
	int fog_back = (kind == 1 || kind == 2) && int(CUSTOM0.w) >= 0 ? fog_state(int(CUSTOM0.w)) : 0;
	bool unseen = max(fog_state(front_id), fog_back) == 0;
	o_fog = unseen && fog_mode == 1 ? 1 : 0;
	bool fog_hides = unseen && fog_mode == 2;
	if (kind == 10) {
		// T6.13s: a ledge edge, on its sector's floor while it is a ledge's top.
		vec4 l_front = data_texel(sector_data, front_id);
		vec4 l_back = data_texel(sector_data, int(CUSTOM0.w));
		bool l_collapse = (height_cues & 1) == 0 || l_front.r - l_back.r <= LEDGE_MIN_DROP;
		o_door = cut_doors == 0 && data_texel(sector_lids, front_id).b > 0.5 ? 1 : 0;
		o_slot = -1;
		o_kind = 4;
		o_cap = -1;
		o_lightnum = shade_light(max((int(floor(l_front.b + 0.5)) >> LIGHTSEGSHIFT) + LEDGE_BRIGHTEN, LEDGE_MIN_LIGHT) + extralight, l_front.r);
		o_texcoord = UV;
		if (l_collapse || fog_hides) {
			VERTEX.xz = vec2(0.0);
		}
		float l_z = l_front.r + LEDGE_LIFT;
		VERTEX.y = l_z / MAP_UNITS_PER_METRE;
		o_height = l_z;
	} else if (kind == 9) {
		// T6.13p: a switch marker, on its sector's floor.
		vec4 m_front = data_texel(sector_data, front_id);
		int m_back_id = int(CUSTOM0.w);
		vec4 m_back = m_back_id >= 0 ? data_texel(sector_data, m_back_id) : m_front;
		vec4 m_side = data_texel(side_textures, int(CUSTOM0.y));
		int m_slot = int(floor(m_side.r + 0.5));
		// The switch's span (WallSection.Span, as the walls').
		float m_bottom = max(plane_height(int(CUSTOM1.x), m_front, m_back) + CUSTOM1.y, m_front.r);
		float m_top = min(plane_height(int(CUSTOM1.z), m_front, m_back) + CUSTOM1.w, m_front.g);
		// Towards the front sector, and the view direction towards the camera, both map axes.
		vec2 n = vec2(sin(CUSTOM2.w), -cos(CUSTOM2.w));
		vec3 view_back = INV_VIEW_MATRIX[2].xyz;
		vec2 to_camera = vec2(view_back.x, -view_back.z);
		bool m_collapse = switch_markers == 0 || m_slot < 0 || m_top <= m_bottom || dot(n, to_camera) > 0.001;
		o_door = cut_doors == 0 && data_texel(sector_lids, front_id).b > 0.5 ? 1 : 0;
		o_slot = m_slot;
		o_kind = 3;
		o_lightnum = (int(floor(m_front.b + 0.5)) >> LIGHTSEGSHIFT) + extralight;
		o_texcoord = vec2(UV.x + m_side.g, UV.y);
		if (m_collapse || fog_hides) {
			VERTEX.xz = vec2(0.0);
		}
		float m_z = m_front.r + SWITCH_MARKER_LIFT;
		o_lightnum = shade_light(o_lightnum, m_front.r);
		VERTEX.y = m_z / MAP_UNITS_PER_METRE;
		o_height = m_z;
	} else if (kind == 6 || kind == 7) {
		// T6.13e: a wall cap (kind 6) or its cutaway cap (kind 7, CUSTOM0.w its centre).
		vec4 cap_front = data_texel(sector_data, front_id);
		vec4 cap_lid = data_texel(sector_lids, front_id);
		vec4 cap_side = data_texel(side_textures, int(CUSTOM0.y));
		int cap_slot = int(floor(cap_side.r + 0.5));
		o_door = cut_doors == 0 && cap_lid.b > 0.5 ? 1 : 0;
		// The wall's top: its sector's ceiling, or its lid above it (a closed door's jambs).
		float top = lids == 1 && cap_lid.g >= 0.0 ? max(cap_front.g, cap_lid.r) : cap_front.g;
		// An island's walls all reach its top.
		int cap_island = int(CUSTOM1.x);
		if (cap_island >= 0) {
			top = max(top, data_texel(wall_islands, cap_island).r);
		}
		// Towards the front sector, and the view direction towards the camera, both map axes.
		vec2 n = vec2(sin(CUSTOM2.w), -cos(CUSTOM2.w));
		o_wall_cap = true;
		vec3 view_back = INV_VIEW_MATRIX[2].xyz;
		vec2 to_camera = vec2(view_back.x, -view_back.z);
		bool cap_collapse = wall_caps == 0 || cap_slot < 0 || top <= cap_front.r || dot(n, to_camera) < -0.001 * length(to_camera);
		float cap_z = top;
		o_cap = kind == 7 ? int(CUSTOM0.w) : -1;
		if (o_cap >= 0) {
			vec4 c = o_cap == 0 ? cut_player : cut_cursor;
			cap_z = c.z + cut_height;
			cap_collapse = cap_collapse || cut_mode == 0 || cut_cap == 0 || c.w < 0.5 || o_door == 1 || top <= cap_z;
		}
		o_slot = cap_slot;
		o_kind = 3;
		o_lightnum = (int(floor(cap_front.b + 0.5)) >> LIGHTSEGSHIFT) + extralight - (o_cap >= 0 && cut_cap == 2 ? CAP_DARKEN : 0);
		o_lightnum = shade_light(o_lightnum, cap_z);
		o_texcoord = vec2(UV.x + cap_side.g, UV.y);
		if (cap_collapse || fog_hides) {
			VERTEX.xz = vec2(0.0);
		}
		VERTEX.y = cap_z / MAP_UNITS_PER_METRE;
		o_height = cap_z;
	} else {
		// T6.13e: an island's wall above its ceiling, drawn as a solid wall.
		bool extension = kind == 8;
		int island = extension ? int(CUSTOM0.w) : -1;
		// T6.13f: a lid's block's end, a lid sector's one-sided wall from its ceiling up to its lid.
		bool lid_end = kind == 11;
		if (extension || lid_end) {
			kind = 1;
		}
		int back_id = o_cap >= 0 || extension || lid_end ? -1 : int(CUSTOM0.w);
		vec4 front = data_texel(sector_data, front_id);
		vec4 back = back_id >= 0 ? data_texel(sector_data, back_id) : front;
		vec4 side = kind == 0 ? vec4(0.0) : data_texel(side_textures, int(CUSTOM0.y));
		// T6.13b: the sector's lid height and its flat's slot (kind 5, and a cap of a lid's solid).
		vec2 lid_data = kind == 0 ? data_texel(sector_lids, front_id).rg : vec2(0.0);
		// T6.13d: a door's floor, lid or caps, or a wall on a door's line.
		bool door = data_texel(sector_lids, front_id).b > 0.5 || (kind != 0 && back_id >= 0 && data_texel(sector_lids, back_id).b > 0.5);
		o_door = cut_doors == 0 && door ? 1 : 0;
		int lid_slot = int(floor(lid_data.g + 0.5));
		int slot = kind == 0 ? int(front.a + 0.5) : int(floor(side.r + 0.5));
		bool collapse = false;
		float z = front.r;
		if (lid) {
			z = lid_data.r;
			slot = lid_slot;
			collapse = lids == 0 || front.g >= z || slot < 0;
		} else if (o_cap >= 0) {
			vec4 c = o_cap == 0 ? cut_player : cut_cursor;
			z = c.z + cut_height;
			// The floor's block, or else the solid above the sector up to its lid, spans the cutoff.
			bool solid_lid = lids == 1 && front.g <= z && z < lid_data.r && lid_slot >= 0;
			if (front.r <= z && solid_lid) {
				slot = lid_slot;
			}
			// Nothing to cap: collapse the triangle.
			collapse = cut_mode == 0 || cut_cap == 0 || c.w < 0.5 || o_door == 1 || (front.r <= z && !solid_lid);
		}
		ivec4 rect = slot >= 0 ? ivec4(data_texel(texture_info, slot) + 0.5) : ivec4(0);
		o_slot = slot;
		o_kind = kind;
		// (not clamped before the shift, as r_segs.c: light 256 is 16 before the fake contrast; P3, D2TWID MAP31)
		int light = int(floor(front.b + 0.5));
		o_lightnum = (light >> LIGHTSEGSHIFT) + extralight + (kind == 0 ? 0 : int(CUSTOM2.z));

		if (kind == 0) {
			o_texcoord = UV;
			if (collapse || fog_hides) {
				VERTEX.xz = vec2(0.0);
			}
			if (o_cap >= 0 && cut_cap == 2) {
				o_lightnum -= CAP_DARKEN;
			}
			o_lightnum = shade_light(o_lightnum, z);
		} else {
			// WallSection.Span: clipped to the front sector, empty when top <= bottom.
			float bottom = max(plane_height(int(CUSTOM1.x), front, back) + CUSTOM1.y, front.r);
			float top = min(plane_height(int(CUSTOM1.z), front, back) + CUSTOM1.w, front.g);
			int texture_plane = int(CUSTOM2.x);
			float texture_top = plane_height(texture_plane & 7, front, back) + CUSTOM2.y + ((texture_plane & 8) != 0 ? float(rect.w) : 0.0);
			if (kind == 2) {
				// Drawn once: only the texture's rows, texture_top down by its height.
				top = min(top, texture_top);
				bottom = max(bottom, texture_top - float(rect.w));
			}
			if (extension) {
				// From the ceiling up to the island's top.
				bottom = front.g;
				top = wall_caps == 1 ? data_texel(wall_islands, island).r : bottom;
			}
			if (lid_end) {
				// From the ceiling up to the lid, while the lid shows (DoorLids.Shows, its flat's slot set).
				vec2 end_lid = data_texel(sector_lids, front_id).rg;
				bottom = front.g;
				top = lids == 1 && end_lid.g >= 0.0 ? end_lid.r : bottom;
			}
			top = max(top, bottom);
			// An upper wall over a sector that does not keep it (a room's lower ceiling).
			bool dropped = false;
			if (upper_walls == 1 && kind == 1 && !extension && back_id >= 0 && int(CUSTOM0.y + 0.5) % 3 == 0) {
				vec4 back_lid = data_texel(sector_lids, back_id);
				dropped = back_lid.a < 0.5 && back_lid.r <= back.g;
			}
			if ((back_face && masked_backs == 0) || slot < 0 || dropped || fog_hides) {
				// Collapsed: a back face turned off, a texture changed to none, a dropped upper wall, or hidden by the fog (T6.13l).
				top = bottom;
			}
			z = UV.y > 0.5 ? top : bottom;
			o_texcoord = vec2(UV.x + side.g, texture_top - z);
			// The fragment's height is this less its texture row.
			o_height = texture_top;
		}
		VERTEX.y = z / MAP_UNITS_PER_METRE;
		if (kind == 0) {
			o_height = z;
		}
	}
	// A flat's atlas corner in place of its slot (T8.4h).
	int o_first = o_slot;
	if (o_kind == 0) {
		ivec2 corner = o_slot >= 0 ? ivec2(data_texel(texture_info, o_slot).xy + 0.5) : ivec2(0);
		o_first = corner.x | (corner.y << 16);
	}
	// Into the one varying (v_flat) and the built-ins; UV is read above, so it is written last.
	v_flat = ivec4(o_first,
		(o_lightnum + LIGHTNUM_BIAS) | (o_kind << 8) | ((o_cap + 1) << 12)
			| (o_door == 1 ? FLAT_DOOR : 0) | (o_fog == 1 ? FLAT_FOG : 0) | (o_wall_cap ? FLAT_WALL_CAP : 0)
			| (o_kind == 0 ? FLAT_RECT : 0),
		floatBitsToInt(CUSTOM2.w), floatBitsToInt(o_height));
	UV = o_texcoord;
	UV2 = vec2(VERTEX.x, -VERTEX.z) * MAP_UNITS_PER_METRE;
}

// r_segs.c's wall scale, min((light_centerx << 20) / dist, MAXLIGHTSCALE - 1)
// (dist > 0 a fixed_t distance up to MAX_DISTANCE units, so under 2^29),
// without an integer division (V3D has none, T8.4h): the float quotient is
// within 1 of the integer one, which one step either way then makes exact.
int wall_scale(int dist) {
	if (dist <= 0) {
		return MAXLIGHTSCALE - 1;
	}
	int n = light_centerx << 20;
	int q = min(int(float(n) / float(dist)), MAXLIGHTSCALE);
	if (q * dist > n) {
		q--;
	} else if ((q + 1) * dist <= n) {
		q++;
	}
	return min(q, MAXLIGHTSCALE - 1);
}

// IsoDoom.Map.LightTables.WallColormap / PlaneColormap: of a surface of kind
// `kind` (v_flat) at map point `map`, `depth` from the camera.
int light_colormap(int kind, int lightnum, vec2 map, float depth) {
	if (colormap_override >= 0) {
		return colormap_override;
	}
	float d = light_mode == 0 ? light_reference : light_mode == 1 ? max(length(map - light_origin), light_near) : depth;
	int dist = int(clamp(d, 0.0, MAX_DISTANCE) * 65536.0);
	lightnum = clamp(lightnum, 0, LIGHTLEVELS - 1);
	if (kind == 0 || kind == 3 || kind == 4) {
		int z = dist <= 0 ? 0 : min(dist >> LIGHTZSHIFT, MAXLIGHTZ - 1);
		return int(texelFetch(light_tables, ivec2(z, lightnum), 0).r * 255.0 + 0.5);
	}
	return int(texelFetch(light_tables, ivec2(wall_scale(dist), LIGHTLEVELS + lightnum), 0).r * 255.0 + 0.5);
}

// floor(a / n) for n > 0 and |a| < 2^22 without an integer division (V3D has
// none, T8.4h): the float quotient is within 1, which one step makes exact.
int floor_div(int a, int n) {
	int q = int(floor(float(a) / float(n)));
	int m = a - q * n;
	if (m < 0) {
		q--;
	} else if (m >= n) {
		q++;
	}
	return q;
}

int floor_mod(int a, int n) {
	return a - floor_div(a, n) * n;
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
	if (n.z > 0.5) {
		// A floor or lid: only on the camera's side of the anchor, horizontally.
		vec2 toward = ortho ? to_camera.xy : cam.xy - a.xy;
		if (dot(toward, toward) < 1e-6 || dot(p.xy - a.xy, normalize(toward)) <= CUT_PLANE_MARGIN) {
			return false;
		}
	}
	vec3 axis = ortho ? to_camera : normalize(cam - a);
	vec3 d = p - a;
	return length(d - axis * dot(d, axis)) < cut_radius;
}

// IsoDoom.Render.Cutaway.WallCut (T6.13e): whether a wall cap's wall would be
// cut by centre c, the centre behind the wall's plane (it faces the camera):
// `wall_normal` the wall's normal towards its sector, `behind` how far behind
// the wall p is. Always true for any other surface (`wall_normal` zero).
bool cap_wall_cut(vec3 p, vec4 c, vec2 wall_normal, float behind) {
	if (wall_normal == vec2(0.0)) {
		return true;
	}
	vec2 a = c.xy;
	// The cap point lies `behind` behind the wall: the centre's distance in front of it.
	return dot(a - p.xy, wall_normal) - behind <= -CUT_PLANE_MARGIN;
}

// 4x4 ordered dither threshold (0-1) of a pixel.
float bayer4(ivec2 q) {
	const int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[(q.y & 3) * 4 + (q.x & 3)]) + 0.5) / 16.0;
}

void fragment() {
	// v_flat's fields (as the vertex shader packs them) and the built-ins' values.
	int slot = v_flat.x;
	int bits = v_flat.y;
	int lightnum = (bits & 255) - LIGHTNUM_BIAS;
	int kind = (bits >> 8) & 15;
	int cap = ((bits >> 12) & 3) - 1;
	bool door = (bits & FLAT_DOOR) != 0;
	bool fog = (bits & FLAT_FOG) != 0;
	float direction = intBitsToFloat(v_flat.z);
	bool wall = kind == 1 || kind == 2;
	vec2 texcoord = UV;
	vec2 map = UV2;
	float height = wall ? intBitsToFloat(v_flat.w) - texcoord.y : intBitsToFloat(v_flat.w);
	if (fog && fog_dither == 1 && bayer4(ivec2(FRAGCOORD.xy)) < 0.5) {
		discard;
	}
	vec3 p = vec3(map, height);
	// A centre cuts only above its cutoff (cut_hides' first test, T8.4h: the rest is skipped below every cutoff); a cap tests its centre.
	bool cut_reach = (cut_player.w >= 0.5 && p.z > cut_player.z + cut_height) || (cut_cursor.w >= 0.5 && p.z > cut_cursor.z + cut_height);
	if (cut_mode != 0 && !door && (cut_reach || cap >= 0)) {
		// Godot space (x, height, -y, metres) to map units (x, y, height).
		vec3 cam = vec3(CAMERA_POSITION_WORLD.x, -CAMERA_POSITION_WORLD.z, CAMERA_POSITION_WORLD.y) * MAP_UNITS_PER_METRE;
		// The camera's +Z axis: from the scene towards the camera.
		vec3 back = normalize(INV_VIEW_MATRIX[2].xyz);
		vec3 to_camera = vec3(back.x, -back.z, back.y);
		bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
		// The surface's normal (map axes): towards the front sector for walls, up for floors.
		vec3 n = wall ? vec3(sin(direction), -cos(direction), 0.0) : vec3(0.0, 0.0, 1.0);
		if (dot(n, ortho ? to_camera : cam - p) < 0.0) {
			n = -n;
		}
		// A wall cap's wall (kind 3): its normal towards its sector; zero for a switch marker (T6.13p).
		vec2 wall_normal = (bits & FLAT_WALL_CAP) != 0 ? vec2(sin(direction), -cos(direction)) : vec2(0.0);
		if ((cut_hides(p, n, cut_player, ortho, to_camera, cam) && cap_wall_cut(p, cut_player, wall_normal, texcoord.y))
				|| (cut_hides(p, n, cut_cursor, ortho, to_camera, cam) && cap_wall_cut(p, cut_cursor, wall_normal, texcoord.y))) {
			if (cut_mode == 1 || bayer4(ivec2(FRAGCOORD.xy)) < 0.5) {
				discard;
			}
		}
		if (cap >= 0) {
			// A cap shows only inside its centre's disc, where the floor above it is cut.
			vec4 c = cap == 0 ? cut_player : cut_cursor;
			vec3 a = vec3(c.xy, c.z + cut_anchor);
			vec3 axis = ortho ? to_camera : normalize(cam - a);
			vec3 d = p - a;
			// A wall cap's cap only where its wall is cut (T6.13e).
			if (length(d - axis * dot(d, axis)) >= cut_radius || !cap_wall_cut(p, c, wall_normal, texcoord.y)) {
				discard;
			}
		}
	}
	int index;
	if (kind == 4) {
		// T6.13s: a ledge edge, bright with a dark rim inside.
		index = texcoord.y >= LEDGE_WIDTH - LEDGE_RIM ? 0 : LEDGE_INDEX;
	} else {
		// A flat's corner rides in v_flat (FLAT_RECT); a wall's rectangle is its slot's.
		ivec4 rect = (bits & FLAT_RECT) != 0 ? ivec4(slot & 0xffff, slot >> 16, 64, 64)
			: slot >= 0 ? ivec4(data_texel(texture_info, slot) + 0.5) : ivec4(0);
		ivec2 t = ivec2(floor(texcoord));
		int w = rect.z;
		int h = rect.w;
		int col;
		int row;
		if (kind == 0) {
			col = t.x & 63;
			row = t.y & 63;
		} else if (kind == 2) {
			// IsoDoom.Map.TextureWrap.MaskedTexel: columns as solid walls, rows not repeated.
			col = wall_tiling == 0 ? t.x & ((1 << findMSB(w)) - 1) : floor_mod(t.x, w);
			row = clamp(t.y, 0, h - 1);
		} else if (wall_tiling == 0) {
			// IsoDoom.Map.TextureWrap.WallTexel (Vanilla)
			col = t.x & ((1 << findMSB(w)) - 1);
			row = t.y & 127;
			if (row >= h) {
				int i = floor_mod(col * h + row, w * h);
				col = floor_div(i, h);
				row = i - col * h;
			}
		} else {
			col = floor_mod(t.x, w);
			row = floor_mod(t.y, h);
		}
		vec2 texel = texelFetch(atlas, rect.xy + ivec2(col, row), 0).rg;
#ifdef MASKED_PASS
		if (texel.g < 0.5) {
			discard;
		}
#endif
		index = int(texel.r * 255.0 + 0.5);
	}
	int mapped = fog
		? int(texelFetch(fog_dim, ivec2(index, 0), 0).r * 255.0 + 0.5)
		: int(texelFetch(colormap, ivec2(index, light_colormap(kind, lightnum, map, -VERTEX.z * MAP_UNITS_PER_METRE)), 0).r * 255.0 + 0.5);
	// ALBEDO is linear (Godot converts back on output).
	ALBEDO = texelFetch(playpal_linear, ivec2(mapped, palette_index), 0).rgb;
}
