// The palette flash around the player (T6.13v, IsoDoom.Render.FlashGlow): a
// disc on the floor at the player's feet in `glow_colour` (sRGB; its alpha
// the flash's level times the strength), fading out to the rim (the vertex
// alpha, 1 at the centre, 0 at the rim) and more softly than linearly.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled, blend_mix;

uniform vec4 glow_colour : source_color = vec4(1.0, 0.0, 0.0, 0.0);

void fragment() {
	ALBEDO = glow_colour.rgb;
	ALPHA = glow_colour.a * smoothstep(0.0, 1.0, COLOR.a);
}
