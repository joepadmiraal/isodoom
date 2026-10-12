// Item beams (T6.13r, IsoDoom.Render.ItemBeams, SPEC §12): a thin vertical
// ribbon facing the camera, rising from a pickup's foot, in its kind's colour,
// drawn only while the item's own billboard is hidden whole on screen.
//
// One MultiMesh instance per beamed item:
//   instance transform  the item's foot (Godot space; basis identity)
//   INSTANCE_CUSTOM     the item billboard's atlas slot (-1: no beam), flags
//                       (1 flip), radius (map units, the wall pull), and the
//                       beam's row of `thing_actors`
//   COLOR               the beam's colour (sRGB)
//   VERTEX              x -1 left / 1 right, y 0 foot / 1 top
//   `thing_actors`      the actors nearest the item (IsoDoom.Render.ItemBeams.Set)
//
// Hidden (the vertex shader, once per instance's vertices): billboard_hidden.gdshaderinc's
// thing_hidden, against the depth texture (what the opaque pass drew: the
// level's walls, floors, masked middles, lids, wall caps and switch markers,
// and the opaque thing sprites, the item's own among them); an item off screen
// gets no beam. An actor (a monster, a barrel, the player) does not hide an
// item: one standing on it is seen with it (so a monster walking over an item
// in the open does not beam it), and one in front leaves out the texels it
// covers (an item behind a wall keeps its beam while one walks past).
//
// The beam itself is depth-tested (not written): the wall hiding the item
// hides the beam's foot, so the beam rises from behind its top. Unshaded and
// full bright, opaque up to FADE_START of its height, fading out above.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

#include "res://shaders/billboard_hidden.gdshaderinc"

// The beam (map units).
uniform float beam_height = 256.0;
uniform float beam_half_width = 1.25;

const float FADE_START = 2.0 / 3.0;

varying float v_height;

void vertex() {
	int slot = int(round(INSTANCE_CUSTOM.x));
	bool flip = (int(round(INSTANCE_CUSTOM.y)) & 1) != 0;
	int row = int(round(INSTANCE_CUSTOM.w));
	vec3 foot = MODEL_MATRIX[3].xyz;
	bool ortho = PROJECTION_MATRIX[3][3] != 0.0;
	// As the sprite shader: the view plane's right axis, flattened, and the upright quad's normal.
	vec3 right = vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z);
	right = dot(right, right) > 1e-8 ? normalize(right) : vec3(1.0, 0.0, 0.0);
	vec3 facing = cross(right, vec3(0.0, 1.0, 0.0));
	v_height = VERTEX.y;
	bool hidden = thing_hidden(slot, flip, INSTANCE_CUSTOM.z, row, foot, right, facing, ortho,
		MODELVIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX, CAMERA_POSITION_WORLD);
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
