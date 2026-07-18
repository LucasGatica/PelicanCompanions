using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

/// <summary>
/// Conservative companion actions which approach and revalidate their target
/// before delegating the world mutation to Stardew Valley's public APIs.
/// </summary>
public sealed partial class ModEntry
{
    private bool TryHarvestTile(GameLocation location, Vector2 rawTile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe(member.OwnerId) || this.config.HarvestingMode == TaskMode.Disabled)
            return false;

        Vector2 tile = NormalizeTile(rawTile);
        if (!this.IsTileWithinOwnerRange(member, location, tile))
            return false;

        HoeDirt? dirt = location.GetHoeDirtAtTile(tile);
        Crop? crop = dirt?.crop;
        if (dirt is null
            || crop is null
            || crop.dead.Value
            || !dirt.readyForHarvest()
            || crop.GetHarvestMethod() != HarvestMethod.Grab)
        {
            return false;
        }

        // Crop.harvest is hard-wired to Game1.player in Stardew Valley 1.6.
        // Running it on the host for a farmhand would credit items, XP, and
        // professions to the wrong farmer, so fail closed until the game API
        // exposes an owner-aware harvest transaction.
        if (Context.IsMainPlayer
            && member.OwnerId != Game1.player.UniqueMultiplayerID
            && Game1.getOnlineFarmers().Count() > 1)
        {
            this.SetTaskFailure(member, "companion.task_failure.remote_harvest_unsupported");
            return manual;
        }

        if (this.IsProtectedBeeHouseFlower(location, tile, crop))
        {
            this.SetTaskFailure(member, "companion.task_failure.bee_flower_protected");
            if (manual)
                this.Warn("tasks.bee_flower_protected");

            return manual;
        }

        return this.TryQueueInstantTask(CompanionTaskKind.Harvesting, location, tile, member, manual);
    }

    private bool IsProtectedBeeHouseFlower(GameLocation location, Vector2 cropTile, Crop crop)
    {
        int radius = this.config.ProtectBeehouseFlowers;
        if (radius <= 0)
            return false;

        string? harvestItemId = crop.GetData()?.HarvestItemId;
        if (string.IsNullOrWhiteSpace(harvestItemId))
            harvestItemId = crop.indexOfHarvest.Value;

        Item? harvestItem;
        try
        {
            harvestItem = ItemRegistry.Create(harvestItemId, allowNull: true);
        }
        catch
        {
            return false;
        }

        if (harvestItem is null
            || (harvestItem.Category != SObject.flowersCategory && !harvestItem.HasContextTag("flower_item")))
        {
            return false;
        }

        foreach (Vector2 objectTile in location.Objects.Keys)
        {
            if (Vector2.Distance(NormalizeTile(cropTile), NormalizeTile(objectTile)) > radius)
                continue;

            if (!location.Objects.TryGetValue(objectTile, out SObject? obj))
                continue;

            if (obj.QualifiedItemId == "(BC)10"
                || obj.HasContextTag("bee_house")
                || obj.Name.Contains("Bee House", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryPetAnimalAtTile(GameLocation location, Vector2 rawTile, SquadMemberState member, bool manual, long? excludedAnimalId = null)
    {
        if (!this.AreTaskActionsSafe(member.OwnerId) || this.config.PettingMode == TaskMode.Disabled)
            return false;

        Vector2 tile = NormalizeTile(rawTile);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        NPC? npc = this.GetNpcByName(member.NpcName);
        if (owner is null || npc is null || owner.currentLocation != location || npc.currentLocation != location)
            return false;

        FarmAnimal? animal = location.Animals.Values
            .Where(candidate => !candidate.wasPet.Value)
            .Where(candidate => !excludedAnimalId.HasValue || candidate.myID.Value != excludedAnimalId.Value)
            .Where(candidate => !this.IsPendingAnimalTarget(candidate.myID.Value))
            .Where(candidate => Vector2.Distance(NormalizeTile(candidate.Tile), tile) <= 1.5f)
            .Where(candidate => IsWithinCompanionDistance(owner.Tile, candidate.Tile))
            .OrderBy(candidate => Vector2.Distance(NormalizeTile(npc.Tile), NormalizeTile(candidate.Tile)))
            .FirstOrDefault();
        if (animal is null)
            return false;

        if (animal.wasPet.Value || animal.currentLocation != location)
            return false;

        return this.TryQueueInstantTask(
            CompanionTaskKind.Petting,
            location,
            animal.Tile,
            member,
            manual,
            animal.myID.Value);
    }

    private bool TryPetNearestAnimal(GameLocation location, SquadMemberState member, long? excludedAnimalId = null)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.currentLocation != location)
            return false;

        foreach (FarmAnimal animal in location.Animals.Values
            .Where(candidate => !candidate.wasPet.Value)
            .Where(candidate => !excludedAnimalId.HasValue || candidate.myID.Value != excludedAnimalId.Value)
            .Where(candidate => !this.IsPendingAnimalTarget(candidate.myID.Value))
            .Where(candidate => IsWithinCompanionDistance(owner.Tile, candidate.Tile))
            .OrderBy(candidate => Vector2.Distance(NormalizeTile(owner.Tile), NormalizeTile(candidate.Tile))))
        {
            if (this.TryPetAnimalAtTile(location, animal.Tile, member, manual: false, excludedAnimalId))
                return true;
        }

        return false;
    }

    private void TryMimicAction(ICursorPosition cursor)
    {
        Vector2 actionTile = NormalizeTile(cursor.GrabTile);
        if (!Context.IsMainPlayer)
        {
            this.SendActionRequest("MimicAction", tile: actionTile);
            return;
        }

        this.TryMimicAction(Game1.player.UniqueMultiplayerID, actionTile);
    }

    private void TryMimicAction(long ownerId, Vector2 actionTile)
    {
        if (!this.AreTaskActionsSafe(ownerId) || !this.AreTasksEnabled(ownerId))
            return;

        SquadMemberState? member = this.GetAvailableMember(ownerId);
        Farmer? owner = this.GetOwnerFarmer(ownerId);
        GameLocation? location = owner?.currentLocation;
        if (member is null || owner is null || location is null)
            return;

        actionTile = NormalizeTile(actionTile);

        if (this.config.HarvestingMode == TaskMode.Mimicking)
        {
            HoeDirt? aimedDirt = location.GetHoeDirtAtTile(actionTile);
            if (aimedDirt?.crop is not null && aimedDirt.readyForHarvest())
            {
                foreach (Vector2 tile in this.GetNearbyTiles(owner.Tile, MaxCompanionDistanceTiles)
                    .Where(tile => NormalizeTile(tile) != actionTile))
                {
                    if (this.TryHarvestTile(location, tile, member, manual: false))
                        return;
                }
            }
        }

        if (this.config.PettingMode == TaskMode.Mimicking)
        {
            FarmAnimal? aimedAnimal = location.Animals.Values
                .Where(candidate => !candidate.wasPet.Value)
                .OrderBy(candidate => Vector2.Distance(NormalizeTile(candidate.Tile), actionTile))
                .FirstOrDefault(candidate => Vector2.Distance(NormalizeTile(candidate.Tile), actionTile) <= 1.5f);
            if (aimedAnimal is not null)
                this.TryPetNearestAnimal(location, member, aimedAnimal.myID.Value);
        }
    }
}
