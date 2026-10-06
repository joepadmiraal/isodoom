using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Graphics;

/// <summary>T3.5: r_things.c <c>R_ProjectSprite</c>'s rotation choice (<see cref="Sprites.R_ProjectSpriteRotation"/>, <see cref="SpriteFrame.Select"/>).</summary>
public class SpriteRotationTests
{
    private const uint ANG45 = 0x20000000, ANG90 = 0x40000000, ANG180 = 0x80000000;

    [Theory]
    // ang is the direction from the viewer to the thing: a thing facing the viewer
    // is seen at ang = facing + 180° (its front, slot 0); from behind at ang = facing (slot 4).
    [InlineData(ANG180, 0u, 0)]
    [InlineData(0u, 0u, 4)]
    [InlineData(ANG90, 0u, 6)]
    [InlineData(ANG90 * 3, 0u, 2)]
    [InlineData(ANG45 * 5, ANG45, 0)]
    [InlineData(ANG45 * 7, ANG45 * 2, 1)]
    public void RotationOfVanillaFormula(uint ang, uint thingAngle, int rot) =>
        Assert.Equal(rot, Sprites.R_ProjectSpriteRotation(ang, thingAngle));

    [Fact]
    public void EachRotationCoversFortyFiveDegreesCentredOnItsDirection()
    {
        // Slot r is centred on ang - angle = 180° + r * 45°: its bucket spans ±22.5°, the upper edge belonging to the next slot.
        for (int r = 0; r < 8; r++)
        {
            uint centre = unchecked(ANG180 + (uint)r * ANG45);
            Assert.Equal(r, Sprites.R_ProjectSpriteRotation(centre, 0));
            Assert.Equal(r, Sprites.R_ProjectSpriteRotation(unchecked(centre - ANG45 / 2), 0));
            Assert.Equal(r, Sprites.R_ProjectSpriteRotation(unchecked(centre + ANG45 / 2 - 1), 0));
            Assert.Equal((r + 1) % 8, Sprites.R_ProjectSpriteRotation(unchecked(centre + ANG45 / 2), 0));
            // Only the difference matters.
            Assert.Equal(r, Sprites.R_ProjectSpriteRotation(unchecked(centre + 0x12345678), 0x12345678));
        }
    }

    [Fact]
    public void SelectPicksTheRotationLumpAndItsMirror()
    {
        WadArchive wad = new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });
        Sprites sprites = Sprites.R_InitSprites(wad);
        SpriteFrame troo = sprites.Find("TROO")!.Frames[0];
        string[] names = { "TROOA1", "TROOA2A8", "TROOA3A7", "TROOA4A6", "TROOA5", "TROOA4A6", "TROOA3A7", "TROOA2A8" };
        for (int r = 0; r < 8; r++)
        {
            (int lump, bool flip, int rot) = troo.Select(unchecked(ANG180 + (uint)r * ANG45), 0);
            Assert.Equal((names[r], r >= 5, r), (wad.Lumps[lump].Name, flip, rot));
        }
        // A rotation-0 frame shows slot 0 from every direction.
        SpriteFrame bar1 = sprites.Find("BAR1")!.Frames[0];
        for (uint ang = 0; ang < 8; ang++)
            Assert.Equal((bar1.Lump[0], false, 0), bar1.Select(ang * ANG45, ANG90));
    }
}
