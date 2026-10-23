// Palette shader for 8-bit indexed graphics (SPEC §5.2, §7.4).
//
// TEXTURE is an RG8 index texture (IsoDoom.Render.IndexedTextures):
//   R = palette index 0-255, G = 255 where the pixel is drawn, 0 where it is
//   transparent (index 0 is a real colour, black, so it can't mean "clear").
// The index goes through COLORMAP row `colormap_index` (0-31 light maps from
// bright to dark, 32 the invulnerability map), then PLAYPAL palette
// `palette_index` (0 normal, 1-8 red, 9-12 gold, 13 radiation suit).
// Draw with nearest filtering and no mipmaps, or indices get blended.
shader_type canvas_item;
render_mode unshaded;

// PLAYPAL: RGB8, 256 x palettes. COLORMAP: R8, 256 x maps.
uniform sampler2D playpal : filter_nearest, repeat_disable;
uniform sampler2D colormap : filter_nearest, repeat_disable;
uniform int palette_index = 0;
uniform int colormap_index = 0;
// T8.4d: the canvas is in linear colour (exact colours under the Mobile renderer, IsoDoom.Game.ScreenCapture); PLAYPAL is sRGB.
global uniform bool linear_canvas;

void fragment() {
	vec2 texel = texture(TEXTURE, UV).rg;
	if (texel.g < 0.5) {
		discard;
	}
	int index = int(texel.r * 255.0 + 0.5);
	int mapped = int(texelFetch(colormap, ivec2(index, colormap_index), 0).r * 255.0 + 0.5);
	vec3 c = texelFetch(playpal, ivec2(mapped, palette_index), 0).rgb;
	if (linear_canvas) {
		c = mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
	}
	COLOR = vec4(c, COLOR.a);
}
