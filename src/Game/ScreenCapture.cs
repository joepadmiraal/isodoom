using System;
using Godot;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// Exact colours under Godot's Mobile renderer, and reading back what a
/// viewport drew as sRGB (T8.4d). Under Mobile, which a Raspberry Pi's V3D
/// cannot avoid (it has too few textures per stage for Forward+), the 3D view
/// renders into a 10-bit buffer of linear colour halved, which moves dark
/// palette colours by up to 8 levels (another palette entry's, invisible in
/// play) and halves what the fuzz reads from the screen texture. Exact colours
/// (<see cref="SetExactColours"/>, the option <c>video/exact_colours</c>) give
/// the root viewport HDR 2D: a half-float buffer, in which every palette colour
/// comes out exact, as under Forward+, at a cost (about 4 ms a frame on a Pi 5,
/// so off there by default: SPEC §12 T8.4d). The viewport's texture then holds
/// linear colour, so a read back is turned into sRGB bytes here, as Godot turns
/// it for the screen.
/// </summary>
public static class ScreenCapture
{
    /// <summary>The global shader uniform our canvas shaders read (<c>project.godot</c>'s <c>[shader_globals]</c>): whether the canvas is in linear colour.</summary>
    private static readonly StringName _linearCanvas = new("linear_canvas");

    /// <summary>
    /// T8.4d: <c>video/exact_colours</c>'s values: <c>auto</c> (on under a
    /// Mobile renderer asked for, off where Godot chose it for the device, a
    /// Raspberry Pi), <c>on</c> or <c>off</c>.
    /// </summary>
    public static string ParseExactColours(string value, string what) => value switch
    {
        "auto" or "on" or "off" => value,
        _ => throw new ArgumentException($"{what}: \"{value}\" (auto, on or off)"),
    };

    /// <summary>
    /// T8.4d: HDR 2D on <paramref name="viewport"/> or not, as <paramref name="mode"/>
    /// (<see cref="ParseExactColours"/>) says; only under the Mobile renderer
    /// (Forward+ is exact as it is). Applied at once: our canvas shaders follow
    /// the global uniform, the captures the texture's format.
    /// </summary>
    public static void SetExactColours(Viewport viewport, string mode)
    {
        bool linear = UsesMobileRenderer() && (mode == "on" || (mode == "auto" && !MobileForced()));
        if (viewport.UseHdr2D == linear && Srgb.LinearCanvas == linear)
            return;
        viewport.UseHdr2D = linear;
        Srgb.LinearCanvas = linear;
        RenderingServer.GlobalShaderParameterSet(_linearCanvas, linear);
        if (UsesMobileRenderer())
            GD.Print($"Mobile renderer: exact colours {(linear ? "on (HDR 2D)" : "off")} (T8.4d)");
    }

    /// <summary>Whether Godot runs Mobile because the device can't run the Forward+ asked for (<see cref="UsesMobileRenderer"/>).</summary>
    public static bool MobileForced()
    {
        RenderingDevice? device = RenderingServer.GetRenderingDevice();
        return RenderingServer.GetCurrentRenderingMethod() == "forward_plus" && device is not null
            && device.LimitGet(RenderingDevice.Limit.MaxTexturesPerShaderStage) < 48;
    }

    /// <summary>
    /// Whether Godot renders the scene with its Mobile renderer: asked for, or
    /// Forward+ on a device with under 48 textures per shader stage, which
    /// Godot runs as Mobile without changing the method it reports
    /// (renderer_compositor_rd.cpp; a Raspberry Pi's V3D has 16).
    /// </summary>
    public static bool UsesMobileRenderer() => RenderingServer.GetCurrentRenderingMethod() == "mobile" || MobileForced();

    /// <summary>The viewport's last frame, RGBA8 sRGB as without HDR 2D.</summary>
    public static Image Capture(Viewport viewport)
    {
        Image frame = viewport.GetTexture().GetImage();
        Image.Format format = frame.GetFormat();
        if (format is not (Image.Format.Rgbah or Image.Format.Rgbh))
            return frame;
        using var keep = GodotRefs.Keep(frame); // T8.3d: alive to the scope's end, not disposed (GodotRefs.Keep)
        int channels = format == Image.Format.Rgbah ? 4 : 3;
        int width = frame.GetWidth(), height = frame.GetHeight();
        byte[] data = frame.GetData();
        byte[] rgba = new byte[width * height * 4];
        for (int p = 0, s = 0; p < rgba.Length; p += 4, s += channels * 2)
        {
            for (int c = 0; c < 3; c++)
                rgba[p + c] = Srgb.ToByte((double)BitConverter.ToHalf(data, s + c * 2));
            rgba[p + 3] = channels == 4 ? (byte)Math.Clamp(Math.Round((double)BitConverter.ToHalf(data, s + 6) * 255), 0, 255) : (byte)255;
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }
}
