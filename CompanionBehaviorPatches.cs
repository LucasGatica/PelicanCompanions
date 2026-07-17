using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Pets;
using StardewValley.Locations;
using StardewValley.Pathfinding;

namespace PelicanCompanions;

/// <summary>Stops base-game movement systems which live outside NPC schedules.</summary>
internal static class CompanionBehaviorPatches
{
    public static Func<NPC, bool>? IsControlled { get; set; }

    public static Func<bool>? IsVanillaMovementExplicitlyAllowed { get; set; }

    public static Func<NPC, bool>? ShouldSuppressVanillaArrival { get; set; }

    public static Action<NPC>? NeutralizeVanillaBedtimeController { get; set; }

    private static bool ShouldControl(NPC npc)
    {
        return IsControlled?.Invoke(npc) == true;
    }

    private static bool ShouldAllowVanillaMovement(NPC? npc)
    {
        return npc is null
            || !ShouldControl(npc)
            || IsVanillaMovementExplicitlyAllowed?.Invoke() == true
            || Game1.newDay
            || !Game1.hasStartedDay
            || Game1.showingEndOfNightStuff
            || Game1.CurrentEvent?.actors.Contains(npc) == true;
    }

    [HarmonyPatch(
        typeof(PathFindController),
        MethodType.Constructor,
        typeof(Character),
        typeof(GameLocation),
        typeof(PathFindController.isAtEnd),
        typeof(int),
        typeof(PathFindController.endBehavior),
        typeof(int),
        typeof(Point),
        typeof(bool))]
    private static class PathControllerDialoguePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Character c, ref bool clearMarriageDialogues)
        {
            if (c is NPC npc && !ShouldAllowVanillaMovement(npc))
                clearMarriageDialogues = false;
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.RunState))]
    private static class PetRunStatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pet __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.GetCurrentPetBehavior))]
    private static class PetBehaviorLookupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Pet __instance, ref PetBehavior __result)
        {
            if (!ShouldAllowVanillaMovement(__instance))
                __result = null!;
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.behaviorOnFarmerLocationEntry))]
    private static class PetLocationEntryBehaviorPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pet __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.behaviorOnFarmerPushing))]
    private static class PetFarmerPushingBehaviorPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pet __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.OnPetPush))]
    private static class PetPushEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pet __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.returnHomeFromFarmPosition))]
    private static class SpouseReturnHomePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.setUpForOutdoorPatioActivity))]
    private static class SpousePatioPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.warpToFarmHouse))]
    private static class PetWarpHomePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pet __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(Pet), nameof(Pet.WarpToPetBowl))]
    private static class PetWarpToBowlPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pet __instance)
        {
            return ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(FarmerTeam), nameof(FarmerTeam.OnRequestPetWarpHomeEvent))]
    private static class PetWarpEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(long uid)
        {
            Farmer? farmer = Game1.GetPlayer(uid) ?? Game1.MasterPlayer;
            Pet? pet = Game1.getCharacterFromName<Pet>(farmer.getPetName(), mustBeVillager: false);
            return ShouldAllowVanillaMovement(pet);
        }
    }

    [HarmonyPatch(typeof(FarmerTeam), nameof(FarmerTeam.OnRequestSpouseSleepEvent))]
    private static class SpouseSleepEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(long uid)
        {
            Farmer? farmer = Game1.GetPlayer(uid);
            NPC? spouse = farmer is null || string.IsNullOrWhiteSpace(farmer.spouse)
                ? null
                : Game1.getCharacterFromName(farmer.spouse);
            return ShouldAllowVanillaMovement(spouse);
        }
    }

    [HarmonyPatch(typeof(FarmerTeam), nameof(FarmerTeam.OnRequestNPCGoHome))]
    private static class NpcGoHomeEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string npc_name)
        {
            return ShouldAllowVanillaMovement(Game1.getCharacterFromName(npc_name, mustBeVillager: false));
        }
    }

    [HarmonyPatch(typeof(NPC), nameof(NPC.arriveAtFarmHouse))]
    private static class FarmHouseArrivalPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(NPC __instance)
        {
            // Restore/rollback can run after the NPC has already been removed
            // from the squad. Suppression is therefore tied to the exact NPC
            // being transferred, not to membership or a process-wide flag.
            return ShouldSuppressVanillaArrival?.Invoke(__instance) != true
                && ShouldAllowVanillaMovement(__instance);
        }
    }

    [HarmonyPatch(typeof(FarmHouse), nameof(FarmHouse.performTenMinuteUpdate))]
    private static class FarmHouseBedtimePatch
    {
        [HarmonyPostfix]
        private static void Postfix(FarmHouse __instance, int timeOfDay)
        {
            if (timeOfDay < 2200 || (timeOfDay != 2200 && timeOfDay % 100 % 30 != 0))
                return;

            foreach (NPC npc in __instance.characters.ToList())
            {
                if (ShouldAllowVanillaMovement(npc))
                    continue;

                try
                {
                    NeutralizeVanillaBedtimeController?.Invoke(npc);
                }
                catch
                {
                    // Never let a compatibility guard interrupt the farmhouse's
                    // own ten-minute update. The periodic repair pass will retry.
                }
            }
        }
    }
}
