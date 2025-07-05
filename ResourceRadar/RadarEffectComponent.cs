using BepInEx.Logging;
using SpaceCraft;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ResourceRadar
{
    public class RadarEffectComponent : MonoBehaviour
    {
        public int Tier { get; set; }

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

        private RadarMode _currentMode = RadarMode.All;
        private string _currentSpecificResource = "Iron";

#pragma warning disable CS0649
        static ManualLogSource logger;
#pragma warning restore CS0649


        private float SCAN_RANGE = 100.0f;
        private float SCAN_INTERVAL = 5.0f;
        private float _timeSinceLastScan = 0f;

        static bool oncePerFrame;

        private readonly Dictionary<string, Color> _resourceColors = new Dictionary<string, Color>
        {
            { "Aluminium", new Color(0.647f, 0.651f, 0.639f) },
            { "Bauxite", new Color(0.533f, 0.275f, 0.227f) },
            { "Blazar Quartz", new Color(0.396f, 0.722f, 0.969f) },
            { "Cobalt", new Color(0.035f, 0.325f, 0.682f) },
            { "Cosmic Quartz", new Color(0.984f, 0.000f, 1.000f) },
            { "Dolomite", new Color(0.573f, 0.663f, 0.769f) },
            { "Ice", new Color(0.565f, 0.753f, 0.835f) },
            { "Iridium", new Color(0.804f, 0.220f, 0.086f) },
            { "Iron", new Color(0.733f, 0.733f, 0.663f) },
            { "Magnesium", new Color(0.9f, 0.9f, 1f) },
            { "Magnetar Quartz", new Color(0.678f, 0.157f, 0.741f) },
            { "Obsidian", new Color(0.263f, 0.035f, 0.333f) },
            { "Osmium", new Color(0.141f, 0.376f, 1.000f) },
            { "Phosphorus", new Color(0.788f, 0.984f, 0.988f) },
            { "Pulsar Quartz", new Color(0.702f, 0.192f, 0.855f) },
            { "Quasar Quartz", new Color(0.851f, 0.996f, 0.161f) },
            { "Selenium", new Color(0.278f, 0.682f, 0.600f) },
            { "Silicon", new Color(0.337f, 0.341f, 0.329f) },
            { "Solar Quartz", new Color(1.000f, 0.984f, 0.047f) },
            { "Sulphur", new Color(0.988f, 0.957f, 0.208f) },
            { "Super Alloy", new Color(0.843f, 0.451f, 0.710f) },
            { "Titanium", new Color(0.690f, 0.659f, 0.569f) },
            { "Uranium", new Color(0.184f, 0.725f, 0.267f) },
            { "Uraninite", new Color(0.576f, 0.439f, 0.424f) },
            { "Zeolite", new Color(0.3f, 0.8f, 0.4f) },
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

        void Awake()
        {
            _blipTexture = new Texture2D(1, 1);
            _blipTexture.SetPixel(0, 0, Color.white);
            _blipTexture.Apply();
        }

        void Update()
        {
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
            if (!Plugin.modEnabled.Value || !Plugin.radarEnabled.Value || currentlyScanning)
            {
                yield break;
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

            Stopwatch totalScanTimer = Stopwatch.StartNew();
            Stopwatch activeProcessingTimer = new Stopwatch();
            Stopwatch objectSortTimer = new Stopwatch();

            var tempBlips = new List<RadarBlip>();

            currentlyScanning = true;

            var playersManager = Managers.GetManager<PlayersManager>();

            if (playersManager == null || playersManager.GetActivePlayerController() == null)
            {
                logger.LogWarning("PlayersManager or active player controller is null. Cannot scan for resources.");
                currentlyScanning = false;
                yield break;
            }

            var player = playersManager.GetActivePlayerController();

            activeProcessingTimer.Start();

            objectSortTimer.Start();

            var allObjects = handler.GetAllWorldObjects().Values.ToHashSet();

            var playerPosition = player.transform.position;

            if (Plugin.hasRadarEquipped && Plugin.currentRadarObject != null)
            {
                var getRadarFromGroupDataItems = Plugin.radarTierItems.Find(i => i.id == Plugin.currentRadarObject.GetGroup().GetId());
                if (getRadarFromGroupDataItems != null)
                {
                    switch (getRadarFromGroupDataItems.value)
                    {
                        case 1:
                            SCAN_RANGE = 100.0f;
                            break;
                        case 2:
                            SCAN_RANGE = 150.0f;
                            break;
                        case 3:
                            SCAN_RANGE = 200.0f;
                            break;
                    }
                }
            }

            allObjects = [.. allObjects
                .Where(obj => Vector3.Distance(obj.GetPosition(), playerPosition) < SCAN_RANGE * 2)
                .OrderBy(allObjects => Vector3.Distance(allObjects.GetPosition(), playerPosition))
            ];

            if (Plugin.hasRadarEquipped && Plugin.currentRadarObject != null)
            {
                var getRadarFromGroupDataItems = Plugin.radarTierItems.Find(i => i.id == Plugin.currentRadarObject.GetGroup().GetId());
                if (getRadarFromGroupDataItems != null)
                {
                    switch (getRadarFromGroupDataItems.value)
                    {
                        case 1:
                            allObjects = [.. allObjects.Take(50)];
                            SCAN_INTERVAL = 5.0f;
                            break;
                        case 2:
                            allObjects = [.. allObjects.Take(500)];
                            SCAN_INTERVAL = 2.0f;
                            break;
                        case 3:
                            allObjects = [.. allObjects.Take(1000)];
                            SCAN_INTERVAL = 1.0f;
                            break;
                    }
                }
            }

            objectSortTimer.Stop();

            int processedCount = 0;

            foreach (var worldObjectKvp in allObjects)
            {
                var worldObject = worldObjectKvp;
                if (worldObject == null || !worldObject.GetIsPlaced())
                    continue;

                var groupId = worldObject.GetGroup()?.GetId();
                if (string.IsNullOrEmpty(groupId))
                    continue;

                foreach (var targetResource in _resourceColors)
                {
                    if (_currentMode == RadarMode.Specific && _currentSpecificResource != targetResource.Key)
                    {
                        continue;
                    }

                    var variations = _resourceGroups.GetValueOrDefault(targetResource.Key, []);

                    if (variations.Any(v => groupId.StartsWith(v, System.StringComparison.InvariantCultureIgnoreCase)))
                    {
                        tempBlips.Add(new RadarBlip
                        {
                            position = worldObject.GetPosition(),
                            color = targetResource.Value
                        });
                        break;
                    }
                }

                processedCount++;

                if (processedCount >= 500)
                {
                    activeProcessingTimer.Stop();

                    processedCount = 0;
                    yield return null;

                    activeProcessingTimer.Start();
                }
            }

            activeProcessingTimer.Stop();

            _blipsToDraw.Clear();
            _blipsToDraw.AddRange(tempBlips);

            currentlyScanning = false;

            totalScanTimer.Stop();

            logger.LogDebug($"Resource scan completed in {totalScanTimer.ElapsedMilliseconds} ms. Found {_blipsToDraw.Count} resources amongst {allObjects.Count}.");
            logger.LogDebug($"Active processing time: {activeProcessingTimer.ElapsedMilliseconds} ms. Average per blip: {(activeProcessingTimer.ElapsedMilliseconds / (float)_blipsToDraw.Count):F2} ms.");
            logger.LogDebug($"Object sorting time: {objectSortTimer.ElapsedMilliseconds} ms. Average per object: {(objectSortTimer.ElapsedMilliseconds / (float)allObjects.Count):F2} ms.");

            yield return null;
        }

        void OnGUI()
        {
            oncePerFrame = !oncePerFrame;

            if (oncePerFrame && Keyboard.current[Key.F5].wasPressedThisFrame)
            {
                Plugin.radarEnabled.Value = !Plugin.radarEnabled.Value;
                return;
            }

            if (oncePerFrame && Keyboard.current[Key.F6].wasPressedThisFrame)
            {
                _currentMode = _currentMode == RadarMode.All ? RadarMode.Specific : RadarMode.All;
                StartCoroutine(ScanForResources());
                return;
            }

            if (oncePerFrame && Keyboard.current[Key.PageDown].wasPressedThisFrame && _currentMode == RadarMode.Specific)
            {
                var resourceKeys = new List<string>(_resourceColors.Keys);
                int currentIndex = resourceKeys.IndexOf(_currentSpecificResource);
                currentIndex = (currentIndex + 1) % resourceKeys.Count; // Loop back to the start
                _currentSpecificResource = resourceKeys[currentIndex];
                StartCoroutine(ScanForResources());
                return;
            }

            if (oncePerFrame && Keyboard.current[Key.PageUp].wasPressedThisFrame && _currentMode == RadarMode.Specific)
            {
                var resourceKeys = new List<string>(_resourceColors.Keys);
                int currentIndex = resourceKeys.IndexOf(_currentSpecificResource);
                currentIndex--;
                if (currentIndex < 0)
                {
                    currentIndex = resourceKeys.Count - 1;
                }

                _currentSpecificResource = resourceKeys[currentIndex];
                StartCoroutine(ScanForResources());
                return;
            }

            if (!Plugin.modEnabled.Value)
            {
                return;
            }

            Color originalColor = GUI.color;

            _radarRect = new Rect(Screen.width - (_radarSize * 1.5f) - 10, Screen.height - (_radarSize * 3) - 10, _radarSize, _radarSize);
            _radarCenter = new Vector2(_radarRect.x + _radarSize / 2, _radarRect.y + _radarSize / 2);

            var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
            if (player == null || !Plugin.radarEnabled.Value)
            {
                return;
            }

            string modeText = $"Mode: {_currentMode} (F6){(_currentMode == RadarMode.Specific ? $" Resource: {_currentSpecificResource} (PgUp/PgDn)" : "")}";
            GUI.color = Color.white;
            GUI.Label(new Rect(_radarRect.x, _radarRect.y - 20, _radarSize, 20), modeText, new GUIStyle()
            {
                fontSize = 14,
                border = new RectOffset(2, 2, 2, 2),
                normal = new GUIStyleState { textColor = Color.white }
            });

            DrawRadarBackground();
            DrawRadarGrid();

            foreach (var blip in _blipsToDraw)
            {
                float distance = Vector3.Distance(player.transform.position, blip.position);

                if (distance < SCAN_RANGE)
                {
                    DrawBlip(player.transform, blip.position, blip.color);
                }
            }

            DrawPlayerBlip();

            GUI.color = originalColor;
        }

        void DrawRadarBackground()
        {
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(_radarRect, _blipTexture);
        }

        void DrawRadarGrid()
        {
            GUI.color = new Color(1, 1, 1, 0.3f);
            GUI.DrawTexture(new Rect(_radarRect.x, _radarCenter.y, _radarSize, 1), _blipTexture);
            GUI.DrawTexture(new Rect(_radarCenter.x, _radarRect.y, 1, _radarSize), _blipTexture);
        }

        void DrawPlayerBlip()
        {
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(_radarCenter.x - 3, _radarCenter.y - 3, 6, 6), _blipTexture);
        }

        void DrawBlip(Transform playerTransform, Vector3 resourcePosition, Color blipColor)
        {
            Vector3 directionVector = resourcePosition - playerTransform.position;

            directionVector.y = 0;

            Vector3 playerForward = playerTransform.forward;
            playerForward.y = 0;

            float angle = Vector3.SignedAngle(playerForward, directionVector, Vector3.up);

            float angleRad = angle * Mathf.Deg2Rad;

            float distance = directionVector.magnitude;
            float scaledDistance = Mathf.Min(distance, SCAN_RANGE) / SCAN_RANGE * (_radarSize / 2);

            float blipX = _radarCenter.x + (scaledDistance * Mathf.Sin(angleRad));
            float blipY = _radarCenter.y - (scaledDistance * Mathf.Cos(angleRad));

            GUI.color = blipColor;
            GUI.DrawTexture(new Rect(blipX - 2, blipY - 2, 4, 4), _blipTexture);
        }

        void OnDestroy()
        {
            StopAllCoroutines();
        }
    }
}
