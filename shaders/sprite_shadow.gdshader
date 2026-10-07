// Blob shadows under actors (T3.6, SPEC §7.5; a presentation option,
// IsoDoom.Render.SpriteSettings.Shadow): a horizontal disc of the thing's
// radius on its floor, SHADOW_LIFT above it (under the sprite rows pulled onto
// the floor, IsoDoom.Render.ThingSprites.PullMargin), drawn by
// IsoDoom.Render.ThingSprites' shadow MultiMesh:
//   instance transform  the thing's position (Godot space; basis identity)
//   INSTANCE_CUSTOM     radius in map units (0: no shadow), unused ×3
//   VERTEX              x, z in −1…1 (the disc's square)
// `shadow_mode`: 0 off, 1 black blended at `shadow_opacity` and fading out
// over the outer 40 % of the radius, 2 black pixels in a 4×4 ordered dither
// (half of the disc's pixels: palette colours only). `shadow_colour` is the
// current palette's colour 0 (black, tinted by a palette flash; T6.8), linear.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

const float MAP_UNITS_PER_METRE = 32.0;
const float SHADOW_LIFT = 0.5;

uniform int shadow_mode = 0;
uniform float shadow_opacity = 0.5;
uniform vec3 shadow_colour = vec3(0.0);

varying vec2 v_disc;

void vertex() {
	float radius = INSTANCE_CUSTOM.x;
	v_disc = VERTEX.xz;
	if (shadow_mode == 0 || radius <= 0.0) {
		VERTEX = vec3(0.0);
	} else {
		VERTEX = vec3(VERTEX.x * radius, SHADOW_LIFT, VERTEX.z * radius) / MAP_UNITS_PER_METRE;
	}
}

void fragment() {
	float d = length(v_disc);
	if (d > 1.0) {
		discard;
	}
	ALBEDO = shadow_colour;
	if (shadow_mode == 2) {
		ivec2 p = ivec2(FRAGCOORD.xy) & ivec2(3);
		// 4×4 Bayer matrix: keep the pixels below the threshold (half).
		int bayer[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
		if (bayer[p.y * 4 + p.x] >= 8) {
			discard;
		}
		ALPHA = 1.0;
	} else {
		ALPHA = shadow_opacity * (1.0 - smoothstep(0.6, 1.0, d));
	}
}
