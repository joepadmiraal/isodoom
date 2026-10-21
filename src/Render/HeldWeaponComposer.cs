using System;
using System.Runtime.CompilerServices;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>T6.14c: where a texel of a <see cref="HeldComposite"/> comes from.</summary>
public enum CompositeTexel : byte
{
    /// <summary>Transparent.</summary>
    None,

    /// <summary>The stock lump's own texel, unchanged (the hands over a front weapon among them).</summary>
    Body,

    /// <summary>A texel of the removed rifle or flash inside the body, filled from its neighbours.</summary>
    Fill,

    /// <summary>The held weapon.</summary>
    Weapon,

    /// <summary>Frame F's muzzle flash, moved to the weapon's muzzle.</summary>
    Flash,

    /// <summary>Our fist art, drawn over the hands with empty hands.</summary>
    Fist,
}

/// <summary>
/// T6.14c: one composite, its image (with the offsets that keep the body where the stock
/// lump draws it), where the stock lump's texel (0, 0) lies in it, and where each texel
/// comes from.
/// </summary>
public sealed class HeldComposite
{
    internal HeldComposite(IndexedImage image, int shiftX, int shiftY, CompositeTexel[] sources, bool[] weapon)
    {
        _weapon = weapon;
        Image = image;
        ShiftX = shiftX;
        ShiftY = shiftY;
        _sources = sources;
    }

    /// <summary>The composite: palette indices and offsets as a patch's.</summary>
    public IndexedImage Image { get; }

    /// <summary>The stock lump's texel (x, y) is the composite's (x + ShiftX, y + ShiftY).</summary>
    public int ShiftX { get; }

    /// <inheritdoc cref="ShiftX"/>
    public int ShiftY { get; }

    /// <summary>Per texel, row-major as <see cref="IndexedImage.Pixels"/>: where it comes from.</summary>
    public ReadOnlySpan<CompositeTexel> Sources => _sources;

    private readonly CompositeTexel[] _sources;

    /// <summary>
    /// Per texel, as <see cref="Sources"/>: whether the weapon covers it before the body and
    /// the hands are layered over it (so under the hands too).
    /// </summary>
    public ReadOnlySpan<bool> WeaponCoverage => _weapon;

    private readonly bool[] _weapon;
}

/// <summary>
/// T6.14c (SPEC §12 "T6.14 Player weapons"): puts a <see cref="HeldWeapon"/> in the hands
/// of one <c>PLAY</c> lump of frames A–G in place of its stock rifle, on the CPU, into one
/// palette image the renderer draws as any sprite. Steps: the rifle and the flash removed
/// (the holes inside the body's silhouette filled from their neighbours, those outside it
/// cleared); the weapon resampled (<see cref="PixelArt.Transform"/>) with its grip on the
/// pose's grip, pointing along the pose's grip→muzzle direction, its own length × scale,
/// stretched along that axis by <c>along</c> (the pose's foreshortening by default,
/// <see cref="HeldWeaponSet.Along"/>'s rule in the set; above 1 only ahead of the grip),
/// from the pose's rear hand through its forward hand where it has one (<see cref="HoldPose.Hands"/>), mirrored when the muzzle
/// points left; with empty hands (the fist) our own fists drawn on the hands; layered in front of the
/// body (the hands over it) or behind it; frame F's flash moved to the new muzzle; the
/// canvas grown to fit, the offsets moved so the body stays where the stock lump draws it.
/// Deterministic; no Godot types (the tests link it).
/// </summary>
public static class HeldWeaponComposer
{
    private static readonly ConditionalWeakTable<IndexedImage, IndexedImage> _mirrored = [];

    /// <summary>
    /// The composite of <paramref name="lump"/> (its pose <paramref name="pose"/>) holding
    /// <paramref name="weapon"/>, or with empty hands when it is null (the fist), the weapon
    /// stretched along its axis by <paramref name="along"/> (default the pose's
    /// foreshortening).
    /// </summary>
    public static IndexedImage Compose(IndexedImage lump, HoldPose pose, HeldWeapon? weapon, double? along = null) =>
        ComposeDetailed(lump, pose, weapon, along).Image;

    /// <inheritdoc cref="Compose"/>
    public static HeldComposite ComposeDetailed(IndexedImage lump, HoldPose pose, HeldWeapon? weapon, double? along = null)
    {
        double stretch = along ?? pose.Foreshortening;
        ArgumentNullException.ThrowIfNull(lump);
        ArgumentNullException.ThrowIfNull(pose);
        int w = lump.Width, h = lump.Height;
        if (pose.Rifle.Length != w * h)
            throw new ArgumentException($"{pose.Lump}: the pose is for another image.", nameof(pose));

        // 2. Place the weapon on a canvas grown by its reach on every side.
        IndexedImage? drawn = null;
        int pad = 0;
        (double X, double Y) muzzle = pose.Muzzle;
        if (weapon is not null)
        {
            IndexedImage image = weapon.Image;
            (double X, double Y) grip = weapon.Grip, tip = weapon.Muzzle;
            // The rear hand takes the weapon's grip; the axis runs through the forward hand
            // where one shows, else towards the stock muzzle.
            (double X, double Y) rear = pose.Hands.Length > 0 ? pose.Hands[0] : pose.Grip;
            (double X, double Y)? forward = pose.Hands.Length > 1 ? pose.Hands[1] : null;
            (double X, double Y) aim = forward ?? pose.Muzzle;
            double dx = aim.X - rear.X, dy = aim.Y - rear.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (!(d > 0))
                (dx, dy, d) = (1, 0, 1);
            if (dx < 0)
            {
                // Mirrored, not turned upside down: the handle stays under the barrel.
                image = _mirrored.GetValue(image, Mirror);
                grip = (image.Width - grip.X, grip.Y);
                tip = (image.Width - tip.X, tip.Y);
            }
            // Across the chest (the walking and pain frames A–D, G) the hand closes round the
            // weapon's body at its grip; aiming (E, F) it holds the handle under it.
            if (pose.Lump.Length > 4 && pose.Lump[4] is not ('E' or 'F'))
                grip = HoldPoint(image, grip);
            double reach = Math.Sqrt((tip.X - grip.X) * (tip.X - grip.X) + (tip.Y - grip.Y) * (tip.Y - grip.Y)) * weapon.Scale;
            // A two-handed weapon reaches past the forward hand (the one-handed pistol need not).
            if (forward is not null && weapon.Weapon != Sim.weapontype_t.wp_pistol && reach > 0)
                stretch = Math.Max(stretch, (d + 2) / reach);
            double diagonal = Math.Sqrt((double)image.Width * image.Width + (double)image.Height * image.Height) * weapon.Scale * Math.Max(1, stretch);
            pad = (int)Math.Ceiling(diagonal) + 2;
            (double X, double Y) from = (rear.X + pad, rear.Y + pad);
            (double X, double Y) to = (from.X + dx / d * reach, from.Y + dy / d * reach);
            drawn = PixelArt.Transform(image, grip, tip, from, to, stretch, w + 2 * pad, h + 2 * pad);
            if (stretch > 1)
            {
                // Stretched (the hands wider apart than in the side view): only ahead of the
                // grip, so the stock behind the hand keeps its length.
                IndexedImage back = PixelArt.Transform(image, grip, tip, from, to, 1, w + 2 * pad, h + 2 * pad);
                drawn = Join(drawn, back, from, (dx / d, dy / d));
            }
            (double X, double Y) m = PixelArt.Map(tip, grip, tip, from, to, stretch);
            muzzle = (m.X - pad, m.Y - pad);
        }

        // 1. Remove the rifle and the flash (and with empty hands in a front pose, the open
        // hands, which fists replace; with a weapon in front, the fingers that wrapped the
        // stock rifle where the weapon is not); fill the holes inside the body.
        bool fists = weapon is null && pose.Layer == HoldLayer.Front;
        bool[]? held = null;
        if (drawn is not null && pose.Layer == HoldLayer.Front)
        {
            held = new bool[w * h];
            for (int i = 0; i < held.Length; i++)
                held[i] = drawn.Opaque[(i / w + pad) * drawn.Width + i % w + pad] != 0;
        }
        (byte[] bodyPixels, CompositeTexel[] bodySources) = RemoveRifle(lump, pose, fists, held);
        bool[]? mainBody = fists ? KeepMainBody(bodySources, w, h) : null;

        if (fists)
            pad = _fistRows.Length;
        int cw = w + 2 * pad, ch = h + 2 * pad;
        byte[] pixels = new byte[cw * ch];
        var sources = new CompositeTexel[cw * ch];

        void Put(int x, int y, byte index, CompositeTexel source)
        {
            if (x < 0 || y < 0 || x >= cw || y >= ch)
                return;
            pixels[y * cw + x] = index;
            sources[y * cw + x] = source;
        }

        void DrawBody(bool overOnly)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (bodySources[i] == CompositeTexel.None || (overOnly && !pose.Over[i]))
                        continue;
                    Put(x + pad, y + pad, bodyPixels[i], bodySources[i]);
                }
        }

        void DrawWeapon()
        {
            if (drawn is null)
                return;
            for (int i = 0; i < drawn.Pixels.Length; i++)
            {
                if (drawn.Opaque[i] != 0)
                    Put(i % cw, i / cw, drawn.Pixels[i], CompositeTexel.Weapon);
            }
        }

        // 3. Layer.
        if (pose.Layer == HoldLayer.Front)
        {
            DrawBody(false);
            DrawWeapon();
            if (drawn is not null)
                DrawBody(true);
        }
        else
        {
            DrawWeapon();
            DrawBody(false);
        }

        if (fists)
            DrawFists(pose, bodySources, w, pad, Put, (x, y) => x >= pad && y >= pad && x < w + pad && y < h + pad && mainBody![(y - pad) * w + x - pad]);

        // 4. Frame F's flash, moved so it keeps its place relative to the muzzle, then back
        // towards the grip a texel at a time (at most 4) until it touches the weapon: a flash a
        // texel off a muzzle whose end is pale (the chaingun's barrels) reads as a spark.
        // The chainsaw has no muzzle flash: its frame F is the flash removed and filled, as
        // the fist's (the frame stays full-bright).
        if (weapon is not null && weapon.Weapon != Sim.weapontype_t.wp_chainsaw && Array.IndexOf(pose.Flash, true) >= 0)
        {
            int sx = (int)Math.Round(muzzle.X - pose.Muzzle.X, MidpointRounding.ToEven);
            int sy = (int)Math.Round(muzzle.Y - pose.Muzzle.Y, MidpointRounding.ToEven);
            double ax = pose.Grip.X - muzzle.X, ay = pose.Grip.Y - muzzle.Y;
            double al = Math.Sqrt(ax * ax + ay * ay);
            bool Touches(int ox, int oy)
            {
                for (int i = 0; i < pose.Flash.Length; i++)
                {
                    if (!pose.Flash[i])
                        continue;
                    int fx = i % w + pad + ox, fy = i / w + pad + oy;
                    for (int ny = fy - 1; ny <= fy + 1; ny++)
                        for (int nx = fx - 1; nx <= fx + 1; nx++)
                            if (nx >= 0 && ny >= 0 && nx < cw && ny < ch && sources[ny * cw + nx] == CompositeTexel.Weapon)
                                return true;
                }
                return false;
            }
            if (al > 0)
            {
                for (int step = 0; step <= 4; step++)
                {
                    int ox = sx + (int)Math.Round(step * ax / al, MidpointRounding.ToEven);
                    int oy = sy + (int)Math.Round(step * ay / al, MidpointRounding.ToEven);
                    if (Touches(ox, oy))
                    {
                        (sx, sy) = (ox, oy);
                        break;
                    }
                }
            }
            for (int i = 0; i < pose.Flash.Length; i++)
            {
                if (pose.Flash[i])
                    Put(i % w + pad + sx, i / w + pad + sy, lump.Pixels[i], CompositeTexel.Flash);
            }
        }

        // 5. Crop to what is drawn; the offsets keep the stock lump's origin.
        int x0 = cw, y0 = ch, x1 = -1, y1 = -1;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == CompositeTexel.None)
                continue;
            int x = i % cw, y = i / cw;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }
        if (x1 < 0)
            (x0, y0, x1, y1) = (pad, pad, pad - 1, pad - 1);
        int ow = x1 - x0 + 1, oh = y1 - y0 + 1;
        byte[] outPixels = new byte[ow * oh], outOpaque = new byte[ow * oh];
        var outSources = new CompositeTexel[ow * oh];
        var outWeapon = new bool[ow * oh];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int i = (y + y0) * cw + x + x0, o = y * ow + x;
                outPixels[o] = pixels[i];
                outSources[o] = sources[i];
                outOpaque[o] = sources[i] == CompositeTexel.None ? (byte)0 : (byte)1;
                outWeapon[o] = drawn is not null && drawn.Opaque[i] != 0;
            }
        int shiftX = pad - x0, shiftY = pad - y0;
        var result = new IndexedImage(ow, oh, lump.LeftOffset + shiftX, lump.TopOffset + shiftY, outPixels, outOpaque);
        return new HeldComposite(result, shiftX, shiftY, outSources, outWeapon);
    }

    /// <summary>
    /// The lump with the pose's rifle and flash removed: each removed texel inside the
    /// body's silhouette (body texels on both sides of it along its row or its column) filled
    /// from its filled or body 8-neighbours, ring by ring, with the most common palette index
    /// among them (edge neighbours count twice, diagonal ones once; the lowest index on a
    /// tie); the others cleared. With <paramref name="hands"/> (empty hands) the pose's hands go
    /// too, and the hand-coloured texels beside the rifle; with <paramref name="held"/> (a front
    /// weapon's coverage) those of the latter it does not cover.
    /// </summary>
    public static (byte[] Pixels, CompositeTexel[] Sources) RemoveRifle(IndexedImage lump, HoldPose pose, bool hands = false, bool[]? held = null)
    {
        ArgumentNullException.ThrowIfNull(lump);
        ArgumentNullException.ThrowIfNull(pose);
        int w = lump.Width, h = lump.Height;
        byte[] pixels = (byte[])lump.Pixels.Clone();
        var sources = new CompositeTexel[w * h];
        var hole = new bool[w * h];
        for (int i = 0; i < w * h; i++)
        {
            hole[i] = lump.Opaque[i] != 0 && (pose.Rifle[i] || pose.Flash[i] || (hands && pose.Over[i]));
        }
        if (hands || held is not null)
        {
            // With empty hands the fingers wrapped round the rifle go too: hand-coloured
            // texels beside the rifle (filled inside the body, cleared outside it); with a
            // weapon (`held`, the texels it covers) those the weapon does not run under.
            var near = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                if (hole[i] || lump.Opaque[i] == 0 || !HoldPoses.IsHandColour(lump.Pixels[i]))
                    continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1 && !near[i]; dy++)
                    for (int dx = -1; dx <= 1 && !near[i]; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        near[i] = nx >= 0 && ny >= 0 && nx < w && ny < h && pose.Rifle[ny * w + nx];
                    }
            }
            for (int i = 0; i < w * h; i++)
                hole[i] |= near[i] && (held is null || !held[i]);
        }
        for (int i = 0; i < w * h; i++)
        {
            if (lump.Opaque[i] != 0 && !hole[i])
                sources[i] = CompositeTexel.Body;
        }
        bool IsBody(int x, int y) => sources[y * w + x] == CompositeTexel.Body;

        var inside = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!hole[y * w + x])
                    continue;
                bool left = false, right = false, up = false, down = false;
                for (int k = x - 1; k >= 0 && !left; k--)
                    left = IsBody(k, y);
                for (int k = x + 1; k < w && !right; k++)
                    right = IsBody(k, y);
                for (int k = y - 1; k >= 0 && !up; k--)
                    up = IsBody(x, k);
                for (int k = y + 1; k < h && !down; k++)
                    down = IsBody(x, k);
                inside[y * w + x] = (left && right) || (up && down);
            }

        Span<int> votes = stackalloc int[256];
        var known = new bool[w * h];
        for (int i = 0; i < w * h; i++)
            known[i] = sources[i] == CompositeTexel.Body;
        var ring = new System.Collections.Generic.List<(int I, byte Index)>();
        while (true)
        {
            ring.Clear();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!inside[i] || known[i])
                        continue;
                    votes.Clear();
                    bool any = false;
                    // The arms' and hands' colours and the blacks (shadows, outlines) vote only
                    // where nothing else does, so a hole across the chest fills with the torso,
                    // not with the arm or the shadow beside it.
                    for (int pass = 0; pass < 2 && !any; pass++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h || !known[ny * w + nx])
                                    continue;
                                byte c = pixels[ny * w + nx];
                                if (pass == 0 && (HoldPoses.IsHandColour(c) || c <= 7))
                                    continue;
                                votes[c] += dx == 0 || dy == 0 ? 2 : 1;
                                any = true;
                            }
                    }
                    if (!any)
                        continue;
                    int best = 0;
                    for (int c = 1; c < 256; c++)
                    {
                        if (votes[c] > votes[best])
                            best = c;
                    }
                    ring.Add((i, (byte)best));
                }
            if (ring.Count == 0)
                break;
            foreach ((int i, byte index) in ring)
            {
                pixels[i] = index;
                known[i] = true;
                sources[i] = CompositeTexel.Fill;
            }
        }
        for (int i = 0; i < w * h; i++)
        {
            if (sources[i] == CompositeTexel.None)
                pixels[i] = 0;
        }
        return (pixels, sources);
    }

    // Our own art (GPL-2.0-or-later, made for IsoDoom; no id pixels): a gloved fist, lit on
    // top, the fingers' crease across, in the marine's glove browns (PLAYPAL 0x41–0x4F, outside the
    // players' green ramp). '.' is transparent.
    private static readonly string[] _fistRows =
    [
        ".xxx.",
        "xhhmx",
        "xkkkx",
        "xmmdx",
        ".xxx.",
    ];

    private static byte FistColour(char c) => c switch
    {
        'x' => 0x4f, // outline
        'h' => 0x41, // lit knuckles
        'm' => 0x45, // glove
        'k' => 0x4a, // the fingers' crease
        _ => 0x4c,   // shade
    };

    /// <summary>T6.14c: the fist art (5 × 5), offsets 0.</summary>
    public static IndexedImage Fist()
    {
        int fw = _fistRows[0].Length, fh = _fistRows.Length;
        byte[] pixels = new byte[fw * fh], opaque = new byte[fw * fh];
        for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
            {
                if (_fistRows[y][x] == '.')
                    continue;
                pixels[y * fw + x] = FistColour(_fistRows[y][x]);
                opaque[y * fw + x] = 1;
            }
        return new IndexedImage(fw, fh, 0, 0, pixels, opaque);
    }

    // A fist on each hand (the pose's hand points, or without them each 8-connected piece of
    // its Over of 3 texels or more), centred on it, then moved towards the body's centre a texel at a time (at most 16) until
    // one of its texels is beside or on the body: hands that held the rifle away from the body (G2) come back to the
    // arms' ends.
    private static void DrawFists(HoldPose pose, CompositeTexel[] body, int w, int pad,
        Action<int, int, byte, CompositeTexel> put, Func<int, int, bool> isBody)
    {
        double bx = 0, by = 0;
        int bn = 0;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == CompositeTexel.None)
                continue;
            bx += i % w + 0.5;
            by += i / w + 0.5;
            bn++;
        }
        if (bn == 0)
            return;
        (bx, by) = (bx / bn, by / bn);
        int fw = _fistRows[0].Length, fh = _fistRows.Length;
        var centres = new System.Collections.Generic.List<(double X, double Y)>(pose.Hands);
        if (centres.Count == 0)
        {
            foreach (System.Collections.Generic.List<int> piece in Pieces(pose.Over, w))
            {
                double sx = 0, sy = 0;
                foreach (int i in piece)
                {
                    sx += i % w + 0.5;
                    sy += i / w + 0.5;
                }
                centres.Add((sx / piece.Count, sy / piece.Count));
            }
        }
        foreach ((double cx, double cy) in centres)
        {
            double vx = bx - cx, vy = by - cy, vl = Math.Sqrt(vx * vx + vy * vy);
            int left = 0, top = 0;
            for (int step = 0; step <= 16; step++)
            {
                double px = cx + (vl > 0 ? step * vx / vl : 0), py = cy + (vl > 0 ? step * vy / vl : 0);
                left = (int)Math.Floor(px - fw / 2.0 + 0.5) + pad;
                top = (int)Math.Floor(py - fh / 2.0 + 0.5) + pad;
                bool touches = false;
                for (int y = 0; y < fh && !touches; y++)
                    for (int x = 0; x < fw && !touches; x++)
                    {
                        int fx = left + x, fy = top + y;
                        touches = _fistRows[y][x] != '.'
                            && (isBody(fx, fy) || isBody(fx - 1, fy) || isBody(fx + 1, fy) || isBody(fx, fy - 1) || isBody(fx, fy + 1));
                    }
                if (touches)
                    break;
            }
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    if (_fistRows[y][x] != '.')
                        put(left + x, top + y, FistColour(_fistRows[y][x]), CompositeTexel.Fist);
                }
        }
    }

    /// <summary>
    /// Where the hand holds <paramref name="image"/>: the middle of its opaque texels in the
    /// grip's column (the weapon's body there, not the bottom of its handle), at the grip's x.
    /// </summary>
    public static (double X, double Y) HoldPoint(IndexedImage image, (double X, double Y) grip)
    {
        ArgumentNullException.ThrowIfNull(image);
        int x = Math.Clamp((int)Math.Floor(grip.X), 0, image.Width - 1);
        int top = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
        {
            if (image.Opaque[y * image.Width + x] == 0)
                continue;
            if (top < 0)
                top = y;
            bottom = y;
        }
        return top < 0 ? grip : (grip.X, (top + bottom + 1) / 2.0);
    }

    /// <summary>The 8-connected pieces of <paramref name="mask"/> of 3 texels or more, each as its texel indices, in scan order.</summary>
    public static System.Collections.Generic.List<System.Collections.Generic.List<int>> Pieces(bool[] mask, int width)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var pieces = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
        int height = mask.Length / width;
        var seen = new bool[mask.Length];
        var stack = new System.Collections.Generic.Stack<int>();
        for (int s = 0; s < mask.Length; s++)
        {
            if (!mask[s] || seen[s])
                continue;
            var piece = new System.Collections.Generic.List<int>();
            seen[s] = true;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                piece.Add(i);
                for (int ny = i / width - 1; ny <= i / width + 1; ny++)
                    for (int nx = i % width - 1; nx <= i % width + 1; nx++)
                    {
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height || seen[ny * width + nx] || !mask[ny * width + nx])
                            continue;
                        seen[ny * width + nx] = true;
                        stack.Push(ny * width + nx);
                    }
            }
            if (piece.Count >= 3)
                pieces.Add(piece);
        }
        return pieces;
    }

    // With empty hands: the body's largest 8-connected piece, which the fists are moved to
    // touch; the pieces of fewer than 8 texels left over from the hands and fingers that held
    // the rifle are cleared.
    private static bool[] KeepMainBody(CompositeTexel[] body, int w, int h)
    {
        var label = new int[w * h];
        var sizes = new System.Collections.Generic.List<int> { 0 };
        var stack = new System.Collections.Generic.Stack<int>();
        for (int s = 0; s < w * h; s++)
        {
            if (body[s] == CompositeTexel.None || label[s] != 0)
                continue;
            int id = sizes.Count, size = 0;
            label[s] = id;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                size++;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = i % w + dx, ny = i / w + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || label[ny * w + nx] != 0 || body[ny * w + nx] == CompositeTexel.None)
                            continue;
                        label[ny * w + nx] = id;
                        stack.Push(ny * w + nx);
                    }
            }
            sizes.Add(size);
        }
        int main = 0;
        for (int id = 1; id < sizes.Count; id++)
        {
            if (sizes[id] > sizes[main])
                main = id;
        }
        var mask = new bool[w * h];
        for (int i = 0; i < w * h; i++)
        {
            mask[i] = label[i] == main && main != 0;
            if (label[i] != 0 && sizes[label[i]] < 8)
                body[i] = CompositeTexel.None;
        }
        return mask;
    }

    // `front`'s texels ahead of the line through `from` across `axis`, `back`'s behind it.
    private static IndexedImage Join(IndexedImage front, IndexedImage back, (double X, double Y) from, (double X, double Y) axis)
    {
        int w = front.Width;
        byte[] pixels = new byte[front.Pixels.Length], opaque = new byte[front.Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            double t = (i % w + 0.5 - from.X) * axis.X + (i / w + 0.5 - from.Y) * axis.Y;
            IndexedImage source = t >= 0 ? front : back;
            pixels[i] = source.Pixels[i];
            opaque[i] = source.Opaque[i];
        }
        return new IndexedImage(w, front.Height, 0, 0, pixels, opaque);
    }

    private static IndexedImage Mirror(IndexedImage m)
    {
        byte[] pixels = new byte[m.Pixels.Length], opaque = new byte[m.Pixels.Length];
        for (int y = 0; y < m.Height; y++)
            for (int x = 0; x < m.Width; x++)
            {
                int from = y * m.Width + m.Width - 1 - x;
                pixels[y * m.Width + x] = m.Pixels[from];
                opaque[y * m.Width + x] = m.Opaque[from];
            }
        return new IndexedImage(m.Width, m.Height, m.Width - m.LeftOffset, m.TopOffset, pixels, opaque);
    }
}
