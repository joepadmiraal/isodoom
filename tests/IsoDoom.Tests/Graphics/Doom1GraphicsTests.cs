using System.IO;
using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.3 decoders against the shareware v1.9 DOOM1.WAD (skipped without it).</summary>
public class Doom1GraphicsTests
{
    private static WadArchive OpenDoom1() => WadArchive.Open(TestWads.RequireDoom1());

    [Fact]
    public void Playpal()
    {
        var pal = IsoDoom.Wad.Graphics.Playpal.Load(OpenDoom1());
        Assert.Equal(14, pal.Count);
        Assert.Equal(((byte)0, (byte)0, (byte)0), pal.GetColor(0, 0));
        Assert.Equal(((byte)255, (byte)255, (byte)255), pal.GetColor(0, 4));
        Assert.Equal(((byte)255, (byte)0, (byte)0), pal.GetColor(0, 176));
        Assert.Equal(((byte)167, (byte)107, (byte)107), pal.GetColor(0, 255));
        Assert.Equal(((byte)28, (byte)0, (byte)0), pal.GetColor(IsoDoom.Wad.Graphics.Playpal.STARTREDPALS, 0)); // red tint
        Assert.Equal(((byte)0, (byte)32, (byte)0), pal.GetColor(IsoDoom.Wad.Graphics.Playpal.RADIATIONPAL, 0)); // green tint
    }

    [Fact]
    public void Colormap()
    {
        var cm = IsoDoom.Wad.Graphics.Colormap.Load(OpenDoom1());
        Assert.Equal(34, cm.Count);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => (byte)i), cm.GetMap(0)[..8].ToArray());
        Assert.Equal(0, cm.GetMap(31)[176]);  // darkest light map: red goes black
        Assert.Equal(89, cm.GetMap(IsoDoom.Wad.Graphics.Colormap.INVERSECOLORMAP)[176]); // invulnerability grey
        Assert.All(cm.GetMap(33).ToArray(), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("TITLEPIC", 320, 200, 0, 0)]
    [InlineData("STBAR", 320, 32, 0, 0)]
    [InlineData("M_DOOM", 123, 60, 0, 0)]
    [InlineData("STCFN065", 8, 7, 0, 0)]
    [InlineData("WALL00_1", 64, 144, 31, 139)]
    [InlineData("TROOA1", 41, 57, 19, 52)]
    [InlineData("TROOA2A8", 40, 55, 17, 50)]
    [InlineData("PLAYA1", 41, 56, 18, 51)]
    public void PatchSizesAndOffsets(string name, int width, int height, int left, int top)
    {
        IndexedImage img = Patch.Load(OpenDoom1(), name);
        Assert.Equal((width, height, left, top), (img.Width, img.Height, img.LeftOffset, img.TopOffset));
    }

    [Fact]
    public void TitlepicIsFullyOpaqueAndSpriteHasTransparency()
    {
        WadArchive wad = OpenDoom1();
        Assert.All(Patch.Load(wad, "TITLEPIC").Opaque, b => Assert.Equal(1, b));
        IndexedImage imp = Patch.LoadSprite(wad, "TROOA1");
        int opaque = imp.Opaque.Count(b => b != 0);
        Assert.InRange(opaque, 1, imp.Opaque.Length - 1);
    }

    [Fact]
    public void EverySpritePatchAndFlatDecodes()
    {
        WadArchive wad = OpenDoom1();
        Assert.Equal(483, wad.GetNamespace(LumpNamespace.Sprites).Count(l => Patch.Decode(l.Data.Span, l.Name).Width > 0));
        Assert.Equal(164, wad.GetNamespace(LumpNamespace.Patches).Count(l => Patch.Decode(l.Data.Span, l.Name).Width > 0));
        Assert.Equal(54, wad.GetNamespace(LumpNamespace.Flats).Count(l => Flat.Decode(l.Data.Span, l.Name).Width == 64));
    }

    /// <summary>
    /// Debug export for the T1.3 visual check: writes TITLEPIC, TROOA1 (also 4×
    /// and at light map 16), the TROOA1 invulnerability version and the NUKAGE1
    /// flat as PNGs to <c>TestResults/graphics/</c> under the repo root
    /// (gitignored; WAD-derived images are never committed).
    /// </summary>
    [Fact]
    public void ExportDebugPngs()
    {
        WadArchive wad = OpenDoom1();
        Assert.SkipWhen(TestWads.RepoRoot is null, "Repo root not found; nowhere to write the debug PNGs.");
        string dir = Path.Combine(TestWads.RepoRoot!, "TestResults", "graphics");
        Directory.CreateDirectory(dir);

        var pal = IsoDoom.Wad.Graphics.Playpal.Load(wad);
        var cm = IsoDoom.Wad.Graphics.Colormap.Load(wad);

        void Write(string file, IndexedImage img, int scale = 1, int map = -1)
        {
            byte[] rgba = map < 0 ? img.ToRgba(pal.GetPalette(0)) : img.ToRgba(pal.GetPalette(0), cm.GetMap(map));
            if (scale > 1)
                rgba = PngWriter.Scale(img.Width, img.Height, rgba, scale);
            string path = Path.Combine(dir, file);
            PngWriter.WriteRgba(path, img.Width * scale, img.Height * scale, rgba);
            Assert.True(new FileInfo(path).Length > 0);
        }

        IndexedImage title = Patch.Load(wad, "TITLEPIC");
        IndexedImage imp = Patch.LoadSprite(wad, "TROOA1");
        Write("TITLEPIC.png", title);
        Write("TROOA1.png", imp);
        Write("TROOA1_x4.png", imp, 4);
        Write("TROOA1_x4_light16.png", imp, 4, 16);
        Write("TROOA1_x4_invuln.png", imp, 4, IsoDoom.Wad.Graphics.Colormap.INVERSECOLORMAP);
        Write("NUKAGE1_x4.png", Flat.Load(wad, "NUKAGE1"), 4);
    }
}
