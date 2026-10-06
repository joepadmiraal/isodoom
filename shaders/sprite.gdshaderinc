// Thing sprite shader body (SPEC §7.5, §12 T3.5), included by sprite.gdshader
// and sprite_hidden.gdshader (UPRIGHT_HIDDEN defined, T3.6a).
//
// Thing sprites: Y-axis billboards drawn from the
// WAD's sprite atlas (IsoDoom.Render.SpriteAtlas, RG8: R = palette index,
// G = opacity) through the palette, lit as r_things.c R_ProjectSprite does.
// One MultiMesh instance per thing (IsoDoom.Render.ThingSprites):
//   instance transform  the thing's position (Godot space; basis identity)
//   INSTANCE_CUSTOM     atlas slot (-1: draw nothing), flags (1 flip, 2 full
//                       bright, 4 actor), sector (light), radius (map units)
//   VERTEX              sprite space: x 0 left / 1 right; y 0 bottom row,
//                       1 the patch origin's height, 2 top row; z 1 for the
//                       lower quad (the rows below the origin)
// `sprite_info` (RGBA float, SECTOR_DATA_WIDTH texels per row): texel 2*slot =
// atlas x, y, width, height; texel 2*slot + 1 = leftoffset, topoffset.
//
// The quad turns about the vertical axis to the camera's view plane (its right
// axis, flattened), 1 map unit per patch pixel, with the patch origin on the
// thing's position. Alpha scissor: transparent texels are discarded, the rest
// write depth, so sprites sort against walls, masked middles and each other by
// depth, and show through walls the cutaway discards (T3.4).
//
// Tilt (T3.6, IsoDoom.Render.SpriteSettings; default full): the quad turns
// about its horizontal axis through the foot towards the camera by `tilt` times
// the camera's elevation seen from the foot (top leaning away), so at tilt 1 it
// faces the camera and shows the patch at its drawn proportions; a horizontal
// view is unaffected. With `tilt_depth` 0 (upright, the default) every vertex
// then moves along its view ray back onto the upright quad's plane (at most
// MAX_UPRIGHT): the same place on screen, an upright billboard's depth, so walls
// and raised floors behind the thing never cut into it.
//
// Rows below the origin (vanilla draws them over the floor): the lower quad's
// vertices move along their view ray towards the camera until they are
// PULL_MARGIN above the thing's floor plane (at most MAX_PULL), so they stay in
// place on screen and pass the depth test against the floor.
//
// Wall pull (T3.5a, IsoDoom.Render.SpriteSettings.WallPull; `wall_pull`
// default 16): last, every vertex moves along its view ray towards the camera
// until it is min(the thing's radius, `wall_pull`) nearer horizontally (at
// most MAX_WALL_PULL times that along the ray): the same place on screen, but
// a wall touching the thing behind it no longer cuts the billboard (vanilla
// draws such a sprite whole, clipping it only by walls in front), while a
// wall in front that the thing cannot pass (it keeps its radius away) still
// hides it. Every actor has a radius of at least 16, so they all move alike
// and keep their order by depth.
//
// Outline (T3.6, `outline` >= 0; default palette index 0): the quad grows by a
// texel on every side, and transparent texels next to an opaque one (4-way)
// draw that palette index through the sprite's colormap.
//
// Cutaway (T3.4b, IsoDoom.Render.CutawayThings; the CPU reference is
// IsoDoom.Render.Cutaway.HidesThing): with `cut_things` 1 (decorations: not
// actors, flag 4) or 2 (all), a fragment is cut as the level shader cuts a
// wall through the thing's foot facing the camera (the billboard's plane): it
// is more than `cut_height` above a centre's floor, the centre's anchor is
// behind that plane (the whole thing is in front of the centre), and it lies
// within `cut_radius` of the anchor's line towards the camera. The point is
// where the fragment writes its depth (on the upright quad with upright tilt
// depth) before the wall pull (T3.5a), which is undone along the view ray
// first. `cut_mode` 1 discards it, 2 dithers it as walls; the uniforms are
// the level shader's (set on every material by IsoDoom.Render.LevelMesh).
//
// Upright hiding (T3.6a, UPRIGHT_HIDDEN, IsoDoom.Render.SpriteSettings.Hidden;
// default off): a tilted billboard is also dropped whole when the upright
// billboard (tilt off) would be hidden whole by the level: up to
// HIDDEN_GRID x HIDDEN_GRID of its opaque texels, at their upright places
// (with the wall pull), are tested against the level's depth (the depth
// texture: walls, floors and masked middles, not other things) in the vertex
// shader, and the quad collapses when none is in front. So a monster the
// upright billboard (and its real height) keeps behind a wall no longer shows
// its head above it, while one partly hidden keeps its whole tilted image,
// hidden pixel by pixel by depth as with this off (hiding single rows or
// columns instead leaves gaps above walls and holes). Reading the depth
// texture puts the material in the transparent pass, with depth writes
// (depth_draw_always), after a copy of the opaque depth.
//
// Light: one colormap per instance, as vanilla's per vissprite: the
// `colormap_override` (vanilla fixedcolormap) if set, else colormap 0 for
// full-bright frames, else scalelight[(sector light >> 4) + extralight]
// [scale] with no fake contrast, the scale from the distance the walls use
// (`light_mode`: 0 the reference, 1 the horizontal distance from
// `light_origin` to the thing, at least `light_near`, 2 the thing's view depth). The CPU reference is
// IsoDoom.Map.LightTables.WallColormap with contrast 0. The level shader's
// shared uniforms (palette, tables, light settings, sector data) are set on
// this material by IsoDoom.Render.LevelMesh too.
const float MAP_UNITS_PER_METRE = 32.0;
const int SECTOR_DATA_WIDTH = 256;
// r_main.c
const int LIGHTLEVELS = 16;
const int LIGHTSEGSHIFT = 4;
const int MAXLIGHTSCALE = 48;
// IsoDoom.Map.LightTables.MaxDistanceUnits
const float MAX_DISTANCE = 4096.0;
// IsoDoom.Render.ThingSprites.PullMargin (map units), and the longest pull.
const float PULL_MARGIN = 1.0;
const float MAX_PULL = 64.0;
// The longest move back onto the upright plane (T3.6), map units.
const float MAX_UPRIGHT = 256.0;
// The longest wall pull along the view ray, in horizontal pulls (T3.5a,
// IsoDoom.Render.SpriteSettings.MaxWallPullFactor).
const float MAX_WALL_PULL = 4.0;

uniform sampler2D sprite_atlas : filter_nearest, repeat_disable;
uniform sampler2D sprite_info : filter_nearest, repeat_disable;
uniform sampler2D sector_data : filter_nearest, repeat_disable;
uniform sampler2D playpal : filter_nearest, repeat_disable;
uniform sampler2D colormap : filter_nearest, repeat_disable;
uniform int palette_index = 0;
uniform int colormap_override = -1;
uniform sampler2D light_tables : filter_nearest, repeat_disable;
uniform int light_mode = 1;
uniform vec2 light_origin = vec2(0.0);
uniform float light_reference = 256.0;
uniform float light_near = 80.0;
uniform int light_centerx = 160;
uniform int extralight = 0;
// Readability options (T3.6, IsoDoom.Render.SpriteSettings).
uniform float tilt = 0.0;
uniform int tilt_depth = 0;
uniform int outline = -1;
uniform float wall_pull = 0.0;
// The cutaway (T3.4, T3.4b): the level shader's uniforms, and which things it
// cuts (0 none, 1 all but actors, 2 all).
uniform int cut_mode = 0;
uniform vec4 cut_player = vec4(0.0);
uniform vec4 cut_cursor = vec4(0.0);
uniform float cut_radius = 96.0;
uniform float cut_height = 32.0;
uniform float cut_anchor = 28.0;
uniform int cut_things = 0;
// IsoDoom.Render.Cutaway.PlaneMargin (map units).
const float CUT_PLANE_MARGIN = 1.0;

#ifdef UPRIGHT_HIDDEN
// Upright hiding (T3.6a): the level's depth (opaque pass), the most texels
// tested per axis, and how far (map units) the level must be in front.
uniform sampler2D level_depth : hint_depth_texture, filter_nearest, repeat_disable;
const int HIDDEN_GRID = 12;
const float HIDDEN_MARGIN = 1.0;
#endif

varying flat ivec4 v_rect;
varying flat int v_flip;
varying flat int v_colormap;
varying vec2 v_texcoord;
// Whether the cutaway may cut this thing (T3.4b), and its foot (map units).
varying flat int v_cuttable;
varying flat vec3 v_foot;
// The wall pull's horizontal distance (T3.5a; 0: none), map units.
varying flat float v_pull;

vec4 data_texel(sampler2D tex, int id) {
	return texelFetch(tex, ivec2(id % SECTOR_DATA_WIDTH, id / SECTOR_DATA_WIDTH), 0);
}

// r_things.c R_ProjectSprite: the vissprite's colormap.
int sprite_colormap(int flags, int sector, vec3 foot_world, mat4 view) {
	if (colormap_override >= 0) {
		return colormap_override;
	}
	if ((flags & 2) != 0) {
		return 0;
	}
	int light = clamp(int(data_texel(sector_data, sector).b + 0.5), 0, 255);
	int lightnum = clamp((light >> LIGHTSEGSHIFT) + extralight, 0, LIGHTLEVELS - 1);
	vec2 map_xy = vec2(foot_world.x, -foot_world.z) * MAP_UNITS_PER_METRE;
	float d = light_mode == 0 ? light_reference
		: light_mode == 1 ? max(length(map_xy - light_origin), light_near)
		: -(view * vec4(foot_world, 1.0)).z * MAP_UNITS_PER_METRE;
	int dist = int(clamp(d, 0.0, MAX_DISTANCE) * 65536.0);
	int scale = dist <= 0 ? MAXLIGHTSCALE - 1 : min((light_centerx << 20) / dist, MAXLIGHTSCALE - 1);
	return int(texelFetch(light_tables, ivec2(scale, LIGHTLEVELS + lightnum), 0).r * 255.0 + 0.5);
}

// How far along a view ray (map units) the wall pull moves a point (T3.5a):
// the ray's horizontal part is `pull`, at most MAX_WALL_PULL times that.
// `up` is the ray's vertical component (unit ray towards the camera).
float wall_pull_length(float up, float pull) {
	float horizontal = sqrt(max(1.0 - up * up, 0.0));
	return min(pull / max(horizontal, 1e-4), MAX_WALL_PULL * pull);
}

#ifdef UPRIGHT_HIDDEN
// Whether the level hides every tested opaque texel of the upright billboard
// (T3.6a): texel (c, row) of the patch at slot `rect` stands at `right` *
// (c + 0.5 - left) + up * (top - row - 0.5) from the foot (rows below the
// origin at the foot's height), then the wall pull; `modelview` maps that
// offset (metres) to view space.
bool upright_hidden(ivec4 rect, bool flip, float left, float top, vec3 right, vec3 foot, float pull,
		bool ortho, mat4 modelview, mat4 projection, mat4 inv_projection, mat4 inv_view, vec3 camera) {
	int nc = min(rect.z, HIDDEN_GRID);
	int nr = min(rect.w, HIDDEN_GRID);
	for (int j = 0; j < nr; j++) {
		int row = int((float(j) + 0.5) * float(rect.w) / float(nr));
		for (int i = 0; i < nc; i++) {
			int c = int((float(i) + 0.5) * float(rect.z) / float(nc));
			int ac = flip ? rect.z - 1 - c : c;
			if (texelFetch(sprite_atlas, rect.xy + ivec2(ac, row), 0).g < 0.5) {
				continue;
			}
			vec3 p = (right * (float(c) + 0.5 - left) + vec3(0.0, max(top - float(row) - 0.5, 0.0), 0.0)) / MAP_UNITS_PER_METRE;
			if (pull > 0.0) {
				vec3 to_camera = ortho ? normalize(inv_view[2].xyz) : normalize(camera - (foot + p));
				p += to_camera * (wall_pull_length(to_camera.y, pull) / MAP_UNITS_PER_METRE);
			}
			vec3 q = (modelview * vec4(p, 1.0)).xyz;
			vec4 clip = projection * vec4(q, 1.0);
			if (clip.w <= 0.0) {
				return false;
			}
			vec3 ndc = clip.xyz / clip.w;
			vec2 uv = ndc.xy * 0.5 + 0.5;
			if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
				// Off screen: nothing to hide it.
				return false;
			}
			vec4 v = inv_projection * vec4(ndc.xy, textureLod(level_depth, uv, 0.0).r, 1.0);
			if (v.z / v.w <= q.z + HIDDEN_MARGIN / MAP_UNITS_PER_METRE) {
				return false;
			}
		}
	}
	return true;
}
#endif

void vertex() {
	int slot = int(round(INSTANCE_CUSTOM.x));
	int flags = int(round(INSTANCE_CUSTOM.y));
	int sector = int(round(INSTANCE_CUSTOM.z));
	vec3 foot = MODEL_MATRIX[3].xyz;
	v_foot = vec3(foot.x, -foot.z, foot.y) * MAP_UNITS_PER_METRE;
	v_pull = clamp(INSTANCE_CUSTOM.w, 0.0, max(wall_pull, 0.0));
	v_cuttable = cut_mode != 0 && (cut_things == 2 || (cut_things == 1 && (flags & 4) == 0)) ? 1 : 0;
	if (slot < 0) {
		// Nothing to draw: a degenerate quad.
		v_rect = ivec4(0);
		v_flip = 0;
		v_colormap = 0;
		v_texcoord = vec2(0.0);
		VERTEX = vec3(0.0);
	} else {
		vec4 r = data_texel(sprite_info, 2 * slot);
		vec4 o = data_texel(sprite_info, 2 * slot + 1);
		v_rect = ivec4(r + 0.5);
		v_flip = flags & 1;
		float w = r.z, h = r.w, left = o.x, top = o.y;
		float bottom = top - h;
		float origin = clamp(0.0, bottom, top);
		// The outline (T3.6) needs one more texel on every side.
		float grow = outline >= 0 ? 1.0 : 0.0;
		float y = VERTEX.y < 0.5 ? bottom - grow : VERTEX.y < 1.5 ? origin : top + grow;
		float sx = VERTEX.x * (w + 2.0 * grow) - grow;
		float x = sx - left;
		v_texcoord = vec2(sx, top - y);

		bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
		// The view plane's right axis, flattened (a Y-axis billboard), and the
		// horizontal direction towards the camera (the upright quad's normal).
		vec3 right = vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z);
		right = dot(right, right) > 1e-8 ? normalize(right) : vec3(1.0, 0.0, 0.0);
		vec3 facing = cross(right, vec3(0.0, 1.0, 0.0));
		// Tilt (T3.6): the quad turns about its horizontal axis through the
		// foot towards the camera by `tilt` times the camera's elevation seen
		// from the foot; its top leans away from the camera.
		vec3 up = vec3(0.0, 1.0, 0.0);
		float lean = 0.0;
		if (tilt > 0.0) {
			vec3 foot_to_camera = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - foot);
			float a = tilt * asin(clamp(foot_to_camera.y, 0.0, 1.0));
			lean = sin(a);
			up = cos(a) * up - lean * facing;
		}
		vec3 offset = (right * x + up * y) / MAP_UNITS_PER_METRE;
		if (tilt_depth == 0 && lean > 0.0) {
			// Upright depth: back along the view ray onto the upright quad's
			// plane (same place on screen, an upright billboard's depth).
			vec3 ray = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - (foot + offset));
			float lambda = -dot(offset, facing) / max(dot(ray, facing), 1e-3);
			offset += ray * clamp(lambda, -MAX_UPRIGHT / MAP_UNITS_PER_METRE, MAX_UPRIGHT / MAP_UNITS_PER_METRE);
		}
		if (VERTEX.z > 0.5) {
			// Rows below the origin: along the view ray, up onto the floor plane.
			vec3 to_camera = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - (foot + offset));
			if (to_camera.y > 0.05) {
				float height = offset.y * MAP_UNITS_PER_METRE;
				float pull = min((max(-height, 0.0) + PULL_MARGIN) / to_camera.y, MAX_PULL);
				offset += to_camera * (pull / MAP_UNITS_PER_METRE);
			}
		}
		if (v_pull > 0.0) {
			// The wall pull (T3.5a): along the view ray, the radius nearer horizontally.
			vec3 to_camera = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - (foot + offset));
			offset += to_camera * (wall_pull_length(to_camera.y, v_pull) / MAP_UNITS_PER_METRE);
		}
		VERTEX = offset;
		v_colormap = sprite_colormap(flags, sector, foot, VIEW_MATRIX);
#ifdef UPRIGHT_HIDDEN
		if (lean > 0.0 && upright_hidden(v_rect, v_flip != 0, left, top, right, foot, v_pull, ortho,
				MODELVIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX, CAMERA_POSITION_WORLD)) {
			// Hidden whole (T3.6a): a degenerate quad.
			v_rect = ivec4(0);
			VERTEX = vec3(0.0);
		}
#endif
	}
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

bool opaque_at(int col, int row) {
	if (col < 0 || row < 0 || col >= v_rect.z || row >= v_rect.w) {
		return false;
	}
	if (v_flip != 0) {
		// r_things.c: a flipped patch keeps its rectangle and mirrors its columns.
		col = v_rect.z - 1 - col;
	}
	return texelFetch(sprite_atlas, v_rect.xy + ivec2(col, row), 0).g >= 0.5;
}

// IsoDoom.Render.Cutaway.HidesThing: whether centre c cuts billboard point p
// (map units); n the billboard's horizontal normal towards the camera.
bool cut_thing(vec3 p, vec3 n, vec4 c, bool ortho, vec3 to_camera, vec3 cam) {
	if (c.w < 0.5 || p.z <= c.z + cut_height) {
		return false;
	}
	vec3 a = vec3(c.xy, c.z + cut_anchor);
	if (dot(a - v_foot, n) > -CUT_PLANE_MARGIN) {
		return false;
	}
	vec3 axis = ortho ? to_camera : normalize(cam - a);
	vec3 d = p - a;
	return length(d - axis * dot(d, axis)) < cut_radius;
}

// 4x4 ordered dither threshold (0-1) of a pixel (as the level shader's).
float bayer4(ivec2 q) {
	const int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[(q.y & 3) * 4 + (q.x & 3)]) + 0.5) / 16.0;
}

void fragment() {
	int w = v_rect.z;
	int h = v_rect.w;
	if (w <= 0 || h <= 0) {
		discard;
	}
	if (v_cuttable != 0) {
		// Godot space (x, height, -y, metres) to map units (x, y, height).
		vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		vec3 p = vec3(world.x, -world.z, world.y) * MAP_UNITS_PER_METRE;
		vec3 cam = vec3(CAMERA_POSITION_WORLD.x, -CAMERA_POSITION_WORLD.z, CAMERA_POSITION_WORLD.y) * MAP_UNITS_PER_METRE;
		vec3 back = normalize(INV_VIEW_MATRIX[2].xyz);
		vec3 to_camera = vec3(back.x, -back.z, back.y);
		bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
		if (v_pull > 0.0) {
			// Undo the wall pull (T3.5a): the cut is tested where the pixel's depth was before it.
			vec3 ray = ortho ? to_camera : normalize(cam - p);
			p -= ray * wall_pull_length(ray.z, v_pull);
		}
		// The billboard's normal: the camera's right axis, flattened, turned up (as the vertex shader).
		vec3 right = vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z);
		right = dot(right, right) > 1e-8 ? normalize(right) : vec3(1.0, 0.0, 0.0);
		vec3 facing = cross(right, vec3(0.0, 1.0, 0.0));
		vec3 n = vec3(facing.x, -facing.z, facing.y);
		if (cut_thing(p, n, cut_player, ortho, to_camera, cam) || cut_thing(p, n, cut_cursor, ortho, to_camera, cam)) {
			if (cut_mode == 1 || bayer4(ivec2(FRAGCOORD.xy)) < 0.5) {
				discard;
			}
		}
	}
	int col = int(floor(v_texcoord.x));
	int row = int(floor(v_texcoord.y));
	int index = 0;
	if (outline < 0) {
		col = clamp(col, 0, w - 1);
		row = clamp(row, 0, h - 1);
	}
	if (opaque_at(col, row)) {
		int c = v_flip != 0 ? w - 1 - col : col;
		index = int(texelFetch(sprite_atlas, v_rect.xy + ivec2(c, row), 0).r * 255.0 + 0.5);
	} else if (outline >= 0 && (opaque_at(col - 1, row) || opaque_at(col + 1, row) || opaque_at(col, row - 1) || opaque_at(col, row + 1))) {
		index = outline;
	} else {
		discard;
	}
	int mapped = int(texelFetch(colormap, ivec2(index, v_colormap), 0).r * 255.0 + 0.5);
	ALBEDO = srgb_to_linear(texelFetch(playpal, ivec2(mapped, palette_index), 0).rgb);
}
