using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace PelicanCompanions;

/// <summary>A path controller with explicit control over Stardew's off-screen completion shortcut.</summary>
/// <remarks>
/// The base controller moves a non-schedule NPC directly to its endpoint when
/// its location has no farmers. That can happen for a few frames while an owner
/// changes maps, producing a visible jump before the companion is transferred.
/// Follow, recall, and manual tasks pause until the map is observed again.
/// Host-owned routine work may opt into the shortcut so a remote scheduled
/// block can continue without requiring its owner to visit the destination.
/// </remarks>
internal sealed class CompanionPathFindController : PathFindController
{
    private readonly bool allowOffscreenCompletion;

    public CompanionPathFindController(
        Character character,
        GameLocation location,
        Point endPoint,
        int finalFacingDirection,
        bool allowOffscreenCompletion = false)
        : base(character, location, endPoint, finalFacingDirection, clearMarriageDialogues: false)
    {
        this.allowOffscreenCompletion = allowOffscreenCompletion;
    }

    public override bool update(GameTime time)
    {
        // The base controller checks completion before applying its off-screen
        // shortcut. Preserve that order so an already-finished path is detached
        // even when the last farmer has just left this location.
        if (this.pathToEndPoint is null || this.pathToEndPoint.Count == 0)
            return true;

        if (!this.allowOffscreenCompletion
            && !this.NPCSchedule
            && !this.location.farmers.Any())
        {
            return false;
        }

        return base.update(time);
    }
}
