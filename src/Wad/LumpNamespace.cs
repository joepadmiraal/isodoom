namespace IsoDoom.Wad;

/// <summary>
/// The namespace a lump sits in, from the marker lumps around it (SPEC §5.1).
/// <c>S_START</c>/<c>SS_START</c> … <c>S_END</c>/<c>SS_END</c> delimit sprites,
/// <c>F_</c>/<c>FF_</c> flats and <c>P_</c>/<c>PP_</c> patches. Everything else is global.
/// </summary>
public enum LumpNamespace
{
    Global,
    Sprites,
    Flats,
    Patches,
}
