using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T1.4 wall textures against the shareware v1.9 DOOM1.WAD (skipped without it).</summary>
public class Doom1TextureTests
{
    private static (WadArchive Wad, Textures Textures) OpenDoom1()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        return (wad, Textures.R_InitTextures(wad));
    }

    [Fact]
    public void TextureDirectory()
    {
        (WadArchive wad, Textures t) = OpenDoom1();
        Assert.Equal(-1, wad.W_CheckNumForName("TEXTURE2"));
        Assert.Equal(350, t.PatchNames.Count);
        Assert.Equal(163, t.PatchNames.Count(n => wad.W_CheckNumForName(n) >= 0)); // the rest are registered-only
        Assert.Equal(125, t.NumTextures);
        Assert.Equal("AASTINKY", t.TextureDefs[0].Name);
        Assert.All(t.TextureDefs, d => Assert.False(d.Masked)); // shareware sets no masked flags

        TextureDef startan3 = t.TextureDefs[t.R_TextureNumForName("STARTAN3")];
        Assert.Equal((128, 128), (startan3.Width, startan3.Height));
        Assert.Equal([("SW19_1", 64, 0), ("SW19_2", 0, 0)],
            startan3.Patches.Select(p => (p.PatchName, p.OriginX, p.OriginY)));

        TextureDef door3 = t.TextureDefs[t.R_TextureNumForName("DOOR3")];
        Assert.Equal((64, 72, 1), (door3.Width, door3.Height, door3.Patches.Count));

        TextureDef tekwall5 = t.TextureDefs[t.R_TextureNumForName("TEKWALL5")];
        Assert.Equal((-120, -8), (tekwall5.Patches[0].OriginX, tekwall5.Patches[0].OriginY)); // negative origins
    }

    [Fact]
    public void EveryTextureComposites()
    {
        (_, Textures t) = OpenDoom1();
        for (int i = 0; i < t.NumTextures; i++)
        {
            foreach (TextureCompositeMode mode in new[] { TextureCompositeMode.Vanilla, TextureCompositeMode.Corrected })
            {
                IndexedImage img = t.R_GenerateComposite(i, mode);
                Assert.Equal((t.TextureDefs[i].Width, t.TextureDefs[i].Height), (img.Width, img.Height));
            }
        }
    }

    [Theory]
    [InlineData("STARTAN3")]
    [InlineData("DOOR3")]
    [InlineData("TEKWALL1")]
    [InlineData("BROWN144")]
    public void SolidWallsAreFullyOpaque(string name)
    {
        (_, Textures t) = OpenDoom1();
        Assert.All(t.GetComposite(name).Opaque, b => Assert.Equal(1, b));
    }

    [Theory]
    [InlineData("BRNSMALC")]
    [InlineData("BRNBIGC")]
    public void GratesHaveHoles(string name)
    {
        (_, Textures t) = OpenDoom1();
        IndexedImage img = t.GetComposite(name);
        Assert.InRange(img.Opaque.Count(b => b == 0), img.Opaque.Length / 10, img.Opaque.Length * 9 / 10);
    }

    [Fact]
    public void VanillaAndCorrectedDifferOnlyWhereOriginYIsUsed()
    {
        (_, Textures t) = OpenDoom1();
        Assert.Equal(t.GetComposite("STARTAN3").Pixels, t.GetComposite("STARTAN3", TextureCompositeMode.Corrected).Pixels);
        Assert.NotEqual(t.GetComposite("TEKWALL1").Pixels, t.GetComposite("TEKWALL1", TextureCompositeMode.Corrected).Pixels);
    }

    /// <summary>
    /// Debug export for the T1.4 visual check: writes wall texture composites
    /// (4×) to <c>TestResults/graphics/</c> under the repo root (gitignored).
    /// MIDGRATE is not in the shareware WAD, so the masked grates BRNSMALC and
    /// BRNBIGC stand in; they are also written over a checkerboard so the
    /// holes show. TEKWALL1 and BROWN144 are written in both composite modes.
    /// </summary>
    [Fact]
    public void ExportDebugPngs()
    {
        (WadArchive wad, Textures t) = OpenDoom1();
        string dir = DebugPng.RequireOutputDir();
        byte[] palette = Playpal.Load(wad).GetPalette(0).ToArray();
        void Write(string file, IndexedImage img, bool checker = false) => DebugPng.Write(dir, file, img, palette, checker: checker);

        Write("TEX_STARTAN3_x4.png", t.GetComposite("STARTAN3"));
        Write("TEX_DOOR3_x4.png", t.GetComposite("DOOR3"));
        foreach (string grate in new[] { "BRNSMALC", "BRNBIGC" })
        {
            Write($"TEX_{grate}_x4.png", t.GetComposite(grate));
            Write($"TEX_{grate}_x4_checker.png", t.GetComposite(grate), checker: true);
        }
        foreach (string name in new[] { "TEKWALL1", "BROWN144" })
        {
            Write($"TEX_{name}_x4_vanilla.png", t.GetComposite(name));
            Write($"TEX_{name}_x4_corrected.png", t.GetComposite(name, TextureCompositeMode.Corrected));
        }
    }
}
