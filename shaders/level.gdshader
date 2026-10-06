// Level shader, solid pass: every floor and solid wall of a level, one
// material for all sector chunks (SPEC §7.2, §12 T2.5). The code is in
// level.gdshaderinc (shared with level_masked.gdshader).
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, shadows_disabled, fog_disabled;

#include "res://shaders/level.gdshaderinc"
