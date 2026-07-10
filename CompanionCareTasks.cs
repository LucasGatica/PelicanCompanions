using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace PelicanCompanions;

/// <summary>
/// Conservative, instant companion actions which delegate the actual world
/// mutation to Stardew Valley's public crop and animal APIs.
/// </summary>
public sealed partial class ModEntry
{
    private bool TryHarvestTile(GameLocation location, Vector2 rawTile, SquadMemberState member, bool manual)
    {
        if (!this.AreTaskActionsSafe() || this.config.HarvestingMode == TaskMode.Disabled)
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

        if (this.IsProtectedBeeHouseFlower(location, tile, crop))
        {
            this.SetTaskFailure(member, "companion.task_failure.bee_flower_protected");
            if (manual)
                this.Warn("tasks.bee_flower_protected");

            return manual;
        }

        NPC? npc = this.GetNpcByName(member.NpcName);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (npc is null || owner is null || npc.currentLocation != location || owner.currentLocation != location)
            return false;

        // Crop.harvest applies vanilla quality, professions, regrowth, stats,
        // sounds, quests, and item spawning. The return value tells the caller
        // whether the one-shot crop should be removed from its dirt.
        bool removeCrop = crop.harvest((int)tile.X, (int)tile.Y, dirt);
        if (removeCrop)
            dirt.crop = null;

        bool harvested = removeCrop || !dirt.readyForHarvest();
        if (!harvested)
            return false;

        this.PositionNpcForInstantTask(npc, location, tile, member);
        this.FaceTile(npc, tile);
        this.ShowCompanionWorkSignal(npc, location, tile, "harvest");
        this.Say(npc, "Harvesting", force: false);
        this.AddCompanionXp(member, 3);
        this.SetTaskResult(member, "companion.task_result.harvested");
        this.InvalidateTargetPreviews();

        if (manual)
            this.Info("tasks.harvested", new { npc = member.DisplayName });

        return true;
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
        if (!this.AreTaskActionsSafe() || this.config.PettingMode == TaskMode.Disabled)
            return false;

        Vector2 tile = NormalizeTile(rawTile);
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        NPC? npc = this.GetNpcByName(member.NpcName);
        if (owner is null || npc is null || owner.currentLocation != location || npc.currentLocation != location)
            return false;

        FarmAnimal? animal = location.Animals.Values
            .Where(candidate => !candidate.wasPet.Value)
            .Where(candidate => !excludedAnimalId.HasValue || candidate.myID.Value != excludedAnimalId.Value)
            .Where(candidate => Vector2.Distance(NormalizeTile(candidate.Tile), tile) <= 1.5f)
            .Where(candidate => IsWithinCompanionDistance(owner.Tile, candidate.Tile))
            .OrderBy(candidate => Vector2.Distance(NormalizeTile(npc.Tile), NormalizeTile(candidate.Tile)))
            .FirstOrDefault();
        if (animal is null)
            return false;

        // Revalidate immediately before the mutation so two companions can't
        // pet the same animal during the same scan.
        if (animal.wasPet.Value || animal.currentLocation != location)
            return false;

        animal.pet(owner);
        this.PositionNpcForInstantTask(npc, location, animal.Tile, member);
        this.FaceTile(npc, animal.Tile);
        this.ShowCompanionWorkSignal(npc, location, animal.Tile, "pet");
        this.Say(npc, "Petting", force: false);
        this.AddCompanionXp(member, 2);
        this.SetTaskResult(member, "companion.task_result.petted");

        if (manual)
            this.Info("tasks.petted", new { npc = member.DisplayName, animal = animal.displayName });

        return true;
    }

    private bool TryPetNearestAnimal(GameLocation location, SquadMemberState member, long? excludedAnimalId = null)
    {
        Farmer? owner = this.GetOwnerFarmer(member.OwnerId);
        if (owner is null || owner.currentLocation != location)
            return false;

        foreach (FarmAnimal animal in location.Animals.Values
            .Where(candidate => !candidate.wasPet.Value)
            .Where(candidate => !excludedAnimalId.HasValue || candidate.myID.Value != excludedAnimalId.Value)
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
        if (!this.AreTaskActionsSafe() || !this.AreTasksEnabled(Game1.player.UniqueMultiplayerID))
            return;

        SquadMemberState? member = this.GetAvailableLocalMember();
        if (member is null)
            return;

        GameLocation location = Game1.currentLocation;
        Vector2 actionTile = NormalizeTile(cursor.GrabTile);

        if (this.config.HarvestingMode == TaskMode.Mimicking)
        {
            HoeDirt? aimedDirt = location.GetHoeDirtAtTile(actionTile);
            if (aimedDirt?.crop is not null && aimedDirt.readyForHarvest())
            {
                foreach (Vector2 tile in this.GetNearbyTiles(Game1.player.Tile, MaxCompanionDistanceTiles)
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
