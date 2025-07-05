using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SpaceCraft;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

namespace ResourceRadar
{
    [BepInPlugin("nolifeking85.theplanetcraftermods.featresourceradar", "(Feat) Resource Radar", PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> radarEnabled;

        static ManualLogSource logger;

        static readonly string[] radarItems = ["ResourceRadar-Tier1", "ResourceRadar-Tier2", "ResourceRadar-Tier3"];

        public static bool hasRadarEquipped = false;
        public static WorldObject currentRadarObject = null;

        public static List<GroupDataItem> radarTierItems = new List<GroupDataItem>();

        private void Awake()
        {
            // Plugin startup logic
            modEnabled = Config.Bind("General", "Enabled", true, "Enable or disable the Resource Radar plugin.");
            radarEnabled = Config.Bind("Radar", "Enabled", true, "Enable or disable the resource radar functionality.");

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} is loaded! Version: {PluginInfo.PLUGIN_VERSION}");
            Logger.LogInfo($"Mod Enabled: {modEnabled.Value}");
            Logger.LogInfo($"Radar Enabled: {radarEnabled.Value}");

            logger = Logger;

            Harmony.CreateAndPatchAll(typeof(Plugin), PluginInfo.PLUGIN_GUID);
        }

        private static GroupDataItem CreateRadarTierItem(List<GroupData> ___groupsData, string id)
        {
            var compassItem = (GroupDataItem)___groupsData.Find((GroupData g) => g.id == "HudCompass");

            if (compassItem == null)
            {
                logger.LogError("HudCompass item not found in groups data. Cannot create ResourceRadarTier1 item.");
                return null;
            }

            var resourceRadarItem = Instantiate(compassItem);

            resourceRadarItem.id = id;
            resourceRadarItem.name = id;
            resourceRadarItem.craftableInList = [DataConfig.CraftableIn.CraftStationT2];
            resourceRadarItem.equipableType = (DataConfig.EquipableType)1000;
            resourceRadarItem.cantBeDestroyed = true;
            resourceRadarItem.cantBeRecycled = true;

            switch (id)
            {
                case "ResourceRadar-Tier1":
                    resourceRadarItem.recipeIngredients =
                        GetRadarRecipe(
                            ___groupsData,
                            new Dictionary<string, int>
                            {
                                { "HudCompass", 1 },
                                { "Iron", 2 }
                            });
                    resourceRadarItem.value = 1;
                    break;
                case "ResourceRadar-Tier2":
                    resourceRadarItem.recipeIngredients =
                        GetRadarRecipe(
                            ___groupsData,
                            new Dictionary<string, int>
                            {
                                { "ResourceRadar-Tier1", 1 },
                                { "Cobalt", 1 },
                                { "Osmium", 1 }
                            });
                    resourceRadarItem.value = 2;
                    break;
                case "ResourceRadar-Tier3":
                    resourceRadarItem.recipeIngredients =
                        GetRadarRecipe(
                            ___groupsData,
                            new Dictionary<string, int>
                            {
                                { "ResourceRadar-Tier2", 1 },
                                { "PulsarQuartz", 1 },
                                { "SolarQuartz", 1 }
                            });
                    resourceRadarItem.value = 3;
                    break;
                default:
                    logger.LogError($"Unknown radar item ID: {id}");
                    return null;
            }

            return resourceRadarItem;
        }

        private static List<GroupDataItem> GetRadarRecipe(List<GroupData> dataItems, Dictionary<string, int> requiredItems)
        {
            var recipe = new List<GroupDataItem>();

            foreach (var requiredItem in requiredItems)
            {
                var requiredDataItem = (GroupDataItem)dataItems.Find((GroupData g) => g.id == requiredItem.Key);
                if (requiredDataItem != null)
                {
                    for (var i = 0; i < requiredItem.Value; i++)
                    {
                        recipe.Add(Instantiate(requiredDataItem));
                    }
                }
                else
                {
                    logger.LogError($"Required item {requiredItem.Key} not found in data items.");
                }
            }
            return recipe;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(StaticDataHandler), "LoadStaticData")]
        private static void StaticDataHandler_LoadStaticData(List<GroupData> ___groupsData)
        {
            for (var i = ___groupsData.Count - 1; i >= 0; i--)
            {
                var groupData = ___groupsData[i];
                if (groupData == null || (groupData.associatedGameObject == null && groupData.id.StartsWith("ResourceRadar-")))
                {
                    ___groupsData.RemoveAt(i);
                }
            }

            var existingGroups = ___groupsData.Select(groupData => groupData.id).ToHashSet();

            foreach (var radarItem in radarItems)
            {
                if (!existingGroups.Contains(radarItem))
                {
                    logger.LogInfo($"Creating {radarItem} item in groups data.");
                    var newItem = CreateRadarTierItem(___groupsData, radarItem);
                    if (newItem != null)
                    {
                        ___groupsData.Add(newItem);
                        radarTierItems.Add(newItem);
                    }
                }
                else
                {
                    logger.LogWarning($"{radarItem} item already exists in groups data. Skipping creation.");
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnlockedGroupsHandler), "SetUnlockedGroups")]
        private static void UnlockedGroupsHandler_SetUnlockedGroups(NetworkList<int> ____unlockedGroups)
        {
            foreach (string text in radarItems)
            {
                Group groupViaId = GroupsHandler.GetGroupViaId(text);
                logger.LogInfo("Unlocking " + text);
                ____unlockedGroups.Add(groupViaId.stableHashCode);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlanetLoader), "HandleDataAfterLoad")]
        static void Patch_PlanetLoader_HandleDataAfterLoad(PlanetLoader __instance)
        {
            __instance.StartCoroutine(WaitForProceduralInstances(__instance));
        }

        static IEnumerator WaitForProceduralInstances(PlanetLoader __instance)
        {
            while (!__instance.GetIsLoaded())
            {
                yield return null; // Wait for the procedural instances to be initialized
            }
            // Here you can add logic to handle the procedural instances


            logger.LogInfo("Procedural instances are ready.");
            radarEnabled.Value = true; // Enable radar by default after loading
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Localization), "LoadLocalization")]
        private static void Localization_LoadLocalization(Dictionary<string, Dictionary<string, string>> ___localizationDictionary)
        {
            if (___localizationDictionary.TryGetValue("english", out var dictionary))
            {
                foreach (string text in radarItems)
                {
                    switch (text)
                    {
                        case "ResourceRadar-Tier1":
                            dictionary["GROUP_NAME_" + text] = "Resource Radar T1";
                            dictionary["GROUP_DESC_" + text] = "A basic radar for detecting resources in the vicinity.";
                            break;
                        case "ResourceRadar-Tier2":
                            dictionary["GROUP_NAME_" + text] = "Resource Radar T2";
                            dictionary["GROUP_DESC_" + text] = "A slightly better radar for detecting resources in the vicinity.";
                            break;
                        case "ResourceRadar-Tier3":
                            dictionary["GROUP_NAME_" + text] = "Resource Radar T3";
                            dictionary["GROUP_DESC_" + text] = "The best radar for detecting resources in the vicinity.";
                            break;
                        default:
                            logger.LogError($"Unknown radar item: {text}");
                            continue;
                    }
                }
            }
        }

        private static RadarEffectComponent _radarEffectComponent;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerEquipment), "UpdateAfterEquipmentChange")]
        private static void PlayerEquipment_UpdateAfterEquipmentChange(PlayerEquipment __instance, WorldObject worldObject, bool hasBeenAdded, bool isFirstInit)
        {
            if (worldObject != null && worldObject.GetGroup() != null)
            {
                var groupItem = (GroupItem)worldObject.GetGroup();

                if (worldObject.GetGroup().GetId().StartsWith("ResourceRadar"))
                {
                    hasRadarEquipped = hasBeenAdded;

                    if (hasBeenAdded)
                    {
                        currentRadarObject = worldObject;

                        if (_radarEffectComponent == null)
                        {
                            _radarEffectComponent = __instance.GetComponent<RadarEffectComponent>();

                            if (_radarEffectComponent == null)
                            {
                                _radarEffectComponent = __instance.gameObject.AddComponent<RadarEffectComponent>();
                            }
                        }
                    }
                    else
                    {
                        currentRadarObject = null;
                        if (_radarEffectComponent != null)
                        {
                            Destroy(_radarEffectComponent);
                            _radarEffectComponent = null;
                        }
                    }
                }
            }
        }

        void Update()
        {
            if (!modEnabled.Value)
            {
                return;
            }

            if (!radarEnabled.Value)
            {
                return;
            }


        }
    }
}
