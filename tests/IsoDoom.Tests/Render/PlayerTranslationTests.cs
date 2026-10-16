using IsoDoom.Render;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T11.10: the players' colours (<see cref="PlayerTranslation"/>) are r_draw.c's translation tables.</summary>
public class PlayerTranslationTests
{
    // r_draw.c R_InitTranslationTables, as vanilla writes it.
    private static byte[] VanillaTables()
    {
        var translationtables = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            if (i >= 0x70 && i <= 0x7f)
            {
                // map green ramp to gray, brown, red
                translationtables[i] = (byte)(0x60 + (i & 0xf));
                translationtables[i + 256] = (byte)(0x40 + (i & 0xf));
                translationtables[i + 512] = (byte)(0x20 + (i & 0xf));
            }
            else
            {
                // Keep all other colors as is.
                translationtables[i] = translationtables[i + 256] = translationtables[i + 512] = (byte)i;
            }
        }
        return translationtables;
    }

    [Fact]
    public void EveryTableIsVanillas()
    {
        byte[] tables = VanillaTables();
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal((byte)i, PlayerTranslation.Translate((byte)i, 0)); // player 1: none
            for (int t = 1; t < PlayerTranslation.Tables; t++)
                Assert.Equal(tables[(t - 1) * 256 + i], PlayerTranslation.Translate((byte)i, t));
        }
    }

    [Fact]
    public void OnlyTheGreenRampChanges()
    {
        for (int i = 0; i < 256; i++)
            Assert.Equal(i is >= 0x70 and <= 0x7f, PlayerTranslation.Changes((byte)i, 3));
    }
}
