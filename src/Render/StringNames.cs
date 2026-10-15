using System;
using System.Collections.Generic;
using Godot;

namespace IsoDoom.Render;

/// <summary>
/// T8.3: names (shader parameters, audio buses) as <see cref="StringName"/>s made once. A
/// string passed to <see cref="ShaderMaterial.SetShaderParameter"/> becomes a
/// new <see cref="StringName"/> at each call (a native string, a finalizable
/// wrapper and a weak reference for Godot's disposables tracker), garbage for
/// the collector when it happens every frame. Main thread only.
/// </summary>
public static class StringNames
{
    private static readonly Dictionary<string, StringName> _names = new(StringComparer.Ordinal);

    /// <summary>The <see cref="StringName"/> of <paramref name="name"/>, made at its first use.</summary>
    public static StringName Get(string name)
    {
        if (!_names.TryGetValue(name, out StringName? stringName))
            _names[name] = stringName = new StringName(name);
        return stringName;
    }

    /// <summary><see cref="ShaderMaterial.SetShaderParameter"/> through <see cref="Get"/>.</summary>
    public static void SetParam(this ShaderMaterial material, string name, Variant value) =>
        material.SetShaderParameter(Get(name), value);
}
