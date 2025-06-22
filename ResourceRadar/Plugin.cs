using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SpaceCraft;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ResourceRadar
{
    [BepInPlugin("nolifeking85.theplanetcraftermods.featresourceradar", "(Feat) Resource Radar", PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private struct RadarBlip
        {
            public Vector3 position;
            public Color color;
        }

        private enum RadarMode
        {
            All,
            Specific
        }

        private RadarMode _currentMode = RadarMode.All; // Default mode
        private string _currentSpecificResource = "Iron"; // Default specific resource

        static ManualLogSource logger;

        static ConfigEntry<bool> modEnabled;
        static ConfigEntry<int> radarRange;
        static ConfigEntry<bool> radarEnabled;

        private const float SCAN_INTERVAL = 2.0f; // Scan every 2 seconds.
        private float _timeSinceLastScan = 0f;

        static bool oncePerFrame;

        private readonly Dictionary<string, Color> _resourceColors = new Dictionary<string, Color>
        {
            { "Aluminium", new Color(0.7f, 0.7f, 0.8f) }, // Dull grey
            { "Bauxite", new Color(0.769f, 0.271f, 0) },
            { "Blazar Quartz", new Color(0, 0.678f, 1) },
            { "Cobalt", Color.blue },
            { "Cosmic Quartz", new Color(1, 0, 0.561f) },
            { "Dolomite", new Color(0.859f, 0.859f, 0.859f) },
            { "Ice", Color.cyan },
            { "Iridium", new Color(1f, 0.5f, 0f) }, // Orange
            { "Iron", Color.grey },
            { "Magnesium", new Color(0.9f, 0.9f, 1f) }, // Pale white
            { "Magnetar Quartz", new Color(0.643f, 0, 1) },
            { "Obsidian", new Color(0.412f, 0, 0.318f) },
            { "Osmium", new Color(0.6f, 0.2f, 0.8f) }, // Purple
            { "Phosphorus", new Color(0.69f, 0.957f, 1f) },
            { "Pulsar Quartz", new Color(1f, 0f, 1f) },
            { "Pulsar Shard", new Color(1f, 0f, 1f) },
            { "Quasar Quartz", new Color(0, 1, 0.259f) },
            { "Selenium", new Color(0.294f, 0.459f, 0.369f) },
            { "Silicon", new Color(0.8f, 0.7f, 0.6f) }, // Sandy color
            { "Solar Quartz", new Color(0.988f, 1f, 0f) },
            { "Sulphur", Color.yellow },
            { "Super Alloy", new Color(0.7f, 0, 1f) },
            { "Titanium", Color.white },
            { "Uranium", Color.green },
            { "Uraninite", new Color(0.929f, 1, 0.949f) },
            { "Zeolite", new Color(0.3f, 0.8f, 0.4f) }, // Dark green
        };

        private readonly Dictionary<string, List<string>> _resourceGroups = new Dictionary<string, List<string>>
        {
            { "Aluminium", new List<string> { "Aluminium" } },
            { "Bauxite", new List<string> { "Bauxite" } },
            { "Blazar Quartz", new List<string> { "BlazarQuartz", "BalzarQuartz" } },
            { "Cobalt", new List<string> { "Cobalt" } },
            { "Cosmic Quartz", new List<string> { "CosmicQuartz" } },
            { "Dolomite", new List<string> { "Dolomite" } },
            { "Ice", new List<string> { "Ice" } },
            { "Iridium", new List<string> { "Iridium" } },
            { "Iron", new List<string> { "Iron" } },
            { "Magnesium", new List<string> { "Magnesium" } },
            { "Magnetar Quartz", new List<string> { "MagnetarQuartz" } },
            { "Obsidian", new List<string> { "Obsidian" } },
            { "Osmium", new List<string> { "Osmium" } },
            { "Phosphorus", new List<string> { "Phosphorus" } },
            { "Pulsar Quartz", new List<string> { "PulsarQuartz" } },
            { "Pulsar Shard", new List<string> { "PulsarShard" } },
            { "Quasar Quartz", new List<string> { "QuasarQuartz" } },
            { "Selenium", new List<string> { "Selenium" } },
            { "Silicon", new List<string> { "Silicon" } },
            { "Solar Quartz", new List<string> { "SolarQuartz" } },
            { "Sulphur", new List<string> { "Sulphur", "Sulfur" } },
            { "Super Alloy", new List<string> { "SuperAlloy", "Alloy" } },
            { "Titanium", new List<string> { "Titanium" } },
            { "Uranium", new List<string> { "Uranium", "Uranim" } },
            { "Uraninite", new List<string> { "Uraninite" } },
            { "Zeolite", new List<string> { "Zeolite" } }
        };

        private readonly List<RadarBlip> _blipsToDraw = new List<RadarBlip>();

        private Texture2D _blipTexture;
        private Rect _radarRect;
        private float _radarSize = 300f;
        private Vector2 _radarCenter;

        private void Awake()
        {
            // Plugin startup logic
            modEnabled = Config.Bind("General", "Enabled", true, "Enable or disable the Resource Radar plugin.");
            radarRange = Config.Bind("Radar", "Range", 100, "Set the range of the resource radar in meters.");
            radarEnabled = Config.Bind("Radar", "Enabled", true, "Enable or disable the resource radar functionality.");

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} is loaded! Version: {PluginInfo.PLUGIN_VERSION}");
            Logger.LogInfo($"Mod Enabled: {modEnabled.Value}");
            Logger.LogInfo($"Radar Range: {radarRange.Value} meters");
            Logger.LogInfo($"Radar Enabled: {radarEnabled.Value}");

            logger = Logger;

            _blipTexture = new Texture2D(1, 1);
            _blipTexture.SetPixel(0, 0, Color.white);
            _blipTexture.Apply();

            Harmony.CreateAndPatchAll(typeof(Plugin), PluginInfo.PLUGIN_GUID);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlanetLoader), "HandleDataAfterLoad")]
        static void Patch_PlanetLoader_HandleDataAfterLoad(PlanetLoader __instance)
        {
            __instance.StartCoroutine(WaitForProceduralInstances(__instance));
        }

        static System.Collections.IEnumerator WaitForProceduralInstances(PlanetLoader __instance)
        {
            while (!__instance.GetIsLoaded())
            {
                yield return null; // Wait for the procedural instances to be initialized
            }
            // Here you can add logic to handle the procedural instances


            logger.LogInfo("Procedural instances are ready.");
            radarEnabled.Value = true; // Enable radar by default after loading
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

            _timeSinceLastScan += Time.deltaTime;

            if (_timeSinceLastScan >= SCAN_INTERVAL)
            {
                _timeSinceLastScan = 0f;
                StartCoroutine(ScanForResources());
            }
        }

        bool currentlyScanning = false;

        IEnumerator ScanForResources()
        {
            if (currentlyScanning)
            {
                yield break; // Prevent overlapping scans
            }

            var handler = WorldObjectsHandler.Instance;

            if (handler == null)
            {
                if (_blipsToDraw.Count > 0)
                {
                    _blipsToDraw.Clear();
                }

                currentlyScanning = false;

                yield break;
            }

            _blipsToDraw.Clear();

            currentlyScanning = true;

            var allObjects = handler.GetAllWorldObjects();

            foreach (var worldObjectKvp in allObjects)
            {
                var worldObject = worldObjectKvp.Value;
                if (worldObject == null)
                    continue;

                var groupId = worldObject.GetGroup()?.GetId();
                if (string.IsNullOrEmpty(groupId))
                    continue;

                foreach (var targetResource in _resourceColors)
                {
                    if (_currentMode == RadarMode.Specific && _currentSpecificResource != targetResource.Key)
                    {
                        continue; // Skip if we're in specific mode and this isn't the target resource
                    }

                    var variations = _resourceGroups.GetValueOrDefault(targetResource.Key, []);

                    if (variations.Any(v => groupId.StartsWith(v, System.StringComparison.InvariantCultureIgnoreCase)))
                    {
                        var objectPosition = worldObject.GetPosition();

                        if (!worldObject.GetIsPlaced())
                        {
                            continue;
                        }

                        _blipsToDraw.Add(new RadarBlip
                        {
                            position = objectPosition,
                            color = targetResource.Value
                        });
                        break;
                    }
                }
            }

            currentlyScanning = false;

            yield return null;
        }

        void OnGUI()
        {
            oncePerFrame = !oncePerFrame;

            if (oncePerFrame && Keyboard.current[Key.F5].wasPressedThisFrame)
            {
                radarEnabled.Value = !radarEnabled.Value;
                return;
            }

            if (oncePerFrame && Keyboard.current[Key.F6].wasPressedThisFrame)
            {
                // Cycle to the next mode
                _currentMode = _currentMode == RadarMode.All ? RadarMode.Specific : RadarMode.All;
                // Force a rescan immediately to reflect the change
                StartCoroutine(ScanForResources());
                return;
            }

            if (oncePerFrame && Keyboard.current[Key.PageDown].wasPressedThisFrame && _currentMode == RadarMode.Specific)
            {
                // Cycle through specific resources
                var resourceKeys = new List<string>(_resourceColors.Keys);
                int currentIndex = resourceKeys.IndexOf(_currentSpecificResource);
                currentIndex = (currentIndex + 1) % resourceKeys.Count; // Loop back to the start
                _currentSpecificResource = resourceKeys[currentIndex];
                // Force a rescan immediately to reflect the change
                StartCoroutine(ScanForResources());
                return;
            }

            if (oncePerFrame && Keyboard.current[Key.PageUp].wasPressedThisFrame && _currentMode == RadarMode.Specific)
            {
                // Cycle through specific resources
                var resourceKeys = new List<string>(_resourceColors.Keys);
                int currentIndex = resourceKeys.IndexOf(_currentSpecificResource);
                currentIndex--;
                if (currentIndex < 0)
                {
                    currentIndex = resourceKeys.Count - 1;
                }

                _currentSpecificResource = resourceKeys[currentIndex];
                // Force a rescan immediately to reflect the change
                StartCoroutine(ScanForResources());
                return;
            }

            if (!modEnabled.Value)
            {
                return; // Skip GUI rendering if the mod is disabled
            }

            Color originalColor = GUI.color;

            // Define the radar's screen position
            _radarRect = new Rect(Screen.width - (_radarSize * 1.5f) - 10, Screen.height - (_radarSize * 3) - 10, _radarSize, _radarSize);
            _radarCenter = new Vector2(_radarRect.x + _radarSize / 2, _radarRect.y + _radarSize / 2);

            var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
            if (player == null || !radarEnabled.Value)
            {
                return; // Skip rendering if player is not available or radar is disabled
            }

            string modeText = $"Mode: {_currentMode} (F6){(_currentMode == RadarMode.Specific ? $" Resource: {_currentSpecificResource} (PgUp/PgDn)" : "")}";
            // , Last update: {_timeSinceLastScan} seconds ago
            GUI.color = Color.white;
            // Position the text just above the radar widget
            GUI.Label(new Rect(_radarRect.x, _radarRect.y - 20, _radarSize, 20), modeText, new GUIStyle()
            {
                fontSize = 14,
                border = new RectOffset(2, 2, 2, 2),
                normal = new GUIStyleState { textColor = Color.white }
            });

            DrawRadarBackground();
            DrawRadarGrid();

            // Draw the resource blips
            foreach (var blip in _blipsToDraw)
            {
                float distance = Vector3.Distance(player.transform.position, blip.position);

                // Only process blips within the radar's range
                if (distance < radarRange.Value)
                {
                    DrawBlip(player.transform, blip.position, blip.color);
                }
            }

            // Draw the player blip on top of everything else
            DrawPlayerBlip();

            GUI.color = originalColor;
        }

        void DrawRadarBackground()
        {
            GUI.color = new Color(0, 0, 0, 0.5f); // Black, 50% transparent
            GUI.DrawTexture(_radarRect, _blipTexture);
        }

        void DrawRadarGrid()
        {
            GUI.color = new Color(1, 1, 1, 0.3f); // Faint white
            // Horizontal line
            GUI.DrawTexture(new Rect(_radarRect.x, _radarCenter.y, _radarSize, 1), _blipTexture);
            // Vertical line
            GUI.DrawTexture(new Rect(_radarCenter.x, _radarRect.y, 1, _radarSize), _blipTexture);
        }

        void DrawPlayerBlip()
        {
            // Set the global color for the player blip
            GUI.color = Color.red;
            // Use DrawTexture instead of Box
            GUI.DrawTexture(new Rect(_radarCenter.x - 3, _radarCenter.y - 3, 6, 6), _blipTexture);
        }

        void DrawBlip(Transform playerTransform, Vector3 resourcePosition, Color blipColor)
        {
            // --- This is the core translation logic ---

            // 1. Get the direction vector from the player to the resource
            Vector3 directionVector = resourcePosition - playerTransform.position;

            // 2. We only care about the X and Z plane for a 2D radar
            directionVector.y = 0;

            // 3. Get the player's forward direction
            Vector3 playerForward = playerTransform.forward;
            playerForward.y = 0;

            // 4. Calculate the angle between the player's forward and the resource direction
            float angle = Vector3.SignedAngle(playerForward, directionVector, Vector3.up);

            // 5. Convert angle to radians for trigonometric functions
            float angleRad = angle * Mathf.Deg2Rad;

            // 6. Calculate the resource's distance from the player and scale it to the radar's size
            float distance = directionVector.magnitude;
            float scaledDistance = Mathf.Min(distance, radarRange.Value) / radarRange.Value * (_radarSize / 2);

            // 7. Use Sine and Cosine to get the (x, y) coordinates on our radar circle
            float blipX = _radarCenter.x + (scaledDistance * Mathf.Sin(angleRad));
            float blipY = _radarCenter.y - (scaledDistance * Mathf.Cos(angleRad)); // Y is inverted in GUI coordinates

            // 8. Draw the blip
            GUI.color = blipColor;
            GUI.DrawTexture(new Rect(blipX - 2, blipY - 2, 4, 4), _blipTexture);
        }
    }
}
