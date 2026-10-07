using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.6: saving and loading in the game scene, once after every map (no
// renderer needed), in the check's own save directory: a new game, the
// quicksave key (the quicksave action, F6) opening the save menu to pick a
// slot, the description typed, the save going with the next tic; the
// quickload key (F9) and its yes loading it back: the scene shows the
// save's map with a new world at the saved tic, which then goes on with the
// same checksums as the game that was saved; a damaged save refused with
// the menus' message, the game shown left as it is.
public partial class LevelCheck
{
    private void CheckSaves()
    {
        GameFlow flow = _scene.Flow;
        MMenu menu = flow.Menu;
        string? saveDir = flow.SaveDir;
        string dir = Path.Combine(Path.GetTempPath(), $"isodoom-check-saves-{System.Environment.ProcessId}");
        flow.SaveDir = dir;
        try
        {
            CheckSavesIn(flow, menu);
        }
        finally
        {
            flow.SaveDir = saveDir;
            menu.quickSaveSlot = -1;
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private void CheckSavesIn(GameFlow flow, MMenu menu)
    {
        string Step(string what) => $"saves (T7.6): {what}: {menu.StateText()}, game {flow.StateText()}";
        foreach (string action in new[] { GameInput.QuickSave, GameInput.QuickLoad })
        {
            if (!InputMap.HasAction(action))
            {
                Fail(Step($"no input action {action}"));
                return;
            }
        }

        _scene.StartNewGame();
        flow.G_DoGameActions();
        if (_scene.World is not { } world || flow.gamestate != gamestate_t.GS_LEVEL)
        {
            Fail(Step("no new game"));
            return;
        }
        // a few steps and turns
        static ticcmd_t Cmd(int tic) => new() { forwardmove = (sbyte)(tic % 20 < 10 ? 25 : -25), angleturn = (short)(tic * 37) };
        for (int tic = 0; tic < 40; tic++)
            _scene.Tic(Cmd(tic));

        // F6: no quicksave slot yet, so the save menu; slot 3 with a description typed
        _scene.MenuEvent(KeyEvent(Key.F6));
        if (menu.StateName != "save" || menu.quickSaveSlot != -2)
        {
            Fail(Step("F6 (quicksave) with no slot did not open the save menu to pick one"));
            return;
        }
        while (menu.itemOn != 3)
            _scene.MenuEvent(KeyEvent(Key.Down));
        _scene.MenuEvent(KeyEvent(Key.Enter));
        foreach (Key k in new[] { Key.C, Key.H, Key.E, Key.C, Key.K })
            _scene.MenuEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = (char)('a' + (k - Key.A)), Pressed = true });
        _scene.MenuEvent(KeyEvent(Key.Enter));
        if (menu.StateName != "closed" || !flow.sendsave || menu.quickSaveSlot != 3)
        {
            Fail(Step("the description typed and Enter did not ask for a save in slot 3"));
            return;
        }
        _scene.Tic(Cmd(40)); // carries BTS_SAVEGAME, saved after it
        int savedAt = world.leveltime;
        if (flow.SaveDescription(3) != "CHECK" || world.players[world.consoleplayer].message != GameFlow.GGSAVED)
        {
            Fail(Step($"the save after the tic: slot 3 is \"{flow.SaveDescription(3)}\", message \"{world.players[world.consoleplayer].message}\""));
            return;
        }
        var expected = new List<ulong>();
        for (int tic = 41; tic < 111; tic++)
        {
            _scene.Tic(Cmd(tic));
            expected.Add(world.Checksum());
        }

        // F9 and yes: the save loads; the scene shows its map and a new world at the saved tic
        _scene.MenuEvent(KeyEvent(Key.F9));
        if (menu.messageString != string.Format(System.Globalization.CultureInfo.InvariantCulture, MMenu.QLPROMPT, "CHECK"))
        {
            Fail(Step("F9 (quickload) did not ask to load slot 3"));
            return;
        }
        _scene.MenuEvent(KeyEvent(Key.Y));
        if (flow.gameaction != gameaction_t.ga_loadgame)
        {
            Fail(Step("yes did not load"));
            return;
        }
        flow.G_DoGameActions();
        if (_scene.World is not { } loaded || ReferenceEquals(loaded, world) || loaded.leveltime != savedAt
            || _scene.Mesh?.Level.Name != world.level.Name || _scene.PlayerMobj is null || flow.gamestate != gamestate_t.GS_LEVEL)
        {
            Fail(Step($"the load shows {_scene.Mesh?.Level.Name} at tic {_scene.World?.leveltime}, expected {world.level.Name} at {savedAt} in a new world"));
            return;
        }
        for (int tic = 41; tic < 111; tic++)
        {
            _scene.Tic(Cmd(tic));
            if (loaded.Checksum() != expected[tic - 41])
            {
                Fail(Step($"the loaded game's tic {tic + 1} differs from the saved game's"));
                return;
            }
        }

        // a damaged save: refused with the menus' message, the game goes on
        byte[] data = File.ReadAllBytes(flow.SaveGamePath(3)!);
        data[data.Length / 2] ^= 0x10;
        File.WriteAllBytes(flow.SaveGamePath(4)!, data);
        flow.G_LoadGame(4);
        flow.G_DoGameActions();
        if (flow.LoadRefused != SaveGameFile.DAMAGED || !menu.messageToPrint || !ReferenceEquals(_scene.World, loaded))
        {
            Fail(Step($"a damaged save: refused with \"{flow.LoadRefused}\", the game {(ReferenceEquals(_scene.World, loaded) ? "kept" : "changed")}"));
            return;
        }
        _scene.MenuEvent(KeyEvent(Key.Space));
        GD.Print($"Level check: saves (T7.6): F6 picked slot 3 and saved {world.level.Name} at tic {savedAt}, F9 loaded it, {expected.Count} tics on the same checksums; a damaged save refused");
    }
}
