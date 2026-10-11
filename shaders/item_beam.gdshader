// Item beams (T6.13r, IsoDoom.Render.ItemBeams, SPEC §12): a thin vertical
// ribbon facing the camera, rising from a pickup's foot, in its kind's colour,
// drawn only while the item's own billboard is hidden whole on screen.
//
// One MultiMesh instance per beamed item:
//   instance transform  the item's foot (Godot space; basis identity)
//   INSTANCE_CUSTOM     the item billboard's atlas slot (-1: no beam), flags
//                       (1 flip), radius (map units, the wall pull)
//   COLOR               the beam's colour (sRGB)
//   VERTEX              x -1 left / 1 right, y 0 foot / 1 top
//
// Hidden (the vertex shader, once per instance's vertices): up to
// HIDDEN_GRID x HIDDEN_GRID of the item patch's opaque texels are put where
// the sprite shader draws them (sprite.gdshaderinc: the tilt towards the
// camera, the move back onto the upright plane with upright tilt depth, the
// wall pull; rows below the origin at the foot's height) and tested against
// the depth texture (what the opaque pass drew: the level's walls, floors,
// masked middles, lids, wall caps and switch markers, and the opaque thing
// sprites, the item's own among them). The beam shows only when something is
// in front of every one by more than HIDDEN_MARGIN; a texel off screen counts
// as seen, so an item off screen gets no beam. With the sprites' upright
// hiding (T3.6a: `upright_hiding` 1) an item whose upright billboard is hidden
// whole is dropped whole by the sprite shader, so it counts as hidden too.
//
// The beam itself is depth-tested (not written): the wall hiding the item
// hides the beam's foot, so the beam rises from behind its top. Unshaded and
// full bright, opaque up to FADE_START of its height, fading out above.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

const float MAP_UNITS_PER_METRE = 32.0;
const int SECTOR_DATA_WIDTH = 256;
// sprite.gdshaderinc's MAX_UPRIGHT and MAX_WALL_PULL.
const float MAX_UPRIGHT = 256.0;
const float MAX_WALL_PULL = 4.0;
const int HIDDEN_GRID = 8;
const float HIDDEN_MARGIN = 1.0;
const float FADE_START = 2.0 / 3.0;

uniform sampler2D sprite_atlas : filter_nearest, repeat_disable;
uniform sampler2D sprite_info : filter_nearest, repeat_disable;
uniform sampler2D level_depth : hint_depth_texture, filter_nearest, repeat_disable;
// The sprites' settings (IsoDoom.Render.SpriteSettings), as on the sprite material.
uniform float tilt = 1.0;
uniform int tilt_depth = 0;
uniform float wall_pull = 16.0;
uniform int upright_hiding = 0;
// The beam (map units).
uniform float beam_height = 256.0;
uniform float beam_half_width = 1.25;

varying float v_height;

vec4 data_texel(sampler2D tex, int id) {
	return texelFetch(tex, ivec2(id % SECTOR_DATA_WIDTH, id / SECTOR_DATA_WIDTH), 0);
}

float wall_pull_length(float up, float pull) {
	float horizontal = sqrt(max(1.0 - up * up, 0.0));
	return min(pull / max(horizontal, 1e-4), MAX_WALL_PULL * pull);
}

// Whether something the opaque pass drew is in front of every tested opaque
// texel of the patch at `rect`, each where the sprite shader draws it: `up`
// the billboard's (tilted) up axis, `lean` its tilt (0: upright), `facing` the
// upright quad's normal.
bool billboard_hidden(ivec4 rect, bool flip, float left, float top, vec3 right, vec3 up, float lean, vec3 facing,
		vec3 foot, float pull, bool ortho, mat4 modelview, mat4 projection, mat4 inv_projection, mat4 inv_view, vec3 camera) {
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
			vec3 p = (right * (float(c) + 0.5 - left) + up * max(top - float(row) - 0.5, 0.0)) / MAP_UNITS_PER_METRE;
			if (tilt_depth == 0 && lean > 0.0) {
				vec3 ray = ortho ? normalize(inv_view[2].xyz) : normalize(camera - (foot + p));
				float lambda = -dot(p, facing) / max(dot(ray, facing), 1e-3);
				p += ray * clamp(lambda, -MAX_UPRIGHT / MAP_UNITS_PER_METRE, MAX_UPRIGHT / MAP_UNITS_PER_METRE);
			}
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
				return false; // off screen: seen
			}
			vec4 v = inv_projection * vec4(ndc.xy, textureLod(level_depth, uv, 0.0).r, 1.0);
			if (v.z / v.w <= q.z + HIDDEN_MARGIN / MAP_UNITS_PER_METRE) {
				return false;
			}
		}
	}
	return true;
}

void vertex() {
	int slot = int(round(INSTANCE_CUSTOM.x));
	bool flip = (int(round(INSTANCE_CUSTOM.y)) & 1) != 0;
	float pull = clamp(INSTANCE_CUSTOM.z, 0.0, max(wall_pull, 0.0));
	vec3 foot = MODEL_MATRIX[3].xyz;
	bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
	// As the sprite shader: the view plane's right axis, flattened, and the upright quad's normal.
	vec3 right = vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z);
	right = dot(right, right) > 1e-8 ? normalize(right) : vec3(1.0, 0.0, 0.0);
	vec3 facing = cross(right, vec3(0.0, 1.0, 0.0));
	v_height = VERTEX.y;
	bool hidden = false;
	if (slot >= 0) {
		vec4 r = data_texel(sprite_info, 2 * slot);
		vec4 o = data_texel(sprite_info, 2 * slot + 1);
		ivec4 rect = ivec4(r + 0.5);
		vec3 up = vec3(0.0, 1.0, 0.0);
		float lean = 0.0;
		if (tilt > 0.0) {
			vec3 foot_to_camera = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - foot);
			float a = clamp(tilt, 0.0, 1.0) * asin(clamp(foot_to_camera.y, 0.0, 1.0));
			lean = sin(a);
			up = cos(a) * up - lean * facing;
		}
		hidden = rect.z > 0 && rect.w > 0 && billboard_hidden(rect, flip, o.x, o.y, right, up, lean, facing, foot, pull, ortho,
			MODELVIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX, CAMERA_POSITION_WORLD);
		if (!hidden && upright_hiding != 0 && lean > 0.0) {
			hidden = billboard_hidden(rect, flip, o.x, o.y, right, vec3(0.0, 1.0, 0.0), 0.0, facing, foot, pull, ortho,
				MODELVIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX, CAMERA_POSITION_WORLD);
		}
	}
	if (hidden) {
		VERTEX = (right * VERTEX.x * beam_half_width + vec3(0.0, VERTEX.y * beam_height, 0.0)) / MAP_UNITS_PER_METRE;
	} else {
		VERTEX = vec3(0.0); // seen, or nothing to beam: a degenerate quad
	}
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	ALBEDO = srgb_to_linear(COLOR.rgb);
	ALPHA = COLOR.a * (1.0 - smoothstep(FADE_START, 1.0, v_height));
}
