using System;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
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

            if (_editorOpen)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                HandleSelectionInput();
                HandleMovementInput();
            }

            _renderer.Update();

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
