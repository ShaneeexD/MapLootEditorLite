using System;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class MapEditorController : MonoBehaviour
    {
        private MarkerManager _manager;
        private MarkerRenderer _renderer;
        private EditorUI _ui;
        private LootPreviewSpawner _previews;
        private GameObject _visualRoot;
        private GameObject _previewRoot;

        private bool _editorOpen;
        private KeyCode _toggleKey => Plugin.ToggleKey;
        private GameWorld _currentGameWorld;
        private string _currentMapId;
        private float _autoSaveTimer;
        private int _lastToggleFrame = -1;

        private bool _freeCam;
        private Camera _freeCamCamera;
        private Camera _gameCamera;
        private Vector3 _freeCamEuler;
        private const float _freeCamSpeed = 5f;
        private const float _freeCamFastSpeed = 15f;
        private const float _freeCamLookSpeed = 2f;

        private bool _mouseDragging;

        public static bool FreeCamInvulnerable { get; private set; }
        private static readonly Harmony _freecamHarmony = new Harmony("com.maplooteditorlite.freecam");
        private static bool _freecamPatchesApplied;

        private bool _freeCamCursorLocked = true;
        private Player _freeCamPlayer;
        private CharacterController _freeCamPlayerController;
        private Vector3 _freeCamPlayerStartPosition;

        private GizmoMode _gizmoMode = GizmoMode.Translate;
        private GizmoAxis _activeGizmoAxis;
        private Vector3 _gizmoDragStartWorld;
        private Vector3 _gizmoDragStartMarkerPos;
        private Vector3 _gizmoDragStartMarkerRot;
        private Vector3 _gizmoDragStartMarkerScale;
        private Plane _gizmoDragPlane;

        private void Awake()
        {
            _manager = new MarkerManager();
            _visualRoot = new GameObject("MLE_VisualRoot");
            _visualRoot.transform.SetParent(transform, false);
            _previewRoot = new GameObject("MLE_PreviewRoot");
            _previewRoot.transform.SetParent(transform, false);

            _renderer = new MarkerRenderer(_manager, _visualRoot);
            _previews = new LootPreviewSpawner(_previewRoot);
            _ui = new EditorUI(this, _manager, _renderer, _previews);
            Plugin.Log.LogInfo($"MapEditorController awake: enabled={enabled} active={gameObject.activeInHierarchy} key={_toggleKey}");
            TryApplyFreeCamPatches();
        }

        private void TryApplyFreeCamPatches()
        {
            if (_freecamPatchesApplied)
                return;
            _freecamPatchesApplied = true;

            PatchMethod("ApplyDamage", nameof(ApplyDamagePrefix));
            PatchMethod("ChangeHealth", nameof(ChangeHealthPrefix));
        }

        private void PatchMethod(string methodName, string prefixName)
        {
            try
            {
                var method = AccessTools.Method(typeof(ActiveHealthController), methodName);
                if (method != null)
                {
                    _freecamHarmony.Patch(method, prefix: new HarmonyMethod(typeof(MapEditorController), prefixName));
                    Plugin.Log.LogInfo($"Patched ActiveHealthController.{methodName} for freecam invulnerability.");
                }
                else
                {
                    Plugin.Log.LogWarning($"Could not find ActiveHealthController.{methodName} to patch.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to patch ActiveHealthController.{methodName}: {ex.Message}");
            }
        }

        public static bool ApplyDamagePrefix(ActiveHealthController __instance, Player ___Player, ref float damage, EBodyPart bodyPart, DamageInfoStruct damageInfo)
        {
            if (!FreeCamInvulnerable || ___Player == null || !___Player.IsYourPlayer)
                return true;

            damage = 0f;
            return true;
        }

        public static bool ChangeHealthPrefix(ActiveHealthController __instance, Player ___Player, EBodyPart bodyPart, ref float value, DamageInfoStruct damageInfo)
        {
            if (!FreeCamInvulnerable || ___Player == null || !___Player.IsYourPlayer)
                return true;

            if (value < 0f)
                value = 0f;
            return true;
        }

        private void Update()
        {
            try { DetectRaid(); }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"DetectRaid failed: {ex.Message}");
            }

            if (Input.GetKeyDown(_toggleKey) && _lastToggleFrame != Time.frameCount)
            {
                _editorOpen = !_editorOpen;
                _lastToggleFrame = Time.frameCount;
                Plugin.Log.LogInfo($"Toggle key pressed via Input: editor open = {_editorOpen}");
            }

            if (Plugin.PlaceLootSpawnHotkey?.Value.IsPressed() == true && _currentGameWorld != null)
                CreateLootSpawn();
            if (Plugin.PlaceLootZoneHotkey?.Value.IsPressed() == true && _currentGameWorld != null)
                CreateLootZone();
            if (Plugin.PlaceStaticObjectHotkey?.Value.IsPressed() == true && _currentGameWorld != null)
                CreateStaticObject();
            if (Plugin.PlaceLootSpawnAtLookHotkey?.Value.IsPressed() == true && _currentGameWorld != null)
                CreateLootSpawnAtLook();
            if (Plugin.PlaceLootZoneAtLookHotkey?.Value.IsPressed() == true && _currentGameWorld != null)
                CreateLootZoneAtLook();
            if (Plugin.PlaceStaticObjectAtLookHotkey?.Value.IsPressed() == true && _currentGameWorld != null)
                CreateStaticObjectAtLook();

            if (!_editorOpen && _freeCam)
                ToggleFreeCam();

            if (_editorOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    SetGizmoMode(GizmoMode.Translate);
                if (Input.GetKeyDown(KeyCode.Alpha2))
                    SetGizmoMode(GizmoMode.Rotate);
                if (Input.GetKeyDown(KeyCode.Alpha3))
                    SetGizmoMode(GizmoMode.Scale);

                if (_freeCam)
                {
                    if (Input.GetMouseButtonDown(2))
                        ToggleFreeCamCursor();

                    if (_freeCamCursorLocked)
                    {
                        Cursor.visible = false;
                        Cursor.lockState = CursorLockMode.Locked;
                        UpdateFreeCam();
                    }
                    else
                    {
                        Cursor.visible = true;
                        Cursor.lockState = CursorLockMode.None;
                        HandleMouseInput();
                    }

                    UpdateFreeCamPlayer();
                }
                else
                {
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                    HandleSelectionInput();
                    HandleMovementInput();
                    HandleMouseInput();
                }
            }

            _renderer.ShowGizmo = !_freeCam || !_freeCamCursorLocked;
            _renderer.Update();

            if (_editorOpen && _manager.Selected != null)
            {
                if (_manager.Selected is LooseLootSpawn spawn)
                    _previews.UpdateSelected(spawn);
                else if (_manager.Selected is LootZone zone)
                    _previews.UpdateSelected(zone);
            }

            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer > 30f)
            {
                _autoSaveTimer = 0f;
                if (_manager.IsDirty)
                    _manager.Save();
            }
        }

        private void OnGUI()
        {
            var ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == _toggleKey && _lastToggleFrame != Time.frameCount)
            {
                _editorOpen = !_editorOpen;
                _lastToggleFrame = Time.frameCount;
                Plugin.Log.LogInfo($"Toggle key pressed via GUI: editor open = {_editorOpen}");
            }

            if (_editorOpen)
            {
                _ui.Draw();
                _renderer.DrawLabels(true);
                _previews.DrawLabels(true);
            }
        }

        private void OnDestroy()
        {
            _previews?.ClearAll();
            _renderer?.Clear();
            if (_manager?.IsDirty == true)
                _manager.Save();
        }

        private void DetectRaid()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == _currentGameWorld)
                return;

            _currentGameWorld = world;
            if (world == null)
            {
                _previews.ClearAll();
                _renderer.Clear();
                _currentMapId = null;
                return;
            }

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentMapId = mapId;
                _manager.LoadMap(mapId);
                _renderer.Rebuild();
                Plugin.Log.LogInfo($"Loaded map: {mapId}");
            }
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
                        _manager.Selected = picked;
                        Plugin.Log.LogInfo($"Selected {picked.name}");
                    }
                }
            }
        }

        private void HandleMovementInput()
        {
            if (_manager.Selected == null)
                return;

            float speed = 5f * Time.deltaTime;
            float rotSpeed = 90f * Time.deltaTime;
            float radiusSpeed = 2f * Time.deltaTime;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                speed *= 3f;
                rotSpeed *= 3f;
            }

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
            if (Input.GetKey(KeyCode.R))
                _manager.RotateSelected(Vector3.up * rotSpeed);
            if (Input.GetKey(KeyCode.F))
                _manager.RotateSelected(Vector3.down * rotSpeed);
            if (Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.Equals))
                _manager.ChangeRadius(radiusSpeed);
            if (Input.GetKey(KeyCode.KeypadMinus) || Input.GetKey(KeyCode.Minus))
                _manager.ChangeRadius(-radiusSpeed);
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
            var marker = _manager.CreateLootSpawn(GetPlayerPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateLootZone()
        {
            if (!EnsureMapLoaded()) return;
            var marker = _manager.CreateLootZone(GetPlayerPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateStaticObject()
        {
            if (!EnsureMapLoaded()) return;
            var marker = _manager.CreateStaticObject(GetPlayerPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateLootSpawnAtLook()
        {
            if (!EnsureMapLoaded()) return;
            var marker = _manager.CreateLootSpawn(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateLootZoneAtLook()
        {
            if (!EnsureMapLoaded()) return;
            var marker = _manager.CreateLootZone(GetLookPosition());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public void CreateStaticObjectAtLook()
        {
            if (!EnsureMapLoaded()) return;
            var marker = _manager.CreateStaticObject(GetLookPosition(), GetPlayerRotation());
            _manager.Selected = marker;
            _renderer.Rebuild();
        }

        public bool IsFreeCam => _freeCam;

        public void ToggleFreeCam()
        {
            _freeCam = !_freeCam;
            if (_freeCam)
                EnterFreeCam();
            else
                ExitFreeCam();
        }

        private void EnterFreeCam()
        {
            _gameCamera = Camera.main;
            if (_gameCamera == null)
            {
                _freeCam = false;
                Plugin.Log.LogWarning("No main camera found; cannot enter freecam.");
                return;
            }

            var go = new GameObject("MLE_FreeCam");
            go.transform.position = _gameCamera.transform.position;
            go.transform.rotation = _gameCamera.transform.rotation;
            _freeCamCamera = go.AddComponent<Camera>();
            _freeCamCamera.CopyFrom(_gameCamera);
            _freeCamCamera.tag = "MainCamera";
            _freeCamEuler = _gameCamera.transform.eulerAngles;

            var camLight = go.AddComponent<Light>();
            camLight.type = LightType.Spot;
            camLight.range = 100f;
            camLight.spotAngle = 120f;
            camLight.intensity = 1.5f;
            camLight.color = Color.white;
            camLight.shadows = LightShadows.None;

            _gameCamera.gameObject.tag = "Untagged";
            _gameCamera.enabled = false;

            _freeCamPlayer = GetMainPlayer();
            if (_freeCamPlayer != null)
            {
                _freeCamPlayerController = _freeCamPlayer.gameObject.GetComponent<CharacterController>();
                if (_freeCamPlayerController != null)
                    _freeCamPlayerController.enabled = false;
                _freeCamPlayerStartPosition = _freeCamPlayer.Position;
            }

            _freeCamCursorLocked = true;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            FreeCamInvulnerable = true;
            Plugin.Log.LogInfo("Entered freecam.");
        }

        private void ExitFreeCam()
        {
            if (_freeCamPlayer != null)
            {
                var exitPos = _freeCamCamera != null ? _freeCamCamera.transform.position : _freeCamPlayer.Transform.position;
                var ground = MarkerManager.GetGroundPosition(exitPos);
                if (ground.HasValue)
                    _freeCamPlayer.Transform.position = ground.Value;

                if (_freeCamPlayerController != null)
                {
                    _freeCamPlayerController.enabled = true;
                    _freeCamPlayerController = null;
                }
                _freeCamPlayer = null;
            }

            if (_freeCamCamera != null)
            {
                _freeCamCamera.gameObject.tag = "Untagged";
                UnityEngine.Object.Destroy(_freeCamCamera.gameObject);
                _freeCamCamera = null;
            }
            if (_gameCamera != null)
            {
                _gameCamera.gameObject.tag = "MainCamera";
                _gameCamera.enabled = true;
                _gameCamera = null;
            }

            _freeCamCursorLocked = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            FreeCamInvulnerable = false;
            Plugin.Log.LogInfo("Exited freecam.");
        }

        private void UpdateFreeCam()
        {
            if (_freeCamCamera == null)
                return;

            _freeCamEuler.x -= Input.GetAxis("Mouse Y") * _freeCamLookSpeed;
            _freeCamEuler.y += Input.GetAxis("Mouse X") * _freeCamLookSpeed;
            _freeCamCamera.transform.eulerAngles = _freeCamEuler;

            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? _freeCamFastSpeed : _freeCamSpeed;
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

            _freeCamCamera.transform.position += move.normalized * speed;
        }

        private void UpdateFreeCamPlayer()
        {
            if (_freeCamPlayer == null || _freeCamCamera == null)
                return;

            var playerPos = _freeCamCamera.transform.position - _freeCamCamera.transform.forward * 2.5f;
            _freeCamPlayer.Transform.position = playerPos;

            var forward = _freeCamCamera.transform.forward;
            forward.y = 0;
            if (forward.sqrMagnitude > 0.001f)
                _freeCamPlayer.Transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void ToggleFreeCamCursor()
        {
            _freeCamCursorLocked = !_freeCamCursorLocked;
            Plugin.Log.LogInfo($"Freecam cursor {(_freeCamCursorLocked ? "locked" : "unlocked")}.");
        }

        private void HandleMouseInput()
        {
            if (_freeCam && _freeCamCursorLocked)
                return;
            if (IsMouseOverUI())
                return;

            var camera = Camera.main;
            if (camera == null)
                return;

            var hoveredAxis = _renderer.PickGizmoAxis(camera, Input.mousePosition, 25f);

            if (Input.GetMouseButtonDown(0))
            {
                if (hoveredAxis != GizmoAxis.None)
                {
                    _activeGizmoAxis = hoveredAxis;
                    _renderer.ActiveAxis = hoveredAxis;
                    _gizmoDragStartMarkerPos = _manager.Selected.position.ToVector3();
                    _gizmoDragStartMarkerRot = _manager.Selected.rotation.ToVector3();
                    _gizmoDragStartMarkerScale = Vector3.one;
                    if (_manager.Selected is StaticObject so)
                        _gizmoDragStartMarkerScale = so.scale.ToVector3();
                    else if (_manager.Selected is LootZone zone)
                        _gizmoDragStartMarkerScale = zone.scale.ToVector3();

                    if (GetGizmoDragPlane(camera, hoveredAxis, out _gizmoDragPlane))
                    {
                        var ray = camera.ScreenPointToRay(Input.mousePosition);
                        if (_gizmoDragPlane.Raycast(ray, out float d))
                            _gizmoDragStartWorld = ray.GetPoint(d);
                        else
                            _gizmoDragStartWorld = _gizmoDragStartMarkerPos;
                    }
                }
                else
                {
                    var picked = _renderer.PickFromScreenPosition(camera, Input.mousePosition, 50f);
                    if (picked != null)
                    {
                        _manager.Selected = picked;
                        _mouseDragging = true;
                    }
                }
            }

            if (Input.GetMouseButton(0) && _manager.Selected != null)
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
                        _manager.Selected.position = TransformData.FromVector3(newPos);
                        _manager.IsDirty = true;
                    }
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                _activeGizmoAxis = GizmoAxis.None;
                _renderer.ActiveAxis = GizmoAxis.None;
                _mouseDragging = false;
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
            var axisDir = GetGizmoAxisDirection(_activeGizmoAxis, _manager.Selected.rotation.ToQuaternion());
            var offset = Vector3.Dot(point - _gizmoDragStartWorld, axisDir);

            switch (_gizmoMode)
            {
                case GizmoMode.Translate:
                    var newPos = _gizmoDragStartMarkerPos + axisDir * offset;
                    _manager.Selected.position = TransformData.FromVector3(newPos);
                    break;

                case GizmoMode.Rotate:
                    var v0 = Vector3.ProjectOnPlane(_gizmoDragStartWorld - _gizmoDragStartMarkerPos, axisDir);
                    var v1 = Vector3.ProjectOnPlane(point - _gizmoDragStartMarkerPos, axisDir);
                    if (v0.sqrMagnitude > 0.0001f && v1.sqrMagnitude > 0.0001f)
                    {
                        var angle = Vector3.SignedAngle(v0, v1, axisDir);
                        var newRot = Quaternion.AngleAxis(angle, axisDir) * Quaternion.Euler(_gizmoDragStartMarkerRot);
                        _manager.Selected.rotation = TransformData.FromVector3(newRot.eulerAngles);
                    }
                    break;

                case GizmoMode.Scale:
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
                    break;
            }

            _manager.IsDirty = true;
        }

        private bool GetGizmoDragPlane(Camera camera, GizmoAxis axis, out Plane plane)
        {
            plane = new Plane();
            if (_manager.Selected == null)
                return false;

            var axisDir = GetGizmoAxisDirection(axis, _manager.Selected.rotation.ToQuaternion());
            if (axisDir == Vector3.zero)
                return false;

            plane = new Plane(camera.transform.forward, _manager.Selected.position.ToVector3());
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
            Plugin.Log.LogInfo($"Gizmo mode: {mode}");
        }

        private bool IsMouseOverUI()
        {
            var rect = _ui.WindowRect;
            var mouse = Input.mousePosition;
            mouse.y = Screen.height - mouse.y;
            return rect.Contains(mouse);
        }

        private MarkerBase PickMarkerFromMouse()
        {
            var camera = Camera.main;
            if (camera == null)
                return null;
            return _renderer.PickFromScreenPosition(camera, Input.mousePosition, 50f);
        }

        public void Save() => _manager.Save();
        public void SnapSelected() => _manager.SnapSelectedToGround();

        public void DuplicateSelected()
        {
            _manager.DuplicateSelected();
            _renderer.Rebuild();
        }

        public void DeleteSelected()
        {
            var markerId = _manager.Selected?.id;
            _previews.ClearByMarkerId(markerId);
            _manager.DeleteSelected();
            _renderer.Rebuild();
        }

        public void ClearPreviews() => _previews.ClearAll();
        public void ClearVisuals() => _renderer.Clear();

        public string CurrentMapId => _currentMapId;

        public void ExportPack(string packName)
        {
            if (string.IsNullOrEmpty(_currentMapId) || _manager.Data == null)
            {
                Plugin.Log.LogWarning("Cannot export pack: no map loaded.");
                return;
            }

            var pack = new PackData
            {
                name = packName,
                maps = new System.Collections.Generic.Dictionary<string, MapData>(System.StringComparer.OrdinalIgnoreCase)
                {
                    [_currentMapId] = _manager.Data
                }
            };

            var safeName = SanitizePackName(packName);
            if (string.IsNullOrEmpty(safeName))
            {
                Plugin.Log.LogWarning("Cannot export pack: name is empty or invalid.");
                return;
            }

            var exportsDir = Plugin.ServerModExportsDirectory;
            Directory.CreateDirectory(exportsDir);
            var path = Path.Combine(exportsDir, safeName + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(pack, Formatting.Indented));
            Plugin.Log.LogInfo($"Exported pack '{safeName}' to {path}");
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
