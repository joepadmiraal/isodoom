using System;
using System.Linq;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>
/// T1.6b: the synthetic IWAD that CI runs the viewer check against
/// (<c>tools/SyntheticIwad</c>) loads and has what the check should exercise.
/// </summary>
public class SyntheticIwadTests
{
    private static WadArchive Open() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    [Fact]
    public void LoadsAsSharewareWithEveryLumpKindAndCategory()
    {
        WadArchive wad = Open();
        Assert.Empty(SyntheticIwad.Check(wad));
        IwadInfo info = IwadIdentification.D_IdentifyVersion(wad);
        Assert.Equal(GameMode.shareware, info.GameMode);
        Assert.Equal(GameMission.doom, info.GameMission);
        Assert.Equal(GameVariant.vanilla, info.GameVariant);
        ModifiedGame.D_CheckModifiedGame(wad, info); // a lone IWAD: no PWAD checks to fail
    }

    [Fact]
    public void IsDeterministic() => Assert.Equal(SyntheticIwad.Build(), SyntheticIwad.Build());

    [Fact]
    public void TexturesCoverSingleMultiPatchMaskedAndShortTextures()
    {
        GraphicsCatalog catalog = GraphicsCatalog.Load(Open());
        Textures t = catalog.Textures;
        Assert.Equal(new[] { "BRICK1", "BRKPNL", "GRATE", "PANEL", "WINFRAME" }, t.TextureDefs.Select(d => d.Name));
        Assert.Equal(5, t.TextureDefs[t.R_TextureNumForName("BRKPNL")].Patches.Count);
        Assert.True(t.TextureDefs[t.R_TextureNumForName("GRATE")].Masked);
        Assert.Equal(72, t.TextureDefs[t.R_TextureNumForName("PANEL")].Height);
        foreach (TextureCompositeMode mode in Enum.GetValues<TextureCompositeMode>())
        {
            Assert.Contains((byte)0, t.GetComposite("GRATE", mode).Opaque);     // holes
            Assert.DoesNotContain((byte)0, t.GetComposite("BRKPNL", mode).Opaque);
        }
    }

    [Fact]
    public void SpritesHaveMirroredRotationsAndRotationZeroFrames()
    {
        WadArchive wad = Open();
        Sprites s = Sprites.R_InitSprites(wad);
        SpriteFrame a = s.Find("TROO")!.Frames.Single();
        Assert.True(a.Rotate);
        Assert.Equal(new[] { false, false, false, false, false, true, true, true }, a.Flip);
        Assert.Equal("TROOA2A8", wad.Lumps[a.Lump[7]].Name);
        SpriteDef bar1 = s.Find("BAR1")!;
        Assert.Equal(2, bar1.NumFrames);
        Assert.All(bar1.Frames, f => Assert.False(f.Rotate));
    }

    [Fact]
    public void LumpListHasAnOverriddenPatchAndAnUnusedSpriteLump()
    {
        WadArchive wad = Open();
        GraphicsCatalog catalog = GraphicsCatalog.Load(wad);
        var lumps = LumpDirectory.Build(wad);
        LumpEntry[] wallpnl = lumps.Where(e => e.Lump.Name == "WALLPNL").ToArray();
        Assert.Equal(2, wallpnl.Length);
        Assert.True(wallpnl[0].IsOverridden);
        Assert.False(wallpnl[1].IsOverridden);
        Assert.Null(catalog.Locate(wallpnl[0].Index)); // shown directly, not through the browser
        LumpEntry unused = lumps.Single(e => e.Lump.Name == "XXXXA0");
        Assert.Equal(LumpKind.Sprite, unused.Kind);
        Assert.Null(catalog.Locate(unused.Index));
        Assert.DoesNotContain(lumps, e => e.Kind == LumpKind.Other && e.Lump.Name != "README");
    }

    [Fact]
    public void PaletteLeavesTheViewerCheckBackgroundFree()
    {
        // WadViewerCheck draws on a background of the form (255, g, 254) that is in no palette.
        Playpal playpal = GraphicsCatalog.Load(Open()).Playpal;
        Assert.Equal(14, playpal.Count);
        for (int p = 0; p < playpal.Count; p++)
        {
            for (int i = 0; i < 256; i++)
                Assert.NotEqual(255, playpal.GetColor(p, i).Item1);
        }
    }
}
