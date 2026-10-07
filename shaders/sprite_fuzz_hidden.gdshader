// Fuzzed thing sprites (T6.9, sprite_fuzz.gdshader) with the upright hiding
// (T3.6a, sprite_hidden.gdshader).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always, shadows_disabled, fog_disabled;

#define FUZZ
#define UPRIGHT_HIDDEN
#include "res://shaders/sprite.gdshaderinc"
