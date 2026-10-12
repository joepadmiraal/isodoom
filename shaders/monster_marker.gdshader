// Monster markers (T6.13u, IsoDoom.Render.MonsterMarkers, SPEC §12): a small
// chevron pointing down at a monster that has a line of sight to the player
// (the owner's choice: P_CheckSight, T6.13t) while the level hides its
// billboard whole on screen, sitting on top of what hides it.
//
// One MultiMesh instance per marked monster:
//   instance transform  the monster's foot (Godot space; basis identity)
//   INSTANCE_CUSTOM     its billboard's atlas slot (-1: no marker), flags
//                       (1 flip), radius (map units, the wall pull), and its
//                       row of `thing_actors`
//   COLOR               the marker's colour (sRGB)
//   VERTEX              x -1 left / 1 right, y 0 tip / 1 top
//   `thing_actors`      the actors nearest the monster, itself left out
//                       (IsoDoom.Render.MonsterMarkers.Set)
//
// Hidden (the vertex shader, once per instance's vertices): billboard_hidden.gdshaderinc's
// thing_hidden, as the item beams'; a monster off screen gets no marker, and
// one that other actors in front of it cover is not hidden.
//
// Placed: from the monster's foot straight up in MARCH_STEP steps, the first
// point the depth texture shows (nothing in front of it by more than
// HIDDEN_MARGIN), at most MARCH_HEIGHT up: the top of the wall hiding the
// monster as the camera sees it. The chevron's tip is MARKER_GAP above that
// point, the chevron in the view plane, drawn over everything (no depth test)
// so the wall's top never clips it. Unshaded and full bright, a dark rim
// around the stroke.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, shadows_disabled, fog_disabled, blend_mix;

#include "res://shaders/billboard_hidden.gdshaderinc"

const float MARCH_STEP = 4.0;
const int MARCH_STEPS = 64;
// The marker (map units): its half width and height, the gap under its tip,
// the stroke's half thickness and its rim.
uniform float marker_half_width = 11.0;
uniform float marker_height = 12.0;
uniform float marker_gap = 4.0;
uniform float marker_stroke = 2.5;
uniform float marker_rim = 1.0;
uniform vec3 marker_rim_color : source_color = vec3(0.08, 0.0, 0.0);

varying vec2 v_local;

// How high above `foot` (map units) the first point the depth texture shows
// is, straight up (MARCH_STEPS * MARCH_STEP when none is).
float visible_height(vec3 foot, mat4 view, mat4 projection, mat4 inv_projection) {
	for (int s = 0; s <= MARCH_STEPS; s++) {
		float h = float(s) * MARCH_STEP;
		vec3 q = (view * vec4(foot + vec3(0.0, h / MAP_UNITS_PER_METRE, 0.0), 1.0)).xyz;
		vec4 clip = projection * vec4(q, 1.0);
		if (clip.w <= 0.0) {
			return h;
		}
		vec3 ndc = clip.xyz / clip.w;
		vec2 uv = ndc.xy * 0.5 + 0.5;
		if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
			return h;
		}
		vec4 v = inv_projection * vec4(ndc.xy, textureLod(level_depth, uv, 0.0).r, 1.0);
		if (v.z / v.w <= q.z + HIDDEN_MARGIN / MAP_UNITS_PER_METRE) {
			return h;
		}
	}
	return float(MARCH_STEPS) * MARCH_STEP;
}

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
	v_local = vec2(VERTEX.x * marker_half_width, VERTEX.y * marker_height);
	bool hidden = thing_hidden(slot, flip, INSTANCE_CUSTOM.z, row, foot, right, facing, ortho,
		MODELVIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX, CAMERA_POSITION_WORLD);
	if (hidden) {
		float tip = visible_height(foot, VIEW_MATRIX, PROJECTION_MATRIX, INV_PROJECTION_MATRIX) + marker_gap;
		vec3 screen_right = normalize(INV_VIEW_MATRIX[0].xyz);
		vec3 screen_up = normalize(INV_VIEW_MATRIX[1].xyz);
		VERTEX = (vec3(0.0, tip, 0.0) + screen_right * v_local.x + screen_up * v_local.y) / MAP_UNITS_PER_METRE;
	} else {
		VERTEX = vec3(0.0); // seen, or nothing to mark: a degenerate quad
	}
}

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float segment_distance(vec2 p, vec2 a, vec2 b) {
	vec2 ab = b - a;
	float t = clamp(dot(p - a, ab) / dot(ab, ab), 0.0, 1.0);
	return length(p - a - ab * t);
}

void fragment() {
	// A V: from the tip (inset by the stroke and rim) to both top corners.
	float inset = marker_stroke + marker_rim;
	vec2 tip = vec2(0.0, inset);
	vec2 arm = vec2(marker_half_width - inset, marker_height - inset);
	vec2 p = vec2(abs(v_local.x), v_local.y);
	float d = segment_distance(p, tip, arm);
	float aa = max(fwidth(d), 1e-4);
	float stroke = 1.0 - smoothstep(marker_stroke - aa, marker_stroke + aa, d);
	float shape = 1.0 - smoothstep(inset - aa, inset + aa, d);
	if (shape <= 0.0) {
		discard;
	}
	ALBEDO = srgb_to_linear(mix(marker_rim_color, COLOR.rgb, stroke));
	ALPHA = shape * COLOR.a;
}
