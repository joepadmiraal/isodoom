// Thing sprites with upright hiding (SPEC §12 T3.6a,
// IsoDoom.Render.SpriteSettings.Hidden): a tilted billboard whose upright
// billboard the level would hide whole is not drawn. It reads the level's
// depth texture, so it is drawn in the transparent pass; depth_draw_always
// keeps sprites sorting against each other by depth as in the opaque pass.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always, shadows_disabled, fog_disabled;

#define UPRIGHT_HIDDEN
#include "res://shaders/sprite.gdshaderinc"
