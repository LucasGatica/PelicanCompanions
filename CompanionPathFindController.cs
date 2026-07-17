using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace PelicanCompanions;

/// <summary>A path controller which never uses Stardew's off-screen teleport shortcut.</summary>
/// <remarks>
/// The base controller moves a non-schedule NPC directly to its endpoint when
/// its location has no farmers. That can happen for a few frames while an owner
/// changes maps, producing a visible jump before the companion is transferred.
/// Pausing until the location is simulated again keeps all position changes in
/// the companion movement arbiter.
/// </remarks>
internal sealed class CompanionPathFindController : PathFindController
{
    public CompanionPathFindController(Character character, GameLocation location, Point endPoint, int finalFacingDirection)
        : base(character, location, endPoint, finalFacingDirection, clearMarriageDialogues: false)
    {
    }

    public override bool update(GameTime time)
    {
        if (!this.NPCSchedule && !this.location.farmers.Any())
            return false;

        return base.update(time);
    }
}
