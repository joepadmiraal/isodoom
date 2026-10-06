// Thing sprites (SPEC §7.5, §12 T3.5, T3.6): the default variant, drawn in
// the opaque pass. The body, and what it does, is in sprite.gdshaderinc;
// sprite_hidden.gdshader adds the upright hiding (T3.6a).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled, fog_disabled;

#include "res://shaders/sprite.gdshaderinc"
