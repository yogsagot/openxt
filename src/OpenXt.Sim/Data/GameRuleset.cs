namespace OpenXt.Sim.Data;

/// <summary>
/// One placed ship in a sector's initial state. Enough for a start position today; when saves and
/// real sector layouts arrive this grows rather than being replaced by code.
/// </summary>
public sealed record SpawnPoint
{
    /// <summary>Ship definition id, from the merged ship catalog.</summary>
    public required string Ship { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Initial orientation, radians.</summary>
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Roll { get; set; }
}

/// <summary>
/// What makes one game different from another on this engine, as data.
///
/// XBTF and X-Tension are the same simulation with different rules and content, so the differences
/// belong in a file the game package owns, not in a branch in the engine. A mod can patch this like
/// any other content — that is how a total conversion changes where the player starts without
/// forking anything.
/// </summary>
public sealed record GameRuleset
{
    /// <summary>Should match the owning package's id.</summary>
    public required string Id { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// Which converted-asset cache the renderer should open (<c>xbtf</c>, <c>xtension</c>).
    ///
    /// An opaque string as far as the simulation is concerned — it never reads it, exactly like
    /// <see cref="ShipDefinition.XbtfBodyId"/>. It lives here because it is a property of the game,
    /// and the game is defined by this file.
    /// </summary>
    public string AssetKey { get; set; } = "";

    /// <summary>Name of the sector the player starts in.</summary>
    public string StartSector { get; set; } = "Unnamed Sector";

    /// <summary>Ship definition id the player flies at the start.</summary>
    public required string PlayerShip { get; set; }

    /// <summary>Where the player starts, if the game wants somewhere other than the origin.</summary>
    public float StartX { get; set; }
    public float StartY { get; set; }
    public float StartZ { get; set; }

    /// <summary>Other ships placed in the start sector. Scenery for now; traffic and trade later.</summary>
    public IReadOnlyList<SpawnPoint>? Traffic { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Id : Title;
}
