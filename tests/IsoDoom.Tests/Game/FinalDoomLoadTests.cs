using System.Diagnostics;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T10.7: Final Doom's maps load within SPEC §9's second; split from
/// <see cref="FinalDoomTests"/> into the <see cref="TimingGroup"/> (T8.6a).
/// </summary>
[Collection(TimingGroup.Name)]
public class FinalDoomLoadTests
{
    /// <summary>Every map loads within SPEC §9's second and runs 35 tics with its monsters, as Final Doom's executable.</summary>
    [Theory]
    [MemberData(nameof(FinalDoomTests.Iwads), MemberType = typeof(FinalDoomTests))]
    public void EveryMapLoadsAndPlays(string iwad)
    {
        var wad = WadArchive.Open(TestWads.RequireFinalDoom(iwad));
        Textures textures = Textures.R_InitTextures(wad);
        for (int map = 1; map <= 32; map++)
        {
            var clock = Stopwatch.StartNew();
            // T10.7 review: no version, the identified mission's default (Final Doom's)
            var world = new World(new SpawnSettings(GameMode.commercial, skill_t.sk_hard, gamemission: IwadIdentification.D_IdentifyVersion(wad).GameMission), Tweaks.Vanilla)
            {
                textures = textures,
                map31exists = true,
            };
            Assert.Equal(GameVersion.exe_final, world.gameversion);
            world.P_InitPicAnims(textures, PicAnims.FlatNames(wad));
            world.G_DoLoadLevel(Level.Load(wad, $"MAP{map:00}"));
            Assert.True(clock.Elapsed.TotalSeconds < 1, $"MAP{map:00} took {clock.Elapsed.TotalMilliseconds:F0} ms");
            for (int t = 0; t < 35; t++)
                world.G_Ticker(new ticcmd_t { forwardmove = 25 });
            Assert.True(world.players[0].mo is not null, $"MAP{map:00}: no player");
        }
    }
}
