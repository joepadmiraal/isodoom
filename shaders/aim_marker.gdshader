// The aim marker (T6.13k, IsoDoom.Render.AimMarker): its ring and chevron
// on the floor around the player's feet, in the vertex colours. Every vertex
// moves along its view ray towards the camera, the same place on screen, until
// it is PULL nearer horizontally (as the sprites' wall pull, T3.5a), so the
// billboards of things lying on the floor around it (pools of blood, gibs,
// items), which the wall pull puts 16 units nearer than their spot, no longer
// cover it. Behind the player's centre (seen from the camera) the pull stops
// short of the player's own billboard (pulled by its radius, 16), so the
// player's sprite still covers the marker's far half.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

const float MAP_UNITS_PER_METRE = 32.0;
// The pull (map units, horizontal): the wall pull's 16 and the floor a 13
// units tall billboard covers behind its spot under the game camera.
const float PULL = 32.0;
// The player's own billboard's pull (its radius), less a unit.
const float PLAYER_PULL = 15.0;
// The longest pull along the view ray, in horizontal pulls (the sprites' MAX_WALL_PULL).
const float MAX_PULL = 4.0;

void vertex() {
	bool ortho = PROJECTION_MATRIX[3][3] == 1.0;
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 centre = MODEL_MATRIX[3].xyz;
	vec3 to_camera = ortho ? normalize(INV_VIEW_MATRIX[2].xyz) : normalize(CAMERA_POSITION_WORLD - world);
	vec3 centre_to_camera = ortho ? to_camera : CAMERA_POSITION_WORLD - centre;
	vec2 towards = normalize(centre_to_camera.xz + vec2(1e-6, 0.0));
	// How far the vertex is in front of the player's centre, horizontally (map units).
	float t = dot((world - centre).xz, towards) * MAP_UNITS_PER_METRE;
	float pull = t < 0.0 ? min(PULL, PLAYER_PULL - t) : PULL;
	float horizontal = sqrt(max(1.0 - to_camera.y * to_camera.y, 0.0));
	world += to_camera * (min(pull / max(horizontal, 1e-4), MAX_PULL * pull) / MAP_UNITS_PER_METRE);
	POSITION = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(world, 1.0));
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ALPHA = COLOR.a;
}
