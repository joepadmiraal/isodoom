// Level shader, solid pass without discard (T8.4i, SPEC §12): the floors and
// solid walls of the sector chunks no cut centre can reach this frame
// (IsoDoom.Render.LevelMesh.ChooseChunkMaterials, a conservative CPU test), so
// they draw as level.gdshader would, without its cutaway and fog-dither
// discards: a shader that may discard costs V3D (the Raspberry Pi's GPU) its
// early depth test. The code is in level.gdshaderinc.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, shadows_disabled, fog_disabled;

#define NO_DISCARD
#include "res://shaders/level.gdshaderinc"
