using System.Numerics;

namespace OpenXt.XArchive;

/// <summary>
/// A surface definition. All three archive variants (<c>MATERIAL</c>, <c>MATERIAL2</c>,
/// <c>MATERIAL3</c>) share the same first eleven fields and differ only in trailing extras,
/// so they parse into this one shape.
/// </summary>
/// <param name="Index">Material slot referenced by a face's first field.</param>
/// <param name="TextureId">Texture number, resolving to <c>tex/true/&lt;id&gt;.jpg</c>.</param>
public sealed record BodMaterial(
    int Index,
    int TextureId,
    Vector3 Ambient,
    Vector3 Diffuse,
    Vector3 Specular)
{
    /// <summary>Colour channels are 0-255 in the file; these are normalised to 0-1.</summary>
    public static Vector3 Colour(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
}

/// <summary>
/// One polygon. Triangles dominate; a handful of quads exist (39 across both games) and are
/// triangulated by <see cref="BodParser"/> callers via <see cref="Triangulate"/>.
/// </summary>
public sealed class BodFace
{
    /// <summary>Material slot. Occasionally negative in the archives, meaning "no material".</summary>
    public int MaterialIndex { get; init; }

    /// <summary>Three or four indices into the owning body's vertex list.</summary>
    public required int[] VertexIndices { get; init; }

    /// <summary>
    /// The record's type marker: one of -25, -57, -17, -9 or -1. It determines whether flags and
    /// UVs follow, but we derive that from the field count instead, which is equivalent and
    /// tolerant of markers we have not seen.
    /// </summary>
    public int Marker { get; init; }

    /// <summary>Power-of-two bitmask, absent on -9 and -1 records. Carried through, not yet acted on.</summary>
    public int? Flags { get; init; }

    /// <summary>One UV per vertex, or null for untextured faces. Values routinely fall outside [0,1].</summary>
    public Vector2[]? Uvs { get; init; }

    public bool IsQuad => VertexIndices.Length == 4;

    /// <summary>Yields (a, b, c) vertex-slot triples — one for a triangle, two for a quad.</summary>
    public IEnumerable<(int A, int B, int C)> Triangulate()
    {
        yield return (0, 1, 2);
        if (IsQuad)
            yield return (0, 2, 3);
    }
}

/// <summary>
/// A named group of faces. Parts carry the model's structure (hull, cockpit, glow, turret mounts);
/// the name comes from the <c>/----- Part N: (…)"Name"</c> comment that precedes the group.
/// </summary>
public sealed class BodPart
{
    public string? Name { get; set; }
    public List<BodFace> Faces { get; } = [];

    /// <summary>The 16-bit mask on the part's terminating <c>-99;</c> line.</summary>
    public int TerminatorFlags { get; set; }
}

/// <summary>
/// One body: a vertex pool plus the parts that index into it. A single <c>.pbd</c> file holds
/// several of these, which read as decreasing levels of detail (for example the Argon M3 has four,
/// at 218 / 297 / 161 / 69 vertices).
/// </summary>
public sealed class BodBody
{
    public List<Vector3> Vertices { get; } = [];
    public List<BodPart> Parts { get; } = [];

    /// <summary>The optional standalone "Automatic Object Size" line preceding the vertex list.</summary>
    public int? SizeHint { get; set; }

    public int FaceCount
    {
        get
        {
            int n = 0;
            foreach (BodPart part in Parts)
                n += part.Faces.Count;
            return n;
        }
    }
}

/// <summary>A parsed <c>.pbd</c> body file. Materials are declared once and shared by every body.</summary>
public sealed class BodFile
{
    /// <summary>Leading comment text, usually the model's name ("Argon M3") and its 3ds source path.</summary>
    public string? Title { get; set; }

    public List<BodMaterial> Materials { get; } = [];
    public List<BodBody> Bodies { get; } = [];

    /// <summary>Materials are sparse and occasionally negative, so look them up rather than indexing.</summary>
    public BodMaterial? FindMaterial(int index)
    {
        foreach (BodMaterial material in Materials)
        {
            if (material.Index == index)
                return material;
        }

        return null;
    }
}
