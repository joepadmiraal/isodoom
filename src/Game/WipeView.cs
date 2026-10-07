using System;
using Godot;

namespace IsoDoom.Game;

/// <summary>
/// T7.1a: draws f_wipe.c's melt (<see cref="FWipe"/>) over the whole
/// window: the frame shown before the game state changed (captured, d_main.c's
/// <c>wipe_StartScreen</c>) slides down in 160 columns over what the game
/// shows now (vanilla's end screen: the game holds still meanwhile, so the
/// live frame under it is the end screen). Vanilla's 320×200 screen is the
/// window: a column is 1/160 of its width, a row 1/200 of its height (at
/// 1280×800 eight by four pixels, vanilla's ×4 exactly; SPEC §12 T7.1a).
/// Over the HUD and the full screens, under the menus (vanilla draws
/// <c>M_Drawer</c> over the wipe).
/// </summary>
public partial class WipeView : CanvasLayer
{
    private const string ShaderCode = @"
shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform sampler2D start_screen : filter_nearest, repeat_disable;
// each column's offset in vanilla rows (0..200): the rows above it show the new frame
uniform float offsets[160];

void fragment() {
    int column = clamp(int(floor(UV.x * 160.0)), 0, 159);
    float shift = offsets[column] / 200.0;
    if (UV.y < shift) {
        discard;
    }
    COLOR = vec4(texture(start_screen, vec2(UV.x, UV.y - shift)).rgb, 1.0);
}
";

    private readonly ColorRect _rect = new() { Name = "Melt", MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly ShaderMaterial _material = new() { Shader = new Shader { Code = ShaderCode } };
    private readonly float[] _offsets = new float[FWipe.COLUMNS];
    private ImageTexture? _start;
    private int _shown = -1;

    public WipeView()
    {
        Visible = false;
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rect.Material = _material;
        AddChild(_rect);
    }

    /// <summary>The frame the melt started from (null without a renderer: nothing to draw).</summary>
    public Image? StartFrame { get; private set; }

    /// <summary>
    /// d_main.c <c>wipe_StartScreen</c>: <paramref name="frame"/>, the frame
    /// shown last (the viewport's), is the screen that melts away (null
    /// without a renderer: the melt runs, nothing is drawn).
    /// </summary>
    public void Begin(Image? frame)
    {
        StartFrame = frame is null || frame.IsEmpty() ? null : frame;
        if (StartFrame is null)
            return;
        if (_start is null || _start.GetSize() != StartFrame.GetSize() || _start.GetFormat() != StartFrame.GetFormat())
            _start = ImageTexture.CreateFromImage(StartFrame);
        else
            _start.Update(StartFrame);
        _material.SetShaderParameter("start_screen", _start);
    }

    /// <summary>
    /// Shows <paramref name="wipe"/>'s melt as it stands, or with
    /// <paramref name="pending"/> the start frame whole, still (the game state
    /// changed, the melt starts at the next tic); hidden otherwise or when
    /// nothing was captured.
    /// </summary>
    public void Show(FWipe wipe, bool pending)
    {
        Visible = (wipe.go || pending) && StartFrame is not null;
        if (!Visible)
        {
            _shown = -1;
            return;
        }
        int key = wipe.go ? wipe.count * 1000 + wipe.steps : -2;
        if (key == _shown)
            return;
        _shown = key;
        if (wipe.go)
            wipe.Offsets(_offsets);
        else
            Array.Clear(_offsets);
        _material.SetShaderParameter("offsets", _offsets);
    }
}
