using IsoDoom.Wad;

namespace IsoDoom.Game;

// f_finale.c's text screens (T7.5): d_englsh.h's texts, as Chocolate Doom
// has them (the vanilla reference's), and f_finale.c's textscreens table.
public sealed partial class FFinale
{
    /// <summary>f_finale.c <c>textscreen_t</c>: the text and flat after map <c>level</c> of <c>episode</c> (Doom II and Final Doom: episode 1) of <c>mission</c>.</summary>
    public readonly record struct textscreen_t(GameMission mission, int episode, int level, string background, string text);

    /// <summary>f_finale.c <c>textscreens</c>.</summary>
    public static readonly textscreen_t[] textscreens =
    [
        new(GameMission.doom, 1, 8, "FLOOR4_8", E1TEXT),
        new(GameMission.doom, 2, 8, "SFLR6_1", E2TEXT),
        new(GameMission.doom, 3, 8, "MFLR8_4", E3TEXT),
        new(GameMission.doom, 4, 8, "MFLR8_3", E4TEXT),

        new(GameMission.doom2, 1, 6, "SLIME16", C1TEXT),
        new(GameMission.doom2, 1, 11, "RROCK14", C2TEXT),
        new(GameMission.doom2, 1, 20, "RROCK07", C3TEXT),
        new(GameMission.doom2, 1, 30, "RROCK17", C4TEXT),
        new(GameMission.doom2, 1, 15, "RROCK13", C5TEXT),
        new(GameMission.doom2, 1, 31, "RROCK19", C6TEXT),

        new(GameMission.pack_tnt, 1, 6, "SLIME16", T1TEXT),
        new(GameMission.pack_tnt, 1, 11, "RROCK14", T2TEXT),
        new(GameMission.pack_tnt, 1, 20, "RROCK07", T3TEXT),
        new(GameMission.pack_tnt, 1, 30, "RROCK17", T4TEXT),
        new(GameMission.pack_tnt, 1, 15, "RROCK13", T5TEXT),
        new(GameMission.pack_tnt, 1, 31, "RROCK19", T6TEXT),

        new(GameMission.pack_plut, 1, 6, "SLIME16", P1TEXT),
        new(GameMission.pack_plut, 1, 11, "RROCK14", P2TEXT),
        new(GameMission.pack_plut, 1, 20, "RROCK07", P3TEXT),
        new(GameMission.pack_plut, 1, 30, "RROCK17", P4TEXT),
        new(GameMission.pack_plut, 1, 15, "RROCK13", P5TEXT),
        new(GameMission.pack_plut, 1, 31, "RROCK19", P6TEXT),
    ];

    /// <summary>d_englsh.h <c>E1TEXT</c>.</summary>
    public const string E1TEXT =
        "Once you beat the big badasses and\n" +
        "clean out the moon base you're supposed\n" +
        "to win, aren't you? Aren't you? Where's\n" +
        "your fat reward and ticket home? What\n" +
        "the hell is this? It's not supposed to\n" +
        "end this way!\n" +
        "\n" +
        "It stinks like rotten meat, but looks\n" +
        "like the lost Deimos base.  Looks like\n" +
        "you're stuck on The Shores of Hell.\n" +
        "The only way out is through.\n" +
        "\n" +
        "To continue the DOOM experience, play\n" +
        "The Shores of Hell and its amazing\n" +
        "sequel, Inferno!\n";

    /// <summary>d_englsh.h <c>E2TEXT</c>.</summary>
    public const string E2TEXT =
        "You've done it! The hideous cyber-\n" +
        "demon lord that ruled the lost Deimos\n" +
        "moon base has been slain and you\n" +
        "are triumphant! But ... where are\n" +
        "you? You clamber to the edge of the\n" +
        "moon and look down to see the awful\n" +
        "truth.\n" +
        "\n" +
        "Deimos floats above Hell itself!\n" +
        "You've never heard of anyone escaping\n" +
        "from Hell, but you'll make the bastards\n" +
        "sorry they ever heard of you! Quickly,\n" +
        "you rappel down to  the surface of\n" +
        "Hell.\n" +
        "\n" +
        "Now, it's on to the final chapter of\n" +
        "DOOM! -- Inferno.";

    /// <summary>d_englsh.h <c>E3TEXT</c>.</summary>
    public const string E3TEXT =
        "The loathsome spiderdemon that\n" +
        "masterminded the invasion of the moon\n" +
        "bases and caused so much death has had\n" +
        "its ass kicked for all time.\n" +
        "\n" +
        "A hidden doorway opens and you enter.\n" +
        "You've proven too tough for Hell to\n" +
        "contain, and now Hell at last plays\n" +
        "fair -- for you emerge from the door\n" +
        "to see the green fields of Earth!\n" +
        "Home at last.\n" +
        "\n" +
        "You wonder what's been happening on\n" +
        "Earth while you were battling evil\n" +
        "unleashed. It's good that no Hell-\n" +
        "spawn could have come through that\n" +
        "door with you ...";

    /// <summary>d_englsh.h <c>E4TEXT</c>.</summary>
    public const string E4TEXT =
        "the spider mastermind must have sent forth\n" +
        "its legions of hellspawn before your\n" +
        "final confrontation with that terrible\n" +
        "beast from hell.  but you stepped forward\n" +
        "and brought forth eternal damnation and\n" +
        "suffering upon the horde as a true hero\n" +
        "would in the face of something so evil.\n" +
        "\n" +
        "besides, someone was gonna pay for what\n" +
        "happened to daisy, your pet rabbit.\n" +
        "\n" +
        "but now, you see spread before you more\n" +
        "potential pain and gibbitude as a nation\n" +
        "of demons run amok among our cities.\n" +
        "\n" +
        "next stop, hell on earth!";

    /// <summary>d_englsh.h <c>C1TEXT</c>.</summary>
    public const string C1TEXT =
        "YOU HAVE ENTERED DEEPLY INTO THE INFESTED\n" +
        "STARPORT. BUT SOMETHING IS WRONG. THE\n" +
        "MONSTERS HAVE BROUGHT THEIR OWN REALITY\n" +
        "WITH THEM, AND THE STARPORT'S TECHNOLOGY\n" +
        "IS BEING SUBVERTED BY THEIR PRESENCE.\n" +
        "\n" +
        "AHEAD, YOU SEE AN OUTPOST OF HELL, A\n" +
        "FORTIFIED ZONE. IF YOU CAN GET PAST IT,\n" +
        "YOU CAN PENETRATE INTO THE HAUNTED HEART\n" +
        "OF THE STARBASE AND FIND THE CONTROLLING\n" +
        "SWITCH WHICH HOLDS EARTH'S POPULATION\n" +
        "HOSTAGE.";

    /// <summary>d_englsh.h <c>C2TEXT</c>.</summary>
    public const string C2TEXT =
        "YOU HAVE WON! YOUR VICTORY HAS ENABLED\n" +
        "HUMANKIND TO EVACUATE EARTH AND ESCAPE\n" +
        "THE NIGHTMARE.  NOW YOU ARE THE ONLY\n" +
        "HUMAN LEFT ON THE FACE OF THE PLANET.\n" +
        "CANNIBAL MUTATIONS, CARNIVOROUS ALIENS,\n" +
        "AND EVIL SPIRITS ARE YOUR ONLY NEIGHBORS.\n" +
        "YOU SIT BACK AND WAIT FOR DEATH, CONTENT\n" +
        "THAT YOU HAVE SAVED YOUR SPECIES.\n" +
        "\n" +
        "BUT THEN, EARTH CONTROL BEAMS DOWN A\n" +
        "MESSAGE FROM SPACE: \"SENSORS HAVE LOCATED\n" +
        "THE SOURCE OF THE ALIEN INVASION. IF YOU\n" +
        "GO THERE, YOU MAY BE ABLE TO BLOCK THEIR\n" +
        "ENTRY.  THE ALIEN BASE IS IN THE HEART OF\n" +
        "YOUR OWN HOME CITY, NOT FAR FROM THE\n" +
        "STARPORT.\" SLOWLY AND PAINFULLY YOU GET\n" +
        "UP AND RETURN TO THE FRAY.";

    /// <summary>d_englsh.h <c>C3TEXT</c>.</summary>
    public const string C3TEXT =
        "YOU ARE AT THE CORRUPT HEART OF THE CITY,\n" +
        "SURROUNDED BY THE CORPSES OF YOUR ENEMIES.\n" +
        "YOU SEE NO WAY TO DESTROY THE CREATURES'\n" +
        "ENTRYWAY ON THIS SIDE, SO YOU CLENCH YOUR\n" +
        "TEETH AND PLUNGE THROUGH IT.\n" +
        "\n" +
        "THERE MUST BE A WAY TO CLOSE IT ON THE\n" +
        "OTHER SIDE. WHAT DO YOU CARE IF YOU'VE\n" +
        "GOT TO GO THROUGH HELL TO GET TO IT?";

    /// <summary>d_englsh.h <c>C4TEXT</c>.</summary>
    public const string C4TEXT =
        "THE HORRENDOUS VISAGE OF THE BIGGEST\n" +
        "DEMON YOU'VE EVER SEEN CRUMBLES BEFORE\n" +
        "YOU, AFTER YOU PUMP YOUR ROCKETS INTO\n" +
        "HIS EXPOSED BRAIN. THE MONSTER SHRIVELS\n" +
        "UP AND DIES, ITS THRASHING LIMBS\n" +
        "DEVASTATING UNTOLD MILES OF HELL'S\n" +
        "SURFACE.\n" +
        "\n" +
        "YOU'VE DONE IT. THE INVASION IS OVER.\n" +
        "EARTH IS SAVED. HELL IS A WRECK. YOU\n" +
        "WONDER WHERE BAD FOLKS WILL GO WHEN THEY\n" +
        "DIE, NOW. WIPING THE SWEAT FROM YOUR\n" +
        "FOREHEAD YOU BEGIN THE LONG TREK BACK\n" +
        "HOME. REBUILDING EARTH OUGHT TO BE A\n" +
        "LOT MORE FUN THAN RUINING IT WAS.\n";

    /// <summary>d_englsh.h <c>C5TEXT</c>.</summary>
    public const string C5TEXT =
        "CONGRATULATIONS, YOU'VE FOUND THE SECRET\n" +
        "LEVEL! LOOKS LIKE IT'S BEEN BUILT BY\n" +
        "HUMANS, RATHER THAN DEMONS. YOU WONDER\n" +
        "WHO THE INMATES OF THIS CORNER OF HELL\n" +
        "WILL BE.";

    /// <summary>d_englsh.h <c>C6TEXT</c>.</summary>
    public const string C6TEXT =
        "CONGRATULATIONS, YOU'VE FOUND THE\n" +
        "SUPER SECRET LEVEL!  YOU'D BETTER\n" +
        "BLAZE THROUGH THIS ONE!\n";

    /// <summary>d_englsh.h <c>P1TEXT</c>.</summary>
    public const string P1TEXT =
        "You gloat over the steaming carcass of the\n" +
        "Guardian.  With its death, you've wrested\n" +
        "the Accelerator from the stinking claws\n" +
        "of Hell.  You relax and glance around the\n" +
        "room.  Damn!  There was supposed to be at\n" +
        "least one working prototype, but you can't\n" +
        "see it. The demons must have taken it.\n" +
        "\n" +
        "You must find the prototype, or all your\n" +
        "struggles will have been wasted. Keep\n" +
        "moving, keep fighting, keep killing.\n" +
        "Oh yes, keep living, too.";

    /// <summary>d_englsh.h <c>P2TEXT</c>.</summary>
    public const string P2TEXT =
        "Even the deadly Arch-Vile labyrinth could\n" +
        "not stop you, and you've gotten to the\n" +
        "prototype Accelerator which is soon\n" +
        "efficiently and permanently deactivated.\n" +
        "\n" +
        "You're good at that kind of thing.";

    /// <summary>d_englsh.h <c>P3TEXT</c>.</summary>
    public const string P3TEXT =
        "You've bashed and battered your way into\n" +
        "the heart of the devil-hive.  Time for a\n" +
        "Search-and-Destroy mission, aimed at the\n" +
        "Gatekeeper, whose foul offspring is\n" +
        "cascading to Earth.  Yeah, he's bad. But\n" +
        "you know who's worse!\n" +
        "\n" +
        "Grinning evilly, you check your gear, and\n" +
        "get ready to give the bastard a little Hell\n" +
        "of your own making!";

    /// <summary>d_englsh.h <c>P4TEXT</c>.</summary>
    public const string P4TEXT =
        "The Gatekeeper's evil face is splattered\n" +
        "all over the place.  As its tattered corpse\n" +
        "collapses, an inverted Gate forms and\n" +
        "sucks down the shards of the last\n" +
        "prototype Accelerator, not to mention the\n" +
        "few remaining demons.  You're done. Hell\n" +
        "has gone back to pounding bad dead folks \n" +
        "instead of good live ones.  Remember to\n" +
        "tell your grandkids to put a rocket\n" +
        "launcher in your coffin. If you go to Hell\n" +
        "when you die, you'll need it for some\n" +
        "final cleaning-up ...";

    /// <summary>d_englsh.h <c>P5TEXT</c>.</summary>
    public const string P5TEXT =
        "You've found the second-hardest level we\n" +
        "got. Hope you have a saved game a level or\n" +
        "two previous.  If not, be prepared to die\n" +
        "aplenty. For master marines only.";

    /// <summary>d_englsh.h <c>P6TEXT</c>.</summary>
    public const string P6TEXT =
        "Betcha wondered just what WAS the hardest\n" +
        "level we had ready for ya?  Now you know.\n" +
        "No one gets out alive.";

    /// <summary>d_englsh.h <c>T1TEXT</c>.</summary>
    public const string T1TEXT =
        "You've fought your way out of the infested\n" +
        "experimental labs.   It seems that UAC has\n" +
        "once again gulped it down.  With their\n" +
        "high turnover, it must be hard for poor\n" +
        "old UAC to buy corporate health insurance\n" +
        "nowadays..\n" +
        "\n" +
        "Ahead lies the military complex, now\n" +
        "swarming with diseased horrors hot to get\n" +
        "their teeth into you. With luck, the\n" +
        "complex still has some warlike ordnance\n" +
        "laying around.";

    /// <summary>d_englsh.h <c>T2TEXT</c>.</summary>
    public const string T2TEXT =
        "You hear the grinding of heavy machinery\n" +
        "ahead.  You sure hope they're not stamping\n" +
        "out new hellspawn, but you're ready to\n" +
        "ream out a whole herd if you have to.\n" +
        "They might be planning a blood feast, but\n" +
        "you feel about as mean as two thousand\n" +
        "maniacs packed into one mad killer.\n" +
        "\n" +
        "You don't plan to go down easy.";

    /// <summary>d_englsh.h <c>T3TEXT</c>.</summary>
    public const string T3TEXT =
        "The vista opening ahead looks real damn\n" +
        "familiar. Smells familiar, too -- like\n" +
        "fried excrement. You didn't like this\n" +
        "place before, and you sure as hell ain't\n" +
        "planning to like it now. The more you\n" +
        "brood on it, the madder you get.\n" +
        "Hefting your gun, an evil grin trickles\n" +
        "onto your face. Time to take some names.";

    /// <summary>d_englsh.h <c>T4TEXT</c>.</summary>
    public const string T4TEXT =
        "Suddenly, all is silent, from one horizon\n" +
        "to the other. The agonizing echo of Hell\n" +
        "fades away, the nightmare sky turns to\n" +
        "blue, the heaps of monster corpses start \n" +
        "to evaporate along with the evil stench \n" +
        "that filled the air. Jeeze, maybe you've\n" +
        "done it. Have you really won?\n" +
        "\n" +
        "Something rumbles in the distance.\n" +
        "A blue light begins to glow inside the\n" +
        "ruined skull of the demon-spitter.";

    /// <summary>d_englsh.h <c>T5TEXT</c>.</summary>
    public const string T5TEXT =
        "What now? Looks totally different. Kind\n" +
        "of like King Tut's condo. Well,\n" +
        "whatever's here can't be any worse\n" +
        "than usual. Can it?  Or maybe it's best\n" +
        "to let sleeping gods lie..";

    /// <summary>d_englsh.h <c>T6TEXT</c>.</summary>
    public const string T6TEXT =
        "Time for a vacation. You've burst the\n" +
        "bowels of hell and by golly you're ready\n" +
        "for a break. You mutter to yourself,\n" +
        "Maybe someone else can kick Hell's ass\n" +
        "next time around. Ahead lies a quiet town,\n" +
        "with peaceful flowing water, quaint\n" +
        "buildings, and presumably no Hellspawn.\n" +
        "\n" +
        "As you step off the transport, you hear\n" +
        "the stomp of a cyberdemon's iron shoe.";
}
