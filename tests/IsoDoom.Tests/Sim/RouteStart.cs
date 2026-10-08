using System;
using IsoDoom.Map;
using IsoDoom.Sim;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T5.6: a route's <c>start X Y ANGLE</c> header (<c>VanillaRoute</c>),
/// shared with <c>tools/RouteGen</c>.
/// </summary>
public static class RouteStart
{
    /// <summary>
    /// As dump.c's <c>dump_pretic</c> does it before the first tic: player 1's
    /// mobj moves to map point (<paramref name="x"/>, <paramref name="y"/>) with
    /// <see cref="World.P_TeleportMove"/>, onto the floor, facing
    /// <paramref name="angle"/> degrees; no fog, nothing else.
    /// </summary>
    public static void Place(World world, int x, int y, int angle)
    {
        mobj_t mo = world.players[world.consoleplayer].mo!;
        if (!world.P_TeleportMove(mo, x << Fixed.FRACBITS, y << Fixed.FRACBITS))
            throw new InvalidOperationException($"start {x} {y}: something stands there");
        mo.z = mo.floorz;
        mo.angle = (uint)((long)angle * 0x100000000L / 360);
    }
}
