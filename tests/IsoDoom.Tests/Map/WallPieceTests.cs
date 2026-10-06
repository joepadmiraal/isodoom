using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Map;

/// <summary>
/// T2.9: wall quads per seg on the floor's corners (<see cref="WallPieces"/>,
/// <see cref="FloorTriangles.SegChains"/>), so no crack opens between a wall
/// and the floor it stands on (or, for a lower wall, the back floor it meets).
/// </summary>
public class WallPieceTests
{
    private static PolygonVertex V(int x, int y) => new(x << FRACBITS, y << FRACBITS);

    private static (Level, FloorTriangles, WallPieces) Build(Level level)
    {
        FloorTriangles floors = FloorTriangles.Build(level, SubsectorPolygons.Build(level));
        return (level, floors, WallPieces.Build(level, floors));
    }

    private static Level LoadSynthetic() =>
        Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M1");

    [Fact]
    public void IntegerSquareRootRounds()
    {
        Assert.Equal(0, WallPieces.ISqrt(0));
        Assert.Equal(1, WallPieces.ISqrt(1));
        Assert.Equal(1, WallPieces.ISqrt(2)); // 1.41
        Assert.Equal(2, WallPieces.ISqrt(3)); // 1.73
        Assert.Equal(5, WallPieces.ISqrt(25));
        Assert.Equal(5, WallPieces.ISqrt(30)); // 5.48
        Assert.Equal(6, WallPieces.ISqrt(31)); // 5.57
        Assert.Equal((Int128)1 << 40, WallPieces.ISqrt((UInt128)1 << 80));
        // A 3-4-5 seg of 3 × 2^32 by 4 × 2^32: exactly 5 × 2^32.
        Assert.Equal((Int128)5 << 32, WallPieces.ISqrt((UInt128)25 << 64));
    }

    [Fact]
    public void SyntheticMapPiecesFollowTheSegs()
    {
        (Level level, FloorTriangles floors, WallPieces pieces) = Build(LoadSynthetic());
        CheckPieces(level, floors, pieces, out int onFloor, out int total);
        // Every axis-aligned seg of the synthetic map is its own floor edge: the pieces are the segs.
        foreach (Seg seg in level.Segs)
        {
            IReadOnlyList<WallPiece> side = pieces.Of(seg.LineDef, seg.Side);
            Assert.Contains(side, p => p.Seg == seg.Index && p.A == new PolygonVertex(seg.V1.X, seg.V1.Y) && p.ColumnA == seg.Offset);
            Assert.Contains(side, p => p.Seg == seg.Index && p.B == new PolygonVertex(seg.V2.X, seg.V2.Y));
        }
        Assert.Equal(0, pieces.Connectors);
        Assert.Equal(total, onFloor);
    }

    [Theory]
    [MemberData(nameof(FloorTriangleTests.E1Maps), MemberType = typeof(FloorTriangleTests))]
    public void Doom1WallsMeetTheFloorsWithoutCracks(string name)
    {
        (Level level, FloorTriangles floors, WallPieces pieces) = Build(Level.Load(WadArchive.Open(TestWads.RequireDoom1()), name));
        CheckPieces(level, floors, pieces, out int onFloor, out int total);
        Assert.True(onFloor >= total * 99 / 100, $"{onFloor} of {total} piece corners are floor corners");
        if (name != "E1M1")
            return;

        // Line 461 is split into three segs, at (3672, -3032), 0.4 units off the line, and (3738, -3144); the
        // wall bends with the floor (one quad per side before T2.9 left a crack along the wall's foot, seen
        // from sector 62).
        Line l461 = level.Lines[461];
        Assert.Equal(new[] { V(3584, -2880), V(3672, -3032), V(3738, -3144), V(3808, -3264) },
            pieces.Of(l461, 0).Select(p => p.A).Append(pieces.Of(l461, 0)[^1].B).ToArray());
        // Line 182's seg starts at (3136, -3072), but its subsector's floor edge only 3.7 units on (the rest
        // of the seg lies along another subsector's floor): the wall still starts at the vertex.
        Assert.Equal(V(3136, -3072), pieces.Of(level.Lines[182], 0)[0].A);
        // Texture columns are vanilla's: the seg offset plus the distance along the seg.
        // The node builder rounded the offsets (the first seg is 175.64 long, the second 130): vanilla's seams.
        Assert.Equal(new[] { 0, 176 << FRACBITS, 305 << FRACBITS }, pieces.Of(l461, 0).Select(p => p.ColumnA).ToArray());
        Assert.Equal(306 << FRACBITS, pieces.Of(l461, 0)[1].ColumnB);
    }

    [Theory]
    [MemberData(nameof(FloorTriangleTests.Doom2Maps), MemberType = typeof(FloorTriangleTests))]
    public void Doom2WallsMeetTheFloorsWithoutCracks(string name)
    {
        (Level level, FloorTriangles floors, WallPieces pieces) = Build(Level.Load(WadArchive.Open(TestWads.RequireDoom2()), name));
        CheckPieces(level, floors, pieces, out int onFloor, out int total);
        Assert.True(onFloor >= total * 98 / 100, $"{onFloor} of {total} piece corners are floor corners");
    }

    /// <summary>
    /// For every side's pieces: each belongs to a seg of the side, with that
    /// seg's fake contrast; the pieces join end to end (connector pairs
    /// aside); no corner of the front sector's floor (or the back sector's,
    /// which a lower wall meets) lies strictly inside a piece's edge (a
    /// T-junction would show as a crack along the wall's foot); and each
    /// seg's chain is a run of its subsector's ring. Counts the piece corners
    /// that are corners of the front sector's floor.
    /// </summary>
    private static void CheckPieces(Level level, FloorTriangles floors, WallPieces pieces, out int onFloor, out int total)
    {
        onFloor = total = 0;
        var floorCorners = floors.BySector.Select(f => new HashSet<PolygonVertex>(f.Vertices)).ToArray();
        var problems = new List<string>();
        foreach (Line line in level.Lines)
        {
            for (int side = 0; side < 2; side++)
            {
                IReadOnlyList<WallPiece> list = pieces.Of(line, side);
                if (line.SideNum[side] < 0)
                {
                    Assert.Empty(list);
                    continue;
                }
                Sector front = level.Sides[line.SideNum[side]].Sector;
                Sector? back = line.SideNum[side ^ 1] >= 0 ? level.Sides[line.SideNum[side ^ 1]].Sector : null;
                for (int i = 0; i < list.Count; i++)
                {
                    WallPiece p = list[i];
                    Seg seg = level.Segs[p.Seg];
                    Assert.True(seg.LineDef == line && seg.Side == side, $"line {line.Index} side {side}: piece {i} on seg {p.Seg} of another side");
                    Assert.Equal(LightTables.FakeContrast(seg.V1.X, seg.V1.Y, seg.V2.X, seg.V2.Y), p.Contrast);
                    Assert.NotEqual(p.A, p.B);
                    bool reversed = (i + 1 < list.Count && list[i + 1].A == p.B && list[i + 1].B == p.A)
                        || (i > 0 && list[i - 1].A == p.B && list[i - 1].B == p.A);
                    if (i > 0 && !reversed && list[i - 1].B != p.A && !(i > 1 && list[i - 2].A == list[i - 1].B && list[i - 2].B == list[i - 1].A))
                        problems.Add($"line {line.Index} side {side}: pieces {i - 1} and {i} don't join");
                    // A connector pair stands across the floor (between two floor edges' ends), not on its edge.
                    foreach (Sector? s in reversed ? Array.Empty<Sector?>() : new[] { front, back })
                    {
                        if (s is null)
                            continue;
                        foreach (PolygonVertex q in floors.BySector[s.Index].Vertices)
                        {
                            if (FloorTriangles.OnEdge(p.A, p.B, q))
                                problems.Add($"line {line.Index} side {side} piece {i} {p.A}→{p.B}: sector {s.Index}'s floor corner {q} lies on it");
                        }
                    }
                    if (!reversed)
                    {
                        total += 2;
                        onFloor += (floorCorners[front.Index].Contains(p.A) ? 1 : 0) + (floorCorners[front.Index].Contains(p.B) ? 1 : 0);
                    }
                }
            }
        }
        foreach (Seg seg in level.Segs)
        {
            PolygonVertex[] chain = floors.SegChains[seg.Index];
            Assert.True(chain.Length == 0 || chain.Length >= 2);
            for (int k = 0; k + 1 < chain.Length; k++)
                Assert.NotEqual(chain[k], chain[k + 1]);
        }
        Assert.True(problems.Count == 0, $"{problems.Count} problem(s):\n" + string.Join("\n", problems.Take(20)));
    }
}
