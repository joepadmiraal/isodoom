using System;
using System.Collections.Generic;
using System.IO;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.1a: the IWAD search order, on temporary folders with a faked environment.</summary>
public sealed class IwadLocatorTests : IDisposable
{
    private readonly string _root;
    private readonly Dictionary<string, string> _env = [];
    private readonly Dictionary<(string, string), string> _registry = [];

    public IwadLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "isodoom-iwad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // Keep the real home, XDG and Steam folders out of the search.
        _env["HOME"] = Dir("home");
        _env["XDG_DATA_DIRS"] = Dir("xdgdirs");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Dir(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Touch(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [(byte)'I', (byte)'W', (byte)'A', (byte)'D']);
        return path;
    }

    private IwadLocator Locator(string[]? args = null, string? config = null, IwadPlatform platform = IwadPlatform.Linux,
        string[]? gameDirs = null) =>
        new(new IwadSearchContext
        {
            CommandLine = new CommandLine("isodoom", args ?? []),
            GetEnv = name => _env.TryGetValue(name, out string? v) ? v : null,
            CurrentDirectory = Dir("cwd"),
            GameDirectories = gameDirs ?? [Dir("game"), Dir("game/wads")],
            ConfiguredIwad = config,
            Platform = platform,
            ReadRegistry = (key, value) => _registry.TryGetValue((key, value), out string? v) ? v : null,
        });

    private IwadSearchResult Find(string[]? args = null, string? config = null, IwadPlatform platform = IwadPlatform.Linux) =>
        Locator(args, config, platform).D_FindIWAD();

    // ---- m_argv.c ----

    [Fact]
    public void CheckParmIsCaseInsensitiveAndSkipsTheProgram()
    {
        var cl = new CommandLine("-iwad", ["-WARP", "1", "-iwad"]);
        Assert.Equal(1, cl.M_CheckParm("-warp"));
        Assert.Equal(3, cl.M_CheckParm("-iwad")); // index 0 is the program, never matched
        Assert.Equal(0, cl.M_CheckParm("-file"));
        Assert.Equal(1, cl.M_CheckParmWithArgs("-warp", 1));
        Assert.Equal(0, cl.M_CheckParmWithArgs("-iwad", 1)); // no argument follows
        Assert.Null(cl.GetParmValue("-iwad"));
    }

    [Fact]
    public void FileListStopsAtTheNextOption()
    {
        var cl = new CommandLine("isodoom", ["-file", "a.wad", "b.wad", "-warp", "1", "c.wad"]);
        Assert.Equal(["a.wad", "b.wad"], cl.GetParmList("-file"));
        Assert.Empty(new CommandLine("isodoom", ["-file"]).GetParmList("-file"));
    }

    // ---- Explicit IWADs ----

    [Fact]
    public void IwadParameterWinsOverEverythingElse()
    {
        string explicitWad = Touch("elsewhere/my.wad");
        _env["ISODOOM_IWAD"] = Touch("env/doom.wad");
        Touch("cwd/doom1.wad");
        IwadSearchResult r = Find(["-iwad", explicitWad], config: Touch("config/doom.wad"));
        Assert.Equal(explicitWad, r.Path);
        Assert.Equal(IwadSource.CommandLine, r.Source);
        Assert.Null(r.Error);
    }

    [Fact]
    public void DoubleDashIwadIsAnAlias()
    {
        string wad = Touch("elsewhere/doom1.wad");
        Assert.Equal(wad, Find(["--iwad", wad]).Path);
    }

    [Fact]
    public void RelativeIwadResolvesAgainstTheWorkingDirectory()
    {
        string wad = Touch("cwd/sub/doom1.wad");
        Assert.Equal(wad, Find(["-iwad", "sub/doom1.wad"]).Path);
    }

    [Fact]
    public void BareIwadNameIsLookedForInTheSearchDirectories()
    {
        _env["DOOMWADDIR"] = Dir("waddir");
        string wad = Touch("waddir/DOOM2.WAD");
        IwadSearchResult r = Find(["-iwad", "doom2.wad"]);
        Assert.Equal(wad, r.Path);
    }

    [Fact]
    public void MissingExplicitIwadIsAnErrorWithoutFallback()
    {
        Touch("cwd/doom1.wad");
        IwadSearchResult r = Find(["-iwad", Path.Combine(_root, "nope.wad")]);
        Assert.Null(r.Path);
        Assert.Equal(IwadSource.CommandLine, r.Source);
        Assert.Contains("nope.wad", r.Error);

        _env["ISODOOM_IWAD"] = Path.Combine(_root, "gone.wad");
        r = Find();
        Assert.Null(r.Path);
        Assert.Equal(IwadSource.Environment, r.Source);
        Assert.Contains("gone.wad", r.Error);
    }

    [Fact]
    public void EnvironmentVariablesComeBeforeTheConfig()
    {
        string config = Touch("config/doom.wad");
        _env["ISODOOM_DOOM1_WAD"] = Touch("tests/DOOM1.WAD");
        IwadSearchResult r = Find(config: config);
        Assert.Equal(_env["ISODOOM_DOOM1_WAD"], r.Path);
        Assert.Equal("ISODOOM_DOOM1_WAD", r.SourceDetail);

        _env["ISODOOM_IWAD"] = Touch("env/doom2.wad");
        r = Find(config: config);
        Assert.Equal(_env["ISODOOM_IWAD"], r.Path);
        Assert.Equal("ISODOOM_IWAD", r.SourceDetail);

        _env["ISODOOM_IWAD"] = "";
        _env["ISODOOM_DOOM1_WAD"] = "";
        r = Find(config: config); // empty variables count as unset
        Assert.Equal(config, r.Path);
        Assert.Equal(IwadSource.Config, r.Source);
    }

    [Fact]
    public void StaleConfigWarnsAndSearchesOn()
    {
        string found = Touch("cwd/doom1.wad");
        IwadSearchResult r = Find(config: Path.Combine(_root, "moved/doom.wad"));
        Assert.Equal(found, r.Path);
        Assert.Equal(IwadSource.Search, r.Source);
        Assert.Contains("moved", r.Warning);
    }

    // ---- Directory search ----

    [Fact]
    public void SearchDirectoriesAreTriedInOrder()
    {
        // One IWAD per location; remove them front to back and check the next one wins.
        _env["DOOMWADDIR"] = Dir("waddir");
        _env["DOOMWADPATH"] = Dir("wadpath1") + ":" + Dir("wadpath2");
        _env["XDG_DATA_HOME"] = Dir("xdghome");
        _env["XDG_DATA_DIRS"] = Dir("xdgdirs");
        string steamLibrary = Dir("sdcard/SteamLibrary");
        File.WriteAllText(Path.Combine(Dir("home/.local/share/Steam/steamapps"), "libraryfolders.vdf"),
            $"\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{steamLibrary}\"\n\t}}\n}}\n");

        string[] order =
        [
            Touch("cwd/doom1.wad"),
            Touch("game/DOOM1.WAD"),
            Touch("game/wads/DOOM1.WAD"),
            Touch("waddir/doom1.wad"),
            Touch("wadpath1/doom1.wad"),
            Touch("wadpath2/doom1.wad"),
            Touch("xdghome/games/doom/doom1.wad"),
            Touch("xdgdirs/games/doom/doom1.wad"),
            Touch("xdgdirs/doom/doom1.wad"),
            Touch("home/.steam/root/steamapps/common/Ultimate Doom/base/DOOM.WAD"),
            Touch("home/.local/share/Steam/steamapps/common/Doom 2/base/DOOM2.WAD"),
            Touch("sdcard/SteamLibrary/steamapps/common/Doom 2/rerelease/DOOM2.WAD"),
            Touch("home/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Final Doom/base/TNT.WAD"),
            Touch("home/GOG Games/DOOM II/Doom2/DOOM2.WAD"),
            Touch("home/Games/Heroic/Ultimate DOOM/base/DOOM.WAD"),
        ];

        foreach (string expected in order)
        {
            IwadSearchResult r = Find();
            Assert.Equal(IwadSource.Search, r.Source);
            Assert.Equal(expected, r.Path);
            File.Delete(expected);
        }
        IwadSearchResult none = Find();
        Assert.Null(none.Path);
        Assert.Equal(IwadSource.None, none.Source);
        Assert.NotNull(none.Error);
    }

    [Fact]
    public void WithinADirectoryDoom1IwadsComeFirst()
    {
        string doom2 = Touch("cwd/doom2.wad");
        string shareware = Touch("cwd/DOOM1.WAD");
        Assert.Equal(shareware, Find().Path);
        string registered = Touch("cwd/Doom.wad");
        Assert.Equal(registered, Find().Path);
        File.Delete(registered);
        File.Delete(shareware);
        Assert.Equal(doom2, Find().Path);
        Touch("cwd/tnt.wad");
        Touch("cwd/plutonia.wad");
        Assert.Equal(doom2, Find().Path);
    }

    [Fact]
    public void EarlierDirectoryBeatsBetterIwadName()
    {
        string shareware = Touch("cwd/DOOM1.WAD");
        Touch("game/wads/DOOM.WAD");
        Assert.Equal(shareware, Find().Path);
    }

    [Fact]
    public void FindAllIwadsListsEveryCandidateOnce()
    {
        string a = Touch("cwd/DOOM1.WAD");
        string b = Touch("cwd/doom2.wad");
        string c = Touch("game/wads/DOOM.WAD");
        IwadLocator locator = Locator(gameDirs: [Dir("game/wads"), Dir("game/wads") + "/../wads"]);
        Assert.Equal([a, b, c], locator.D_FindAllIWADs());
    }

    [Fact]
    public void WadPathEntryMayNameAFile()
    {
        string wad = Touch("files/DOOM1.WAD");
        _env["DOOMWADPATH"] = wad;
        Assert.Equal(wad, Find().Path);
    }

    [Fact]
    public void NonIwadNamesAreIgnored()
    {
        Touch("cwd/mymap.wad");
        Touch("cwd/doom3.wad");
        Assert.Null(Find().Path);
    }

    [Fact]
    public void WindowsUsesGogAndSteamRegistryKeys()
    {
        _env["USERPROFILE"] = Dir("winhome");
        _registry[(@"SOFTWARE\GOG.com\Games\1435848814", "PATH")] = Dir("gog/DOOM 2");
        _registry[(@"SOFTWARE\Valve\Steam", "InstallPath")] = Dir("steam");
        string steamDoom = Touch("steam/steamapps/common/Ultimate Doom/base/DOOM.WAD");
        string gogDoom2 = Touch("gog/DOOM 2/Doom2/DOOM2.WAD");
        Assert.Equal(gogDoom2, Find(platform: IwadPlatform.Windows).Path);
        File.Delete(gogDoom2);
        Assert.Equal(steamDoom, Find(platform: IwadPlatform.Windows).Path);
        File.Delete(steamDoom);

        _env["ProgramFiles(x86)"] = Dir("pf86");
        string defaultSteam = Touch("pf86/Steam/steamapps/common/Doom 2/base/DOOM2.WAD");
        Assert.Equal(defaultSteam, Find(platform: IwadPlatform.Windows).Path);
        File.Delete(defaultSteam);
        string galaxy = Touch("pf86/GOG Galaxy/Games/DOOM + DOOM II/base/DOOM.WAD");
        Assert.Equal(galaxy, Find(platform: IwadPlatform.Windows).Path);
    }

    [Fact]
    public void WindowsSplitsWadPathOnSemicolons()
    {
        string wad = Touch("b/doom1.wad");
        _env["DOOMWADPATH"] = Dir("a") + ";" + Dir("b");
        Assert.Equal(wad, Find(platform: IwadPlatform.Windows).Path);
    }

    [Fact]
    public void MacUsesTheSteamFolder()
    {
        string wad = Touch("home/Library/Application Support/Steam/steamapps/common/Ultimate Doom/base/DOOM.WAD");
        Assert.Equal(wad, Find(platform: IwadPlatform.MacOS).Path);
        Assert.Null(Find(platform: IwadPlatform.Linux).Path);
    }

    [Fact]
    public void ParsesSteamLibraryFolders()
    {
        const string Vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"/home/deck/.local/share/Steam\"\n\t\t\"label\"\t\t\"\"\n\t}\n"
            + "\t\"1\"\n\t{\n\t\t\"PATH\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}\n";
        Assert.Equal(["/home/deck/.local/share/Steam", @"D:\SteamLibrary"], IwadLocator.ParseSteamLibraryFolders(Vdf));
    }

    // ---- Files ----

    [Fact]
    public void FileCaseExistsTriesNameVariants()
    {
        string upper = Touch("case/DOOM1.WAD");
        string lower = Touch("case/doom2.wad");
        string capital = Touch("case/Tnt.wad");
        Assert.Equal(upper, IwadLocator.M_FileCaseExists(Path.Combine(_root, "case/doom1.wad")));
        Assert.Equal(lower, IwadLocator.M_FileCaseExists(Path.Combine(_root, "case/DOOM2.WAD")));
        Assert.Equal(capital, IwadLocator.M_FileCaseExists(Path.Combine(_root, "case/TNT.WAD")));
        Assert.Null(IwadLocator.M_FileCaseExists(Path.Combine(_root, "case/doom.wad")));
    }

    [Fact]
    public void PwadsAreResolvedInOrder()
    {
        _env["DOOMWADDIR"] = Dir("waddir");
        string local = Touch("cwd/maps/a.wad");
        string inWadDir = Touch("waddir/B.WAD");
        Touch("cwd/doom1.wad");
        IwadSearchResult r = Find(["-file", "maps/a.wad", "b.wad", "missing.wad", "-warp", "1"]);
        Assert.Equal([local, inWadDir, Path.Combine(_root, "cwd", "missing.wad")], r.Pwads);
        Assert.Empty(Find().Pwads);
    }

    [Fact]
    public void FindsTheRepoTestIwads()
    {
        // The real wads/ folder as the game folder: with DOOM1.WAD and doom2.wad there, DOOM1.WAD wins.
        string? repo = Support.TestWads.RepoRoot;
        string wads = Path.Combine(repo ?? _root, "wads");
        if (!File.Exists(Path.Combine(wads, "DOOM1.WAD")))
            Assert.Skip("wads/DOOM1.WAD not present");
        IwadSearchResult r = Locator(gameDirs: [wads]).D_FindIWAD();
        Assert.Equal(Path.Combine(wads, "DOOM1.WAD"), r.Path);
    }
}
