using System;
using Godot;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// Reading back what a viewport drew, as sRGB (T8.4d). Under Godot's Mobile
/// renderer, which a Raspberry Pi's V3D cannot avoid (it has too few textures
/// per stage for Forward+), the 3D view renders into a 10-bit buffer of linear
/// colour halved, which moves dark palette colours by up to 5 levels (another
/// palette entry's) and halves what the fuzz reads from the screen texture.
/// <see cref="UseExactColours"/> gives the root viewport HDR 2D there: a half
/// float buffer, in which every palette colour comes out exact, as under
/// Forward+ (SPEC §12 T8.4d). Its texture then holds linear colour, so a read
/// back is turned into sRGB bytes here, as Godot turns it for the screen.
/// </summary>
public static class ScreenCapture
{
    /// <summary>T8.4d: HDR 2D on <paramref name="viewport"/> under the Mobile renderer, for exact palette colours.</summary>
    public static void UseExactColours(Viewport viewport)
    {
        if (!UsesMobileRenderer())
            return;
        viewport.UseHdr2D = true;
        Srgb.LinearCanvas = true;
        GD.Print("Mobile renderer: HDR 2D on, for exact palette colours (T8.4d)");
    }

    /// <summary>
    /// Whether Godot renders the scene with its Mobile renderer: asked for, or
    /// Forward+ on a device with under 48 textures per shader stage, which
    /// Godot runs as Mobile without changing the method it reports
    /// (renderer_compositor_rd.cpp; a Raspberry Pi's V3D has 16).
    /// </summary>
    public static bool UsesMobileRenderer()
    {
        string method = RenderingServer.GetCurrentRenderingMethod();
        if (method == "mobile")
            return true;
        RenderingDevice? device = RenderingServer.GetRenderingDevice();
        return method == "forward_plus" && device is not null && device.LimitGet(RenderingDevice.Limit.MaxTexturesPerShaderStage) < 48;
    }

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
