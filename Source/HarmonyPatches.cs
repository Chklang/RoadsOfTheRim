﻿using HarmonyLib;
using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System;
using System.Diagnostics;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Security.Principal;

namespace RoadsOfTheRim
{
    [StaticConstructorOnStartup]
    public class HarmonyPatches
    {
        public static RoadsOfTheRimSettings settings = LoadedModManager.GetMod<RoadsOfTheRim>().GetSettings<RoadsOfTheRimSettings>();

        static HarmonyPatches()
        {
            var harmony = new Harmony("Loconeko.Rimworld.RoadsOfTheRim");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            /* How I found the hidden methods :
            var methods = typeof(Tile).GetMethods();
            foreach (var method in methods)
            {
                Log.Message(method.Name);
            }
            */

            // Initialise the list of terrains that are specific to built roads. Doing it here is hacky, but this is a quick way to use defs after they were loaded
            foreach (RoadDef thisDef in DefDatabase<RoadDef>.AllDefs)
            {
                //RoadsOfTheRim.DebugLog("initialising roadDef " + thisDef);
                if (thisDef.HasModExtension<DefModExtension_RotR_RoadDef>() && thisDef.GetModExtension<DefModExtension_RotR_RoadDef>().built) // Only add RoadDefs that are buildable, based on DefModExtension_RotR_RoadDef.built
                {
                    foreach (RoadDefGenStep_Place aStep in thisDef.roadGenSteps.OfType<RoadDefGenStep_Place>()) // Only get RoadDefGenStep_Place
                    {
                        TerrainDef t = (TerrainDef)aStep.place; // Cast the buildableDef into a TerrainDef
                        if (!RoadsOfTheRim.builtRoadTerrains.Contains(t))
                        {
                            RoadsOfTheRim.builtRoadTerrains.Add(t);
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), "GetGizmos")]
    public static class Patch_Caravan_GetGizmos
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<Gizmo> __result, Caravan __instance)
        {
            bool isThereAConstructionSiteHere = Find.WorldObjects.AnyWorldObjectOfDefAt(DefDatabase<WorldObjectDef>.GetNamed("RoadConstructionSite", true), __instance.Tile);
            bool isTheCaravanWorkingOnASite = true;
            try
            {
                isTheCaravanWorkingOnASite = __instance.GetComponent<WorldObjectComp_Caravan>().currentlyWorkingOnSite;
            }
            catch (Exception e)
            {
                RoadsOfTheRim.DebugLog(null, e);
            }
            __result = __result.Concat(new Gizmo[] { RoadsOfTheRim.AddConstructionSite(__instance) })
                               .Concat(new Gizmo[] { RoadsOfTheRim.RemoveConstructionSite(__instance.Tile) });
            if (isThereAConstructionSiteHere & !isTheCaravanWorkingOnASite && RoadsOfTheRim.RoadBuildingState.CurrentlyTargeting == null)
            {
                __result = __result.Concat(new Gizmo[] { RoadsOfTheRim.WorkOnSite(__instance) });
            }
            if (isTheCaravanWorkingOnASite)
            {
                __result = __result.Concat(new Gizmo[] { RoadsOfTheRim.StopWorkingOnSite(__instance) });
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), "GetInspectString")]
    public static class Patch_Caravan_GetInspectString
    {
        [HarmonyPostfix]
        public static void Postfix(ref string __result, Caravan __instance)
        {
            try
            {
                WorldObjectComp_Caravan CaravanComp = __instance.GetComponent<WorldObjectComp_Caravan>();
                bool isoffroad = CaravanVehiclesUtility.IsOffRoad(__instance);
                int tile = __instance.Tile;
                if (CaravanComp != null && CaravanComp.currentlyWorkingOnSite)
                {
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.Append(__result);
                    stringBuilder.AppendLine();
                    stringBuilder.Append("RoadsOfTheRim_CaravanInspectStringWorkingOn".Translate(CaravanComp.getSite().fullName(), string.Format("{0:0.00}", CaravanComp.amountOfWork())));
                    __result = stringBuilder.ToString();
                }
            }
            catch
            {
                // lazy way out : the caravan can, on occasions (mainly debug teleport, though...), not have a site linked to the comp
            }
        }

    }

    [HarmonyPatch(typeof(Alert_CaravanIdle), "GetExplanation")]
    public static class Patch_Alert_CaravanIdle_GetExplanation
    {
        [HarmonyPostfix]
        public static void Postfix(ref TaggedString __result)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                WorldObjectComp_Caravan caravanComp = caravan.GetComponent<WorldObjectComp_Caravan>();
                if (caravan.Spawned && caravan.IsPlayerControlled && !caravan.pather.MovingNow && !caravan.CantMove && !caravanComp.currentlyWorkingOnSite)
                {
                    stringBuilder.AppendLine("  - " + caravan.Label);
                }
            }
            __result = "CaravanIdleDesc".Translate(stringBuilder.ToString());
        }
    }

    [HarmonyPatch(typeof(Alert_CaravanIdle), "GetReport")]
    public static class Patch_Alert_CaravanIdle_GetReport
    {
        [HarmonyPostfix]
        public static void Postfix(ref AlertReport __result)
        {
            List<Caravan> newList = new List<Caravan>();
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                WorldObjectComp_Caravan caravanComp = caravan.GetComponent<WorldObjectComp_Caravan>();
                if (caravan.Spawned && caravan.IsPlayerControlled && !caravan.pather.MovingNow && !caravan.CantMove && !caravanComp.currentlyWorkingOnSite)
                {
                    newList.Add(caravan);
                }
            }
            __result = AlertReport.CulpritsAre(newList);
        }
    }

    [HarmonyPatch(typeof(FactionDialogMaker), "FactionDialogFor")]
    public static class Patch_FactionDialogMaker_FactionDialogFor
    {
        [HarmonyPostfix]
        public static void Postfix(ref DiaNode __result, Pawn negotiator, Faction faction)
        {
            // Allies can help build roads
            if (faction.PlayerRelationKind == FactionRelationKind.Ally)
            {
                __result.options.Insert(0, RoadsOfTheRim.HelpRoadConstruction(faction, negotiator));
            }
        }
    }

    /*
     * Patching roads so they cancel all or part of the Tile.biome.movementDifficulty and Hilliness
     * The actual rates are stored in static method RoadsOfTheRim.calculateRoadModifier
     */
    [HarmonyPatch(typeof(WorldGrid), "GetRoadMovementDifficultyMultiplier")]
    public static class Patch_WorldGrid_GetRoadMovementDifficultyMultiplier
    {
        private static readonly MethodInfo HillinessMovementDifficultyOffset = AccessTools.Method(typeof(WorldPathGrid), "HillinessMovementDifficultyOffset", new Type[] { typeof(Hilliness) });

        [HarmonyPostfix]
        public static void Postifx(ref float __result, WorldGrid __instance, ref int fromTile, ref int toTile, ref StringBuilder explanation)
        {
            List<Tile.RoadLink> roads = __instance.tiles[fromTile].Roads;
            if (roads == null)
            {
                return;
            }
            if (toTile == -1)
            {
                toTile = __instance.FindMostReasonableAdjacentTileForDisplayedPathCost(fromTile);
            }
            float BiomeModifier = 0;
            float HillModifier = 0;
            float WinterModifier = 0;

            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].neighbor == toTile)
                {
                    Tile ToTileAsTile = Find.WorldGrid[toTile];
                    float HillinessOffset = (float)HillinessMovementDifficultyOffset.Invoke(null, new object[] { ToTileAsTile.hilliness });
                    if (HillinessOffset > 12f) { HillinessOffset = 12f; }

                    // If the tile has an impassable biome, set the biomemovement difficulty to 12, as per the patch for CalculatedMovementDifficultyAt
                    float biomeMovementDifficulty = (ToTileAsTile.biome.impassable ? 12f : ToTileAsTile.biome.movementDifficulty);

                    // Calculate biome, Hillines & winter modifiers, update explanation &  multiply result by biome modifier
                    float RoadModifier = RoadsOfTheRim.calculateRoadModifier(
                        roads[i].road,
                        biomeMovementDifficulty,
                        HillinessOffset,
                        WorldPathGrid.GetCurrentWinterMovementDifficultyOffset(toTile),
                        out BiomeModifier,
                        out HillModifier,
                        out WinterModifier
                    );
                    float resultBefore = __result;
                    __result *= RoadModifier;
                    if (explanation != null)
                    {
                        explanation.AppendLine();
                        explanation.Append(String.Format(
                            "The road cancels {0:P0} of the biome ({3:##.###}), {1:P0} of the hills ({4:##.###}) & {2:P0} of winter movement costs. Total modifier={5} applied to {6}",
                            BiomeModifier, HillModifier, WinterModifier,
                            biomeMovementDifficulty, HillinessOffset, RoadModifier, resultBefore
                        ));
                    }
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(WorldPathGrid), "CalculatedMovementDifficultyAt")]
    static class Patch_WorldPathGrid_CalculatedMovementDifficultyAt
    {
        [HarmonyPostfix]
        public static void PostFix(ref float __result, int tile, bool perceivedStatic, int? ticksAbs, StringBuilder explanation)
        {
            if (__result > 999f)
            {
                try
                {
                    if (Find.WorldGrid.InBounds(tile))
                    {
                        Tile tile2 = Find.WorldGrid.tiles[tile];
                        List<Tile.RoadLink> roads = tile2.Roads;
                        if (roads?.Count > 0)
                        {
                            RoadDef BestRoad = null;
                            for (int i = 0; i < roads.Count; i++)
                            {
                                if (BestRoad == null)
                                {
                                    BestRoad = roads[i].road;
                                }
                                else
                                {
                                    if (BestRoad.movementCostMultiplier < roads[i].road.movementCostMultiplier)
                                    {
                                        BestRoad = roads[i].road;
                                    }
                                }
                            }
                            if (BestRoad != null)
                            {
                                DefModExtension_RotR_RoadDef roadDefExtension = BestRoad.GetModExtension<DefModExtension_RotR_RoadDef>();
                                if (roadDefExtension != null && ((tile2.biome.impassable && roadDefExtension.biomeModifier > 0) || (tile2.hilliness == Hilliness.Impassable)))
                                {
                                    __result = 12f;
                                    //RoadsOfTheRim.DebugLog(String.Format("[RotR] - Impassable Tile {0} of biome {1} movement difficulty patched to 12", tile , tile2.biome.label));
                                }
                            }

                        }
                    }
                    else
                    {
                        RoadsOfTheRim.DebugLog("CalculatedMovementDifficultyAt Patch - Tile out of bounds");
                    }
                }
                catch (Exception e)
                {
                    RoadsOfTheRim.DebugLog("CalculatedMovementDifficultyAt Patch - Catastrophic failure", e);
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(WorldTargeter), "StopTargeting")]
    public static class Patch_WorldTargeter_StopTargeting
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (RoadsOfTheRim.RoadBuildingState.CurrentlyTargeting != null)
            {
                RoadsOfTheRim.FinaliseConstructionSite(RoadsOfTheRim.RoadBuildingState.CurrentlyTargeting);
                RoadsOfTheRim.RoadBuildingState.CurrentlyTargeting = null;
            }
        }
    }

    [HarmonyPatch(typeof(CaravanUIUtility), "AddPawnsSections")]
    /*
    * Adds a Road equipment section to pawns & animals
    */
    public static class Patch_CaravanUIUtility_AddPawnsSections
    {
        [HarmonyPostfix]
        public static void Postfix(ref TransferableOneWayWidget widget, List<TransferableOneWay> transferables)
        {
            RoadsOfTheRim.DebugLog("DEBUG AddPawnsSection: ");
            List<TransferableOneWay> source = new List<TransferableOneWay>();
            foreach (TransferableOneWay tow in transferables)
            {
                if (tow.ThingDef.IsWithinCategory(ThingCategoryDef.Named("RoadEquipment")))
                {
                    source.Add(tow);
                }
            }
            widget.AddSection("RoadsOfTheRim_RoadEquipment".Translate(), source);
        }
    }


    [HarmonyPatch(typeof(CaravanUIUtility), "CreateCaravanTransferableWidgets")]
    //Remove Road equipments from Item tab to put them in pawns tab when forming caravans
    public static class Patch_CaravanUIUtility_CreateCaravanTransferableWidgets
    {
        [HarmonyPostfix]
        public static void Postfix(List<TransferableOneWay> transferables, ref TransferableOneWayWidget pawnsTransfer, ref TransferableOneWayWidget itemsTransfer, string thingCountTip, IgnorePawnsInventoryMode ignorePawnInventoryMass, Func<float> availableMassGetter, bool ignoreSpawnedCorpsesGearAndInventoryMass, int tile, bool playerPawnsReadOnly)
        {
            List<TransferableOneWay> modifiedTransferables = transferables.Where((TransferableOneWay x) => x.ThingDef.category != ThingCategory.Pawn).ToList();
            modifiedTransferables = modifiedTransferables.Where(x => !x.ThingDef.IsWithinCategory(ThingCategoryDef.Named("RoadEquipment"))).ToList();
            itemsTransfer = new TransferableOneWayWidget(modifiedTransferables, null, null, thingCountTip, drawMass: true, ignorePawnInventoryMass, includePawnsMassInMassUsage: false, availableMassGetter, 0f, ignoreSpawnedCorpsesGearAndInventoryMass, tile, drawMarketValue: true, drawEquippedWeapon: false, drawNutritionEatenPerDay: false, drawItemNutrition: true, drawForagedFoodPerDay: false, drawDaysUntilRot: true);
        }
    }

    [HarmonyPatch(typeof(ThingFilter), "SetFromPreset")]
    //Road equipment should be storable in a normal stockpile by default
    public static class Patch_ThingFilter_SetFromPreset
    {
        [HarmonyPostfix]
        public static void Postfix(ref ThingFilter __instance, StorageSettingsPreset preset)
        {
            if (preset == StorageSettingsPreset.DefaultStockpile)
            {
                __instance.SetAllow(ThingCategoryDef.Named("RoadEquipment"), allow: true);
            }
        }
    }

    // All Tiles can now have roads
    [HarmonyPatch(typeof(Tile), "Roads", MethodType.Getter)]
    public static class Patch_Tile_Roads
    {
        [HarmonyPostfix]
        public static void Postfix(Tile __instance, ref List<Tile.RoadLink> __result)
        {
            __result = __instance.potentialRoads;
        }
    }

    // When WorldLayer_Paths.AddPathEndPoint calls WaterCovered, it should return 1, not 0.5
    /*
     * NOT EVEN SURE THIS IS NECESSARY
    [HarmonyPatch(typeof(WorldLayer_Paths))]
    [HarmonyPatch("AddPathEndpoint")]
    public static class Patch_WorldLayer_Paths_AddPathEndpoint
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            RoadsOfTheRim.DebugLog("TRANSPILING");
            int index = -1;
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                //RoadsOfTheRim.DebugLog("Transpiler operand =" + codes[i].operand.ToStringSafe());
                if (codes[i].operand is float && (float)codes[i].operand == 0.5)
                {
                    index = i;
                    break;
                }
            }
            if (index != -1)
            {
                codes[index].operand = 1f;
                RoadsOfTheRim.DebugLog("Transpiler found 0.5 in AddPathEndPoint: " + codes[index].ToString());
            }
            return codes.AsEnumerable();
        }
    }
    */

    /*
     * ============================================================
     * 
     * Harmony Patches for Vehicles
     * 
     * ============================================================
     */
    [HarmonyPatch(typeof(CaravanFormingUtility), "AllReachableColonyItems")]
    public static class Patch_CaravanFormingUtility_AllReachableColonyItems
    {
        // TO DO : Extend that to all vehicles
        [HarmonyPostfix]
        public static void Postfix(ref List<Thing> __result, Map map)
        {
            // Put all Vehicles (Things with a ThingComp_RotR_Vehicles) in the list
            List<Thing> allThings = map.listerThings.AllThings;
            for (int i = 0; i < allThings.Count; i++)
            {
                Thing thing = allThings[i];
                if (ThingCompUtility.TryGetComp<ThingComp_RotR_Vehicles>(thing) != null)
                {
                    __result.Add(thing);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Caravan), "Material", MethodType.Getter)]
    public static class Patch_Caravan_Texture
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance, ref Material __result)
        {
            if (CaravanVehiclesUtility.TotalVehicleSpeed(__instance) >= 0)
            {
                Color PlayerCaravanColor = new Color(1f, 0.863f, 0.33f);
                __result = MaterialPool.MatFrom(color: (__instance.Faction == null) ? Color.white : ((!__instance.Faction.IsPlayer) ? __instance.Faction.Color : PlayerCaravanColor), texPath: "World/WorldObjects/MotorisedCaravan", shader: ShaderDatabase.WorldOverlayTransparentLit, renderQueue: WorldMaterials.DynamicObjectRenderQueue);
            }
        }
    }

    [HarmonyPatch(typeof(CaravanTicksPerMoveUtility), "GetTicksPerMove", new Type[] { typeof(Caravan), typeof(StringBuilder) })]
    public static class Patch_CaravanTicksPerMoveUtility_GetTicksPerMove
    {
        [HarmonyPrefix]
        public static bool Prefix(ref int __result, Caravan caravan, StringBuilder explanation)
        {
            float speed = CaravanVehiclesUtility.TotalVehicleSpeed(caravan);
            if (speed > 0)
            {
                __result = (int)speed;
                return false;
            }
            return true; // To proceed to the original method, Prefix must return true
        }
    }

    [HarmonyPatch(typeof(Caravan_PathFollower), "IsPassable")]
    public static class Patch_Caravan_PathFollower_IsPassable
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result, Caravan_PathFollower __instance, int tile, ref Caravan ___caravan)
        {
            float speed = CaravanVehiclesUtility.TotalVehicleSpeed(___caravan);
            // If the caravan is motorised, bypass IsPassable
            if (speed > 0)
            {
                List<Tile.RoadLink> roads = Find.WorldGrid.tiles[tile].Roads;
                __result = (roads != null);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Caravan_PathFollower), "CostToMove", new Type[] { typeof(Caravan), typeof(int), typeof(int), typeof(int) })]
    public static class Patch_Caravan_PathFollower_CostToMove
    {
        [HarmonyPrefix]
        public static bool Prefix(ref int __result, Caravan_PathFollower __instance, ref Caravan caravan , int start , int end, int? ticksAbs)
        {
            float speed = CaravanVehiclesUtility.TotalVehicleSpeed(caravan);
            bool OffRoad = CaravanVehiclesUtility.IsOffRoad(caravan);
            // If the caravan is motorised and not OffRoad, return crazy high values for Cost off roads
            if (speed > 0 && !OffRoad)
            {
                List<Tile.RoadLink> Start_roads = Find.WorldGrid.tiles[start].Roads;
                List<Tile.RoadLink> End_roads = Find.WorldGrid.tiles[end].Roads;
                if (Start_roads == null || End_roads == null)
                {
                    RoadsOfTheRim.DebugLog("Cost to move adjusted to 30000");
                    __result = 30000;
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WorldPathFinder), "FindPath")]
    public static class Patch_WorldPathFinder_FindPath
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions , ILGenerator ILGen)
        {
            MethodInfo IsOffRoad = AccessTools.Method(typeof(CaravanVehiclesUtility), "IsOffRoad");
            ILGen.DeclareLocal(typeof(bool));
            // Find caravan
            // If OffRoad -> do nothing
            // If not -> replace World.Impassable by impassable for motorised caravans
            var codes = new List<CodeInstruction>(instructions);
            List<CodeInstruction> newCodes = new List<CodeInstruction> {
                new CodeInstruction (OpCodes.Ldarg_3), // Caravan
                new CodeInstruction (OpCodes.Call , IsOffRoad), //call CaravanVehiclesUtility.IsOffRoad on Caravan
                new CodeInstruction (OpCodes.Stloc_S , 20) // put it in local variable 20
            };
            codes.InsertRange(0, newCodes);

            // Find World.Impassable and replace it with CaravanVehiclesUtility.Impassable
            int index = -1;
            for (int i = 0; i < codes.Count; i++)
            {
                string s = codes[i].ToString() ;
                if (s!=null && s.Contains("Impassable"))
                {
                    index = i;
                    break;
                }
            }
            RoadsOfTheRim.DebugLog("========== BEFORE transpiling ==========");
            for (int i = index - 5; i < index + 5; i++)
            {
                string s = codes[i].ToString();
                RoadsOfTheRim.DebugLog("Transpiled : " + s);
            }
            if (index!=-1)
            {
                MethodInfo Impassable = AccessTools.Method(typeof(CaravanVehiclesUtility), "Impassable");
                codes[index] = new CodeInstruction(OpCodes.Ldloc_S , 20); // Load IsOffRoad
                codes.Insert(index+1, new CodeInstruction(OpCodes.Call , Impassable)); // Call CaravanVehiclesUtility.Impassable on (tile , IsOffRoad)
            }
            RoadsOfTheRim.DebugLog("========== AFTER transpiling ==========");
            for (int i = index-5; i < index+5; i++)
            {
                string s = codes[i].ToString();
                RoadsOfTheRim.DebugLog("Transpiled : " + s);
            }
            return codes.AsEnumerable();
        }
    }

}