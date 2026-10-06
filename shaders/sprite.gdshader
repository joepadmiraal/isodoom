// Thing sprites (SPEC §7.5, §12 T3.5): Y-axis billboards drawn from the
// WAD's sprite atlas (IsoDoom.Render.SpriteAtlas, RG8: R = palette index,
// G = opacity) through the palette, lit as r_things.c R_ProjectSprite does.
// One MultiMesh instance per thing (IsoDoom.Render.ThingSprites):
//   instance transform  the thing's position (Godot space; basis identity)
//   INSTANCE_CUSTOM     atlas slot (-1: draw nothing), flags (1 flip, 2 full
//                       bright), sector (light), unused
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
// Outline (T3.6, `outline` >= 0; default palette index 0): the quad grows by a
// texel on every side, and transparent texels next to an opaque one (4-way)
// draw that palette index through the sprite's colormap.
//
// Light: one colormap per instance, as vanilla's per vissprite: the
// `colormap_override` (vanilla fixedcolormap) if set, else colormap 0 for
// full-bright frames, else scalelight[(sector light >> 4) + extralight]
// [scale] with no fake contrast, the scale from the distance the walls use
// (`light_mode`: 0 the reference, 1 the horizontal distance from
// `light_origin` to the thing, 2 the thing's view depth). The CPU reference is
// IsoDoom.Map.LightTables.WallColormap with contrast 0. The level shader's
// shared uniforms (palette, tables, light settings, sector data) are set on
// this material by IsoDoom.Render.LevelMesh too.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled, fog_disabled;

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
uniform int light_centerx = 160;
uniform int extralight = 0;
// Readability options (T3.6, IsoDoom.Render.SpriteSettings).
uniform float tilt = 0.0;
uniform int tilt_depth = 0;
uniform int outline = -1;

varying flat ivec4 v_rect;
varying flat int v_flip;
varying flat int v_colormap;
varying vec2 v_texcoord;

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
		: light_mode == 1 ? length(map_xy - light_origin)
		: -(view * vec4(foot_world, 1.0)).z * MAP_UNITS_PER_METRE;
	int dist = int(clamp(d, 0.0, MAX_DISTANCE) * 65536.0);
	int scale = dist <= 0 ? MAXLIGHTSCALE - 1 : min((light_centerx << 20) / dist, MAXLIGHTSCALE - 1);
	return int(texelFetch(light_tables, ivec2(scale, LIGHTLEVELS + lightnum), 0).r * 255.0 + 0.5);
}

void vertex() {
	int slot = int(round(INSTANCE_CUSTOM.x));
	int flags = int(round(INSTANCE_CUSTOM.y));
	int sector = int(round(INSTANCE_CUSTOM.z));
	vec3 foot = MODEL_MATRIX[3].xyz;
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
		VERTEX = offset;
		v_colormap = sprite_colormap(flags, sector, foot, VIEW_MATRIX);
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

void fragment() {
	int w = v_rect.z;
	int h = v_rect.w;
	if (w <= 0 || h <= 0) {
		discard;
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
