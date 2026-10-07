// Thing sprites with MF_SHADOW, fuzzed (SPEC §6.3 #6, §12 T6.9): the next pass
// of the sprite material, drawing only those things (flag 16), as r_draw.c
// R_DrawFuzzColumn does. It reads the screen texture, so it is drawn in the
// transparent pass; depth_draw_always keeps it sorting by depth.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always, shadows_disabled, fog_disabled;

#define FUZZ
#include "res://shaders/sprite.gdshaderinc"
