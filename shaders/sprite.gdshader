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
// Rows below the origin (vanilla draws them over the floor): the lower quad's
// vertices move along their view ray towards the camera until they are
// PULL_MARGIN above the thing's floor plane (at most MAX_PULL), so they stay in
// place on screen and pass the depth test against the floor.
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
		float y = VERTEX.y < 0.5 ? bottom : VERTEX.y < 1.5 ? origin : top;
		float x = VERTEX.x * w - left;
		v_texcoord = vec2(VERTEX.x * w, top - y);

		// The view plane's right axis, flattened (a Y-axis billboard).
		vec3 right = vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z);
		right = dot(right, right) > 1e-8 ? normalize(right) : vec3(1.0, 0.0, 0.0);
		vec3 offset = (right * x + vec3(0.0, y, 0.0)) / MAP_UNITS_PER_METRE;
		if (VERTEX.z > 0.5) {
			// Rows below the origin: along the view ray, up onto the floor plane.
			bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
			vec3 to_camera = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - (foot + offset));
			if (to_camera.y > 0.05) {
				float pull = min((max(-y, 0.0) + PULL_MARGIN) / to_camera.y, MAX_PULL);
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

void fragment() {
	int w = v_rect.z;
	int h = v_rect.w;
	if (w <= 0 || h <= 0) {
		discard;
	}
	int col = clamp(int(floor(v_texcoord.x)), 0, w - 1);
	int row = clamp(int(floor(v_texcoord.y)), 0, h - 1);
	if (v_flip != 0) {
		// r_things.c: a flipped patch keeps its rectangle and mirrors its columns.
		col = w - 1 - col;
	}
	vec2 texel = texelFetch(sprite_atlas, v_rect.xy + ivec2(col, row), 0).rg;
	if (texel.g < 0.5) {
		discard;
	}
	int index = int(texel.r * 255.0 + 0.5);
	int mapped = int(texelFetch(colormap, ivec2(index, v_colormap), 0).r * 255.0 + 0.5);
	ALBEDO = srgb_to_linear(texelFetch(playpal, ivec2(mapped, palette_index), 0).rgb);
}
