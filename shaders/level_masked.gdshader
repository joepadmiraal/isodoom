// Level shader, masked pass: the masked middle textures (grates, fences) of
// a level, a second material on each sector chunk (SPEC §7.2, §12 T3.1).
// Alpha scissor: texels whose opacity (atlas G) is below 0.5 are discarded,
// and the rest are written to the depth buffer like solid walls, so sprites
// and anything else sort against them by depth. Only this variant discards
// (MASKED_PASS), so the solid pass keeps early depth testing.
// One face per sidedef, as vanilla (cull_back; see SPEC §12 T3.1).
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, shadows_disabled, fog_disabled;

#define MASKED_PASS
#include "res://shaders/level.gdshaderinc"
