// The palette flash around the player (T6.13v, IsoDoom.Render.FlashGlow): a
// disc on the floor at the player's feet in `glow_colour` (sRGB; its alpha
// the flash's level times the strength), fading out to the rim (the vertex
// alpha, 1 at the centre, 0 at the rim) and more softly than linearly.
// T6.13w: `crisp` (the pickup ripple) keeps the alpha over a band at the rim
// only, `ring_width` of the radius wide (in vertex alpha: 1 fills the disc),
// both its edges antialiased: a line circle.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

uniform vec4 glow_colour : source_color = vec4(1.0, 0.0, 0.0, 0.0);
uniform bool crisp = false;
uniform float ring_width = 1.0;

void fragment() {
	ALBEDO = glow_colour.rgb;
	float aa = 1.5 * fwidth(COLOR.a);
	float falloff = crisp
		? smoothstep(0.0, aa, COLOR.a) * (1.0 - smoothstep(ring_width, ring_width + aa, COLOR.a))
		: smoothstep(0.0, 1.0, COLOR.a);
	ALPHA = glow_colour.a * falloff;
}
