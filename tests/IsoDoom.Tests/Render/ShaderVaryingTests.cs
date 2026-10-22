using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IsoDoom.Tests.Support;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>
/// Every shader under <c>shaders/</c> fits a Raspberry Pi 5's varyings (SPEC
/// §12 T8.4d): its V3D has 16 varying locations (<c>maxVertexOutputComponents</c>
/// 64), and Godot 4.7's scene shaders (Mobile and Forward+) take locations 0–14
/// for their built-ins (<c>base_varying_index</c> 15), so a spatial shader may
/// declare one varying of its own; a canvas item shader (base 9) seven. Godot
/// counts each varying's locations as <c>ShaderNode::Varying::get_size()</c>
/// does: one per array element, times 2, 3 or 4 for a matrix, whatever its
/// width. A shader over the limit fails to compile on the Pi and draws nothing.
/// </summary>
public partial class ShaderVaryingTests
{
    /// <summary>The varying locations a Raspberry Pi 5's V3D has (v3dv's <c>maxVertexOutputComponents</c> / 4).</summary>
    private const int DeviceLocations = 16;

    /// <summary>The locations Godot 4.7 keeps for its built-ins, per shader type (<c>actions.base_varying_index</c>).</summary>
    private static readonly Dictionary<string, int> _baseIndex = new(StringComparer.Ordinal)
    {
        ["spatial"] = 15,
        ["canvas_item"] = 9,
        ["sky"] = 10,
    };

    public static TheoryData<string> Shaders()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(ShaderDir(), "*.gdshader").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(Shaders))]
    public void FitsTheVaryingsOfARaspberryPi(string name)
    {
        string code = Expand(Path.Combine(ShaderDir(), name), []);
        Match type = ShaderType().Match(code);
        Assert.True(type.Success, $"{name}: no shader_type");
        string shaderType = type.Groups[1].Value;
        if (!_baseIndex.TryGetValue(shaderType, out int baseIndex))
        {
            Assert.DoesNotMatch(Varying(), code);
            return;
        }
        var varyings = Varying().Matches(code).Select(m => (Name: m.Groups["name"].Value, Size: Size(m))).ToList();
        int used = varyings.Sum(v => v.Size);
        Assert.True(baseIndex + used <= DeviceLocations,
            $"{name} ({shaderType}): {used} varying location(s) ({string.Join(", ", varyings.Select(v => $"{v.Name} {v.Size}"))}),"
            + $" {DeviceLocations - baseIndex} fit a Raspberry Pi 5 beside Godot's built-ins");
    }

    [Fact]
    public void CountsAsGodotDoes()
    {
        Assert.Equal(1, Size(Varying().Match("varying flat ivec4 v;")));
        Assert.Equal(1, Size(Varying().Match("varying highp vec2 v;")));
        Assert.Equal(3, Size(Varying().Match("varying smooth mat3 m;")));
        Assert.Equal(8, Size(Varying().Match("varying mat4 m[2];")));
        Assert.Equal(5, Size(Varying().Match("varying float[5] a;")));
        Assert.Equal(2, Varying().Count(Strip("// varying vec4 a;\nvarying vec4 b; /* varying vec4 c; */ varying float d;")));
        Assert.Matches(Include(), Strip("#include \"res://shaders/x.gdshaderinc\" // varying vec4 a;"));
    }

    /// <summary>A varying's locations (<c>ShaderNode::Varying::get_size()</c>).</summary>
    private static int Size(Match m)
    {
        string count = m.Groups["count"].Success ? m.Groups["count"].Value : m.Groups["count2"].Value;
        int size = count.Length > 0 ? int.Parse(count, System.Globalization.CultureInfo.InvariantCulture) : 1;
        return m.Groups["type"].Value switch
        {
            "mat2" => size * 2,
            "mat3" => size * 3,
            "mat4" => size * 4,
            _ => size,
        };
    }

    /// <summary>The shader's code, comments removed, its <c>res://</c> includes inlined (every branch of the preprocessor kept).</summary>
    private static string Expand(string path, HashSet<string> seen)
    {
        Assert.True(seen.Add(Path.GetFullPath(path)), $"{path}: included twice");
        string code = Strip(File.ReadAllText(path));
        return Include().Replace(code, m =>
        {
            string included = Path.Combine(RepoRoot(), m.Groups[1].Value);
            Assert.True(File.Exists(included), $"{path}: no {m.Groups[1].Value}");
            return Expand(included, seen);
        });
    }

    /// <summary>The code without its comments (string literals, the includes' paths, kept).</summary>
    private static string Strip(string code) => Comment().Replace(code, m => m.Value[0] == '"' ? m.Value : " ");

    private static string RepoRoot() =>
        TestWads.RepoRoot ?? throw new InvalidOperationException("the repository root was not found");

    private static string ShaderDir() => Path.Combine(RepoRoot(), "shaders");

    [GeneratedRegex(@"""[^""\n]*""|//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"^\s*#include\s+""res://([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex Include();

    [GeneratedRegex(@"\bshader_type\s+(\w+)\s*;")]
    private static partial Regex ShaderType();

    [GeneratedRegex(@"\bvarying\s+(?:(?:flat|smooth)\s+)?(?:(?:lowp|mediump|highp)\s+)?(?<type>\w+)\s*(?:\[\s*(?<count2>\d+)\s*\])?\s+(?<name>\w+)\s*(?:\[\s*(?<count>\d+)\s*\])?\s*;")]
    private static partial Regex Varying();
}
