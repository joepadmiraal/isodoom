// Whether a thing's billboard is hidden whole on screen (T6.13r's test, shared
// by the item beams, shaders/item_beam.gdshader, and the monster markers,
// shaders/monster_marker.gdshader, T6.13u): up to HIDDEN_GRID x HIDDEN_GRID of
// the patch's opaque texels are put where the sprite shader draws them
// (sprite.gdshaderinc: the tilt towards the camera, the move back onto the
// upright plane with upright tilt depth, the wall pull; rows below the origin
// at the foot's height) and tested against the depth texture (what the opaque
// pass drew). Hidden when something is in front of every one by more than
// HIDDEN_MARGIN; a texel off screen counts as seen. An actor near the thing
// (`thing_actors`, the owner's: IsoDoom.Render.ItemBeams.FillActors) does
// not hide it: a texel whose occluder lies on one standing on the thing counts
// as seen, one on an actor in front does not count, and with no texel counted
// the thing is not hidden. With the sprites' upright hiding (T3.6a) a thing
// whose upright billboard is hidden whole counts as hidden too.
//
//   `thing_actors`  texel (k, row): the k-th of the THING_ACTORS actors
//                   nearest the thing: its foot from the thing's (Godot
//                   space) and radius (map units; < 0: none)

const float MAP_UNITS_PER_METRE = 32.0;
const int SECTOR_DATA_WIDTH = 256;
// sprite.gdshaderinc's MAX_UPRIGHT and MAX_WALL_PULL.
const float MAX_UPRIGHT = 256.0;
const float MAX_WALL_PULL = 4.0;
const int HIDDEN_GRID = 8;
const float HIDDEN_MARGIN = 1.0;
// The actors near a thing (IsoDoom.Render.ItemBeams.ActorsPerBeam) and how far
// from an actor's billboard plane, and its extent, a surface counts as the
// actor's (map units).
const int THING_ACTORS = 4;
const float ACTOR_PLANE = 2.0;
const float ACTOR_HALF_WIDTH = 64.0;
const float ACTOR_HEIGHT = 160.0;
// How much more than their radii apart an actor and an item may stand for the
// actor to count as standing on it (map units).
const float ACTOR_ON = 8.0;
// sprite.gdshaderinc's PULL_MARGIN and MAX_PULL: the rows below the origin.
const float PULL_MARGIN = 1.0;
const float MAX_PULL = 64.0;

uniform sampler2D sprite_atlas : filter_nearest, repeat_disable;
uniform sampler2D sprite_info : filter_nearest, repeat_disable;
uniform sampler2D level_depth : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D thing_actors : filter_nearest, repeat_disable;
// The sprites' settings (IsoDoom.Render.SpriteSettings), as on the sprite material.
uniform float tilt = 1.0;
uniform int tilt_depth = 0;
uniform float wall_pull = 16.0;
uniform int upright_hiding = 0;

vec4 data_texel(sampler2D tex, int id) {
	return texelFetch(tex, ivec2(id % SECTOR_DATA_WIDTH, id / SECTOR_DATA_WIDTH), 0);
}

float wall_pull_length(float up, float pull) {
	float horizontal = sqrt(max(1.0 - up * up, 0.0));
	return min(pull / max(horizontal, 1e-4), MAX_WALL_PULL * pull);
}

// What the surface at `w` (Godot space) is to beam `beam`'s actors (the
// sprite shader's billboard: its plane, upright through the foot or tilted
// with tilted depth, `right` the view plane's right axis, flattened, `facing`
// the upright plane's normal; its rows below the origin on the floor plane,
// PULL_MARGIN above it; all moved towards the camera by the wall pull): 0 none
// of theirs, 1 a billboard of an actor in front of the item, 2 one of an actor
// standing on it (their radii overlapping, ACTOR_ON).
int actor_surface(vec3 w, int beam, vec3 item_foot, float item_radius, vec3 right, vec3 facing, bool ortho, mat4 inv_view, vec3 camera) {
	for (int k = 0; k < THING_ACTORS; k++) {
		vec4 a = texelFetch(thing_actors, ivec2(k, beam), 0);
		if (a.w < 0.0) {
			continue;
		}
		vec3 foot = item_foot + a.xyz;
		vec3 to_camera = ortho ? normalize(inv_view[2].xyz) : normalize(camera - foot);
		vec3 up = vec3(0.0, 1.0, 0.0);
		if (tilt > 0.0 && tilt_depth != 0) {
			float angle = clamp(tilt, 0.0, 1.0) * asin(clamp(to_camera.y, 0.0, 1.0));
			up = cos(angle) * up - sin(angle) * facing;
		}
		vec3 normal = normalize(cross(right, up));
		float pull = clamp(a.w, 0.0, max(wall_pull, 0.0));
		vec3 shift = pull > 0.0 ? to_camera * (wall_pull_length(to_camera.y, pull) / MAP_UNITS_PER_METRE) : vec3(0.0);
		vec3 origin = foot + shift;
		vec3 d = w - origin;
		bool on_actor = false;
		if (abs(dot(d, right)) <= ACTOR_HALF_WIDTH / MAP_UNITS_PER_METRE) {
			// Its plane, from below its floor (the rows below the origin) to its top.
			float h = dot(d, up);
			on_actor = abs(dot(d, normal)) <= ACTOR_PLANE / MAP_UNITS_PER_METRE
				&& h >= -MAX_PULL / MAP_UNITS_PER_METRE && h <= ACTOR_HEIGHT / MAP_UNITS_PER_METRE;
			// The rows below the origin, pulled onto the floor plane.
			on_actor = on_actor || (abs(d.y - PULL_MARGIN / MAP_UNITS_PER_METRE) <= ACTOR_PLANE / MAP_UNITS_PER_METRE
				&& abs(dot(d, facing)) <= MAX_PULL / MAP_UNITS_PER_METRE);
		}
		if (on_actor) {
			bool on_item = length(a.xz) <= (a.w + item_radius + ACTOR_ON) / MAP_UNITS_PER_METRE;
			return on_item ? 2 : 1;
		}
	}
	return 0;
}

// Whether something the opaque pass drew is in front of every tested opaque
// texel of the patch at `rect`, each where the sprite shader draws it: `up`
// the billboard's (tilted) up axis, `lean` its tilt (0: upright), `facing` the
// upright quad's normal. Actors do not hide the item (actor_surface): a texel
// one standing on the item covers counts as seen (whatever hides the item
// there hides the actor too), one an actor in front covers does not count,
// and with none counted the item is not hidden.
bool billboard_hidden(ivec4 rect, bool flip, float left, float top, vec3 right, vec3 up, float lean, vec3 facing,
		vec3 foot, float pull, bool ortho, mat4 modelview, mat4 projection, mat4 inv_projection, mat4 inv_view, vec3 camera,
		int beam, float radius) {
	int counted = 0;
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
			int actor = actor_surface((inv_view * vec4(v.xyz / v.w, 1.0)).xyz, beam, foot, radius, right, facing, ortho, inv_view, camera);
			if (actor == 2) {
				return false;
			}
			if (actor == 0) {
				counted++;
			}
		}
	}
	return counted > 0;
}

// Whether the billboard of atlas slot `slot` (mirrored with `flip`), standing
// at `foot` (Godot space) with radius `radius` (map units; the wall pull's
// too) and row `row` of `thing_actors`, is hidden whole (the vertex shader's
// built-ins passed in). `right` the view plane's right axis, flattened, and
// `facing` the upright quad's normal, as the sprite shader's.
bool thing_hidden(int slot, bool flip, float radius, int row, vec3 foot, vec3 right, vec3 facing, bool ortho,
		mat4 modelview, mat4 projection, mat4 inv_projection, mat4 inv_view, vec3 camera) {
	if (slot < 0) {
		return false;
	}
	float pull = clamp(radius, 0.0, max(wall_pull, 0.0));
	vec4 r = data_texel(sprite_info, 2 * slot);
	vec4 o = data_texel(sprite_info, 2 * slot + 1);
	ivec4 rect = ivec4(r + 0.5);
	if (rect.z <= 0 || rect.w <= 0) {
		return false;
	}
	vec3 up = vec3(0.0, 1.0, 0.0);
	float lean = 0.0;
	if (tilt > 0.0) {
		vec3 foot_to_camera = ortho ? normalize(inv_view[2].xyz) : normalize(camera - foot);
		float a = clamp(tilt, 0.0, 1.0) * asin(clamp(foot_to_camera.y, 0.0, 1.0));
		lean = sin(a);
		up = cos(a) * up - lean * facing;
	}
	bool hidden = billboard_hidden(rect, flip, o.x, o.y, right, up, lean, facing, foot, pull, ortho,
		modelview, projection, inv_projection, inv_view, camera, row, radius);
	if (!hidden && upright_hiding != 0 && lean > 0.0) {
		hidden = billboard_hidden(rect, flip, o.x, o.y, right, vec3(0.0, 1.0, 0.0), 0.0, facing, foot, pull, ortho,
			modelview, projection, inv_projection, inv_view, camera, row, radius);
	}
	return hidden;
}
