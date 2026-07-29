using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Marks an int field that is a Unity display INDEX (0-based) but must be shown to the user
    /// as the display NUMBER they see everywhere else (Game view "Display 1", Camera "Target
    /// Display 1"). Without this the 0/1 vs 1/2 mismatch is a constant trap.
    /// </summary>
    public sealed class ContentDisplayAttribute : PropertyAttribute
    {
    }
}
