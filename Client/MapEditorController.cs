using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
using EFT.HealthSystem;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MapLootEditorLite.Client
{
    public class MapEditorController : MonoBehaviour
    {
        public static MapEditorController Instance { get; private set; }

        private MarkerManager _manager;
        private MarkerRenderer _renderer;
        private CustomEditorUI _ui;
        private LootPreviewSpawner _previews;
        private GameObject _visualRoot;
        private GameObject _previewRoot;

        private bool _editorOpen;
        private KeyCode _toggleKey => Plugin.ToggleKey;
        private GameWorld _currentGameWorld;
        private string _currentMapId;
        private int _lastToggleFrame = -1;
        private int UnlockCursorButton => Plugin.UnlockCursorMouseButton?.Value ?? 2;

        private bool _freeCam;
        private Camera _freeCamCamera;
        private Camera _gameCamera;
        private bool _gameCameraActive;
        private Vector3 _freeCamEuler;
        public const float FreeCamSpeed = 5f;
        public const float FreeCamFastSpeed = 15f;
        public const float FreeCamLookSpeed = 2f;

        private bool _mouseDragging;
        private bool _visualsCleared;

        public static bool FreeCamInvulnerable => _editorModeActive || Time.time < _editorModeInvincibleEndTime;
        private static bool _editorModeActive;
        private static float _editorModeInvincibleEndTime;
        private static readonly Harmony _freecamHarmony = new Harmony("com.shane.mapeditorlite.freecam");
        private static bool _freecamPatchesApplied;
        private static readonly Harmony _inputHarmony = new Harmony("com.shane.mapeditorlite.input");
        private static bool _inputPatchesApplied;

        private bool _freeCamCursorLocked = true;
        private bool _menuCursorLocked = true;
        private bool _gameInputPaused;
        private readonly List<EventSystem> _disabledEventSystems = new List<EventSystem>();
        private Quaternion _menuUnlockedCameraRotation;
        private bool _menuUnlockedCameraRotationCaptured;
        private Player _freeCamPlayer;
        private CharacterController _freeCamPlayerController;
        private GamePlayerOwner _gamePlayerOwner;
        private bool _originalGamePlayerOwnerEnabled;
        private List<DisablerCullingObjectBase> _cullingObjects;
        private List<bool> _cullingObjectsEnabled;

        private GizmoMode _gizmoMode = GizmoMode.Translate;
        public GizmoMode GizmoMode => _gizmoMode;
        private GizmoAxis _activeGizmoAxis;
        private Vector3 _gizmoDragStartWorld;
        private Vector3 _gizmoDragStartMarkerPos;
        private Vector3 _gizmoDragStartMarkerRot;
        private Vector3 _gizmoDragStartMarkerScale;
        private Plane _gizmoDragPlane;
        private Vector3 _gizmoDragStartCenter;
        private Quaternion _gizmoDragStartCenterRot;
        private Vector3 _gizmoDragStartCenterScale;
        private readonly Dictionary<string, Vector3> _gizmoDragStartPositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Quaternion> _gizmoDragStartRotations = new Dictionary<string, Quaternion>();
        private readonly Dictionary<string, Vector3> _gizmoDragStartScales = new Dictionary<string, Vector3>();

        private void Awake()
        {
            Instance = this;
            _manager = new MarkerManager();
            _visualRoot = new GameObject("MLE_VisualRoot");
            _visualRoot.transform.SetParent(transform, false);
            _previewRoot = new GameObject("MLE_PreviewRoot");
            _previewRoot.transform.SetParent(transform, false);

            _renderer = new MarkerRenderer(_manager, _visualRoot)
            {
                VanillaRenderDistance = Plugin.VanillaRenderDistance?.Value ?? 150f
            };
            _previews = new LootPreviewSpawner(_previewRoot);
            _ui = gameObject.AddComponent<CustomEditorUI>();
            _ui.Init(this, _manager, _renderer, _previews);
            Plugin.Log.LogInfo($"MapEditorController awake: enabled={enabled} active={gameObject.activeInHierarchy} key={_toggleKey}");
            Camera.onPreRender += OnCameraPreRender;
            TryApplyFreeCamPatches();
            TryApplyInputPatches();
        }

        private void TryApplyFreeCamPatches()
        {
            if (_freecamPatchesApplied)
                return;
            _freecamPatchesApplied = true;

            PatchMethod(typeof(ActiveHealthController), "ApplyDamage", nameof(ApplyDamagePrefix));
            PatchMethod(typeof(ActiveHealthController), "ChangeHealth", nameof(ChangeHealthPrefix));
            PatchMethod(typeof(Player), "ReceiveDamage", nameof(ReceiveDamagePrefix));
            PatchMethod(typeof(CameraClass), nameof(CameraClass.ForceSetPosition), nameof(ForceSetCameraPositionPrefix));
        }

        private void TryApplyInputPatches()
        {
            if (_inputPatchesApplied)
                return;
            _inputPatchesApplied = true;

            try
            {
                var getAxis = AccessTools.Method(typeof(Input), "GetAxis", new[] { typeof(string) });
                if (getAxis != null)
                {
                    _inputHarmony.Patch(getAxis, prefix: new HarmonyMethod(typeof(MapEditorController), nameof(InputGetAxisPrefix)));
                    Plugin.Log.LogInfo("Patched Input.GetAxis to suppress game mouse look while menu cursor is unlocked.");
                }
                else
                {
                    Plugin.Log.LogWarning("Could not find Input.GetAxis to patch.");
                }

                var getAxisRaw = AccessTools.Method(typeof(Input), "GetAxisRaw", new[] { typeof(string) });
                if (getAxisRaw != null)
                {
                    _inputHarmony.Patch(getAxisRaw, prefix: new HarmonyMethod(typeof(MapEditorController), nameof(InputGetAxisPrefix)));
                    Plugin.Log.LogInfo("Patched Input.GetAxisRaw to suppress game mouse look while menu cursor is unlocked.");
                }
                else
                {
                    Plugin.Log.LogWarning("Could not find Input.GetAxisRaw to patch.");
                }

                var setLockState = AccessTools.Method(typeof(Cursor), "set_lockState", new[] { typeof(CursorLockMode) });
                if (setLockState != null)
                {
                    _inputHarmony.Patch(setLockState, prefix: new HarmonyMethod(typeof(MapEditorController), nameof(CursorSetLockStatePrefix)));
                    Plugin.Log.LogInfo("Patched Cursor.lockState setter to keep cursor unlocked while menu cursor is unlocked.");
                }
                else
                {
                    Plugin.Log.LogWarning("Could not find Cursor.lockState setter to patch.");
                }

                var setVisible = AccessTools.Method(typeof(Cursor), "set_visible", new[] { typeof(bool) });
                if (setVisible != null)
                {
                    _inputHarmony.Patch(setVisible, prefix: new HarmonyMethod(typeof(MapEditorController), nameof(CursorSetVisiblePrefix)));
                    Plugin.Log.LogInfo("Patched Cursor.visible setter to keep cursor visible while menu cursor is unlocked.");
                }
                else
                {
                    Plugin.Log.LogWarning("Could not find Cursor.visible setter to patch.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to patch input/cursor methods: {ex.Message}");
            }
        }

        private static bool InputGetAxisPrefix(string axisName, ref float __result)
        {
            if (Instance == null)
                return true;

            if (!Instance._editorOpen)
                return true;

            var cursorUnlocked = Instance._freeCam ? !Instance._freeCamCursorLocked : !Instance._menuCursorLocked;
            if (!cursorUnlocked)
                return true;

            if (axisName == "Mouse X" || axisName == "Mouse Y")
            {
                // Allow mouse look while right-click panning in freecam.
                if (Instance._freeCam && Input.GetMouseButton(1))
                    return true;
                __result = 0f;
                return false;
            }

            return true;
        }

        private static bool CursorSetLockStatePrefix(CursorLockMode value)
        {
            if (Instance == null)
                return true;

            if (!Instance._editorOpen || value != CursorLockMode.Locked)
                return true;

            var cursorUnlocked = Instance._freeCam ? !Instance._freeCamCursorLocked : !Instance._menuCursorLocked;
            if (cursorUnlocked)
                return false;

            return true;
        }

        private static bool CursorSetVisiblePrefix(bool value)
        {
            if (Instance == null)
                return true;

            if (!Instance._editorOpen || value)
                return true;

            var cursorUnlocked = Instance._freeCam ? !Instance._freeCamCursorLocked : !Instance._menuCursorLocked;
            if (cursorUnlocked)
                return false;

            return true;
        }

        private void PatchMethod(System.Type type, string methodName, string prefixName)
        {
            try
            {
                var method = AccessTools.Method(type, methodName);
                if (method != null)
                {
                    _freecamHarmony.Patch(method, prefix: new HarmonyMethod(typeof(MapEditorController), prefixName));
                    Plugin.Log.LogInfo($"Patched {type.Name}.{methodName} for editor mode invulnerability.");
                }
                else
                {
                    Plugin.Log.LogWarning($"Could not find {type.Name}.{methodName} to patch.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to patch {type.Name}.{methodName}: {ex.Message}");
            }
        }

        public static bool ApplyDamagePrefix(EBodyPart bodyPart, ActiveHealthController __instance, ref float __result)
        {
            if (!FreeCamInvulnerable || __instance == null)
                return true;

            var player = __instance.Player;
            if (player == null || !player.IsYourPlayer)
                return true;

            __result = 0f;
            return false;
        }

        public static bool ChangeHealthPrefix(EBodyPart bodyPart, ActiveHealthController __instance, ref float value, DamageInfoStruct damageInfo)
        {
            if (!FreeCamInvulnerable || __instance == null)
                return true;

            var player = __instance.Player;
            if (player == null || !player.IsYourPlayer)
                return true;

            if (value < 0f)
                value = 0f;
            return true;
        }

        public static bool ReceiveDamagePrefix(float damage, EBodyPart part, EDamageType type, float absorbed, MaterialType special, Player __instance)
        {
            if (!FreeCamInvulnerable || __instance == null || !__instance.IsYourPlayer)
                return true;
            return false;
        }

        public static bool ForceSetCameraPositionPrefix()
        {
            if (Instance == null || !Instance._freeCam)
                return true;
            return false;
        }

        private void Update()
        {
            try { DetectRaid(); }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"DetectRaid failed: {ex.Message}");
            }

            if (_visualRoot != null)
                _visualRoot.SetActive(_editorOpen);
            if (_previewRoot != null)
                _previewRoot.SetActive(_editorOpen);

            if (Input.GetKeyDown(_toggleKey) && _lastToggleFrame != Time.frameCount)
            {
                _editorOpen = !_editorOpen;
                _lastToggleFrame = Time.frameCount;
            }

            if (_editorOpen && _ui != null && !_ui.IsVisible)
                _ui.Show();
            else if (!_editorOpen && _ui != null && _ui.IsVisible)
                _ui.Hide();

            if (!_editorOpen && _freeCam)
                ToggleFreeCam();

            if (_editorOpen && !_freeCam)
                ToggleFreeCam();

            if (_editorOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    SetGizmoMode(GizmoMode.Translate);
                if (Input.GetKeyDown(KeyCode.Alpha2))
                    SetGizmoMode(GizmoMode.Rotate);
                if (Input.GetKeyDown(KeyCode.Alpha3))
                    SetGizmoMode(GizmoMode.Scale);

                if (_ui.IsDeleteConfirmed)
                {
                    DeleteSelected();
                    _ui.ClearDeletePending();
                }

                if (Input.GetKeyDown(KeyCode.Delete) && !_ui.IsDeletePending && !_ui.IsAnyInputFocused)
                    _ui.RequestDelete();

                if (Input.GetKeyDown(KeyCode.C) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !_ui.IsAnyInputFocused)
                    CopySelected();
                if (Input.GetKeyDown(KeyCode.V) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !_ui.IsAnyInputFocused)
                    Paste();

                if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !_ui.IsAnyInputFocused)
                    Undo();
                if (Input.GetKeyDown(KeyCode.Y) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !_ui.IsAnyInputFocused)
                    Redo();

                if (_freeCam)
                {
                    if (Input.GetMouseButtonDown(UnlockCursorButton))
                        ToggleFreeCamCursor();

                    bool rightClickPan = Input.GetMouseButton(1) && !IsMouseOverUI();
                    if (_freeCamCursorLocked)
                    {
                        Cursor.visible = false;
                        Cursor.lockState = CursorLockMode.Locked;
                        UpdateFreeCam();
                    }
                    else if (rightClickPan)
                    {
                        Cursor.visible = true;
                        Cursor.lockState = CursorLockMode.None;
                        UpdateFreeCam();
                    }
                    else
                    {
                        Cursor.visible = true;
                        Cursor.lockState = CursorLockMode.None;
                        HandleMouseInput();
                    }
                }
                else
                {
                    if (Input.GetMouseButtonDown(UnlockCursorButton))
                        ToggleMenuCursor();

                    if (_menuCursorLocked)
                    {
                        Cursor.visible = false;
                        Cursor.lockState = CursorLockMode.Locked;
                    }
                    else
                    {
                        Cursor.visible = true;
                        Cursor.lockState = CursorLockMode.None;
                    }

                    if (_menuCursorLocked)
                    {
                        HandleSelectionInput();
                        HandleMovementInput();
                    }
                    if (!_menuCursorLocked)
                        HandleMouseInput();
                }
            }

            UpdateGameInputFocus();

            _renderer.ShowGizmo = !_freeCam || !_freeCamCursorLocked;
            _renderer.Update();

            if (_editorOpen && _manager.Selected != null)
                UpdatePreviewsForSelection();
        }

        private void OnGUI()
        {
            if (_editorOpen)
            {
                _renderer.DrawLabels(true);
                _previews.DrawLabels(true);
            }
        }

        private void OnDestroy()
        {
            Camera.onPreRender -= OnCameraPreRender;
            Instance = null;
            _previews?.ClearAll(true);
            _renderer?.Clear();
        }

        private void OnCameraPreRender(Camera cam)
        {
            if (!_editorOpen)
                return;

            if (cam == Camera.main)
            {
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                cam.aspect = Screen.width / (float)Screen.height;

                if (!_freeCam && !_menuCursorLocked && _menuUnlockedCameraRotationCaptured)
                    cam.transform.rotation = _menuUnlockedCameraRotation;
            }
        }

        public void ResetState()
        {
            _currentGameWorld = null;
            _currentMapId = null;
            _previews?.ClearAll(true);
            _renderer?.Clear();
            _visualsCleared = true;
        }

        private void DetectRaid()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                if (_currentGameWorld != null)
                {
                    _previews.ClearAll(true);
                    _renderer.Clear();
                    _visualsCleared = true;
                }
                _currentGameWorld = null;
                _currentMapId = null;
                return;
            }

            _currentGameWorld = world;

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentMapId = mapId;
                _visualsCleared = false;
                _previews.ClearAll(true);
                _manager.VanillaData = null;
                var mapData = LoadMapDataFromPacks(mapId) ?? JsonStorage.Load(mapId);
                mapData.map = mapId;
                _manager.SetMapData(mapData);
                _renderer.Rebuild();
                _previews.SpawnAllPreviews(_manager.Data);
                Plugin.Log.LogInfo($"Loaded map: {mapId}");
            }
            else if (_visualsCleared && !string.IsNullOrEmpty(_currentMapId))
            {
                _visualsCleared = false;
                _renderer.Rebuild();
                _previews.SpawnAllPreviews(_manager.Data);
            }
        }

        private MapData LoadMapDataFromPacks(string mapId)
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModExportsDirectory) && Directory.Exists(Plugin.ServerModExportsDirectory))
                directories.Add(Plugin.ServerModExportsDirectory);
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            var preferredName = _ui?.PackName;
            if (!string.IsNullOrEmpty(preferredName))
            {
                var safeName = SanitizePackName(preferredName);
                if (!string.IsNullOrEmpty(safeName))
                {
                    foreach (var dir in directories)
                    {
                        var path = Path.Combine(dir, safeName + ".json");
                        if (File.Exists(path))
                        {
                            try
                            {
                                var pack = JsonConvert.DeserializeObject<PackData>(File.ReadAllText(path));
                                if (pack?.maps != null && pack.maps.TryGetValue(mapId, out var map))
                                {
                                    Plugin.Log.LogInfo($"Loaded map {mapId} from preferred pack '{safeName}'");
                                    return map;
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.LogWarning($"Failed to read preferred pack {path}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var pack = JsonConvert.DeserializeObject<PackData>(File.ReadAllText(file));
                        if (pack?.maps != null && pack.maps.TryGetValue(mapId, out var map))
                        {
                            Plugin.Log.LogInfo($"Loaded map {mapId} from pack {file}");
                            return map;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Failed to read pack {file}: {ex.Message}");
                    }
                }
            }

            return null;
        }

        private void HandleSelectionInput()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                var camera = Camera.main;
                if (camera != null)
                {
                    var picked = _renderer.PickFromScreenCenter(camera,
                        new Vector2(Screen.width / 2f, Screen.height / 2f), 50f);
                    if (picked != null)
                    {
                        _manager.Snapshot();
                        _manager.Selected = picked;
                        Plugin.Log.LogInfo($"Selected {picked.name}");
                    }
                }
            }
        }

        private void HandleMovementInput()
        {
            if (_manager.Selected == null || _manager.Selected.isVanilla)
                return;

            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
                Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
                Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.PageDown) ||
                Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals) ||
                Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus))
            {
                _manager.Snapshot();
            }

            float speed = 5f * Time.deltaTime;
            float radiusSpeed = 2f * Time.deltaTime;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= 3f;

            if (Input.GetKey(KeyCode.UpArrow))
                _manager.MoveSelected(Vector3.forward * speed);
            if (Input.GetKey(KeyCode.DownArrow))
                _manager.MoveSelected(Vector3.back * speed);
            if (Input.GetKey(KeyCode.LeftArrow))
                _manager.MoveSelected(Vector3.left * speed);
            if (Input.GetKey(KeyCode.RightArrow))
                _manager.MoveSelected(Vector3.right * speed);
            if (Input.GetKey(KeyCode.PageUp))
                _manager.MoveSelected(Vector3.up * speed);
            if (Input.GetKey(KeyCode.PageDown))
                _manager.MoveSelected(Vector3.down * speed);
            if (Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.Equals))
                _manager.ChangeRadius(radiusSpeed);
            if (Input.GetKey(KeyCode.KeypadMinus) || Input.GetKey(KeyCode.Minus))
                _manager.ChangeRadius(-radiusSpeed);

            UpdatePreviewsForSelection();
        }

        public Vector3 GetPlayerPosition()
        {
            var player = GetLocalPlayer();
            if (player != null)
                return player.Position;
            var camera = Camera.main;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        public Vector3 GetPlayerRotation()
        {
            var player = GetLocalPlayer();
            if (player != null)
                return player.Transform.eulerAngles;
            var camera = Camera.main;
            return camera != null ? camera.transform.eulerAngles : Vector3.zero;
        }

        public Vector3 GetLookPosition()
        {
            var camera = Camera.main;
            if (camera == null)
                return GetPlayerPosition();
            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                return hit.point;
            return camera.transform.position + camera.transform.forward * 2f;
        }

        private IPlayer GetLocalPlayer()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null)
                return null;
            return world.RegisteredPlayers?.FirstOrDefault(p => p.IsYourPlayer);
        }

        private Player GetMainPlayer()
        {
            var world = Singleton<GameWorld>.Instance;
            return world?.MainPlayer;
        }

        private bool EnsureMapLoaded()
        {
            if (_manager.Data != null)
                return true;

            if (!string.IsNullOrEmpty(_currentMapId))
            {
                _manager.LoadMap(_currentMapId);
                return true;
            }

            var world = Singleton<GameWorld>.Instance;
            var mapId = world?.LocationId;
            if (string.IsNullOrEmpty(mapId) && world?.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId))
            {
                _currentMapId = mapId;
                _manager.LoadMap(mapId);
                return true;
            }

            Plugin.Log.LogWarning("Cannot create marker: no map/raid loaded.");
            return false;
        }

        public void CreateLootSpawn()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateLootSpawn(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateLootZone()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateLootZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateStaticObject()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateStaticObject(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateLootSpawnAtLook()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateLootSpawn(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateLootZoneAtLook()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateLootZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void PlaceStaticFromSceneGO(GameObject go)
        {
            if (!EnsureMapLoaded() || go == null) return;
            _manager.Snapshot();
            var marker = _manager.CreateStaticObject(GetLookPosition(), go.transform.rotation.eulerAngles);
            marker.name = go.name;
            marker.sourceObjectName = go.name;
            marker.sourceObjectPosition = TransformData.FromVector3(go.transform.position);
            _previews.RegisterStaticSource(marker.id, go);
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
            _ui?.RequestRefresh();
        }

        public void CreateStaticObjectAtLook()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateStaticObject(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateWTTQuestZone()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateWTTQuestZone(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateWTTStaticObject()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateWTTStaticObject(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateInteractiveObject(InteractiveObjectType type)
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateInteractiveObject(GetLookPosition(), GetPlayerRotation());
            marker.interactiveType = type;
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnPreviewForMarker(marker);
        }

        public void CreateExtractZone()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateExtractZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateBotSpawnPoint()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateBotSpawnPoint(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateBotSpawnZone()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateBotSpawnZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateLightZone()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateLightZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
            _previews.SpawnLightPreview(marker);
        }

        public void CreateTriggerZone()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateTriggerZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateOcclusionRepairVolume()
        {
            if (!EnsureMapLoaded()) return;
            _manager.Snapshot();
            var marker = _manager.CreateOcclusionRepairVolume(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public bool IsFreeCam => _freeCam;
        public bool IsFreeCamCursorLocked => _freeCamCursorLocked;

        public void CloseEditor()
        {
            _editorOpen = false;
        }

        public void GoToMarker(MarkerBase marker)
        {
            if (marker == null)
                return;
            if (!_freeCam)
                ToggleFreeCam();
            if (_freeCamCamera == null)
                return;

            var targetPos = marker.position.ToVector3();
            var offset = _freeCamCamera.transform.position - targetPos;
            if (offset.sqrMagnitude < 0.0001f)
                offset = Vector3.back;
            var cameraPos = targetPos + offset.normalized * 2f;
            _freeCamCamera.transform.position = cameraPos;
            _freeCamCamera.transform.LookAt(targetPos);
            _freeCamEuler = _freeCamCamera.transform.eulerAngles;
        }

        public void ToggleFreeCam()
        {
            _freeCam = !_freeCam;
            if (_freeCam)
                EnterFreeCam();
            else
                ExitFreeCam();
        }

        public void ToggleVanillaGizmos()
        {
            if (_renderer == null)
                return;
            _renderer.ShowVanillaGizmos = !_renderer.ShowVanillaGizmos;
            Plugin.Log.LogInfo($"Vanilla gizmos: {(_renderer.ShowVanillaGizmos ? "ON" : "OFF")}");
        }

        public void TogglePackGizmos()
        {
            if (_renderer == null)
                return;
            _renderer.ShowPackGizmos = !_renderer.ShowPackGizmos;
            Plugin.Log.LogInfo($"Pack gizmos: {(_renderer.ShowPackGizmos ? "ON" : "OFF")}");
        }

        public void DumpDoorIds()
        {
            DumpDoorIdsInternal(false);
        }

        public void DumpKeyedDoorIds()
        {
            DumpDoorIdsInternal(true);
        }

        private void DumpDoorIdsInternal(bool onlyWithKeys)
        {
            try
            {
                var world = Singleton<GameWorld>.Instance;
                if (world == null)
                {
                    var noRaidMsg = "DumpDoorIds: no active raid (GameWorld is null).";
                    Plugin.Log.LogWarning(noRaidMsg);
                    CustomEditorUI.LogOutput(noRaidMsg);
                    return;
                }

                var doorObjects = FindObjectsOfType<WorldInteractiveObject>()
                    .Where(o => o != null && o.GetComponent<Door>() != null)
                    .Where(o => !onlyWithKeys || !string.IsNullOrEmpty(o.KeyId))
                    .OrderBy(o => o.name)
                    .Select(o => new
                    {
                        id = o.Id,
                        keyId = o.KeyId,
                        name = o.name,
                        position = new
                        {
                            x = o.transform.position.x,
                            y = o.transform.position.y,
                            z = o.transform.position.z
                        }
                    })
                    .ToList();

                var suffix = onlyWithKeys ? "_keyed" : "";
                var path = Path.Combine(Plugin.ModDataDirectory, $"door_ids_dump{suffix}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = JsonConvert.SerializeObject(doorObjects, Formatting.Indented, new JsonSerializerSettings
                {
                    Culture = CultureInfo.InvariantCulture
                });
                File.WriteAllText(path, json);
                var successMsg = $"Dumped {doorObjects.Count} door IDs to {path}";
                Plugin.Log.LogInfo(successMsg);
                CustomEditorUI.LogOutput(successMsg);
            }
            catch (System.Exception ex)
            {
                var errorMsg = $"Failed to dump door IDs: {ex.Message}";
                Plugin.Log.LogWarning(errorMsg);
                CustomEditorUI.LogOutput(errorMsg);
            }
        }

        private void EnterFreeCam()
        {
            EnsureMapLoaded();

            _gameCamera = Camera.main;
            if (_gameCamera == null)
            {
                _freeCam = false;
                Plugin.Log.LogWarning("No main camera found; cannot enter editor mode.");
                return;
            }

            var go = UnityEngine.Object.Instantiate(_gameCamera.gameObject);
            go.name = "MLE_FreeCam";
            go.transform.position = _gameCamera.transform.position;
            go.transform.rotation = _gameCamera.transform.rotation;
            _freeCamCamera = go.GetComponent<Camera>();
            if (_freeCamCamera == null)
            {
                _freeCam = false;
                Plugin.Log.LogWarning("Cloned camera has no Camera component; cannot enter editor mode.");
                UnityEngine.Object.Destroy(go);
                return;
            }
            _freeCamCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _freeCamCamera.targetTexture = null;
            _freeCamCamera.tag = "MainCamera";
            _freeCamEuler = _gameCamera.transform.eulerAngles;

            var camLight = go.GetComponent<Light>();
            if (camLight == null)
            {
                camLight = go.AddComponent<Light>();
                camLight.type = LightType.Spot;
                camLight.range = 100f;
                camLight.spotAngle = 120f;
                camLight.intensity = 1.5f;
                camLight.color = Color.white;
                camLight.shadows = LightShadows.None;
            }

            var renderController = go.GetComponent<MapEditorFreecamRenderController>();
            if (renderController == null)
                renderController = go.AddComponent<MapEditorFreecamRenderController>();
            renderController.enabled = true;

            _gameCameraActive = _gameCamera.gameObject.activeSelf;
            _gameCamera.gameObject.tag = "Untagged";
            _gameCamera.gameObject.SetActive(false);

            _freeCamPlayer = GetMainPlayer();
            if (_freeCamPlayer != null)
            {
                _freeCamPlayerController = _freeCamPlayer.gameObject.GetComponent<CharacterController>();
                if (_freeCamPlayerController != null)
                    _freeCamPlayerController.enabled = false;

                _gamePlayerOwner = _freeCamPlayer.GetComponentInChildren<GamePlayerOwner>();
                if (_gamePlayerOwner != null)
                {
                    _originalGamePlayerOwnerEnabled = _gamePlayerOwner.enabled;
                    _gamePlayerOwner.enabled = false;
                }
            }

            var cullingObjects = FindObjectsOfType<DisablerCullingObjectBase>();
            _cullingObjects = new List<DisablerCullingObjectBase>(cullingObjects.Length);
            _cullingObjectsEnabled = new List<bool>(cullingObjects.Length);
            foreach (var cullingObject in cullingObjects)
            {
                if (cullingObject == null)
                    continue;
                _cullingObjects.Add(cullingObject);
                _cullingObjectsEnabled.Add(cullingObject.enabled);
                cullingObject.enabled = false;
                cullingObject.SetComponentsEnabled(true);
            }

            _freeCamCursorLocked = true;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            _editorModeActive = true;
        }

        private void ExitFreeCam()
        {
            if (_freeCamPlayer != null)
            {
                if (_freeCamCamera != null)
                    _freeCamPlayer.Transform.position = _freeCamCamera.transform.position;

                if (_gamePlayerOwner != null)
                    _gamePlayerOwner.enabled = _originalGamePlayerOwnerEnabled;

                if (_freeCamPlayerController != null)
                {
                    _freeCamPlayerController.enabled = true;
                    _freeCamPlayerController = null;
                }
                _freeCamPlayer = null;
            }

            if (_cullingObjects != null)
            {
                for (int i = 0; i < _cullingObjects.Count; i++)
                {
                    var cullingObject = _cullingObjects[i];
                    if (cullingObject == null)
                        continue;
                    cullingObject.enabled = _cullingObjectsEnabled[i];
                    cullingObject.UpdateComponentsStatusOnUpdate();
                }
                _cullingObjects = null;
                _cullingObjectsEnabled = null;
            }

            if (_freeCamCamera != null)
            {
                var renderController = _freeCamCamera.gameObject.GetComponent<MapEditorFreecamRenderController>();
                if (renderController != null)
                    renderController.enabled = false;

                _freeCamCamera.gameObject.tag = "Untagged";
                UnityEngine.Object.Destroy(_freeCamCamera.gameObject);
                _freeCamCamera = null;
            }
            if (_gameCamera != null)
            {
                _gameCamera.gameObject.tag = "MainCamera";
                _gameCamera.gameObject.SetActive(_gameCameraActive);
                _gameCamera = null;
            }

            _freeCamCursorLocked = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            _editorModeActive = false;
            _editorModeInvincibleEndTime = Time.time + 5f;
        }

        private void UpdateFreeCam()
        {
            if (_freeCamCamera == null)
                return;

            _freeCamEuler.x -= Input.GetAxis("Mouse Y") * FreeCamLookSpeed;
            _freeCamEuler.y += Input.GetAxis("Mouse X") * FreeCamLookSpeed;
            _freeCamCamera.transform.eulerAngles = _freeCamEuler;

            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? FreeCamFastSpeed : FreeCamSpeed;
            speed *= Time.deltaTime;

            var forward = _freeCamCamera.transform.forward;
            var right = _freeCamCamera.transform.right;
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += forward;
            if (Input.GetKey(KeyCode.S)) move -= forward;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) move -= Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move += Vector3.up;
            if (Input.GetKey(KeyCode.E)) move -= Vector3.up;

            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
                move += forward * scroll * 100f;

            if (move.sqrMagnitude > 0.001f)
                _freeCamCamera.transform.position += move.normalized * speed;
        }

        private void ToggleFreeCamCursor()
        {
            _freeCamCursorLocked = !_freeCamCursorLocked;
        }

        private void ToggleMenuCursor()
        {
            _menuCursorLocked = !_menuCursorLocked;
            if (!_menuCursorLocked)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    _menuUnlockedCameraRotation = cam.transform.rotation;
                    _menuUnlockedCameraRotationCaptured = true;
                }
            }
        }

        private void UpdateGameInputFocus()
        {
            if (!_editorOpen)
            {
                if (_gameInputPaused)
                    ResumeGameInput();
                return;
            }

            var cursorUnlocked = _freeCam ? !_freeCamCursorLocked : !_menuCursorLocked;
            if (cursorUnlocked)
            {
                if (!_gameInputPaused)
                    PauseGameInput();

                var modEventSystem = _ui?.ModEventSystem;
                if (modEventSystem != null && EventSystem.current != modEventSystem)
                    EventSystem.current = modEventSystem;
            }
            else if (_gameInputPaused)
            {
                ResumeGameInput();
            }
        }

        private void PauseGameInput()
        {
            _gameInputPaused = true;

            var modEventSystem = _ui?.ModEventSystem;
            if (modEventSystem == null)
                return;

            foreach (var es in FindObjectsOfType<EventSystem>())
            {
                if (es == modEventSystem || !es.enabled)
                    continue;
                es.enabled = false;
                _disabledEventSystems.Add(es);
            }
            modEventSystem.gameObject.SetActive(true);
            modEventSystem.enabled = true;
            EventSystem.current = modEventSystem;
        }

        private void ResumeGameInput()
        {
            if (_gameInputPaused)
                _gameInputPaused = false;

            var modEventSystem = _ui?.ModEventSystem;
            if (modEventSystem != null)
            {
                modEventSystem.enabled = false;
                modEventSystem.gameObject.SetActive(false);
                if (EventSystem.current == modEventSystem)
                    EventSystem.current = null;
            }
            foreach (var es in _disabledEventSystems)
            {
                if (es != null)
                    es.enabled = true;
            }
            _disabledEventSystems.Clear();
        }

        private void HandleMouseInput()
        {
            if (_freeCam && _freeCamCursorLocked)
                return;
            if (IsMouseOverUI())
                return;
            if (_ui?.IsDeletePending == true)
                return;

            if (_ui?.IsPickingSource == true)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    var picked = _ui.TryPickSourceSceneObject();
                    var target = _ui.PickingSourceTarget;
                    if (picked != null && target != null)
                    {
                        target.sourceObjectName = picked.name;
                        target.sourceObjectPosition = TransformData.FromVector3(picked.transform.position);
                        if (target is MarkerBase markerBase)
                        {
                            markerBase.rotation = TransformData.FromVector3(picked.transform.rotation.eulerAngles);
                            _previews.RegisterStaticSource(markerBase.id, picked);
                            _previews.SpawnPreviewForMarker(markerBase);
                        }
                        _manager.IsDirty = true;
                    }
                    _ui.ClearPickingSource();
                }
                return;
            }

            if (_ui?.IsPickingScatter == true)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    var picked = _ui.TryPickSourceSceneObject();
                    if (picked != null)
                        _ui.OnScatterObjectPicked(picked.name);
                    else
                        _ui.ClearPickingScatter();
                }
                return;
            }

            if (_ui?.IsPickingSceneGO == true)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    var picked = _ui.TryPickSourceSceneObject();
                    if (picked != null)
                        _ui.SelectSceneGO(picked);
                    _ui.SetPickingSceneGO(false);
                }
                return;
            }

            var camera = Camera.main;
            if (camera == null)
                return;

            var hoveredAxis = _renderer.PickGizmoAxis(camera, Input.mousePosition, 25f);

            if (Input.GetMouseButtonDown(0))
            {
                _manager.Snapshot();
                if (hoveredAxis != GizmoAxis.None)
                {
                    _activeGizmoAxis = hoveredAxis;
                    _renderer.ActiveAxis = hoveredAxis;
                    RecordGizmoDragStart(camera);
                }
                else
                {
                    var picked = _renderer.PickFromScreenPosition(camera, Input.mousePosition, 50f);
                    if (picked != null)
                    {
                        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                            _manager.ToggleSelected(picked);
                        else
                            _manager.SelectOnly(picked);
                        _mouseDragging = true;
                        RecordDragStartPositions();
                    }
                    else if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                    {
                        _manager.ClearSelection();
                    }
                }
            }

            if (Input.GetMouseButton(0) && _manager.Selected != null && !_manager.Selected.isVanilla)
            {
                if (_activeGizmoAxis != GizmoAxis.None)
                {
                    ApplyGizmoDrag(camera);
                }
                else if (_mouseDragging)
                {
                    var markerPos = _manager.Selected.position.ToVector3();
                    var plane = new Plane(Vector3.up, markerPos);
                    var ray = camera.ScreenPointToRay(Input.mousePosition);
                    if (plane.Raycast(ray, out float distance))
                    {
                        var point = ray.GetPoint(distance);
                        var newPos = new Vector3(point.x, markerPos.y, point.z);
                        var delta = newPos - _gizmoDragStartPositions[_manager.Selected.id];
                        if (_manager.SelectedIds.Count > 1)
                        {
                            foreach (var id in _manager.SelectedIds)
                            {
                                var m = _manager.FindById(id);
                                if (m == null) continue;
                                m.position = TransformData.FromVector3(_gizmoDragStartPositions[id] + delta);
                            }
                        }
                        else
                        {
                            _manager.Selected.position = TransformData.FromVector3(newPos);
                        }
                        UpdatePreviewsForSelection();
                        _manager.IsDirty = true;
                    }
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                bool wasDragging = _activeGizmoAxis != GizmoAxis.None || _mouseDragging;
                _activeGizmoAxis = GizmoAxis.None;
                _renderer.ActiveAxis = GizmoAxis.None;
                _mouseDragging = false;
                if (wasDragging)
                {
                    UpdatePreviewsForSelection();
                    _ui?.RequestInspectorRefresh();
                }
            }
        }

        private void ApplyGizmoDrag(Camera camera)
        {
            if (!GetGizmoDragPlane(camera, _activeGizmoAxis, out _gizmoDragPlane))
                return;

            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!_gizmoDragPlane.Raycast(ray, out float d))
                return;

            var point = ray.GetPoint(d);
            var axisDir = GetGizmoAxisDirection(_activeGizmoAxis, GetGizmoOrientation());
            var offset = Vector3.Dot(point - _gizmoDragStartWorld, axisDir);
            var isGroup = _manager.SelectedIds.Count > 1;

            switch (_gizmoMode)
            {
                case GizmoMode.Translate:
                    var newCenter = _gizmoDragStartCenter + axisDir * offset;
                    var delta = newCenter - _gizmoDragStartCenter;
                    if (isGroup)
                    {
                        foreach (var id in _manager.SelectedIds)
                        {
                            var m = _manager.FindById(id);
                            if (m == null) continue;
                            m.position = TransformData.FromVector3(_gizmoDragStartPositions[id] + delta);
                        }
                    }
                    else
                    {
                        _manager.Selected.position = TransformData.FromVector3(_gizmoDragStartPositions[_manager.Selected.id] + delta);
                    }
                    break;

                case GizmoMode.Rotate:
                    var v0 = Vector3.ProjectOnPlane(_gizmoDragStartWorld - _gizmoDragStartCenter, axisDir);
                    var v1 = Vector3.ProjectOnPlane(point - _gizmoDragStartCenter, axisDir);
                    if (v0.sqrMagnitude > 0.0001f && v1.sqrMagnitude > 0.0001f)
                    {
                        var angle = Vector3.SignedAngle(v0, v1, axisDir);
                        var deltaRot = Quaternion.AngleAxis(angle, axisDir);
                        if (isGroup)
                        {
                            foreach (var id in _manager.SelectedIds)
                            {
                                var m = _manager.FindById(id);
                                if (m == null) continue;
                                var startPos = _gizmoDragStartPositions[id];
                                var startRot = _gizmoDragStartRotations[id];
                                m.position = TransformData.FromVector3(_gizmoDragStartCenter + deltaRot * (startPos - _gizmoDragStartCenter));
                                m.rotation = TransformData.FromVector3((deltaRot * startRot).eulerAngles);
                            }
                        }
                        else
                        {
                            var newRot = deltaRot * Quaternion.Euler(_gizmoDragStartMarkerRot);
                            _manager.Selected.rotation = TransformData.FromVector3(newRot.eulerAngles);
                        }
                    }
                    break;

                case GizmoMode.Scale:
                    var newCenterScale = _gizmoDragStartCenterScale;
                    switch (_activeGizmoAxis)
                    {
                        case GizmoAxis.X: newCenterScale.x += offset; break;
                        case GizmoAxis.Y: newCenterScale.y += offset; break;
                        case GizmoAxis.Z: newCenterScale.z += offset; break;
                    }
                    var centerScaleDelta = newCenterScale - _gizmoDragStartCenterScale;
                    var centerScaleFactor = new Vector3(
                        Mathf.Abs(_gizmoDragStartCenterScale.x) > 0.0001f ? newCenterScale.x / _gizmoDragStartCenterScale.x : 1f,
                        Mathf.Abs(_gizmoDragStartCenterScale.y) > 0.0001f ? newCenterScale.y / _gizmoDragStartCenterScale.y : 1f,
                        Mathf.Abs(_gizmoDragStartCenterScale.z) > 0.0001f ? newCenterScale.z / _gizmoDragStartCenterScale.z : 1f);

                    if (isGroup)
                    {
                        foreach (var id in _manager.SelectedIds)
                        {
                            var m = _manager.FindById(id);
                            if (m == null) continue;
                            var startPos = _gizmoDragStartPositions[id];
                            var startScale = _gizmoDragStartScales[id];
                            m.position = TransformData.FromVector3(_gizmoDragStartCenter + Vector3.Scale(startPos - _gizmoDragStartCenter, centerScaleFactor));
                            if (m is StaticObject so)
                                so.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is LootZone zone)
                                zone.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is WTTQuestZone qz)
                                qz.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is WTTStaticObject wso)
                                wso.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is InteractiveObject io)
                                io.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is ExtractZone mez)
                                mez.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is BotSpawnZone mbz)
                                mbz.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is TriggerZone mtz)
                                mtz.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                            else if (m is OcclusionRepairVolume morv)
                                morv.scale = TransformData.FromVector3(startScale + centerScaleDelta);
                        }
                        _renderer.Rebuild();
                    }
                    else
                    {
                        var newScale = _gizmoDragStartMarkerScale;
                        switch (_activeGizmoAxis)
                        {
                            case GizmoAxis.X: newScale.x += offset; break;
                            case GizmoAxis.Y: newScale.y += offset; break;
                            case GizmoAxis.Z: newScale.z += offset; break;
                        }
                        if (_manager.Selected is StaticObject so)
                        {
                            so.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is LootZone zone)
                        {
                            zone.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is WTTQuestZone qz)
                        {
                            qz.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is WTTStaticObject wso)
                        {
                            wso.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is InteractiveObject io)
                        {
                            io.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is ExtractZone ez)
                        {
                            ez.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is BotSpawnZone bz)
                        {
                            bz.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is TriggerZone tz)
                        {
                            tz.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                        else if (_manager.Selected is OcclusionRepairVolume orv)
                        {
                            orv.scale = TransformData.FromVector3(newScale);
                            _renderer.Rebuild();
                        }
                    }
                    break;
            }

            UpdatePreviewsForSelection();
            _manager.IsDirty = true;
        }

        private void UpdatePreviewsForSelection()
        {
            foreach (var id in _manager.SelectedIds)
            {
                var m = _manager.FindById(id);
                if (m != null)
                    _previews.UpdateForMarker(m);
            }
        }

        private void RecordDragStartPositions()
        {
            _gizmoDragStartPositions.Clear();
            foreach (var id in _manager.SelectedIds)
            {
                var m = _manager.FindById(id);
                if (m != null)
                    _gizmoDragStartPositions[id] = m.position.ToVector3();
            }
        }

        private void RecordGizmoDragStart(Camera camera)
        {
            _gizmoDragStartMarkerPos = _manager.Selected.position.ToVector3();
            _gizmoDragStartMarkerRot = _manager.Selected.rotation.ToVector3();
            _gizmoDragStartMarkerScale = Vector3.one;
            if (_manager.Selected is StaticObject so)
                _gizmoDragStartMarkerScale = so.scale.ToVector3();
            else if (_manager.Selected is LootZone zone)
                _gizmoDragStartMarkerScale = zone.scale.ToVector3();
            else if (_manager.Selected is WTTQuestZone qz)
                _gizmoDragStartMarkerScale = qz.scale.ToVector3();
            else if (_manager.Selected is WTTStaticObject wso)
                _gizmoDragStartMarkerScale = wso.scale.ToVector3();
            else if (_manager.Selected is InteractiveObject io)
                _gizmoDragStartMarkerScale = io.scale.ToVector3();
            else if (_manager.Selected is ExtractZone ez)
                _gizmoDragStartMarkerScale = ez.scale?.ToVector3() ?? Vector3.one;
            else if (_manager.Selected is BotSpawnZone bz)
                _gizmoDragStartMarkerScale = bz.scale?.ToVector3() ?? Vector3.one;
            else if (_manager.Selected is TriggerZone tz)
                _gizmoDragStartMarkerScale = tz.scale?.ToVector3() ?? Vector3.one;
            else if (_manager.Selected is OcclusionRepairVolume orv)
                _gizmoDragStartMarkerScale = orv.scale?.ToVector3() ?? Vector3.one;

            _gizmoDragStartCenter = _manager.SelectedIds.Count > 1 ? _manager.SelectionCenter : _gizmoDragStartMarkerPos;
            _gizmoDragStartCenterRot = _manager.SelectedIds.Count > 1 ? Quaternion.identity : _manager.Selected.rotation.ToQuaternion();
            _gizmoDragStartCenterScale = Vector3.one;

            _gizmoDragStartPositions.Clear();
            _gizmoDragStartRotations.Clear();
            _gizmoDragStartScales.Clear();
            foreach (var id in _manager.SelectedIds)
            {
                var m = _manager.FindById(id);
                if (m == null) continue;
                _gizmoDragStartPositions[id] = m.position.ToVector3();
                _gizmoDragStartRotations[id] = m.rotation.ToQuaternion();
                if (m is StaticObject mso)
                    _gizmoDragStartScales[id] = mso.scale.ToVector3();
                else if (m is LootZone mlz)
                    _gizmoDragStartScales[id] = mlz.scale.ToVector3();
                else if (m is WTTQuestZone mqz)
                    _gizmoDragStartScales[id] = mqz.scale.ToVector3();
                else if (m is WTTStaticObject mwso)
                    _gizmoDragStartScales[id] = mwso.scale.ToVector3();
                else if (m is InteractiveObject mio)
                    _gizmoDragStartScales[id] = mio.scale.ToVector3();
                else if (m is ExtractZone mez)
                    _gizmoDragStartScales[id] = mez.scale?.ToVector3() ?? Vector3.one;
                else if (m is BotSpawnZone mbz)
                    _gizmoDragStartScales[id] = mbz.scale?.ToVector3() ?? Vector3.one;
                else if (m is TriggerZone mtz)
                    _gizmoDragStartScales[id] = mtz.scale?.ToVector3() ?? Vector3.one;
                else if (m is OcclusionRepairVolume morv)
                    _gizmoDragStartScales[id] = morv.scale?.ToVector3() ?? Vector3.one;
                else
                    _gizmoDragStartScales[id] = Vector3.one;
            }

            if (GetGizmoDragPlane(camera, _activeGizmoAxis, out _gizmoDragPlane))
            {
                var ray = camera.ScreenPointToRay(Input.mousePosition);
                if (_gizmoDragPlane.Raycast(ray, out float d))
                    _gizmoDragStartWorld = ray.GetPoint(d);
                else
                    _gizmoDragStartWorld = _gizmoDragStartCenter;
            }
        }

        private Quaternion GetGizmoOrientation()
        {
            if (_manager.SelectedIds.Count > 1)
                return Quaternion.identity;
            return _manager.Selected?.rotation.ToQuaternion() ?? Quaternion.identity;
        }

        private bool GetGizmoDragPlane(Camera camera, GizmoAxis axis, out Plane plane)
        {
            plane = new Plane();
            if (_manager.Selected == null)
                return false;

            var axisDir = GetGizmoAxisDirection(axis, GetGizmoOrientation());
            if (axisDir == Vector3.zero)
                return false;

            var pos = _manager.SelectedIds.Count > 1 ? _manager.SelectionCenter : _manager.Selected.position.ToVector3();
            plane = new Plane(camera.transform.forward, pos);
            return true;
        }

        private Vector3 GetGizmoAxisDirection(GizmoAxis axis, Quaternion localRotation)
        {
            Vector3 dir;
            switch (axis)
            {
                case GizmoAxis.X: dir = Vector3.right; break;
                case GizmoAxis.Y: dir = Vector3.up; break;
                case GizmoAxis.Z: dir = Vector3.forward; break;
                default: return Vector3.zero;
            }
            return localRotation * dir;
        }

        public void SetGizmoMode(GizmoMode mode)
        {
            _gizmoMode = mode;
            _renderer.GizmoMode = mode;
        }

        private bool IsMouseOverUI()
        {
            return _ui != null && _ui.IsMouseOverMainWindow();
        }

        private MarkerBase PickMarkerFromMouse()
        {
            var camera = Camera.main;
            if (camera == null)
                return null;
            return _renderer.PickFromScreenPosition(camera, Input.mousePosition, 50f);
        }

        public void Save() => _manager.Save();

        public void SnapSelected()
        {
            _manager.Snapshot();
            _manager.SnapSelectedToGround();
            _renderer.Rebuild();
        }

        public void DuplicateSelected()
        {
            _manager.Snapshot();
            _manager.DuplicateSelection();
            _renderer.Rebuild();
            _previews.SpawnAllPreviews(_manager.Data);
        }

        public void DeleteSelected()
        {
            foreach (var id in _manager.SelectedIds.ToList())
                _previews.ClearByMarkerId(id);
            _manager.DeleteSelection();
            _renderer.Rebuild();
        }

        public void ClearPreviews() => _previews.ClearAll();
        public void ClearVisuals() => _renderer.Clear();

        public void ImportVanillaLoot()
        {
            if (string.IsNullOrEmpty(_currentMapId))
            {
                Plugin.Log.LogWarning("Cannot import vanilla loot: no map loaded.");
                return;
            }

            var vanillaData = VanillaImporter.Import(_currentMapId);
            if (vanillaData == null)
                return;

            _manager.VanillaData = vanillaData;
            _renderer.Rebuild();
            Plugin.Log.LogInfo($"Imported vanilla loot for {_currentMapId}.");
            _ui?.RequestRefresh();
        }

        public void Undo()
        {
            _manager.Undo();
            _renderer.Rebuild();
            _previews.ClearAll();
            _previews.SpawnAllPreviews(_manager.Data);
        }

        public void Redo()
        {
            _manager.Redo();
            _renderer.Rebuild();
            _previews.ClearAll();
            _previews.SpawnAllPreviews(_manager.Data);
        }

        public void SavePrefab(string name, string description)
        {
            if (_manager.Selected == null || _manager.SelectedIds.Count == 0)
            {
                Plugin.Log.LogWarning("No markers selected to save as prefab.");
                return;
            }

            var pivot = _manager.Selected;
            var pivotPos = pivot.position.ToVector3();
            var pivotRot = pivot.rotation.ToQuaternion();
            var prefab = new PrefabData { name = name, description = description };

            foreach (var id in _manager.SelectedIds)
            {
                var marker = _manager.FindById(id);
                if (marker == null)
                    continue;

                var localPos = Quaternion.Inverse(pivotRot) * (marker.position.ToVector3() - pivotPos);
                var localRot = Quaternion.Inverse(pivotRot) * marker.rotation.ToQuaternion();

                var data = JObject.FromObject(marker);
                data["id"] = null;
                data["position"] = JObject.FromObject(TransformData.FromVector3(localPos));
                data["rotation"] = JObject.FromObject(TransformData.FromVector3(localRot.eulerAngles));
                prefab.markers.Add(new PrefabEntry { kind = marker.Kind.ToString(), data = data });
            }

            PrefabStorage.Save(prefab);
            Plugin.Log.LogInfo($"Saved prefab '{name}' with {prefab.markers.Count} markers.");
        }

        public void PlacePrefab(string name)
        {
            var prefab = PrefabStorage.Load(name);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"Prefab '{name}' not found.");
                return;
            }
            if (!EnsureMapLoaded())
                return;

            _manager.Snapshot();
            var pivotPos = GetLookPosition();
            var pivotRot = Quaternion.Euler(GetPlayerRotation());
            var created = new List<MarkerBase>();

            foreach (var entry in prefab.markers)
            {
                MarkerBase copy;
                switch (entry.kind)
                {
                    case "LooseLoot":
                        copy = entry.data.ToObject<LooseLootSpawn>();
                        break;
                    case "LootZone":
                        copy = entry.data.ToObject<LootZone>();
                        break;
                    case "StaticObject":
                        copy = entry.data.ToObject<StaticObject>();
                        break;
                    case "WTTQuestZone":
                        copy = entry.data.ToObject<WTTQuestZone>();
                        break;
                    case "WTTStaticObject":
                        copy = entry.data.ToObject<WTTStaticObject>();
                        break;
                    case "InteractiveObject":
                        copy = entry.data.ToObject<InteractiveObject>();
                        break;
                    case "ExtractZone":
                        copy = entry.data.ToObject<ExtractZone>();
                        break;
                    case "BotSpawnPoint":
                        copy = entry.data.ToObject<BotSpawnPoint>();
                        break;
                    case "BotSpawnZone":
                        copy = entry.data.ToObject<BotSpawnZone>();
                        break;
                    case "LightZone":
                        copy = entry.data.ToObject<LightZone>();
                        break;
                    case "TriggerZone":
                        copy = entry.data.ToObject<TriggerZone>();
                        break;
                    case "OcclusionRepairVolume":
                        copy = entry.data.ToObject<OcclusionRepairVolume>();
                        break;
                    default:
                        continue;
                }

                copy.id = Guid.NewGuid().ToString();
                copy.name = copy.name + "_prefab";
                var localPos = copy.position.ToVector3();
                var localRotEuler = copy.rotation.ToVector3();
                var localRot = Quaternion.Euler(localRotEuler);
                copy.position = TransformData.FromVector3(pivotPos + pivotRot * localPos);
                copy.rotation = TransformData.FromVector3((pivotRot * localRot).eulerAngles);
                _manager.AddMarker(copy);
                created.Add(copy);
                _previews.SpawnPreviewForMarker(copy);
            }

            _manager.SetSelection(created);
            if (created.Count > 0)
                _manager.SetGroupOnSelection(name);
            _renderer.Rebuild();
            _manager.IsDirty = true;
            Plugin.Log.LogInfo($"Placed prefab '{name}' with {created.Count} markers.");
        }

        public void ScatterObjectsInZone(LootZone zone, string prefabPath, int count, float minHeight, float maxHeight, bool snapToGround)
        {
            if (!EnsureMapLoaded()) return;
            if (zone == null || string.IsNullOrWhiteSpace(prefabPath))
            {
                Plugin.Log.LogWarning("Scatter requires a zone and a prefab path.");
                return;
            }

            _manager.Snapshot();
            var group = $"scatter_{zone.name}";
            var created = new List<MarkerBase>();
            for (int i = 0; i < count; i++)
            {
                var point = LootPreviewSpawner.GetRandomPointInZone(zone);
                var position = point;
                if (snapToGround)
                {
                    var ground = MarkerManager.GetGroundPosition(position);
                    if (ground.HasValue)
                        position.y = ground.Value.y;
                }
                position.y += UnityEngine.Random.Range(minHeight, maxHeight);
                var rotation = new Vector3(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var marker = _manager.CreateStaticObject(position, rotation);
                marker.prefabPath = prefabPath;
                marker.group = group;
                marker.name = "scatter_object";
                created.Add(marker);
                _previews.SpawnPreviewForMarker(marker);
            }
            _manager.SetSelection(created);
            _renderer.Rebuild();
            _manager.IsDirty = true;
            Plugin.Log.LogInfo($"Scattered {created.Count} objects in zone '{zone.name}'.");
        }

        public void CopySelected()
        {
            if (_manager.SelectedIds.Count == 0)
                return;

            var entries = new List<ClipboardEntry>();
            foreach (var id in _manager.SelectedIds)
            {
                var m = _manager.FindById(id);
                if (m == null)
                    continue;
                entries.Add(new ClipboardEntry
                {
                    kind = m.Kind.ToString(),
                    data = JObject.FromObject(m)
                });
            }

            if (entries.Count == 0)
                return;

            GUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(new ClipboardData { entries = entries });
        }

        public void Paste()
        {
            if (!EnsureMapLoaded())
                return;

            var json = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                var data = JsonConvert.DeserializeObject<ClipboardData>(json);
                if (data?.entries == null || data.entries.Count == 0)
                    return;

                _manager.Snapshot();

                var copies = new List<MarkerBase>();
                var center = _manager.SelectionCenter;
                var pastePos = GetLookPosition() + Vector3.up * 0.5f;
                var offset = pastePos - center;

                foreach (var entry in data.entries)
                {
                    var copy = CreateMarkerFromClipboard(entry);
                    if (copy == null)
                        continue;

                    copy.id = Guid.NewGuid().ToString();
                    copy.name = copy.name + "_paste";
                    copy.position = TransformData.FromVector3(copy.position.ToVector3() + offset);
                    _manager.AddMarker(copy);
                    copies.Add(copy);
                }

                if (copies.Count > 0)
                {
                    foreach (var c in copies)
                        c.isVanilla = false;
                    _manager.SetSelection(copies);
                    _manager.IsDirty = true;
                    _renderer.Rebuild();
                    foreach (var c in copies)
                        _previews.SpawnPreviewForMarker(c);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Paste failed: {ex.Message}");
            }
        }

        private MarkerBase CreateMarkerFromClipboard(ClipboardEntry entry)
        {
            switch (entry.kind)
            {
                case "LooseLoot": return entry.data.ToObject<LooseLootSpawn>();
                case "LootZone": return entry.data.ToObject<LootZone>();
                case "StaticObject": return entry.data.ToObject<StaticObject>();
                case "WTTQuestZone": return entry.data.ToObject<WTTQuestZone>();
                case "WTTStaticObject": return entry.data.ToObject<WTTStaticObject>();
                case "InteractiveObject": return entry.data.ToObject<InteractiveObject>();
                case "ExtractZone": return entry.data.ToObject<ExtractZone>();
                case "BotSpawnPoint": return entry.data.ToObject<BotSpawnPoint>();
                case "BotSpawnZone": return entry.data.ToObject<BotSpawnZone>();
                case "LightZone": return entry.data.ToObject<LightZone>();
                case "TriggerZone": return entry.data.ToObject<TriggerZone>();
                case "OcclusionRepairVolume": return entry.data.ToObject<OcclusionRepairVolume>();
                default: return null;
            }
        }

        private class ClipboardData
        {
            public List<ClipboardEntry> entries = new List<ClipboardEntry>();
        }

        private class ClipboardEntry
        {
            public string kind;
            public JObject data;
        }

        public string CurrentMapId => _currentMapId;

        public void ExportPack(string packName)
        {
            if (string.IsNullOrEmpty(_currentMapId) || _manager.Data == null)
            {
                var noMapMsg = "Cannot export pack: no map loaded.";
                Plugin.Log.LogWarning(noMapMsg);
                CustomEditorUI.LogOutput(noMapMsg);
                return;
            }

            var safeName = SanitizePackName(packName);
            if (string.IsNullOrEmpty(safeName))
            {
                var invalidMsg = "Cannot export pack: name is empty or invalid.";
                Plugin.Log.LogWarning(invalidMsg);
                CustomEditorUI.LogOutput(invalidMsg);
                return;
            }

            var exportsDir = Plugin.ServerModExportsDirectory;
            Directory.CreateDirectory(exportsDir);
            var path = Path.Combine(exportsDir, safeName + ".json");

            PackData pack = null;
            if (File.Exists(path))
            {
                try
                {
                    pack = JsonConvert.DeserializeObject<PackData>(File.ReadAllText(path), new JsonSerializerSettings
                    {
                        Culture = CultureInfo.InvariantCulture
                    });
                }
                catch (Exception ex)
                {
                    var readMsg = $"Failed to read existing pack {path}: {ex.Message}";
                    Plugin.Log.LogWarning(readMsg);
                    CustomEditorUI.LogOutput(readMsg);
                }
            }

            if (pack == null)
            {
                pack = new PackData
                {
                    name = packName,
                    author = "",
                    version = "1.0.0",
                    maps = new System.Collections.Generic.Dictionary<string, MapData>(System.StringComparer.OrdinalIgnoreCase)
                };
            }

            pack.name = packName;
            pack.maps[_currentMapId] = _manager.Data;

            var settings = new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                Formatting = Formatting.Indented
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(pack, settings));
            var successMsg = $"Exported pack '{safeName}' to {path}";
            Plugin.Log.LogInfo(successMsg);
            CustomEditorUI.LogOutput(successMsg);
        }

        private static string SanitizePackName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var trimmed = name.Trim();
            var fileName = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            var clean = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            clean = clean.Trim('.', ' ');
            if (string.IsNullOrWhiteSpace(clean))
                return string.Empty;

            return clean;
        }
    }
}
