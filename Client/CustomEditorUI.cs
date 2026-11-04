using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using Koenigz.PerfectCulling.EFT;
using Newtonsoft.Json;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BepInEx;

namespace MapLootEditorLite.Client
{
    public static class RemovedObjectHelper
    {
        [Serializable]
        public class RemovalState
        {
            public GameObject GameObject;
            public bool WasActive;
            public readonly List<Component> DisabledComponents = new List<Component>();
            public readonly List<PerfectCullingCrossSceneGroup> DisabledGroups = new List<PerfectCullingCrossSceneGroup>();
            public readonly List<MonoBehaviour> DisabledCullingObjects = new List<MonoBehaviour>();
            public readonly List<(Renderer renderer, bool previousForceRenderingOff)> ForceOffRenderers = new List<(Renderer, bool)>();
            public int OriginalDoorState = -1;
        }

        public static RemovalState SoftRemove(GameObject go)
        {
            var state = new RemovalState { GameObject = go, WasActive = go.activeSelf };

            var wio = go.GetComponentInChildren<WorldInteractiveObject>(true);
            if (wio != null)
            {
                state.OriginalDoorState = (int)wio.DoorState;
                try { wio.SetDoorState(EDoorState.Open, true); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Could not open door before removal: {ex.Message}"); }
            }

            string GetPath(Transform t)
            {
                var sb = new StringBuilder(t.name);
                while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
                return sb.ToString();
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                bool wasForceOff = r.forceRenderingOff;
                r.forceRenderingOff = true;
                state.ForceOffRenderers.Add((renderer: r, previousForceRenderingOff: wasForceOff));
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c != null && c.enabled) { c.enabled = false; state.DisabledComponents.Add(c); }
            }

            go.SetActive(false);

            int rendererCount = state.ForceOffRenderers.Count;
            int colliderCount = state.DisabledComponents.Count(c => c is Collider);
            Plugin.Log.LogInfo($"SoftRemove '{go.name}' ({GetPath(go.transform)}): force-off {rendererCount} renderers, disabled {colliderCount} colliders.");

            return state;
        }

        public static void Restore(RemovalState state)
        {
            if (state == null || state.GameObject == null) return;

            foreach (var c in state.DisabledComponents)
            {
                if (c == null) continue;
                if (c is Renderer r) r.enabled = true;
                else if (c is Collider col) col.enabled = true;
                else if (c is Behaviour b) b.enabled = true;
            }

            foreach (var entry in state.ForceOffRenderers)
            {
                if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.previousForceRenderingOff;
            }

            foreach (var g in state.DisabledGroups)
            {
                if (g != null) g.allowGroupCulling = true;
            }

            foreach (var cull in state.DisabledCullingObjects)
            {
                if (cull == null) continue;
                if (cull is DisablerCullingObjectBase dcb) { try { dcb.SetComponentsEnabled(true); } catch { } }
                else if (cull is CullingObject co) { try { co.SetVisibility(true); } catch { } }
                cull.enabled = true;
            }

            if (state.OriginalDoorState >= 0)
            {
                var wio = state.GameObject.GetComponentInChildren<WorldInteractiveObject>(true);
                if (wio != null)
                {
                    try { wio.SetDoorState((EDoorState)state.OriginalDoorState, true); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"Could not restore door state: {ex.Message}"); }
                }
            }

            state.GameObject.SetActive(state.WasActive);
        }
    }

    public class ScenePickFilterConfig
    {
        public bool enabled = true;
        public List<string> filters = new List<string> { "SPAWNS", "LOD", "BornPositions" };
    }

    public class CustomEditorUI : MonoBehaviour
    {
        public MapEditorController controller;
        public MarkerManager manager;
        public MarkerRenderer renderer;
        public LootPreviewSpawner previews;

        private GameObject _canvas;
        private CanvasScaler _canvasScaler;
        private RectTransform _mainWindow;
        private RectTransform _topPanel;
        private RectTransform _hierarchyPanel;
        private RectTransform _inspectorPanel;
        private RectTransform _bottomPanel;
        private RectTransform _bottomTabContent;
        private RectTransform _deleteConfirmPanel;
        private RectTransform _exportDialogPanel;
        private RectTransform _renderDistanceDialogPanel;
        private RectTransform _contextMenuPanel;
        private string _fieldClipboard = "";

        private RectTransform _hierarchyContent;
        private RectTransform _inspectorContent;
        private RectTransform _prefabsContent;
        private RectTransform _groupsContent;
        private int _itemsListPage;
        private List<LootItem> _itemsListPageTarget;
        private int _hierarchyPage;
        private string _hierarchyPageTarget;

        private Text _titleText;
        private Text _deleteConfirmText;
        private InputField _exportPackInput;
        private InputField _renderDistanceInput;
        private InputField _objectRenderDistanceInput;
        private InputField _searchInput;
        private InputField _groupInput;
        private InputField _prefabNameInput;
        private InputField _scatterCountInput;
        private RectTransform _scatterPickRow;
        private InputField _scatterMinHeightInput;
        private InputField _scatterMaxHeightInput;
        private Toggle _scatterSnapToggle;

        private Button _editorModeButton;
        private Button _translateButton;
        private Button _rotateButton;
        private Button _scaleButton;
        private List<Button> _gizmoButtons = new List<Button>();
        private EventSystem _modEventSystem;

        public EventSystem ModEventSystem => _modEventSystem;

        private bool _isVisible;
        private bool _isDeletePending;
        private bool _isDeleteConfirmed;
        private bool _isPickingSource;
        private IHasSourceObject _pickingSourceTarget;
        private bool _pickUseParent;
        private ScenePickFilterConfig _pickFilterConfig;
        private string _pickFiltersPath;
        private RectTransform _pickFiltersDialogPanel;
        private RectTransform _itemPickerPanel;
        private RectTransform _itemPickerContent;
        private InputField _itemPickerSearch;
        private Text _itemPickerStatus;
        private LootItem _itemPickerTarget;
        private const int ItemPickerMaxResults = 60;
        private RectTransform _pickFiltersContent;
        private InputField _pickFiltersInput;

        private RectTransform _addComponentDialogPanel;
        private RectTransform _addComponentListPanel;
        private GameObject _addComponentTarget;
        private List<string> _addComponentTargetList;
        private string _addComponentSearch = "";
        private Toggle _pickFiltersEnabledToggle;
        private bool _isPickingScatter;
        private string _prefabName = "MyPrefab";
        private string _scatterPrefabPath = "";
        private int _scatterCount = 10;
        private float _scatterMinHeight = 0f;
        private float _scatterMaxHeight = 0f;
        private bool _scatterSnapToGround = true;
        private string _packName = "MyLootPack";
        private string _searchText = "";
        private string _newGroupName = "";
        private readonly HashSet<string> _collapsedGroups = new HashSet<string>();
        private readonly HashSet<string> _expandedChildNodes = new HashSet<string>();
        private MarkerBase _selectedChildMarker;
        private string _selectedChildPath;
        private bool _selectedChildIsDoor;
        private bool _selectedChildIsContainer;
        private const int TopPanelHeight = 56;
        private int _hierarchyWidth = 240;
        private int _inspectorWidth = 280;
        private const int CollapsedBottomPanelHeight = 40;
        private const int MinWorkspaceHeight = 180;
        private const int MaxWorkspaceHeight = 420;
        private const int TabBarHeight = 30;
        private const int WorkspaceHandleThickness = 8;
        private float _workspaceHeight = MinWorkspaceHeight;
        private bool _workspaceCollapsed;
        private bool _workspaceReady;
        private bool _workspaceDragging;
        private bool _workspaceHandleHover;
        private int _appliedBottomHeight = -1;
        private RectTransform _workspaceResizeHandle;
        private Image _workspaceResizeImage;
        private Button _workspaceToggleButton;
        private string _workspaceLayoutPath;
        private static Texture2D _resizeCursorTexture;

        // Single source of truth for the workspace height; hierarchy, inspector, side handles and the bottom panel all read it.
        private int BottomPanelHeight => _workspaceCollapsed ? CollapsedBottomPanelHeight : EffectiveWorkspaceHeight();

        private int EffectiveWorkspaceHeight()
        {
            var canvasHeight = CanvasSize().y;
            // Always leave room for the top bar plus a usable strip of viewport.
            var max = Mathf.Min(MaxWorkspaceHeight, Mathf.FloorToInt(canvasHeight - TopPanelHeight - 160f));
            max = Mathf.Max(CollapsedBottomPanelHeight, max);
            var min = Mathf.Min(MinWorkspaceHeight, max);
            return Mathf.Clamp(Mathf.RoundToInt(_workspaceHeight), min, max);
        }
        private string _selectedGroupName;

        private RectTransform _hierarchyResizeHandle;
        private RectTransform _inspectorResizeHandle;

        private Button _packTabButton;
        private Button _vanillaTabButton;
        private bool _vanillaHierarchyActive;

        private RectTransform _prefabsTab;
        private RectTransform _scatterTab;
        private RectTransform _groupsTab;
        private Button _prefabsTabButton;
        private Button _scatterTabButton;
        private Button _groupsTabButton;
        private Text _groupsStatusText;
        private RectTransform _deleteGroupConfirmPanel;
        private string _pendingGroupDeleteName;
        private int _activeBottomTab;

        private MarkerBase _lastSelected;
        private int _lastMarkerCount = -1;
        private int _lastSelectedCount = -1;
        private bool _refreshPending;
        private bool _hierarchyRefreshPending;
        private bool _inspectorRefreshPending;

        // GameObjects browser
        private RectTransform _objectsTab;
        private Button _objectsTabButton;
        private RectTransform _objectsContent;
        private string _objectsSearchText = "";
        private const int ObjectsPageSize = 150;
        private int _objectsPage;
        private Button _objectsPrevButton;
        private Button _objectsNextButton;
        private Text _objectsPageLabel;
        private Text _objectsRangeLabel;
        private bool _scanning;
        private bool _scanRadiusEnabled;
        private float _scanRadius = 10f;
        private const float MinScanRadius = 0.5f;
        private const float MaxScanRadius = 2000f;
        private bool _hasScanned;
        private bool _scanSnapLimited;
        private Vector3 _scanSnapCenter;
        private float _scanSnapRadius;
        private string _scanStatusOverride;
        private float _nextScanStatusTime;
        private Text _objectsScanLabel;
        private RectTransform _scanRadiusRow;
        private readonly List<Renderer> _scanRendererBuffer = new List<Renderer>();
        private Text _goPreviewNameText;
        private RectTransform _goActionBtnRow;
        private RectTransform _goPickRow;
        private Text _goDetailsText;
        private bool _isPickingSceneGO;

        // Removed vanilla objects tab
        private RectTransform _removedTab;
        private Button _removedTabButton;
        private RectTransform _removedContent;
        private readonly Dictionary<string, RemovedObjectHelper.RemovalState> _removedObjectInstances = new Dictionary<string, RemovedObjectHelper.RemovalState>();

        // Scene object picker notice
        private RectTransform _scenePickerNotice;
        private RectTransform _scenePickerCursorTooltip;

        // Output tab
        private RectTransform _outputTab;
        private Button _outputTabButton;
        private RectTransform _outputContent;
        private static readonly List<string> _outputMessages = new List<string>();
        private static CustomEditorUI _outputInstance;
        private const int MaxOutputMessages = 100;
        private readonly List<GameObject> _sceneObjectCache = new List<GameObject>();
        private GameObject _selectedSceneGO;
        private IHasSourceObject _goListTarget;

        private readonly Dictionary<string, string> _itemNameCache = new Dictionary<string, string>();

        public Rect WindowRect
        {
            get
            {
                if (_mainWindow == null)
                    return new Rect(0, 0, Screen.width, Screen.height);
                var corners = new Vector3[4];
                _mainWindow.GetWorldCorners(corners);
                var scale = _canvasScaler?.scaleFactor ?? 1f;
                return new Rect(corners[0].x / scale, corners[0].y / scale, _mainWindow.rect.width / scale, _mainWindow.rect.height / scale);
            }
        }

        public bool IsPickingSource => _isPickingSource;
        public bool IsPickingScatter => _isPickingScatter;
        public bool IsPickingSceneGO => _isPickingSceneGO;
        public bool IsPickingSourceObject => _isPickingSource || _isPickingSceneGO || _goListTarget != null;
        public bool IsDeletePending => _isDeletePending;
        public bool IsDeleteConfirmed => _isDeleteConfirmed;
        public bool IsVisible => _isVisible;
        public string PackName => _packName;

        public void SetPickingSceneGO(bool picking)
        {
            _isPickingSceneGO = picking;
            if (picking && !_scanning)
            {
                if (_sceneObjectCache.Count == 0)
                    ScanSceneObjects();
                else
                    renderer?.SetSceneObjects(_sceneObjectCache, _selectedSceneGO);
            }
            if (_scenePickerNotice != null)
                _scenePickerNotice.gameObject.SetActive(picking);
            RefreshGOPickRow();
        }

        public void SelectSceneGO(GameObject go)
        {
            if (go != null && manager != null)
                manager.ClearSelection();
            _selectedSceneGO = go;
            if (go != null && !_scanning)
            {
                // Jump to the page holding the object so viewport picks are visible in the list.
                var idx = GetFilteredSceneObjects().IndexOf(go);
                if (idx >= 0)
                    _objectsPage = idx / ObjectsPageSize;
            }
            if (_goPreviewNameText != null)
                _goPreviewNameText.text = go != null ? go.name : Locale.Get("No selection");
            RefreshObjectsList();
            RefreshGOActionRow();
            RequestInspectorRefresh();
        }

        public bool IsAnyInputFocused
        {
            get
            {
                if (EventSystem.current == null)
                    return false;
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected == null)
                    return false;
                return selected.GetComponent<InputField>() != null || selected.GetComponentInParent<InputField>() != null;
            }
        }

        public void Init(MapEditorController ctrl, MarkerManager mgr, MarkerRenderer rnd, LootPreviewSpawner prw)
        {
            controller = ctrl;
            manager = mgr;
            renderer = rnd;
            previews = prw;
            LoadPickFilters();
            LoadWorkspaceLayout();
        }

        private void Awake()
        {
            try
            {
                BuildUI();
                _outputInstance = this;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"CustomEditorUI BuildUI failed: {ex}");
            }
            Hide();
        }

        private void Start()
        {
            _modEventSystem = UIBuilder.EnsureEventSystem(transform);
            LogOutput($"Mod data directory: {Plugin.ModDataDirectory}");
            LogOutput($"Server mod directory: {Plugin.ServerModDirectory}");
        }

        private void Update()
        {
            if (!_isVisible)
                return;

            UpdateCanvasScale();
            ClampPanelWidthsToScreen();
            FitAddComponentDialog();
            SyncWorkspaceLayout();
            UpdateScanStatusLabel(false);
            UpdateGizmoButtons();
            UpdateEditorModeButton();
            UpdateScenePickerCursorTooltip();

            if (_contextMenuPanel != null && _contextMenuPanel.gameObject.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    HideContextMenu();
                }
                else if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    var canvasRect = _canvas.GetComponent<RectTransform>();
                    Vector2 localPos;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out localPos);
                    var anchorOffset = new Vector2(canvasRect.rect.width / 2f, -canvasRect.rect.height / 2f);
                    var menuPos = _contextMenuPanel.anchoredPosition - anchorOffset;
                    var menuRect = _contextMenuPanel.rect;
                    var menuMin = menuPos + menuRect.min;
                    var menuMax = menuPos + menuRect.max;
                    if (localPos.x < menuMin.x || localPos.x > menuMax.x || localPos.y < menuMin.y || localPos.y > menuMax.y)
                        HideContextMenu();
                }
            }

            if (manager == null || controller == null)
                return;

            try
            {
                if (_refreshPending || _lastMarkerCount != manager.GetAllMarkers().Count())
                {
                    _refreshPending = false;
                    RefreshAll();
                }
                else if (_hierarchyRefreshPending)
                {
                    _hierarchyRefreshPending = false;
                    RefreshHierarchy();
                }
                else if (_inspectorRefreshPending || _lastSelected != manager.Selected || _lastSelectedCount != manager.SelectedIds.Count)
                {
                    _inspectorRefreshPending = false;
                    _hierarchyRefreshPending = false;
                    RefreshHierarchy();
                    RefreshInspector();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"CustomEditorUI Update refresh failed: {ex}");
            }
        }

        private void OnDestroy()
        {
            SetResizeCursor(false);
            if (_canvas != null)
                Destroy(_canvas);
        }

        public void Show()
        {
            _isVisible = true;
            if (_canvas != null)
                _canvas.SetActive(true);
            _refreshPending = true;
            UpdateCanvasScale();
        }

        public void Hide()
        {
            _isVisible = false;
            if (_workspaceHandleHover || _workspaceDragging)
            {
                _workspaceHandleHover = false;
                _workspaceDragging = false;
                SetResizeCursor(false);
            }
            CloseAllMenus();
            HideContextMenu();
            if (_canvas != null)
                _canvas.SetActive(false);
            if (_deleteConfirmPanel != null)
                _deleteConfirmPanel.gameObject.SetActive(false);
            CloseItemPicker();
        }

        public void Toggle()
        {
            if (_isVisible)
                Hide();
            else
                Show();
        }

        public void RequestRefresh()
        {
            _refreshPending = true;
        }

        public void RequestHierarchyRefresh()
        {
            _hierarchyRefreshPending = true;
        }

        public void RequestInspectorRefresh()
        {
            _inspectorRefreshPending = true;
        }

        public static void LogOutput(string message)
        {
            lock (_outputMessages)
            {
                _outputMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                while (_outputMessages.Count > MaxOutputMessages)
                    _outputMessages.RemoveAt(0);
            }
            _outputInstance?.EnsureOutputTab();
            _outputInstance?.RefreshOutput();
        }

        public static void ClearOutput()
        {
            lock (_outputMessages)
            {
                _outputMessages.Clear();
            }
            _outputInstance?.RefreshOutput();
        }

        public static void CopyOutputToClipboard()
        {
            string text;
            lock (_outputMessages)
            {
                text = string.Join("\n", _outputMessages);
            }
            GUIUtility.systemCopyBuffer = text;
            LogOutput("Copied output log to clipboard.");
        }

        private void UpdateCanvasScale()
        {
            if (_canvasScaler == null)
                return;
            var scale = Plugin.UIScale?.Value ?? 1f;
            if (Math.Abs(_canvasScaler.scaleFactor - scale) > 0.001f)
                _canvasScaler.scaleFactor = scale;
        }

        private void SetLanguage(string code)
        {
            if (string.Equals(code, Locale.Current, StringComparison.OrdinalIgnoreCase))
                return;

            Plugin.Language.Value = code;
            Locale.Initialize(code, Plugin.LocalizationDirectory);
            CloseAllMenus();
            var wasVisible = _isVisible;
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            BuildUI();
            if (wasVisible)
                Show();
            else
                Hide();
        }

        private void BuildUI()
        {
            _canvas = UIBuilder.CreateCanvas("MLE_Canvas", transform, 1000);
            _canvasScaler = _canvas.GetComponent<CanvasScaler>();
            _canvasScaler.scaleFactor = Plugin.UIScale?.Value ?? 1f;

            _mainWindow = UIBuilder.CreatePanel("MainWindow", _canvas.transform, new Color(0, 0, 0, 0));
            _mainWindow.anchorMin = Vector2.zero;
            _mainWindow.anchorMax = Vector2.one;
            _mainWindow.offsetMin = Vector2.zero;
            _mainWindow.offsetMax = Vector2.zero;
            _mainWindow.GetComponent<Image>().raycastTarget = false;
            // No layout group — each panel is independently anchored

            void SafeBuild(string name, System.Action step)
            {
                try
                {
                    step();
                    Plugin.Log.LogInfo($"CustomEditorUI built {name}");
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogError($"CustomEditorUI failed to build {name}: {ex}");
                }
            }

            SafeBuild("TopPanel", BuildTopPanel);
            SafeBuild("HierarchyPanel", BuildHierarchyPanel);
            SafeBuild("InspectorPanel", BuildInspectorPanel);
            SafeBuild("BottomPanel", BuildBottomPanel);
            SafeBuild("ScenePickerNotice", BuildScenePickerNotice);
            SafeBuild("DeleteConfirm", BuildDeleteConfirm);
            SafeBuild("ExportDialog", BuildExportDialog);
            SafeBuild("RenderDistanceDialog", BuildRenderDistanceDialog);
            SafeBuild("PickFiltersDialog", BuildPickFiltersDialog);
            SafeBuild("ItemPickerDialog", BuildItemPickerDialog);
            SafeBuild("ContextMenu", BuildContextMenu);
            SafeBuild("ResizeHandles", BuildResizeHandles);
            _lastLayoutScreenWidth = -1f;
            ClampPanelWidthsToScreen();

            if (manager != null && controller != null)
                RefreshAll();
        }

        private void BuildTopPanel()
        {
            _topPanel = UIBuilder.CreatePanel("TopPanel", _mainWindow, new Color(0.075f, 0.09f, 0.12f, 0.98f));
            // Anchor to top edge, full width
            _topPanel.anchorMin = new Vector2(0f, 1f);
            _topPanel.anchorMax = new Vector2(1f, 1f);
            _topPanel.pivot     = new Vector2(0f, 1f);
            _topPanel.offsetMin = new Vector2(0, -TopPanelHeight);
            _topPanel.offsetMax = Vector2.zero;
            UIBuilder.AddVerticalLayout(_topPanel, 2, 2, true, true);

            // Row 1 – action buttons
            var row1 = CreateHorizontalScrollRow("TopRow1", _topPanel, new Color(0, 0, 0, 0), 28);
            var hlg1 = UIBuilder.AddHorizontalLayout(row1, 3, 3, false, false);
            hlg1.childAlignment = TextAnchor.MiddleLeft;

            _titleText = UIBuilder.CreateText(row1, "Map Loot Editor Lite", 12, Color.white, FontStyle.Bold);
            _titleText.rectTransform.sizeDelta = new Vector2(190, 22);
            // Small gap between title and buttons
            var sep = UIBuilder.CreatePanel("TitleSep", row1, new Color(0, 0, 0, 0));
            sep.GetComponent<Image>().raycastTarget = false;
            sep.sizeDelta = new Vector2(6, 22);
            BuildMenuButton(row1, "File", new List<MenuItem>
            {
                new MenuItem("Export", () => DirectExport()),
                new MenuItem("Export As", () => ShowExportDialog()),
                new MenuItem("Import Vanilla Loot", () => controller.ImportVanillaLoot()),
                new MenuItem("Language", subItems: Locale.GetAvailableLanguages(Plugin.LocalizationDirectory)
                    .Select(lang => new MenuItem(lang.name, () => SetLanguage(lang.code)))
                    .ToList())
            }, 40, 22);
            BuildMenuButton(row1, "View", new List<MenuItem>
            {
                new MenuItem("Hierarchy", subItems: new List<MenuItem>
                {
                    new MenuItem("Toggle Vanilla Gizmos", () => controller.ToggleVanillaGizmos()),
                    new MenuItem("Toggle Pack Gizmos", () => controller.TogglePackGizmos()),
                    new MenuItem("Toggle 3D Scene Outlines", () => controller.Toggle3DSceneObjectOutlines())
                }),
                new MenuItem("Vanilla Render Distance", () => ShowRenderDistanceDialog())
            }, 40, 22);
            BuildMenuButton(row1, "Tools", new List<MenuItem>
            {
                new MenuItem("Scene Pick Filters", () => ShowPickFiltersDialog()),
                new MenuItem("Toggle Preview Mode", () => controller.TogglePreviewMode()),
                new MenuItem("Debug", subItems: new List<MenuItem>
                {
                    new MenuItem("Dump door IDs", () => controller.DumpDoorIds()),
                    new MenuItem("Dump keyed door IDs", () => controller.DumpKeyedDoorIds()),
                    new MenuItem("Extend Raid Timer (24h)", () => controller.ToggleRaidTimerPause())
                })
            }, 40, 22);
            BuildMenuButton(row1, "Add Spawn", new List<MenuItem>
            {
                new MenuItem("Common", subItems: new List<MenuItem>
                {
                    new MenuItem("Loot Spawn", () => controller.CreateLootSpawn()),
                    new MenuItem("Loot Zone", () => controller.CreateLootZone()),
                    new MenuItem("Static Object", () => controller.CreateStaticObject()),
                    new MenuItem("Extract Zone", () => controller.CreateExtractZone()),
                    new MenuItem("Bot Spawn Point", () => controller.CreateBotSpawnPoint()),
                    new MenuItem("Bot Spawn Zone", () => controller.CreateBotSpawnZone()),
                    new MenuItem("PMC Spawn Zone", () => controller.CreatePmcSpawnZone()),
                    new MenuItem("Light Zone", () => controller.CreateLightZone()),
                    new MenuItem("Trigger Zone", () => controller.CreateTriggerZone()),
                    new MenuItem("Bot Activity Zone", () => controller.CreateBotActivityZone())
                    // new MenuItem("Cut Volume", () => controller.CreateCutVolume()) // disabled until mesh cutting is fully reliable
                }),
                new MenuItem("WTT", subItems: new List<MenuItem>
                {
                    new MenuItem("WTT Quest Area", () => controller.CreateWTTQuestZone()),
                    new MenuItem("WTT Static Object", () => controller.CreateWTTStaticObject())
                }),
                new MenuItem("Interactive", subItems: new List<MenuItem>
                {
                    new MenuItem("Custom Door", () => controller.CreateInteractiveObject(InteractiveObjectType.Door)),
                    new MenuItem("Custom Container", () => controller.CreateInteractiveObject(InteractiveObjectType.Container)),
                    new MenuItem("Custom Stationary Weapon", () => controller.CreateInteractiveObject(InteractiveObjectType.StationaryWeapon))
                }),
                new MenuItem("Other", subItems: new List<MenuItem>
                {
                    new MenuItem("Blocker", () => controller.CreateBlocker())
                    // new MenuItem("NavMesh Area", () => controller.CreateNavMeshArea())
                })
            }, 84, 22);
            var snapButton = UIBuilder.CreateButton(row1, "Snap", () => controller.SnapSelected(), 46, 22);
            ApplyButtonAccent(snapButton, new Color(0.16f, 0.42f, 0.48f, 1f));
            var duplicateButton = UIBuilder.CreateButton(row1, "Duplicate", () => controller.DuplicateSelected(), 68, 22);
            ApplyButtonAccent(duplicateButton, new Color(0.2f, 0.36f, 0.58f, 1f));
            var deleteButton = UIBuilder.CreateButton(row1, "Delete", () => RequestDelete(), 54, 22);
            ApplyButtonAccent(deleteButton, new Color(0.48f, 0.22f, 0.24f, 1f));
            UIBuilder.CreateButton(row1, "Clear Prev",   () => controller.ClearPreviews(),    76, 22);
            _editorModeButton = UIBuilder.CreateButton(row1, "Editor Mode", () =>
            {
                if (controller.IsFreeCam)
                    controller.CloseEditor();
                else
                    controller.ToggleFreeCam();
            }, 92, 22);
            ApplyButtonAccent(_editorModeButton, new Color(0.13f, 0.39f, 0.37f, 1f));
            _translateButton = UIBuilder.CreateButton(row1, "T", () => controller.SetGizmoMode(GizmoMode.Translate), 24, 22, 10);
            _rotateButton    = UIBuilder.CreateButton(row1, "R", () => controller.SetGizmoMode(GizmoMode.Rotate),    24, 22, 10);
            _scaleButton     = UIBuilder.CreateButton(row1, "S", () => controller.SetGizmoMode(GizmoMode.Scale),     24, 22, 10);
            _gizmoButtons.Add(_translateButton);
            _gizmoButtons.Add(_rotateButton);
            _gizmoButtons.Add(_scaleButton);
            UIBuilder.CreateLabel(row1, "Search", 11, 38, 22);
            _searchInput = UIBuilder.CreateInputField(row1, "Search", _searchText, (v) => { _searchText = v; _hierarchyRefreshPending = true; }, 86, 22);

            // Row 2 – keybind hints (fill-anchored, centred)
            var row2 = UIBuilder.CreatePanel("TopRow2", _topPanel, new Color(0.06f, 0.06f, 0.06f, 1f));
            UIBuilder.AddLayoutElement(row2, null, 22, null, 22, null, 0);
            var hints = UIBuilder.CreateText(row2, "[Del] Delete  |  [Ctrl+Z] Undo  |  [Ctrl+Y] Redo  |  [Ctrl+C] Copy  |  [Ctrl+V] Paste  |  [1] Move  |  [2] Rotate  |  [3] Scale  |  [F] Focus  |  [MMB] Toggle Cursor", 10, new Color(0.6f, 0.6f, 0.6f, 1f));
            hints.alignment = TextAnchor.MiddleCenter;
            UIBuilder.AddEllipsis(hints);
            hints.rectTransform.anchorMin = Vector2.zero;
            hints.rectTransform.anchorMax = Vector2.one;
            hints.rectTransform.offsetMin = Vector2.zero;
            hints.rectTransform.offsetMax = Vector2.zero;
            AddPanelAccent(_topPanel, atBottom: true);
        }

        private void BuildHierarchyPanel()
        {
            _hierarchyPanel = UIBuilder.CreatePanel("HierarchyPanel", _mainWindow, new Color(0.105f, 0.125f, 0.15f, 0.98f));
            _hierarchyPanel.anchorMin = new Vector2(0f, 0f);
            _hierarchyPanel.anchorMax = new Vector2(0f, 1f);
            _hierarchyPanel.pivot     = new Vector2(0f, 0.5f);
            _hierarchyPanel.offsetMin = new Vector2(0, BottomPanelHeight);
            _hierarchyPanel.offsetMax = new Vector2(_hierarchyWidth, -TopPanelHeight);
            UIBuilder.AddVerticalLayout(_hierarchyPanel, 0, 0, true, true);

            // Header — first child in VLG = always at visual top; scroll mask clips content below it
            var header = UIBuilder.CreatePanel("HierarchyHeader", _hierarchyPanel, new Color(0.1f, 0.1f, 0.1f, 1f));
            UIBuilder.AddLayoutElement(header, null, 24, null, 24, null, 0);
            UIBuilder.AddHorizontalLayout(header, 4, 0, false, true);
            UIBuilder.CreateText(header, "Hierarchy", 12, Color.white, FontStyle.Bold);
            AddPanelAccent(header);

            // Tab bar — Pack / Vanilla
            var tabBar = UIBuilder.CreatePanel("HierarchyTabBar", _hierarchyPanel, new Color(0.08f, 0.08f, 0.08f, 1f));
            UIBuilder.AddLayoutElement(tabBar, null, 24, null, 24, null, 0);
            UIBuilder.AddHorizontalLayout(tabBar, 2, 0, false, false);
            _packTabButton = UIBuilder.CreateButton(tabBar, "Pack", () => SelectHierarchyTab(false), 60, 20);
            _vanillaTabButton = UIBuilder.CreateButton(tabBar, "Vanilla", () => SelectHierarchyTab(true), 60, 20);
            UpdateHierarchyTabButtons();

            // Scroll — fills remaining height
            var scroll = UIBuilder.CreateScrollView(_hierarchyPanel, out _hierarchyContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_hierarchyContent, 1, 2, true, true);
        }

        private void SelectHierarchyTab(bool vanilla)
        {
            _vanillaHierarchyActive = vanilla;
            if (manager != null)
                manager.IsVanillaActive = vanilla;
            UpdateHierarchyTabButtons();
            RequestHierarchyRefresh();
            RequestInspectorRefresh();
        }

        private void UpdateHierarchyTabButtons()
        {
            if (_packTabButton == null || _vanillaTabButton == null)
                return;
            _packTabButton.GetComponent<Image>().color = !_vanillaHierarchyActive
                ? new Color(0.15f, 0.43f, 0.55f, 1f)
                : new Color(0.24f, 0.24f, 0.24f, 1f);
            _vanillaTabButton.GetComponent<Image>().color = _vanillaHierarchyActive
                ? new Color(0.15f, 0.43f, 0.55f, 1f)
                : new Color(0.24f, 0.24f, 0.24f, 1f);
        }

        private void BuildInspectorPanel()
        {
            _inspectorPanel = UIBuilder.CreatePanel("InspectorPanel", _mainWindow, new Color(0.105f, 0.125f, 0.15f, 0.98f));
            _inspectorPanel.anchorMin = new Vector2(1f, 0f);
            _inspectorPanel.anchorMax = new Vector2(1f, 1f);
            _inspectorPanel.pivot     = new Vector2(1f, 0.5f);
            _inspectorPanel.offsetMin = new Vector2(-_inspectorWidth, BottomPanelHeight);
            _inspectorPanel.offsetMax = new Vector2(0, -TopPanelHeight);
            UIBuilder.AddVerticalLayout(_inspectorPanel, 0, 0, true, true);

            // Header — first child in VLG = always at visual top; scroll mask clips content below it
            var header = UIBuilder.CreatePanel("InspectorHeader", _inspectorPanel, new Color(0.1f, 0.1f, 0.1f, 1f));
            UIBuilder.AddLayoutElement(header, null, 24, null, 24, null, 0);
            UIBuilder.AddHorizontalLayout(header, 4, 0, false, true);
            UIBuilder.CreateText(header, "Inspector", 12, Color.white, FontStyle.Bold);
            AddPanelAccent(header);

            // Scroll — second child, fills remaining height
            var scroll = UIBuilder.CreateScrollView(_inspectorPanel, out _inspectorContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_inspectorContent, 1, 2, true, true);
        }

        private void BuildResizeHandles()
        {
            // Hierarchy right-edge drag handle
            _hierarchyResizeHandle = UIBuilder.CreatePanel("HierarchyResize", _mainWindow, new Color(0.4f, 0.55f, 0.8f, 0.18f));
            _hierarchyResizeHandle.anchorMin = new Vector2(0f, 0f);
            _hierarchyResizeHandle.anchorMax = new Vector2(0f, 1f);
            _hierarchyResizeHandle.pivot     = new Vector2(0f, 0.5f);
            // Inspector left-edge drag handle
            _inspectorResizeHandle = UIBuilder.CreatePanel("InspectorResize", _mainWindow, new Color(0.4f, 0.55f, 0.8f, 0.18f));
            _inspectorResizeHandle.anchorMin = new Vector2(1f, 0f);
            _inspectorResizeHandle.anchorMax = new Vector2(1f, 1f);
            _inspectorResizeHandle.pivot     = new Vector2(1f, 0.5f);
            UpdateResizeHandles();
            AddDragResize(_hierarchyResizeHandle, isHierarchy: true);
            AddDragResize(_inspectorResizeHandle, isHierarchy: false);
            BuildWorkspaceResizeHandle();
        }

        private static void AddPanelAccent(RectTransform panel, bool atBottom = false)
        {
            var accent = UIBuilder.CreatePanel("Accent", panel, new Color(0.18f, 0.66f, 0.76f, 1f));
            var layout = accent.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            var image = accent.GetComponent<Image>();
            image.raycastTarget = false;
            if (atBottom)
            {
                accent.anchorMin = new Vector2(0f, 0f);
                accent.anchorMax = new Vector2(1f, 0f);
                accent.pivot = new Vector2(0.5f, 0f);
                accent.offsetMin = Vector2.zero;
                accent.offsetMax = new Vector2(0f, 2f);
            }
            else
            {
                accent.anchorMin = new Vector2(0f, 0f);
                accent.anchorMax = new Vector2(0f, 1f);
                accent.pivot = new Vector2(0f, 0.5f);
                accent.offsetMin = Vector2.zero;
                accent.offsetMax = new Vector2(3f, 0f);
            }
        }

        private static void ApplyButtonAccent(Button button, Color color)
        {
            if (button == null)
                return;
            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = color;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
            button.colors = colors;
        }

        private void UpdateResizeHandles()
        {
            if (_workspaceResizeHandle != null)
            {
                var y = BottomPanelHeight;
                _workspaceResizeHandle.offsetMin = new Vector2(0, y - WorkspaceHandleThickness / 2);
                _workspaceResizeHandle.offsetMax = new Vector2(0, y + WorkspaceHandleThickness / 2);
            }
            if (_hierarchyResizeHandle != null)
            {
                _hierarchyResizeHandle.offsetMin = new Vector2(_hierarchyWidth - 3, BottomPanelHeight);
                _hierarchyResizeHandle.offsetMax = new Vector2(_hierarchyWidth + 3, -TopPanelHeight);
            }
            if (_inspectorResizeHandle != null)
            {
                _inspectorResizeHandle.offsetMin = new Vector2(-_inspectorWidth - 3, BottomPanelHeight);
                _inspectorResizeHandle.offsetMax = new Vector2(-_inspectorWidth + 3, -TopPanelHeight);
            }
        }

        private const int MinHierarchyWidth = 140;
        private const int MinInspectorWidth = 160;
        private const int MaxSidePanelWidth = 500;
        private const int MinViewportWidth = 320;
        private float _lastLayoutScreenWidth = -1f;
        private float _lastLayoutScale = -1f;

        // Width (canvas units) left for both side panels so a center viewport of MinViewportWidth remains.
        private float SidePanelBudget()
        {
            var scale = _canvasScaler != null && _canvasScaler.scaleFactor > 0.01f ? _canvasScaler.scaleFactor : 1f;
            return Screen.width / scale - MinViewportWidth;
        }

        private void ApplyPanelWidths()
        {
            if (_hierarchyPanel != null)
            {
                _hierarchyPanel.offsetMin = new Vector2(0, BottomPanelHeight);
                _hierarchyPanel.offsetMax = new Vector2(_hierarchyWidth, -TopPanelHeight);
            }
            if (_inspectorPanel != null)
                _inspectorPanel.offsetMin = new Vector2(-_inspectorWidth, BottomPanelHeight);
            UpdateResizeHandles();
        }

        // Pushes the current workspace height to every panel that sits above it.
        private void ApplyWorkspaceLayout()
        {
            var height = BottomPanelHeight;
            _appliedBottomHeight = height;
            if (_bottomPanel != null)
                _bottomPanel.offsetMax = new Vector2(0, height);
            if (_bottomTabContent != null)
                _bottomTabContent.gameObject.SetActive(!_workspaceCollapsed);
            if (_workspaceToggleButton != null)
            {
                var label = _workspaceToggleButton.GetComponentInChildren<Text>();
                if (label != null)
                    label.text = _workspaceCollapsed ? "Show" : "Hide";
            }
            ApplyPanelWidths();
        }

        // Re-applies the layout when the screen shrinks or grows enough to change the clamped height.
        private void SyncWorkspaceLayout()
        {
            if (_appliedBottomHeight != BottomPanelHeight)
                ApplyWorkspaceLayout();
        }

        public void SetWorkspaceCollapsed(bool collapsed)
        {
            if (_workspaceCollapsed == collapsed)
                return;
            _workspaceCollapsed = collapsed;
            ApplyWorkspaceLayout();
            SaveWorkspaceLayout();
        }

        private void BuildWorkspaceResizeHandle()
        {
            _workspaceResizeHandle = UIBuilder.CreatePanel("WorkspaceResize", _mainWindow, new Color(0.4f, 0.55f, 0.8f, 0.3f));
            _workspaceResizeImage = _workspaceResizeHandle.GetComponent<Image>();
            _workspaceResizeHandle.anchorMin = new Vector2(0f, 0f);
            _workspaceResizeHandle.anchorMax = new Vector2(1f, 0f);
            _workspaceResizeHandle.pivot = new Vector2(0.5f, 0f);

            // Centered grip so the handle reads as draggable.
            var grip = UIBuilder.CreatePanel("Grip", _workspaceResizeHandle, new Color(0.85f, 0.92f, 1f, 0.9f));
            grip.GetComponent<Image>().raycastTarget = false;
            grip.anchorMin = grip.anchorMax = new Vector2(0.5f, 0.5f);
            grip.pivot = new Vector2(0.5f, 0.5f);
            grip.sizeDelta = new Vector2(48f, 2f);

            var et = _workspaceResizeHandle.gameObject.AddComponent<EventTrigger>();
            AddTrigger(et, EventTriggerType.PointerEnter, _ =>
            {
                _workspaceHandleHover = true;
                SetResizeCursor(true);
                UpdateWorkspaceHandleColor();
            });
            AddTrigger(et, EventTriggerType.PointerExit, _ =>
            {
                _workspaceHandleHover = false;
                if (!_workspaceDragging)
                    SetResizeCursor(false);
                UpdateWorkspaceHandleColor();
            });
            AddTrigger(et, EventTriggerType.BeginDrag, _ =>
            {
                _workspaceDragging = true;
                // Dragging a collapsed workspace opens it first, at its remembered height.
                if (_workspaceCollapsed)
                {
                    _workspaceCollapsed = false;
                    ApplyWorkspaceLayout();
                }
                UpdateWorkspaceHandleColor();
            });
            AddTrigger(et, EventTriggerType.Drag, data =>
            {
                var scale = _canvasScaler != null && _canvasScaler.scaleFactor > 0.01f ? _canvasScaler.scaleFactor : 1f;
                // Accumulate the float height so slow drags aren't lost to rounding; upward drag grows the workspace.
                _workspaceHeight = Mathf.Clamp(_workspaceHeight + ((PointerEventData)data).delta.y / scale, MinWorkspaceHeight, MaxWorkspaceHeight);
                ApplyWorkspaceLayout();
            });
            AddTrigger(et, EventTriggerType.EndDrag, _ =>
            {
                _workspaceDragging = false;
                if (!_workspaceHandleHover)
                    SetResizeCursor(false);
                UpdateWorkspaceHandleColor();
                SaveWorkspaceLayout();
            });
            // Swallow clicks so they cannot reach viewport selection.
            AddTrigger(et, EventTriggerType.PointerClick, _ => { });
            UpdateResizeHandles();
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private void UpdateWorkspaceHandleColor()
        {
            if (_workspaceResizeImage == null)
                return;
            _workspaceResizeImage.color = _workspaceDragging || _workspaceHandleHover
                ? new Color(0.95f, 0.65f, 0.2f, 0.85f)
                : new Color(0.4f, 0.55f, 0.8f, 0.3f);
        }

        private static void SetResizeCursor(bool resize)
        {
            if (!resize)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }

            if (_resizeCursorTexture == null)
            {
                const int size = 24;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var clear = new Color32[size * size];
                tex.SetPixels32(clear);
                // Up/down double arrow with a dark outline so it is visible on any background.
                for (var pass = 0; pass < 2; pass++)
                {
                    var color = pass == 0 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                    var grow = pass == 0 ? 1 : 0;
                    for (var y = 3; y <= 20; y++)
                        for (var x = 11 - grow; x <= 12 + grow; x++)
                            tex.SetPixel(x, y, color);
                    for (var i = 0; i <= 6; i++)
                        for (var x = 11 - i - grow; x <= 12 + i + grow; x++)
                            tex.SetPixel(x, 21 - i, color);
                }
                // Lower arrow head (mirror of the upper one).
                for (var pass = 0; pass < 2; pass++)
                {
                    var color = pass == 0 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                    var grow = pass == 0 ? 1 : 0;
                    for (var i = 0; i <= 6; i++)
                        for (var x = 11 - i - grow; x <= 12 + i + grow; x++)
                            tex.SetPixel(x, 2 + i, color);
                }
                tex.Apply();
                _resizeCursorTexture = tex;
            }

            Cursor.SetCursor(_resizeCursorTexture, new Vector2(12f, 12f), CursorMode.Auto);
        }

        private class WorkspaceLayoutConfig
        {
            public bool collapsed;
            public float height = MinWorkspaceHeight;
            public int tab;
        }

        private void LoadWorkspaceLayout()
        {
            try
            {
                var configDir = Path.GetDirectoryName(Plugin.Instance.Config.ConfigFilePath);
                _workspaceLayoutPath = Path.Combine(configDir, "MapEditorLite", "workspaceLayout.json");
                if (File.Exists(_workspaceLayoutPath))
                {
                    var cfg = JsonConvert.DeserializeObject<WorkspaceLayoutConfig>(File.ReadAllText(_workspaceLayoutPath));
                    if (cfg != null)
                    {
                        _workspaceHeight = Mathf.Clamp(cfg.height, MinWorkspaceHeight, MaxWorkspaceHeight);
                        _workspaceCollapsed = cfg.collapsed;
                        if (cfg.tab >= 0 && cfg.tab <= 5)
                            SelectBottomTab(cfg.tab);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load workspace layout: {ex.Message}");
            }

            _workspaceReady = true;
            ApplyWorkspaceLayout();
        }

        private void SaveWorkspaceLayout()
        {
            if (!_workspaceReady || string.IsNullOrEmpty(_workspaceLayoutPath))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_workspaceLayoutPath));
                var cfg = new WorkspaceLayoutConfig { collapsed = _workspaceCollapsed, height = _workspaceHeight, tab = _activeBottomTab };
                File.WriteAllText(_workspaceLayoutPath, JsonConvert.SerializeObject(cfg, Formatting.Indented, PackData.InvariantSettings));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to save workspace layout: {ex.Message}");
            }
        }

        // Re-fits both side panels after a screen-size or UI-scale change (proportional shrink, minimums respected).
        private void ClampPanelWidthsToScreen()
        {
            var scale = _canvasScaler != null ? _canvasScaler.scaleFactor : 1f;
            if (Mathf.Approximately(_lastLayoutScreenWidth, Screen.width) && Mathf.Approximately(_lastLayoutScale, scale))
                return;
            _lastLayoutScreenWidth = Screen.width;
            _lastLayoutScale = scale;

            var budget = SidePanelBudget();
            var h = Mathf.Clamp(_hierarchyWidth, MinHierarchyWidth, MaxSidePanelWidth);
            var i = Mathf.Clamp(_inspectorWidth, MinInspectorWidth, MaxSidePanelWidth);
            if (h + i > budget && budget > MinHierarchyWidth + MinInspectorWidth)
            {
                var f = budget / (h + i);
                h = Mathf.Max(MinHierarchyWidth, Mathf.FloorToInt(h * f));
                i = Mathf.Max(MinInspectorWidth, Mathf.FloorToInt(i * f));
                if (h + i > budget)
                    i = Mathf.Max(MinInspectorWidth, Mathf.FloorToInt(budget) - h);
            }
            else if (h + i > budget)
            {
                h = MinHierarchyWidth;
                i = MinInspectorWidth;
            }
            _hierarchyWidth = h;
            _inspectorWidth = i;
            ApplyPanelWidths();
        }

        private void AddDragResize(RectTransform handle, bool isHierarchy)
        {
            var et = handle.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            entry.callback.AddListener((data) =>
            {
                var scale = _canvasScaler != null ? _canvasScaler.scaleFactor : 1f;
                var dx = Mathf.RoundToInt(((PointerEventData)data).delta.x / scale);
                var budget = SidePanelBudget();
                if (isHierarchy)
                {
                    var max = Mathf.Max(MinHierarchyWidth, Mathf.Min(MaxSidePanelWidth, Mathf.FloorToInt(budget) - _inspectorWidth));
                    _hierarchyWidth = Mathf.Clamp(_hierarchyWidth + dx, MinHierarchyWidth, max);
                }
                else
                {
                    var max = Mathf.Max(MinInspectorWidth, Mathf.Min(MaxSidePanelWidth, Mathf.FloorToInt(budget) - _hierarchyWidth));
                    _inspectorWidth = Mathf.Clamp(_inspectorWidth - dx, MinInspectorWidth, max);
                }
                ApplyPanelWidths();
            });
            et.triggers.Add(entry);
        }

        // Fixed-height row whose content scrolls horizontally (wheel or drag) when wider than the screen.
        // Returns the content rect; callers add layout and children to it.
        private RectTransform CreateHorizontalScrollRow(string name, RectTransform parent, Color background, int height)
        {
            var container = UIBuilder.CreatePanel(name, parent, background);
            UIBuilder.AddLayoutElement(container, null, height, null, height, null, 0);
            container.gameObject.AddComponent<RectMask2D>();

            var content = UIBuilder.CreatePanel(name + "Content", container, new Color(0, 0, 0, 0));
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            UIBuilder.AddContentSizeFitter(content, ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.Unconstrained);

            var scroll = container.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = container;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;
            scroll.inertia = false;
            return content;
        }

        private void BuildBottomPanel()
        {
            _bottomPanel = UIBuilder.CreatePanel("BottomPanel", _mainWindow, new Color(0.1f, 0.1f, 0.1f, 0.97f));
            // Anchor: bottom edge, full width
            _bottomPanel.anchorMin = new Vector2(0f, 0f);
            _bottomPanel.anchorMax = new Vector2(1f, 0f);
            _bottomPanel.pivot     = new Vector2(0f, 0f);
            _bottomPanel.offsetMin = Vector2.zero;
            _bottomPanel.offsetMax = new Vector2(0, BottomPanelHeight);
            UIBuilder.AddVerticalLayout(_bottomPanel, 4, 4, true, true);

            // Tab row: scrollable tab strip (so six tabs never clip) plus a fixed collapse/expand button.
            var tabRow = UIBuilder.CreatePanel("TabRow", _bottomPanel, new Color(0, 0, 0, 0));
            tabRow.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddLayoutElement(tabRow, null, TabBarHeight, null, TabBarHeight, null, 0);
            UIBuilder.AddHorizontalLayout(tabRow, 0, 4, true, true);

            var tabBar = CreateHorizontalScrollRow("TabBar", tabRow, new Color(0.075f, 0.09f, 0.12f, 1f), TabBarHeight);
            var tabBarLayout = tabBar.parent.GetComponent<LayoutElement>();
            tabBarLayout.flexibleWidth = 1f;
            UIBuilder.AddHorizontalLayout(tabBar, 3, 4, false, false);
            AddPanelAccent((RectTransform)tabBar.parent, atBottom: true);
            _prefabsTabButton  = CreateWorkspaceTab(tabBar, "Prefabs",     0, 84);
            _scatterTabButton  = CreateWorkspaceTab(tabBar, "Scatter",     1, 84);
            _groupsTabButton   = CreateWorkspaceTab(tabBar, "Groups",      2, 84);
            _objectsTabButton  = CreateWorkspaceTab(tabBar, "GameObjects", 3, 112);
            _removedTabButton  = CreateWorkspaceTab(tabBar, "Removed",     4, 84);
            _outputTabButton   = CreateWorkspaceTab(tabBar, "Output",      5, 84);

            _workspaceToggleButton = UIBuilder.CreateButton(tabRow, "Hide", () => SetWorkspaceCollapsed(!_workspaceCollapsed), 52, TabBarHeight, 12);
            UIBuilder.AddLayoutElement(_workspaceToggleButton.gameObject, 52, TabBarHeight, 52, TabBarHeight, 0, 0);
            ApplyButtonAccent(_workspaceToggleButton, new Color(0.3f, 0.3f, 0.34f, 1f));

            _bottomTabContent = UIBuilder.CreatePanel("TabContent", _bottomPanel, new Color(0, 0, 0, 0));
            _bottomTabContent.anchorMin = Vector2.zero;
            _bottomTabContent.anchorMax = Vector2.one;
            _bottomTabContent.offsetMin = Vector2.zero;
            _bottomTabContent.offsetMax = Vector2.zero;
            UIBuilder.AddLayoutElement(_bottomTabContent, null, null, null, null, null, 1);

            BuildPrefabsTab(_bottomTabContent);
            BuildScatterTab(_bottomTabContent);
            BuildGroupsTab(_bottomTabContent);
            BuildObjectsTab(_bottomTabContent);
            BuildRemovedTab(_bottomTabContent);
            BuildOutputTab(_bottomTabContent);

            SelectBottomTab(0);
        }

        private Button CreateWorkspaceTab(RectTransform parent, string label, int index, int width)
        {
            var btn = UIBuilder.CreateButton(parent, label, () => SelectBottomTab(index), width, TabBarHeight - 4, 12);
            return btn;
        }

        private void SelectBottomTab(int index)
        {
            // Choosing a tab while collapsed opens the workspace so the click always has a visible effect.
            if (_workspaceCollapsed && _workspaceReady)
                SetWorkspaceCollapsed(false);
            if (index == 5)
                EnsureOutputTab();
            _activeBottomTab = index;
            SaveWorkspaceLayout();
            _prefabsTab.gameObject.SetActive(index == 0);
            _scatterTab.gameObject.SetActive(index == 1);
            _groupsTab.gameObject.SetActive(index == 2);
            if (_objectsTab != null) _objectsTab.gameObject.SetActive(index == 3);
            if (_removedTab != null) _removedTab.gameObject.SetActive(index == 4);
            if (_outputTab != null) _outputTab.gameObject.SetActive(index == 5);

            UpdateTabButton(_prefabsTabButton, index == 0);
            UpdateTabButton(_scatterTabButton, index == 1);
            UpdateTabButton(_groupsTabButton, index == 2);
            if (_objectsTabButton != null) UpdateTabButton(_objectsTabButton, index == 3);
            if (_removedTabButton != null) UpdateTabButton(_removedTabButton, index == 4);
            if (_outputTabButton != null) UpdateTabButton(_outputTabButton, index == 5);
        }

        private void BuildScenePickerNotice()
        {
            _scenePickerNotice = UIBuilder.CreatePanel("ScenePickerNotice", _mainWindow, new Color(0.1f, 0.1f, 0.1f, 0.95f));
            _scenePickerNotice.anchorMin = new Vector2(0.5f, 1f);
            _scenePickerNotice.anchorMax = new Vector2(0.5f, 1f);
            _scenePickerNotice.pivot = new Vector2(0.5f, 1f);
            _scenePickerNotice.offsetMin = new Vector2(-180, -48);
            _scenePickerNotice.offsetMax = new Vector2(180, 0);
            UIBuilder.AddHorizontalLayout(_scenePickerNotice, 4, 4, false, false);
            UIBuilder.CreateText(_scenePickerNotice, "Click an object in the world", 11, Color.white);
            UIBuilder.CreateToggle(_scenePickerNotice, "Use Parent", _pickUseParent, (v) => _pickUseParent = v, 18);
            UIBuilder.CreateButton(_scenePickerNotice, "Cancel", () => CloseSceneObjectPicker(), 60, 22);
            _scenePickerNotice.gameObject.SetActive(false);

            // Small label that follows the mouse cursor while picking
            _scenePickerCursorTooltip = UIBuilder.CreatePanel("ScenePickerCursorTooltip", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.92f));
            _scenePickerCursorTooltip.anchorMin = new Vector2(0f, 0f);
            _scenePickerCursorTooltip.anchorMax = new Vector2(0f, 0f);
            _scenePickerCursorTooltip.pivot = new Vector2(0f, 0f);
            _scenePickerCursorTooltip.sizeDelta = new Vector2(190, 22);
            UIBuilder.AddHorizontalLayout(_scenePickerCursorTooltip, 4, 2, false, false);
            UIBuilder.CreateText(_scenePickerCursorTooltip, "Click an object to select it", 11, Color.white);
            _scenePickerCursorTooltip.gameObject.SetActive(false);
        }

        private void UpdateScenePickerCursorTooltip()
        {
            if (_scenePickerCursorTooltip == null)
                return;
            bool picking = _isPickingSceneGO;
            if (_scenePickerCursorTooltip.gameObject.activeSelf != picking)
            {
                _scenePickerCursorTooltip.gameObject.SetActive(picking);
                if (picking)
                    _scenePickerCursorTooltip.SetAsLastSibling();
            }
            if (picking)
                _scenePickerCursorTooltip.anchoredPosition = (Vector2)Input.mousePosition + new Vector2(12f, 12f);
        }

        public void OpenSceneObjectPicker()
        {
            if (_objectsTabButton != null)
                SelectBottomTab(3);
            SetPickingSceneGO(true);
        }

        public void CloseSceneObjectPicker()
        {
            SetPickingSceneGO(false);
        }

        private void UpdateTabButton(Button btn, bool active)
        {
            if (btn == null)
                return;
            var color = active ? new Color(0.15f, 0.5f, 0.65f, 1f) : new Color(0.22f, 0.22f, 0.24f, 1f);
            ApplyButtonAccent(btn, color);
            var label = btn.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                label.color = active ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f);
            }
        }

        private void BuildPrefabsTab(RectTransform parent)
        {
            _prefabsTab = UIBuilder.CreatePanel("PrefabsTab", parent, new Color(0.14f, 0.14f, 0.14f, 1f));
            _prefabsTab.anchorMin = Vector2.zero;
            _prefabsTab.anchorMax = Vector2.one;
            _prefabsTab.offsetMin = Vector2.zero;
            _prefabsTab.offsetMax = Vector2.zero;
            UIBuilder.AddHorizontalLayout(_prefabsTab, 4, 4, true, true);

            // Left column: save controls
            var leftCol = UIBuilder.CreatePanel("PrefabsLeft", _prefabsTab, new Color(0, 0, 0, 0));
            leftCol.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddLayoutElement(leftCol, 180, null, 180, null, 0, 1);
            UIBuilder.AddVerticalLayout(leftCol, 2, 3, true, true);

            UIBuilder.CreateText(leftCol, "Prefabs", 12, Color.white, FontStyle.Bold);
            var saveRow = UIBuilder.CreatePanel("PrefabSaveRow", leftCol, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(saveRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(saveRow, null, 22, null, 22, null, 0);
            _prefabNameInput = UIBuilder.CreateInputField(saveRow, "Prefab name", _prefabName, (v) => _prefabName = v, 100, 22);
            UIBuilder.CreateButton(saveRow, "Save", () => { controller.SavePrefab(_prefabName, ""); RequestRefresh(); }, 46, 22);

            // Right column: list of saved prefabs
            var rightCol = UIBuilder.CreatePanel("PrefabsList", _prefabsTab, new Color(0.1f, 0.1f, 0.1f, 1f));
            UIBuilder.AddLayoutElement(rightCol, null, null, null, null, 1, 1);
            UIBuilder.AddVerticalLayout(rightCol, 2, 2, true, true);
            UIBuilder.CreateText(rightCol, "Saved Prefabs (click to place)", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
            var prefabsScroll = UIBuilder.CreateScrollView(rightCol, out _prefabsContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(prefabsScroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_prefabsContent, 2, 2, true, true);
        }

        private void BuildScatterTab(RectTransform parent)
        {
            _scatterTab = UIBuilder.CreatePanel("ScatterTab", parent, new Color(0.14f, 0.14f, 0.14f, 1f));
            _scatterTab.anchorMin = Vector2.zero;
            _scatterTab.anchorMax = Vector2.one;
            _scatterTab.offsetMin = Vector2.zero;
            _scatterTab.offsetMax = Vector2.zero;
            UIBuilder.AddVerticalLayout(_scatterTab, 4, 4, true, true);

            UIBuilder.CreateText(_scatterTab, "Scatter (select LootZone)", 12, Color.white, FontStyle.Bold);

            // Dynamic pick-from-scene row
            _scatterPickRow = UIBuilder.CreatePanel("ScatterPickRow", _scatterTab, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(_scatterPickRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(_scatterPickRow, null, 22, null, 22, null, 0);
            RefreshScatterPickRow();

            var row = UIBuilder.CreatePanel("ScatterCountRow", _scatterTab, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, "Count", 11, 40, 22);
            _scatterCountInput = UIBuilder.CreateInputField(row, "Count", _scatterCount.ToString(), (v) => int.TryParse(v, out _scatterCount), 50, 22);

            var row2 = UIBuilder.CreatePanel("ScatterHeightRow", _scatterTab, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row2, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row2, null, 22, null, 22, null, 0);
            _scatterMinHeightInput = UIBuilder.CreateInputField(row2, "Min H", _scatterMinHeight.ToString("F2", CultureInfo.InvariantCulture), (v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _scatterMinHeight), 60, 22);
            _scatterMaxHeightInput = UIBuilder.CreateInputField(row2, "Max H", _scatterMaxHeight.ToString("F2", CultureInfo.InvariantCulture), (v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _scatterMaxHeight), 60, 22);
            _scatterSnapToggle = UIBuilder.CreateToggle(row2, "Snap to ground", _scatterSnapToGround, (v) => _scatterSnapToGround = v, 20);

            UIBuilder.CreateButton(_scatterTab, "Scatter", () =>
            {
                if (manager.Selected is LootZone zone)
                    controller.ScatterObjectsInZone(zone, _scatterPrefabPath, _scatterCount, _scatterMinHeight, _scatterMaxHeight, _scatterSnapToGround);
            }, 80, 24);
        }

        private void BuildGroupsTab(RectTransform parent)
        {
            _groupsTab = UIBuilder.CreatePanel("GroupsTab", parent, new Color(0.14f, 0.14f, 0.14f, 1f));
            _groupsTab.anchorMin = Vector2.zero;
            _groupsTab.anchorMax = Vector2.one;
            _groupsTab.offsetMin = Vector2.zero;
            _groupsTab.offsetMax = Vector2.zero;
            UIBuilder.AddVerticalLayout(_groupsTab, 4, 4, true, true);

            UIBuilder.CreateText(_groupsTab, "Groups", 12, Color.white, FontStyle.Bold);
            var row = UIBuilder.CreatePanel("GroupAssignRow", _groupsTab, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            _groupInput = UIBuilder.CreateInputField(row, "Group", _newGroupName, (v) => _newGroupName = v, 120, 22);
            UIBuilder.CreateButton(row, "Assign", () => { if (manager.Selected != null) { manager.SetGroupOnSelection(_newGroupName); RequestRefresh(); } }, 60, 22);
            UIBuilder.CreateButton(row, "Clear", ClearGroupAssignmentFromSelection, 50, 22);
            UIBuilder.CreateButton(row, "Delete Group", RequestDeleteGroup, 90, 22);

            _groupsStatusText = UIBuilder.CreateText(_groupsTab, "", 10, new Color(1f, 0.75f, 0.35f, 1f));
            UIBuilder.AddLayoutElement(_groupsStatusText.gameObject, null, 16, null, 16, null, 0);

            BuildDeleteGroupConfirm();

            var groupsScroll = UIBuilder.CreateScrollView(_groupsTab, out _groupsContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(groupsScroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_groupsContent, 1, 1, true, false);
        }

        private void ClearGroupAssignmentFromSelection()
        {
            var selectedMarkers = manager.SelectedIds
                .Select(manager.FindById)
                .Where(marker => marker != null && !manager.IsVanilla(marker))
                .ToList();
            if (selectedMarkers.Count == 0)
            {
                SetGroupsStatus("Select editable pack markers in the hierarchy before clearing their group.");
                return;
            }

            manager.SetGroupOnSelection("");
            SetGroupsStatus("");
            RequestRefresh();
        }

        private void SetGroupsStatus(string message)
        {
            if (_groupsStatusText == null)
                return;
            _groupsStatusText.text = Locale.Get(message);
        }

        private void BuildDeleteGroupConfirm()
        {
            _deleteGroupConfirmPanel = UIBuilder.CreatePanel("DeleteGroupConfirm", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.97f));
            _deleteGroupConfirmPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _deleteGroupConfirmPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _deleteGroupConfirmPanel.pivot = new Vector2(0.5f, 0.5f);
            _deleteGroupConfirmPanel.sizeDelta = new Vector2(420, 150);
            UIBuilder.AddVerticalLayout(_deleteGroupConfirmPanel, 12, 8, true, true);

            var message = UIBuilder.CreateText(
                _deleteGroupConfirmPanel,
                "Deleting a group clears its assignment from all members, including locked markers, and removes its group lock. Markers and scene objects are not deleted.",
                12,
                Color.white);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.GetComponent<LayoutElement>().preferredHeight = 72;

            var row = UIBuilder.CreatePanel("DeleteGroupConfirmActions", _deleteGroupConfirmPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 8, 8, false, false);
            UIBuilder.AddLayoutElement(row, null, 32, null, 32, null, 0);
            UIBuilder.CreateButton(row, "Delete Group", ConfirmDeleteGroup, 110, 28);
            UIBuilder.CreateButton(row, "Cancel", CancelDeleteGroup, 100, 28);
            _deleteGroupConfirmPanel.gameObject.SetActive(false);
        }

        private void RequestDeleteGroup()
        {
            if (string.IsNullOrWhiteSpace(_selectedGroupName))
            {
                SetGroupsStatus("Select a group row before deleting a group.");
                return;
            }

            SetGroupsStatus("");
            _pendingGroupDeleteName = _selectedGroupName;
            _deleteGroupConfirmPanel.gameObject.SetActive(true);
            _deleteGroupConfirmPanel.SetAsLastSibling();
        }

        private void ConfirmDeleteGroup()
        {
            var group = _pendingGroupDeleteName;
            _pendingGroupDeleteName = null;
            _deleteGroupConfirmPanel.gameObject.SetActive(false);
            if (string.IsNullOrWhiteSpace(group))
                return;

            manager.DeleteGroupAssignments(group);
            if (string.Equals(_selectedGroupName, group, StringComparison.OrdinalIgnoreCase))
            {
                _selectedGroupName = null;
                _newGroupName = "";
                if (_groupInput != null)
                    _groupInput.text = "";
            }
            SetGroupsStatus("");
            RequestRefresh();
        }

        private void CancelDeleteGroup()
        {
            _pendingGroupDeleteName = null;
            _deleteGroupConfirmPanel.gameObject.SetActive(false);
        }

        private void BuildObjectsTab(RectTransform parent)
        {
            _objectsTab = UIBuilder.CreatePanel("ObjectsTab", parent, new Color(0.14f, 0.14f, 0.14f, 1f));
            _objectsTab.anchorMin = Vector2.zero;
            _objectsTab.anchorMax = Vector2.one;
            _objectsTab.offsetMin = Vector2.zero;
            _objectsTab.offsetMax = Vector2.zero;
            UIBuilder.AddHorizontalLayout(_objectsTab, 4, 4, true, true);

            // ── Left: filter bar + scrollable name list ──────────────────────
            var leftCol = UIBuilder.CreatePanel("GOLeft", _objectsTab, new Color(0, 0, 0, 0));
            leftCol.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddLayoutElement(leftCol, null, null, null, null, 1, 1);
            UIBuilder.AddVerticalLayout(leftCol, 2, 2, true, true);

            var searchRow = UIBuilder.CreatePanel("GOSearch", leftCol, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(searchRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(searchRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(searchRow, "Filter", 11, 34, 22);
            UIBuilder.CreateInputField(searchRow, "type to filter...", _objectsSearchText,
                (v) => { _objectsSearchText = v; _objectsPage = 0; RefreshObjectsList(); }, 110, 22);
            UIBuilder.CreateButton(searchRow, "Scan Scene", () => { ScanSceneObjects(); RefreshObjectsList(); }, 80, 22);

            _scanRadiusRow = UIBuilder.CreatePanel("GORadius", searchRow, new Color(0, 0, 0, 0));
            _scanRadiusRow.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddHorizontalLayout(_scanRadiusRow, 0, 2, false, false);
            UIBuilder.AddLayoutElement(_scanRadiusRow, null, 22, null, 22, null, 0);
            BuildScanRadiusControls();

            _goPickRow = UIBuilder.CreatePanel("GOPick", leftCol, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(_goPickRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(_goPickRow, null, 22, null, 22, null, 0);
            RefreshGOPickRow();

            var pageRow = UIBuilder.CreatePanel("GOPager", leftCol, new Color(0, 0, 0, 0));
            pageRow.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddHorizontalLayout(pageRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(pageRow, null, 22, null, 22, null, 0);
            _objectsPrevButton = UIBuilder.CreateButton(pageRow, "<", () =>
            {
                if (_objectsPage > 0) { _objectsPage--; RefreshObjectsList(); }
            }, 26, 22);
            _objectsPageLabel = UIBuilder.CreateLabel(pageRow, "", 11, 80, 22);
            _objectsPageLabel.alignment = TextAnchor.MiddleCenter;
            _objectsNextButton = UIBuilder.CreateButton(pageRow, ">", () =>
            {
                _objectsPage++;
                RefreshObjectsList();
            }, 26, 22);
            _objectsRangeLabel = UIBuilder.CreateLabel(pageRow, "", 11, 150, 22);
            _objectsScanLabel = UIBuilder.CreateLabel(pageRow, "", 11, 0, 22);
            UIBuilder.AddLayoutElement(_objectsScanLabel.gameObject, null, 22, null, 22, 1, 0);
            _objectsScanLabel.color = new Color(0.95f, 0.75f, 0.35f, 1f);
            UpdateScanStatusLabel(true);

            var scroll = UIBuilder.CreateScrollView(leftCol, out _objectsContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_objectsContent, 1, 2, true, true);

            var et = _objectsContent.gameObject.AddComponent<EventTrigger>();
            var deselectEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            deselectEntry.callback.AddListener((BaseEventData data) =>
            {
                if (data is PointerEventData ped && ped.button == PointerEventData.InputButton.Left)
                    SelectSceneGO(null);
            });
            et.triggers.Add(deselectEntry);

            // ── Right: name label + action buttons ────────────────────────────
            var rightCol = UIBuilder.CreatePanel("GORight", _objectsTab, new Color(0.1f, 0.1f, 0.1f, 1f));
            UIBuilder.AddLayoutElement(rightCol, 120, null, 120, null, 0, 1);
            UIBuilder.AddVerticalLayout(rightCol, 4, 4, true, true);

            // Action row stays fixed; name + details share one scroll view so long text is reachable.
            _goActionBtnRow = UIBuilder.CreatePanel("GOActions", rightCol, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(_goActionBtnRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(_goActionBtnRow, null, 22, null, 22, null, 0);
            RefreshGOActionRow();

            var detailsScroll = UIBuilder.CreateScrollView(rightCol, out var detailsContent, out _, 0, 0, 10);
            UIBuilder.AddLayoutElement(detailsScroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(detailsContent, 2, 4, true, true);

            _goPreviewNameText = UIBuilder.CreateText(detailsContent, "No selection", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
            if (_goPreviewNameText != null)
            {
                _goPreviewNameText.alignment = TextAnchor.UpperCenter;
                _goPreviewNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _goPreviewNameText.verticalOverflow = VerticalWrapMode.Overflow;
                _goPreviewNameText.GetComponent<LayoutElement>().preferredHeight = -1f;
            }

            _goDetailsText = UIBuilder.CreateText(detailsContent, "Select an object to view details.", 10, new Color(0.7f, 0.7f, 0.7f, 1f));
            if (_goDetailsText != null)
            {
                _goDetailsText.alignment = TextAnchor.UpperLeft;
                _goDetailsText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _goDetailsText.verticalOverflow = VerticalWrapMode.Overflow;
                _goDetailsText.GetComponent<LayoutElement>().preferredHeight = -1f;
            }
        }

        private void BuildRemovedTab(RectTransform parent)
        {
            _removedTab = UIBuilder.CreatePanel("RemovedTab", parent, new Color(0.14f, 0.14f, 0.14f, 1f));
            _removedTab.anchorMin = Vector2.zero;
            _removedTab.anchorMax = Vector2.one;
            _removedTab.offsetMin = Vector2.zero;
            _removedTab.offsetMax = Vector2.zero;
            UIBuilder.AddVerticalLayout(_removedTab, 4, 4, true, true);

            var header = UIBuilder.CreatePanel("RemovedHeader", _removedTab, new Color(0, 0, 0, 0));
            header.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddHorizontalLayout(header, 2, 2, false, false);
            UIBuilder.AddLayoutElement(header, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(header, "Refresh", () => RefreshRemovedObjectsList(), 70, 22);
            UIBuilder.CreateButton(header, "Apply Preview", () => ApplyRemovedObjectsPreview(), 90, 22);
            UIBuilder.CreateButton(header, "Restore All", () => RestoreAllRemovedObjects(), 80, 22);

            var scroll = UIBuilder.CreateScrollView(_removedTab, out _removedContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_removedContent, 2, 2, true, true);

            RefreshRemovedObjectsList();
        }

        private void BuildOutputTab(RectTransform parent)
        {
            _outputTab = UIBuilder.CreatePanel("OutputTab", parent, new Color(0.14f, 0.14f, 0.14f, 1f));
            _outputTab.anchorMin = Vector2.zero;
            _outputTab.anchorMax = Vector2.one;
            _outputTab.offsetMin = Vector2.zero;
            _outputTab.offsetMax = Vector2.zero;
            UIBuilder.AddVerticalLayout(_outputTab, 4, 4, true, true);

            var header = UIBuilder.CreatePanel("OutputHeader", _outputTab, new Color(0, 0, 0, 0));
            header.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddHorizontalLayout(header, 2, 2, false, false);
            UIBuilder.AddLayoutElement(header, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(header, "Copy", () => CopyOutputToClipboard(), 60, 22);
            UIBuilder.CreateButton(header, "Clear", () => ClearOutput(), 60, 22);

            var scroll = UIBuilder.CreateScrollView(_outputTab, out _outputContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_outputContent, 2, 2, true, true);

            RefreshOutput();
        }

        private void EnsureOutputTab()
        {
            if (_outputTab != null || _bottomTabContent == null)
                return;
            try
            {
                BuildOutputTab(_bottomTabContent);
                Plugin.Log.LogInfo("CustomEditorUI rebuilt OutputTab on demand");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"CustomEditorUI failed to rebuild OutputTab: {ex}");
            }
        }

        private void RefreshOutput()
        {
            if (_outputContent == null)
                return;
            ClearChildren(_outputContent);
            List<string> messages;
            lock (_outputMessages)
            {
                messages = new List<string>(_outputMessages);
            }
            if (messages.Count == 0)
            {
                UIBuilder.CreateText(_outputContent, "No messages yet.", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
                return;
            }
            foreach (var msg in messages)
            {
                var line = UIBuilder.CreateWrappedText(_outputContent, msg, 11, Color.white);
                line.alignment = TextAnchor.UpperLeft;
            }
        }

        private void UpdateObjectsPager(int total, int pageCount, int start, int shown)
        {
            if (_objectsPageLabel != null)
                _objectsPageLabel.text = $"Page {_objectsPage + 1} / {pageCount}";
            if (_objectsRangeLabel != null)
                _objectsRangeLabel.text = total == 0
                    ? "Showing 0 of 0"
                    : $"Showing {(start + 1):N0}–{(start + shown):N0} of {total:N0}";
            if (_objectsPrevButton != null)
                _objectsPrevButton.interactable = _objectsPage > 0;
            if (_objectsNextButton != null)
                _objectsNextButton.interactable = _objectsPage < pageCount - 1;
        }

        // Drops destroyed entries first so totals, pages and neighbour checks only see live objects.
        private List<GameObject> GetFilteredSceneObjects()
        {
            _sceneObjectCache.RemoveAll(g => g == null);
            var filter = _objectsSearchText?.Trim() ?? "";
            return string.IsNullOrEmpty(filter)
                ? _sceneObjectCache
                : _sceneObjectCache.Where(g => g.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private void RefreshObjectsList()
        {
            if (_objectsContent == null) return;
            ClearChildren(_objectsContent);

            if (_scanning)
            {
                UpdateObjectsPager(0, 1, 0, 0);
                UIBuilder.CreateText(_objectsContent, "Scanning scene... please wait", 11, new Color(0.7f, 0.85f, 0.7f, 1f));
                return;
            }

            var filtered = GetFilteredSceneObjects();

            int total = filtered.Count;
            int pageCount = Mathf.Max(1, (total + ObjectsPageSize - 1) / ObjectsPageSize);
            _objectsPage = Mathf.Clamp(_objectsPage, 0, pageCount - 1);
            int start = _objectsPage * ObjectsPageSize;
            var list = filtered.Skip(start).Take(ObjectsPageSize).ToList();
            UpdateObjectsPager(total, pageCount, start, list.Count);

            renderer?.SetSelectedSceneObject(_selectedSceneGO);

            if (list.Count == 0)
            {
                UIBuilder.CreateText(_objectsContent,
                    _sceneObjectCache.Count == 0
                        ? (_hasScanned ? "No objects found in the scanned area." : "Click 'Scan Scene' to populate the list.")
                        : "No objects match the filter.",
                    11, new Color(0.5f, 0.5f, 0.5f, 1f));
                return;
            }

            for (int index = 0; index < list.Count; index++)
            {
                var go = list[index];
                if (go == null) continue;
                var captured = go;
                bool isSelected = _selectedSceneGO == go;

                // The list is sorted by name, so same-named objects are neighbours; add parent + instance ID so they can be told apart.
                var label = go.name;
                var globalIndex = start + index;
                if ((globalIndex > 0 && filtered[globalIndex - 1] != null && string.Equals(filtered[globalIndex - 1].name, go.name, StringComparison.Ordinal)) ||
                    (globalIndex + 1 < filtered.Count && filtered[globalIndex + 1] != null && string.Equals(filtered[globalIndex + 1].name, go.name, StringComparison.Ordinal)))
                {
                    var parent = go.transform.parent;
                    label = $"{go.name}  [{(parent != null ? parent.name : "root")} #{go.GetInstanceID()}]";
                }

                var row = UIBuilder.CreatePanel("GORow", _objectsContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 1, 1, true, false);
                UIBuilder.AddLayoutElement(row, null, 14, null, 14, null, 0);
                row.GetComponent<Image>().color = isSelected
                    ? new Color(0.2f, 0.3f, 0.45f, 0.6f)
                    : new Color(0.13f, 0.13f, 0.13f, 0.3f);

                var btn = UIBuilder.CreateButton(row, label, () =>
                {
                    // Never fall back to another object if the one in the list has since been destroyed.
                    if (captured == null)
                    {
                        _scanStatusOverride = "That object no longer exists (scene changed) - rescan.";
                        UpdateScanStatusLabel(true);
                        RefreshObjectsList();
                        return;
                    }
                    SelectSceneGO(captured);
                }, 0, 12, 10);
                UIBuilder.AddLayoutElement(btn.gameObject, null, 12, null, 12, 1, 0);
                btn.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                var bc = btn.colors;
                bc.normalColor = Color.white;
                bc.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
                bc.pressedColor = new Color(0.7f, 0.8f, 1f, 1f);
                btn.colors = bc;
                var lbl = btn.GetComponentInChildren<Text>();
                if (lbl != null)
                {
                    lbl.fontSize = 10;
                    lbl.alignment = TextAnchor.MiddleLeft;
                    lbl.color = isSelected ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.72f, 0.72f, 0.72f, 1f);
                }
            }
        }

        private Toggle _scanRadiusToggle;
        private InputField _scanRadiusInput;

        // Radius settings only affect the next Scan Scene press; they never trigger a scan themselves.
        private void BuildScanRadiusControls()
        {
            _scanRadiusToggle = UIBuilder.CreateToggle(_scanRadiusRow, "Radius", _scanRadiusEnabled, (v) =>
            {
                _scanRadiusEnabled = v;
                UpdateScanStatusLabel(true);
            }, 18);

            _scanRadiusInput = UIBuilder.CreateInputField(_scanRadiusRow, "m", _scanRadius.ToString("0.#"), (v) =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0f)
                {
                    _scanRadius = Mathf.Clamp(r, MinScanRadius, MaxScanRadius);
                    UpdateScanStatusLabel(true);
                }
            }, 42, 22);
            _scanRadiusInput.onEndEdit.AddListener(_ => _scanRadiusInput.SetTextWithoutNotify(_scanRadius.ToString("0.#")));

            foreach (var preset in new[] { 5f, 10f, 25f, 50f })
            {
                var p = preset;
                UIBuilder.CreateButton(_scanRadiusRow, p.ToString("0"), () => SetScanRadiusPreset(p), 28, 22, 10);
            }

            UIBuilder.CreateButton(_scanRadiusRow, "Entire", () =>
            {
                _scanRadiusEnabled = false;
                _scanRadiusToggle.SetIsOnWithoutNotify(false);
                UpdateScanStatusLabel(true);
            }, 40, 22, 10);
        }

        private void SetScanRadiusPreset(float meters)
        {
            _scanRadius = Mathf.Clamp(meters, MinScanRadius, MaxScanRadius);
            _scanRadiusEnabled = true;
            _scanRadiusToggle?.SetIsOnWithoutNotify(true);
            _scanRadiusInput?.SetTextWithoutNotify(_scanRadius.ToString("0.#"));
            UpdateScanStatusLabel(true);
        }

        // Describes the snapshot currently in the list, and flags when pressing Scan Scene would give a different result.
        private void UpdateScanStatusLabel(bool force)
        {
            if (_objectsScanLabel == null)
                return;
            if (!force && Time.unscaledTime < _nextScanStatusTime)
                return;
            _nextScanStatusTime = Time.unscaledTime + 0.5f;

            string text;
            if (!string.IsNullOrEmpty(_scanStatusOverride))
                text = _scanStatusOverride;
            else if (!_hasScanned)
                text = _scanRadiusEnabled ? $"Next scan: {_scanRadius:0.#} m around editor camera" : "Next scan: entire scene";
            else if (!_scanSnapLimited)
            {
                text = "Scan: entire scene";
                if (_scanRadiusEnabled)
                    text += " (radius set - rescan to apply)";
            }
            else
            {
                var c = _scanSnapCenter;
                text = $"LIMITED scan: {_scanSnapRadius:0.#} m around ({c.x:0}, {c.y:0}, {c.z:0})";
                if (!_scanRadiusEnabled || !Mathf.Approximately(_scanRadius, _scanSnapRadius))
                    text += " - settings changed, rescan";
                else if (controller != null && controller.TryGetEditorCameraPosition(out var cam) && (cam - c).sqrMagnitude > 1f)
                    text += $" - camera now {Vector3.Distance(cam, c):0} m away, rescan";
            }

            _objectsScanLabel.text = text;
        }

        private void RefreshGOPickRow()
        {
            if (_goPickRow == null) return;
            ClearChildren(_goPickRow);
            if (_isPickingSceneGO)
            {
                UIBuilder.CreateLabel(_goPickRow, "Click object in world...", 11, 136, 22);
                UIBuilder.CreateButton(_goPickRow, "Cancel", () => SetPickingSceneGO(false), 54, 22);
            }
            else
            {
                UIBuilder.CreateButton(_goPickRow, "Pick from Scene", () => SetPickingSceneGO(true), 100, 22);
                UIBuilder.CreateToggle(_goPickRow, "Use Parent", _pickUseParent, (v) => _pickUseParent = v, 18);
            }
        }

        private void RefreshGOActionRow()
        {
            if (_goActionBtnRow == null) return;
            ClearChildren(_goActionBtnRow);

            if (_goDetailsText != null)
                _goDetailsText.text = FormatSceneGODetails(_selectedSceneGO);

            if (_selectedSceneGO == null)
            {
                UIBuilder.CreateLabel(_goActionBtnRow, "Select an object", 10, 120, 22);
                _goActionBtnRow.gameObject.SetActive(true);
                return;
            }

            if (_goListTarget != null)
            {
                UIBuilder.CreateButton(_goActionBtnRow, "Set Source", () =>
                {
                    _goListTarget.sourceObjectName = _selectedSceneGO.name;
                    _goListTarget.sourceObjectPosition = TransformData.FromVector3(_selectedSceneGO.transform.position);
                    _goListTarget.sourceWorldObjectId = _selectedSceneGO.GetComponentInChildren<WorldInteractiveObject>(true)?.Id ?? "";
                    if (_goListTarget is MarkerBase markerBase)
                    {
                        markerBase.rotation = TransformData.FromVector3(_selectedSceneGO.transform.rotation.eulerAngles);
                        previews?.RegisterStaticSource(markerBase.id, _selectedSceneGO);
                        previews?.SpawnPreviewForMarker(markerBase);
                    }
                    manager.IsDirty = true;
                    _goListTarget = null;
                    RefreshInspector();
                    RefreshGOActionRow();
                }, 76, 22);
                UIBuilder.CreateButton(_goActionBtnRow, "Cancel", () =>
                {
                    _goListTarget = null;
                    RefreshGOActionRow();
                }, 46, 22);
                _goActionBtnRow.gameObject.SetActive(true);
                return;
            }

            // Place Here / Remove have moved to the Inspector tab
            _goActionBtnRow.gameObject.SetActive(false);
        }

        private string FormatSceneGODetails(GameObject go)
        {
            if (go == null)
                return Locale.Get("Select an object to view details.");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{Locale.Get("Name:")} {go.name}");
            sb.AppendLine($"{Locale.Get("Path:")} {GetFullPath(go.transform)}");
            sb.AppendLine($"{Locale.Get("Pos:")} {go.transform.position}");
            sb.AppendLine($"{Locale.Get("Rot:")} {go.transform.rotation.eulerAngles}");
            sb.AppendLine($"{Locale.Get("Scale:")} {go.transform.localScale}");

            var wio = go.GetComponentInChildren<WorldInteractiveObject>(true);
            if (wio != null)
            {
                sb.AppendLine($"--- {Locale.Get("WorldInteractiveObject")} ---");
                sb.AppendLine($"{Locale.Get("Id:")} {wio.Id}");
                sb.AppendLine($"{Locale.Get("KeyId:")} {wio.KeyId}");
                sb.AppendLine($"{Locale.Get("DoorState:")} {wio.DoorState}");
                sb.AppendLine($"{Locale.Get("Initial:")} {wio.InitialDoorState}");
                sb.AppendLine($"{Locale.Get("Fallback:")} {wio.FallbackState}");
            }

            var container = wio as LootableContainer ?? go.GetComponentInChildren<LootableContainer>(true);
            if (container != null)
            {
                sb.AppendLine($"--- {Locale.Get("LootableContainer")} ---");
                sb.AppendLine($"{Locale.Get("Id:")} {container.Id}");
                sb.AppendLine($"{Locale.Get("Template:")} {container.Template}");
            }

            var sw = go.GetComponentInChildren<StationaryWeapon>(true);
            if (sw != null)
            {
                sb.AppendLine($"--- {Locale.Get("StationaryWeapon")} ---");
                sb.AppendLine($"{Locale.Get("IdEditable:")} {sw.IdEditable}");
                sb.AppendLine($"{Locale.Get("Template:")} {sw.Template}");
            }

            return sb.ToString();
        }

        private static string GetFullPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void RemoveSelectedSceneGO()
        {
            if (_selectedSceneGO == null || manager.Data == null) return;
            if (IsEditorObject(_selectedSceneGO))
            {
                Plugin.Log.LogWarning($"Cannot remove editor-owned object '{_selectedSceneGO.name}'.");
                return;
            }
            var go = _selectedSceneGO;
            var goWio = go.GetComponentInChildren<WorldInteractiveObject>(true);
            var removed = new RemovedObject
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = go.name,
                path = GetFullPath(go.transform),
                worldObjectId = goWio != null ? goWio.Id : string.Empty,
                position = TransformData.FromVector3(go.transform.position),
                rotation = TransformData.FromVector3(go.transform.rotation.eulerAngles),
                scale = TransformData.FromVector3(go.transform.localScale)
            };
            var state = RemovedObjectHelper.SoftRemove(go);
            removed.originalDoorState = state.OriginalDoorState;
            _removedObjectInstances[removed.id] = state;
            manager.Data.removedObjects.Add(removed);
            _sceneObjectCache.Remove(go);
            _selectedSceneGO = null;
            renderer?.SetSceneObjects(_sceneObjectCache, _selectedSceneGO);
            manager.IsDirty = true;
            if (_goPreviewNameText != null)
                _goPreviewNameText.text = Locale.Get("No selection");
            RefreshObjectsList();
            RefreshGOActionRow();
            RefreshRemovedObjectsList();
            Plugin.Log.LogInfo($"Marked scene object for removal: {removed.name} at {removed.position}");
        }

        private void RestoreRemovedObject(string id)
        {
            var removed = manager.Data?.removedObjects?.FirstOrDefault(r => r.id == id);
            if (removed == null) return;
            if (_removedObjectInstances.TryGetValue(id, out var state) && state != null)
            {
                RemovedObjectHelper.Restore(state);
            }
            _removedObjectInstances.Remove(id);
            manager.Data.removedObjects.Remove(removed);
            manager.IsDirty = true;
            RefreshObjectsList();
            RefreshGOActionRow();
            RefreshRemovedObjectsList();
            Plugin.Log.LogInfo($"Restored scene object: {removed.name}");
        }

        private void RestoreAllRemovedObjects()
        {
            if (manager.Data?.removedObjects == null) return;
            foreach (var state in _removedObjectInstances.Values)
            {
                if (state != null) RemovedObjectHelper.Restore(state);
            }
            _removedObjectInstances.Clear();
            manager.Data.removedObjects.Clear();
            manager.IsDirty = true;
            RefreshObjectsList();
            RefreshGOActionRow();
            RefreshRemovedObjectsList();
            Plugin.Log.LogInfo("Restored all removed scene objects.");
        }

        private void ApplyRemovedObjectsPreview()
        {
            if (manager.Data?.removedObjects == null) return;
            foreach (var removed in manager.Data.removedObjects)
            {
                if (_removedObjectInstances.ContainsKey(removed.id)) continue;
                var go = FindSceneObjectByNameAndPosition(removed.name, removed.position.ToVector3());
                if (go != null && !IsEditorObject(go))
                {
                    var state = RemovedObjectHelper.SoftRemove(go);
                    removed.originalDoorState = state.OriginalDoorState;
                    _removedObjectInstances[removed.id] = state;
                }
                else if (go == null)
                {
                    Plugin.Log.LogWarning($"Could not find scene object to preview removal: {removed.name}");
                }
                else
                {
                    Plugin.Log.LogWarning($"Skipping preview removal of editor-owned object '{removed.name}'.");
                }
            }
            RefreshObjectsList();
            RefreshGOActionRow();
            RefreshRemovedObjectsList();
        }

        private GameObject FindSceneObjectByNameAndPosition(string name, Vector3 position)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;
            var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != name) continue;
                        var dist = (t.position - position).sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = t.gameObject;
                        }
                    }
                }
            }
            return best;
        }

        private void RefreshRemovedObjectsList()
        {
            if (_removedContent == null) return;
            ClearChildren(_removedContent);
            var list = manager?.Data?.removedObjects;
            if (list == null || list.Count == 0)
            {
                UIBuilder.CreateText(_removedContent, "No vanilla objects marked for removal.", 11, new Color(0.5f, 0.5f, 0.5f, 1f));
                return;
            }
            foreach (var removed in list)
            {
                if (removed == null) continue;
                var row = UIBuilder.CreatePanel("RemovedRow", _removedContent, new Color(0.13f, 0.13f, 0.13f, 0.3f));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 18, null, 18, null, 0);
                var label = UIBuilder.CreateText(row, removed.name, 10, new Color(0.72f, 0.72f, 0.72f, 1f));
                if (label != null)
                {
                    label.alignment = TextAnchor.MiddleLeft;
                    UIBuilder.AddLayoutElement(label.gameObject, null, 18, null, 18, 1, 0);
                    UIBuilder.AddEllipsis(label);
                }
                var captured = removed;
                UIBuilder.CreateButton(row, "Restore", () => RestoreRemovedObject(captured.id), 60, 18);
            }
        }

        private void ScanSceneObjects()
        {
            if (_scanning) return;

            var limited = _scanRadiusEnabled;
            var center = Vector3.zero;
            if (limited && (controller == null || !controller.TryGetEditorCameraPosition(out center)))
            {
                _scanStatusOverride = "Editor camera unavailable - radius scan not run.";
                UpdateScanStatusLabel(true);
                return;
            }

            _scanStatusOverride = null;
            StartCoroutine(ScanSceneObjectsCoroutine(limited, center, _scanRadius));
        }

        private bool IsWithinScanRange(GameObject go, bool isLod, Vector3 center, float sqrRadius)
        {
            if (isLod)
            {
                go.GetComponentsInChildren(true, _scanRendererBuffer);
                var any = false;
                var bounds = default(Bounds);
                for (int i = 0; i < _scanRendererBuffer.Count; i++)
                {
                    var r = _scanRendererBuffer[i];
                    if (r == null) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                _scanRendererBuffer.Clear();
                if (any)
                    return bounds.SqrDistance(center) <= sqrRadius;
            }
            else
            {
                var rend = go.GetComponent<Renderer>();
                if (rend != null)
                    return rend.bounds.SqrDistance(center) <= sqrRadius;
            }

            return (go.transform.position - center).sqrMagnitude <= sqrRadius;
        }

        private IEnumerator ScanSceneObjectsCoroutine(bool limited, Vector3 center, float radius)
        {
            _scanning = true;
            _sceneObjectCache.Clear();
            RefreshObjectsList();
            yield return null;

            var sqrRadius = radius * radius;
            var seen = new HashSet<int>();
            var stack = new Stack<Transform>();

            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null)
                        continue;
                    stack.Push(root.transform);
                }
            }

            int processed = 0;
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null)
                    continue;

                var go = t.gameObject;
                if (go == null)
                    continue;

                // Skip inactive objects — they're not visible and we skip their entire subtree
                if (!go.activeSelf)
                {
                    if (++processed % 300 == 0) yield return null;
                    continue;
                }

                if (!go.name.StartsWith("MLE_", StringComparison.Ordinal) && go.GetComponent<PreviewLootMarker>() == null && go.GetComponent<PreviewStaticObjectMarker>() == null)
                {
                    var lod = go.GetComponent<LODGroup>();
                    if (lod != null)
                    {
                        // Include LOD objects only if they have a mesh in their children. Range is tested before
                        // the mesh search and before the object is stored, so out-of-range objects cost nothing later.
                        if ((!limited || IsWithinScanRange(go, true, center, sqrRadius)) && HasMeshInChildren(go) && seen.Add(go.GetInstanceID()))
                            _sceneObjectCache.Add(go);
                        // Never push children — sub-meshes belong to this LOD object
                    }
                    else
                    {
                        for (int i = 0; i < t.childCount; i++)
                        {
                            var child = t.GetChild(i);
                            if (child != null)
                                stack.Push(child);
                        }

                        // Include any object that has an actual mesh (MeshFilter or SkinnedMeshRenderer), regardless of enabled state
                        if (HasMesh(go) && (!limited || IsWithinScanRange(go, false, center, sqrRadius)) && seen.Add(go.GetInstanceID()))
                            _sceneObjectCache.Add(go);
                    }
                }

                if (++processed % 300 == 0)
                    yield return null;
            }

            // Instance ID breaks name ties so duplicate names keep a stable order between scans.
            _sceneObjectCache.Sort((a, b) =>
            {
                var c = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });

            _hasScanned = true;
            _scanSnapLimited = limited;
            _scanSnapCenter = center;
            _scanSnapRadius = radius;
            _objectsPage = 0;
            _scanning = false;
            RefreshObjectsList();
            UpdateScanStatusLabel(true);
            renderer?.SetSceneObjects(_sceneObjectCache, _selectedSceneGO);
        }

        private bool HasMesh(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return true;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
                return true;
            return false;
        }

        private bool HasMeshInChildren(GameObject go)
        {
            if (go == null)
                return false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null)
                    continue;
                return true;
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null)
                    continue;
                return true;
            }
            return false;
        }

        private void BuildDeleteConfirm()
        {
            _deleteConfirmPanel = UIBuilder.CreatePanel("DeleteConfirm", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.95f));
            _deleteConfirmPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _deleteConfirmPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _deleteConfirmPanel.pivot = new Vector2(0.5f, 0.5f);
            _deleteConfirmPanel.sizeDelta = new Vector2(340, 120);
            UIBuilder.AddVerticalLayout(_deleteConfirmPanel, 12, 8, true, true);

            _deleteConfirmText = UIBuilder.CreateText(_deleteConfirmPanel, "Delete selected marker(s)?", 13, Color.white, FontStyle.Bold);

            var row = UIBuilder.CreatePanel("ConfirmRow", _deleteConfirmPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 8, 8, false, false);
            UIBuilder.AddLayoutElement(row, null, 32, null, 32, null, null);

            UIBuilder.CreateButton(row, "Confirm", () => ConfirmDelete(), 100, 28);
            UIBuilder.CreateButton(row, "Cancel", () => CancelDelete(), 100, 28);

            _deleteConfirmPanel.gameObject.SetActive(false);
        }

        private void EnsureDeleteConfirm()
        {
            if (_deleteConfirmPanel != null || _canvas == null)
                return;
            try
            {
                BuildDeleteConfirm();
                Plugin.Log.LogInfo("CustomEditorUI rebuilt DeleteConfirm on demand");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"CustomEditorUI failed to rebuild DeleteConfirm: {ex}");
            }
        }

        private void BuildExportDialog()
        {
            _exportDialogPanel = UIBuilder.CreatePanel("ExportDialog", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.95f));
            _exportDialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _exportDialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _exportDialogPanel.pivot = new Vector2(0.5f, 0.5f);
            _exportDialogPanel.sizeDelta = new Vector2(340, 140);
            UIBuilder.AddVerticalLayout(_exportDialogPanel, 12, 8, true, true);

            UIBuilder.CreateText(_exportDialogPanel, "Export As", 13, Color.white, FontStyle.Bold);
            UIBuilder.CreateText(_exportDialogPanel, "Enter pack name or pick an existing export:", 11, new Color(0.7f, 0.7f, 0.7f, 1f));
            var nameRow = UIBuilder.CreatePanel("ExportNameRow", _exportDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(nameRow, 4, 2, false, false);
            UIBuilder.AddLayoutElement(nameRow, null, 24, null, 24, null, 0);
            _exportPackInput = UIBuilder.CreateInputField(nameRow, "Pack name", _packName, null, 200, 22);
            UIBuilder.CreateButton(nameRow, "Existing \u25BC", (() => { }), 90, 22, 10).gameObject.AddComponent<EventTrigger>().triggers.Add(BuildExistingPackTrigger());

            var row = UIBuilder.CreatePanel("ExportDialogRow", _exportDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 8, 8, false, false);
            UIBuilder.AddLayoutElement(row, null, 32, null, 32, null, null);

            UIBuilder.CreateButton(row, "Export", () => ConfirmExport(), 100, 28);
            UIBuilder.CreateButton(row, "Cancel", () => CancelExport(), 100, 28);

            _exportDialogPanel.gameObject.SetActive(false);
        }

        // Lists the .json packs already in the exports folder; picking one fills the name box (export then merges into it).
        private EventTrigger.Entry BuildExistingPackTrigger()
        {
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener((data) =>
            {
                var ped = data as PointerEventData;
                if (ped == null) return;
                var items = new List<(string, Action)>();
                try
                {
                    var dir = Plugin.ServerModExportsDirectory;
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    {
                        foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
                        {
                            var name = System.IO.Path.GetFileNameWithoutExtension(file);
                            items.Add((name, () => { if (_exportPackInput != null) _exportPackInput.text = name; }));
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"Could not list exports folder: {ex.Message}");
                }
                items.Sort((a, b) => string.Compare(a.Item1, b.Item1, System.StringComparison.OrdinalIgnoreCase));
                if (items.Count == 0)
                    items.Add(("(no exports found)", () => { }));
                ShowContextMenu(ped.position, items);
            });
            return entry;
        }

        private void DirectExport()
        {
            CloseAllMenus();
            if (!string.IsNullOrEmpty(_packName))
                controller.ExportPack(_packName);
        }

        private void ShowExportDialog()
        {
            CloseAllMenus();
            if (_exportDialogPanel == null || _exportPackInput == null) return;
            _exportPackInput.text = _packName;
            _exportDialogPanel.gameObject.SetActive(true);
        }

        private void ConfirmExport()
        {
            if (_exportDialogPanel == null || _exportPackInput == null) return;
            _packName = _exportPackInput.text.Trim();
            if (!string.IsNullOrEmpty(_packName))
                controller.ExportPack(_packName);
            _exportDialogPanel.gameObject.SetActive(false);
        }

        private void CancelExport()
        {
            if (_exportDialogPanel != null)
                _exportDialogPanel.gameObject.SetActive(false);
        }

        private void BuildRenderDistanceDialog()
        {
            _renderDistanceDialogPanel = UIBuilder.CreatePanel("RenderDistanceDialog", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.95f));
            _renderDistanceDialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _renderDistanceDialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _renderDistanceDialogPanel.pivot = new Vector2(0.5f, 0.5f);
            _renderDistanceDialogPanel.sizeDelta = new Vector2(340, 260);
            UIBuilder.AddVerticalLayout(_renderDistanceDialogPanel, 12, 8, true, true);

            UIBuilder.CreateText(_renderDistanceDialogPanel, "Vanilla Render Distance", 13, Color.white, FontStyle.Bold);
            UIBuilder.CreateWrappedText(_renderDistanceDialogPanel, "Max distance to render vanilla gizmos (0 = unlimited):", 11, new Color(0.7f, 0.7f, 0.7f, 1f));
            _renderDistanceInput = UIBuilder.CreateInputField(_renderDistanceDialogPanel, "meters", (Plugin.VanillaRenderDistance?.Value ?? 50f).ToString("F0", CultureInfo.InvariantCulture), null, 240, 22);

            UIBuilder.CreateText(_renderDistanceDialogPanel, "Object Render Distance", 13, Color.white, FontStyle.Bold);
            UIBuilder.CreateWrappedText(_renderDistanceDialogPanel, "Max distance to render scene object picker outlines (0 = unlimited):", 11, new Color(0.7f, 0.7f, 0.7f, 1f));
            _objectRenderDistanceInput = UIBuilder.CreateInputField(_renderDistanceDialogPanel, "meters", (Plugin.ObjectRenderDistance?.Value ?? 50f).ToString("F0", CultureInfo.InvariantCulture), null, 240, 22);

            var row = UIBuilder.CreatePanel("RenderDistanceDialogRow", _renderDistanceDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 8, 8, false, false);
            UIBuilder.AddLayoutElement(row, null, 32, null, 32, null, null);

            UIBuilder.CreateButton(row, "Apply", () => ConfirmRenderDistance(), 100, 28);
            UIBuilder.CreateButton(row, "Cancel", () => CancelRenderDistance(), 100, 28);

            _renderDistanceDialogPanel.gameObject.SetActive(false);
        }

        private void ShowRenderDistanceDialog()
        {
            CloseAllMenus();
            if (_renderDistanceDialogPanel == null || _renderDistanceInput == null || _objectRenderDistanceInput == null) return;
            _renderDistanceInput.text = (Plugin.VanillaRenderDistance?.Value ?? 50f).ToString("F0", CultureInfo.InvariantCulture);
            _objectRenderDistanceInput.text = (Plugin.ObjectRenderDistance?.Value ?? 50f).ToString("F0", CultureInfo.InvariantCulture);
            _renderDistanceDialogPanel.gameObject.SetActive(true);
        }

        private void ConfirmRenderDistance()
        {
            if (_renderDistanceDialogPanel == null || _renderDistanceInput == null || _objectRenderDistanceInput == null) return;
            if (float.TryParse(_renderDistanceInput.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                value = Mathf.Clamp(value, 0f, 500f);
                if (Plugin.VanillaRenderDistance != null)
                    Plugin.VanillaRenderDistance.Value = value;
                if (renderer != null)
                {
                    renderer.VanillaRenderDistance = value;
                    renderer.Rebuild();
                }
            }
            if (float.TryParse(_objectRenderDistanceInput.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var objectValue))
            {
                objectValue = Mathf.Clamp(objectValue, 0f, 500f);
                if (Plugin.ObjectRenderDistance != null)
                    Plugin.ObjectRenderDistance.Value = objectValue;
                if (renderer != null)
                {
                    renderer.ObjectRenderDistance = objectValue;
                    renderer.Rebuild();
                }
            }
            _renderDistanceDialogPanel.gameObject.SetActive(false);
        }

        private void CancelRenderDistance()
        {
            if (_renderDistanceDialogPanel != null)
                _renderDistanceDialogPanel.gameObject.SetActive(false);
        }

        private void BuildContextMenu()
        {
            _contextMenuPanel = UIBuilder.CreatePanel("ContextMenu", _canvas.transform, new Color(0.12f, 0.12f, 0.12f, 0.98f));
            _contextMenuPanel.anchorMin = new Vector2(0, 1);
            _contextMenuPanel.anchorMax = new Vector2(0, 1);
            _contextMenuPanel.pivot = new Vector2(0, 1);
            _contextMenuPanel.sizeDelta = new Vector2(100, 0);
            _contextMenuPanel.transform.SetAsLastSibling();
            UIBuilder.AddVerticalLayout(_contextMenuPanel, 2, 1, true, true);
            UIBuilder.AddContentSizeFitter(_contextMenuPanel, ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);
            _contextMenuPanel.gameObject.SetActive(false);
        }

        private void ShowContextMenu(Vector2 screenPos, List<(string label, Action action)> items)
        {
            if (_contextMenuPanel == null) return;
            HideContextMenu();

            var canvasRect = _canvas.GetComponent<RectTransform>();
            Vector2 localPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out localPos);
            var anchorOffset = new Vector2(canvasRect.rect.width / 2f, -canvasRect.rect.height / 2f);
            _contextMenuPanel.anchoredPosition = localPos + anchorOffset;
            _contextMenuPanel.gameObject.SetActive(true);
            _contextMenuPanel.transform.SetAsLastSibling();

            foreach (var item in items)
            {
                var btn = UIBuilder.CreateButton(_contextMenuPanel, item.label, () =>
                {
                    item.action?.Invoke();
                    HideContextMenu();
                }, 96, 18, 10);
                var rt = btn.GetComponent<RectTransform>();
                var lbl = rt.GetComponentInChildren<Text>();
                if (lbl != null)
                    lbl.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void HideContextMenu()
        {
            if (_contextMenuPanel == null) return;
            _contextMenuPanel.gameObject.SetActive(false);
            ClearChildren(_contextMenuPanel);
        }

        private void RefreshAll()
        {
            if (manager == null || controller == null)
                return;
            try
            {
                RefreshTitle();
                RefreshHierarchy();
                RefreshInspector();
                RefreshPrefabs();
                RefreshGroups();
                UpdateGizmoButtons();
                UpdateEditorModeButton();
                _lastMarkerCount = manager.GetAllMarkers().Count();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"CustomEditorUI RefreshAll failed: {ex}");
                LogOutput($"UI refresh error: {ex.Message}");
            }
        }

        private void RefreshTitle()
        {
            var map = manager.Data?.map ?? "none";
            var count = manager.GetAllMarkers().Count();
            _titleText.text = string.Format(Locale.Get("Map Loot Editor Lite - {0} ({1} markers)"), map, count);
        }

        private abstract class HierarchyEntry { }
        private sealed class GroupHeaderEntry : HierarchyEntry
        {
            public readonly string Key;
            public readonly int Count;
            public readonly bool Collapsed;
            public GroupHeaderEntry(string key, int count, bool collapsed)
            {
                Key = key;
                Count = count;
                Collapsed = collapsed;
            }
        }
        private sealed class MarkerEntry : HierarchyEntry
        {
            public readonly MarkerBase Marker;
            public readonly bool Indent;
            public MarkerEntry(MarkerBase marker, bool indent)
            {
                Marker = marker;
                Indent = indent;
            }
        }
        private sealed class ChildEntry : HierarchyEntry
        {
            public readonly MarkerBase Parent;
            public readonly string Path;
            public readonly string DisplayName;
            public readonly int Depth;
            public readonly bool CanExpand;
            public readonly bool Expanded;
            public readonly bool IsInteractable;
            public readonly bool IsDoor;
            public readonly bool IsContainer;
            public ChildEntry(MarkerBase parent, string path, string displayName, int depth, bool canExpand, bool expanded, bool isInteractable, bool isDoor, bool isContainer)
            {
                Parent = parent;
                Path = path;
                DisplayName = displayName;
                Depth = depth;
                CanExpand = canExpand;
                Expanded = expanded;
                IsInteractable = isInteractable;
                IsDoor = isDoor;
                IsContainer = isContainer;
            }
        }

        private void RefreshHierarchy()
        {
            if (_hierarchyContent == null || manager == null)
                return;
            ClearChildren(_hierarchyContent);

            var allMarkers = manager.GetActiveMarkers().Where(MatchesSearch).ToList();

            var entries = new List<HierarchyEntry>();

            // Grouped sections (collapsible)
            var grouped = allMarkers
                .Where(m => !string.IsNullOrWhiteSpace(m.group))
                .GroupBy(m => m.group)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var grp in grouped)
            {
                var key = grp.Key;
                bool collapsed = _collapsedGroups.Contains(key);
                entries.Add(new GroupHeaderEntry(key, grp.Count(), collapsed));
                if (!collapsed)
                {
                    foreach (var m in grp)
                        AppendMarkerAndChildren(m, indent: true, entries);
                }
            }

            // Ungrouped items
            foreach (var m in allMarkers.Where(m => string.IsNullOrWhiteSpace(m.group)))
                AppendMarkerAndChildren(m, indent: false, entries);

            const int pageSize = 100;
            int start = 0;
            int end = entries.Count;
            int pageCount = 1;

            if (entries.Count > pageSize)
            {
                var pageKey = (_vanillaHierarchyActive ? "vanilla" : "pack") + "|" + (_searchText ?? "");
                if (_hierarchyPageTarget != pageKey)
                {
                    _hierarchyPageTarget = pageKey;
                    _hierarchyPage = 0;
                }
                pageCount = (entries.Count + pageSize - 1) / pageSize;
                _hierarchyPage = Mathf.Clamp(_hierarchyPage, 0, pageCount - 1);
                start = _hierarchyPage * pageSize;
                end = Mathf.Min(start + pageSize, entries.Count);

                var pageRow = UIBuilder.CreatePanel("HierarchyPageRow", _hierarchyContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(pageRow, 4, 2, false, false);
                UIBuilder.AddLayoutElement(pageRow, null, 22, null, 22, null, 0);
                UIBuilder.CreateButton(pageRow, "<", () => { _hierarchyPage--; RequestHierarchyRefresh(); }, 30, 22, 10);
                UIBuilder.CreateText(pageRow, string.Format(Locale.Get("Page {0} / {1} ({2} rows)"), _hierarchyPage + 1, pageCount, entries.Count), 11, Color.white);
                UIBuilder.CreateButton(pageRow, ">", () => { _hierarchyPage++; RequestHierarchyRefresh(); }, 30, 22, 10);
            }

            for (int i = start; i < end; i++)
            {
                if (entries[i] is GroupHeaderEntry g)
                    BuildHierarchyGroupHeader(g);
                else if (entries[i] is MarkerEntry m)
                    BuildHierarchyRow(m.Marker, m.Indent);
                else if (entries[i] is ChildEntry c)
                    BuildHierarchyChildRow(c);
            }
        }

        private void AppendMarkerAndChildren(MarkerBase marker, bool indent, List<HierarchyEntry> entries)
        {
            entries.Add(new MarkerEntry(marker, indent));
            var sourceObj = marker as IHasSourceObject;
            if (sourceObj == null || string.IsNullOrEmpty(sourceObj.sourceObjectName))
                return;
            if (!_expandedChildNodes.Contains(marker.id))
                return;

            var source = previews?.FindSourceObject(sourceObj.sourceObjectName, sourceObj.sourceObjectPosition.ToVector3());
            if (source == null)
                return;

            var overrides = GetChildInteractables(marker);
            foreach (Transform child in source.transform)
            {
                if (child == null)
                    continue;
                AppendChildEntries(marker, child, child.name, 1, overrides, entries);
            }
        }

        private void AppendChildEntries(MarkerBase marker, Transform child, string childPath, int depth, List<ChildInteractableData> overrides, List<HierarchyEntry> entries)
        {
            bool deleted = overrides != null && overrides.Any(x => x.childPath == childPath && x.deleted);
            if (deleted)
                return;

            bool canExpand = child.childCount > 0;
            bool expanded = _expandedChildNodes.Contains(marker.id + ":" + childPath);
            bool isDoor = child.GetComponent<WorldInteractiveObject>() != null;
            bool isContainer = child.GetComponent<LootableContainer>() != null;
            bool isInteractable = isDoor || isContainer;
            entries.Add(new ChildEntry(marker, childPath, child.name, depth, canExpand, expanded, isInteractable, isDoor, isContainer));
            if (canExpand && expanded)
            {
                foreach (Transform sub in child)
                {
                    if (sub == null)
                        continue;
                    AppendChildEntries(marker, sub, childPath + "/" + sub.name, depth + 1, overrides, entries);
                }
            }
        }

        private void BuildHierarchyGroupHeader(GroupHeaderEntry entry)
        {
            var key = entry.Key;
            var grpHdr = UIBuilder.CreatePanel("GrpHdr", _hierarchyContent, new Color(0.18f, 0.2f, 0.28f, 0.9f));
            UIBuilder.AddHorizontalLayout(grpHdr, 3, 1, true, false);
            UIBuilder.AddLayoutElement(grpHdr, null, 18, null, 18, null, 0);
            var arrowLabel = UIBuilder.CreateLabel(grpHdr, entry.Collapsed ? "\u25ba" : "\u25bc", 10, 12, 14);
            var titleLabel = UIBuilder.CreateLabel(grpHdr, $"{key}  ({entry.Count})", 10, 0, 14);
            var capturedKey = key;
            var isGroupLocked = manager.IsGroupLocked(key);
            var hdrBtn = grpHdr.gameObject.AddComponent<Button>();
            hdrBtn.targetGraphic = grpHdr.GetComponent<Image>();
            var hdrBtnColors = hdrBtn.colors;
            hdrBtnColors.normalColor = new Color(0.18f, 0.2f, 0.28f, 0.9f);
            hdrBtnColors.highlightedColor = new Color(0.25f, 0.28f, 0.38f, 1f);
            hdrBtn.colors = hdrBtnColors;
            hdrBtn.onClick.AddListener(() =>
            {
                if (_collapsedGroups.Contains(capturedKey)) _collapsedGroups.Remove(capturedKey);
                else _collapsedGroups.Add(capturedKey);
                manager.SelectGroup(capturedKey);
                RequestHierarchyRefresh();
                RequestInspectorRefresh();
            });
            hdrBtn.interactable = !isGroupLocked;
            if (isGroupLocked)
            {
                arrowLabel.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                titleLabel.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            }

            var lockBtn = UIBuilder.CreateButton(grpHdr, isGroupLocked ? "L" : "", () =>
            {
                manager.SetGroupLocked(capturedKey, !manager.IsGroupLocked(capturedKey));
                RequestHierarchyRefresh();
                RequestInspectorRefresh();
            }, 14, 16, 10);
            lockBtn.GetComponent<Image>().color = isGroupLocked ? new Color(0.9f, 0.3f, 0.3f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
        }

        private void BuildHierarchyRow(MarkerBase marker, bool indent)
        {
            var row = UIBuilder.CreatePanel("MarkerRow", _hierarchyContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 1, true, false);
            UIBuilder.AddLayoutElement(row, null, 18, null, 18, null, 0);

            if (indent)
            {
                var pad = UIBuilder.CreatePanel("Indent", row, new Color(0, 0, 0, 0));
                pad.GetComponent<Image>().raycastTarget = false;
                UIBuilder.AddLayoutElement(pad, 10, 18, 10, 18, 0, 0);
            }

            var sourceObj = marker as IHasSourceObject;
            bool canExpand = sourceObj != null && !string.IsNullOrEmpty(sourceObj.sourceObjectName);
            bool expanded = canExpand && _expandedChildNodes.Contains(marker.id);

            if (canExpand)
            {
                var expandLabel = expanded ? "\u25bc" : "\u25ba";
                UIBuilder.CreateButton(row, expandLabel, () =>
                {
                    manager.SelectOnly(marker);
                    _selectedChildMarker = null;
                    _selectedChildPath = null;
                    manager.SelectedChild = null;
                    manager.SelectedChildPath = null;
                    if (_expandedChildNodes.Contains(marker.id)) _expandedChildNodes.Remove(marker.id);
                    else _expandedChildNodes.Add(marker.id);
                    RequestHierarchyRefresh();
                    RequestInspectorRefresh();
                }, 14, 16, 10);
            }
            else
            {
                var pad = UIBuilder.CreatePanel("Indent", row, new Color(0, 0, 0, 0));
                pad.GetComponent<Image>().raycastTarget = false;
                UIBuilder.AddLayoutElement(pad, 14, 18, 14, 18, 0, 0);
            }

            UIBuilder.CreateButton(row, "Go", () => controller.GoToMarker(marker), 24, 16, 10);

            bool selected = manager.IsSelected(marker) && _selectedChildMarker == null;
            bool locked = manager.IsLocked(marker);
            bool groupLocked = manager.IsGroupLocked(marker.group ?? "");
            var capturedMarker = marker;

            // Transparent clickable area — clicking selects the marker
            var selBtn = UIBuilder.CreateButton(row,
                $"{(marker.isVanilla ? "[V] " : "")}{marker.Kind} | {marker.name}",
                () =>
                {
                    if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                        manager.ToggleSelected(capturedMarker);
                    else
                        manager.SelectOnly(capturedMarker);
                    _selectedChildMarker = null;
                    _selectedChildPath = null;
                    manager.SelectedChild = null;
                    manager.SelectedChildPath = null;
                    RequestHierarchyRefresh();
                    RequestInspectorRefresh();
                },
                0, 16, 10);
            UIBuilder.AddLayoutElement(selBtn.gameObject, null, 16, null, 16, 1, 0);
            selBtn.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var bc = selBtn.colors;
            bc.normalColor = Color.white;
            bc.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
            bc.pressedColor = new Color(0.7f, 0.8f, 1f, 1f);
            selBtn.colors = bc;
            selBtn.interactable = !locked;
            var lockBtn = UIBuilder.CreateButton(row, locked ? "L" : "", () =>
            {
                manager.SetMarkerLocked(capturedMarker, !capturedMarker.isLocked);
                RequestHierarchyRefresh();
                RequestInspectorRefresh();
            }, 14, 16, 10);
            lockBtn.GetComponent<Image>().color = locked ? new Color(0.9f, 0.3f, 0.3f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            lockBtn.interactable = !groupLocked;
            var lblText = selBtn.GetComponentInChildren<Text>();
            if (lblText != null)
            {
                lblText.fontSize = 10;
                lblText.alignment = TextAnchor.MiddleLeft;
                lblText.color = locked ? new Color(0.45f, 0.45f, 0.45f, 1f) : (selected ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.72f, 0.72f, 0.72f, 1f));
            }

            row.GetComponent<Image>().color = selected
                ? new Color(0.2f, 0.3f, 0.45f, 0.6f)
                : new Color(0.13f, 0.13f, 0.13f, 0.3f);
        }

        private void BuildHierarchyChildRow(ChildEntry c)
        {
            var row = UIBuilder.CreatePanel("ChildRow", _hierarchyContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 1, true, false);
            UIBuilder.AddLayoutElement(row, null, 16, null, 16, null, 0);

            int indent = 10 + c.Depth * 10;
            var pad = UIBuilder.CreatePanel("Indent", row, new Color(0, 0, 0, 0));
            pad.GetComponent<Image>().raycastTarget = false;
            UIBuilder.AddLayoutElement(pad, indent, 16, indent, 16, 0, 0);

            if (c.CanExpand)
            {
                UIBuilder.CreateButton(row, c.Expanded ? "\u25bc" : "\u25ba", () =>
                {
                    var key = c.Parent.id + ":" + c.Path;
                    if (_expandedChildNodes.Contains(key)) _expandedChildNodes.Remove(key);
                    else _expandedChildNodes.Add(key);
                    RequestHierarchyRefresh();
                }, 14, 16, 10);
            }
            else
            {
                var spacer = UIBuilder.CreatePanel("Indent", row, new Color(0, 0, 0, 0));
                spacer.GetComponent<Image>().raycastTarget = false;
                UIBuilder.AddLayoutElement(spacer, 14, 16, 14, 16, 0, 0);
            }

            bool selected = _selectedChildMarker == c.Parent && _selectedChildPath == c.Path;
            var label = c.DisplayName + (c.IsDoor ? " (Door)" : c.IsContainer ? " (Container)" : "");

            var captured = c;
            var selBtn = UIBuilder.CreateButton(row, label, () =>
            {
                manager.SelectOnly(captured.Parent);
                _selectedChildMarker = captured.Parent;
                _selectedChildPath = captured.Path;
                _selectedChildIsDoor = captured.IsDoor;
                _selectedChildIsContainer = captured.IsContainer;
                previews?.SpawnPreviewForMarker(captured.Parent);
                var preview = previews?.GetPreviewInstance(captured.Parent);
                var childTransform = preview?.transform.Find(captured.Path);
                manager.SelectedChild = childTransform;
                manager.SelectedChildPath = captured.Path;
                RequestInspectorRefresh();
                RequestHierarchyRefresh();
            }, 0, 16, 10);
            UIBuilder.AddLayoutElement(selBtn.gameObject, null, 16, null, 16, 1, 0);
            selBtn.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var bc = selBtn.colors;
            bc.normalColor = Color.white;
            bc.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
            bc.pressedColor = new Color(0.7f, 0.8f, 1f, 1f);
            selBtn.colors = bc;
            var lblText = selBtn.GetComponentInChildren<Text>();
            if (lblText != null)
            {
                lblText.fontSize = 10;
                lblText.alignment = TextAnchor.MiddleLeft;
                lblText.color = selected ? new Color(0.9f, 0.9f, 0.9f, 1f) : new Color(0.72f, 0.72f, 0.72f, 1f);
            }

            row.GetComponent<Image>().color = selected
                ? new Color(0.2f, 0.3f, 0.45f, 0.6f)
                : new Color(0.13f, 0.13f, 0.13f, 0.3f);
        }

        private static readonly HashSet<string> FlowRowNames = new HashSet<string>
        {
            "GroupActions", "ShapeRow", "ZoneShapeRow", "NavMeshShapeRow", "PreviewRow", "VanillaActions1",
            "VanillaActions2", "SceneGOActions", "SourceRow1", "SourceRow2", "SourceRow3", "SourcePickRow"
        };

        // Button/shape rows are built as fixed-width horizontal rows; swap them for wrapping rows so they stay
        // inside a narrow Inspector instead of being clipped by the scroll mask.
        private static void ConvertButtonRowsToFlow(RectTransform content)
        {
            if (content == null)
                return;
            for (int i = 0; i < content.childCount; i++)
            {
                var row = content.GetChild(i) as RectTransform;
                if (row == null || !FlowRowNames.Contains(row.name))
                    continue;
                var hlg = row.GetComponent<HorizontalLayoutGroup>();
                if (hlg == null || hlg.childControlWidth || hlg.childControlHeight)
                    continue;

                var padding = hlg.padding;
                var spacing = hlg.spacing;
                UnityEngine.Object.DestroyImmediate(hlg);
                foreach (var le in row.GetComponents<LayoutElement>())
                    UnityEngine.Object.DestroyImmediate(le);

                var flow = row.gameObject.AddComponent<FlowLayoutGroup>();
                flow.padding = padding;
                flow.spacing = spacing;
                flow.childAlignment = TextAnchor.UpperLeft;
            }
        }

        private void RefreshInspector()
        {
            RefreshInspectorContent();
            ConvertButtonRowsToFlow(_inspectorContent);
        }

        private void RefreshInspectorContent()
        {
            if (_inspectorContent == null || manager == null)
                return;
            ClearChildren(_inspectorContent);
            _lastSelected = manager.Selected;
            _lastSelectedCount = manager.SelectedIds.Count;

            if (_selectedChildMarker != null && manager.Selected != _selectedChildMarker)
            {
                _selectedChildMarker = null;
                _selectedChildPath = null;
                manager.SelectedChild = null;
                manager.SelectedChildPath = null;
            }

            var selected = manager.Selected;
            if (selected == null)
            {
                if (_selectedSceneGO != null)
                {
                    UIBuilder.CreateText(_inspectorContent, "Scene Object", 12, Color.white, FontStyle.Bold);

                    var detailsPanel = UIBuilder.CreatePanel("SceneGODetails", _inspectorContent, new Color(0, 0, 0, 0));
                    detailsPanel.GetComponent<Image>().raycastTarget = false;
                    UIBuilder.AddVerticalLayout(detailsPanel, 2, 6, true, false);
                    UIBuilder.AddLayoutElement(detailsPanel.gameObject, null, null, null, null, null, 1);

                    var details = FormatSceneGODetails(_selectedSceneGO);
                    var lines = details.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var t = UIBuilder.CreateText(detailsPanel, line, 10, new Color(0.75f, 0.75f, 0.75f, 1f));
                        if (t != null)
                        {
                            t.horizontalOverflow = HorizontalWrapMode.Wrap;
                            t.verticalOverflow = VerticalWrapMode.Overflow;
                            t.lineSpacing = 1.2f;
                            var rt = t.GetComponent<RectTransform>();
                            if (rt != null)
                            {
                                rt.anchorMin = new Vector2(0f, 1f);
                                rt.anchorMax = new Vector2(1f, 1f);
                                rt.pivot = new Vector2(0.5f, 1f);
                                rt.sizeDelta = Vector2.zero;
                            }
                            var csf = t.gameObject.AddComponent<ContentSizeFitter>();
                            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                        }
                    }

                    var actionRow = UIBuilder.CreatePanel("SceneGOActions", _inspectorContent, new Color(0, 0, 0, 0));
                    actionRow.GetComponent<Image>().raycastTarget = false;
                    UIBuilder.AddHorizontalLayout(actionRow, 4, 2, false, false);
                    UIBuilder.AddLayoutElement(actionRow.gameObject, null, 26, null, 26, null, 0);
                    UIBuilder.CreateButton(actionRow, "Go To", () => controller?.GoToSceneObject(_selectedSceneGO), 50, 22);
                    UIBuilder.CreateButton(actionRow, "Place Here", () => controller?.PlaceFromSceneGO(_selectedSceneGO), 80, 22);
                    UIBuilder.CreateButton(actionRow, "Add Component", () => ShowAddComponentDialog(_selectedSceneGO, null), 90, 22);
                    UIBuilder.CreateButton(actionRow, "Remove", () => RemoveSelectedSceneGO(), 60, 22);
                }
                else
                {
                    UIBuilder.CreateText(_inspectorContent, "No marker selected.", 12, new Color(0.6f, 0.6f, 0.6f, 1f));
                }
                return;
            }

            var selectedCount = manager.SelectedIds.Count;
            UIBuilder.CreateWrappedText(_inspectorContent, selectedCount > 1 ? $"Selected {selectedCount} markers (primary: {selected.name})" : $"Selected: {selected.Kind} - {selected.name}", 12, Color.white, FontStyle.Bold);

            if (selected.isVanilla)
            {
                BuildVanillaInspector(selected);
                return;
            }

            if (_selectedChildMarker != null && selected == _selectedChildMarker)
            {
                BuildChildInspector(_selectedChildMarker, _selectedChildPath, _selectedChildIsDoor, _selectedChildIsContainer);
                return;
            }

            BuildStringField(_inspectorContent, "Name", selected.name, (v) => { selected.name = v; manager.IsDirty = true; RequestHierarchyRefresh(); });
            BuildStringField(_inspectorContent, "Group", selected.group ?? "", (v) => { selected.group = v; manager.IsDirty = true; RequestHierarchyRefresh(); });

            if (selectedCount > 1)
            {
                var row = UIBuilder.CreatePanel("GroupActions", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
                UIBuilder.CreateButton(row, "Assign Group to Selection", () => manager.SetGroupOnSelection(selected.group), 150, 22);
                UIBuilder.CreateButton(row, "Clear Group from Selection", () => manager.SetGroupOnSelection(""), 150, 22);
            }

            BuildVector3Field(_inspectorContent, "Position", selected.position.ToVector3(), (v) => { selected.position = TransformData.FromVector3(v); manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Rotation", selected.rotation.ToVector3(), (v) => { selected.rotation = TransformData.FromVector3(v); manager.IsDirty = true; });

            if (selectedCount == 1)
            {
                previews?.SpawnPreviewForMarker(selected);
                var preview = previews?.GetPreviewInstance(selected);
                if (preview != null)
                    UIBuilder.CreateButton(_inspectorContent, "Add Component", () => ShowAddComponentDialog(preview, selected.addedComponents), 110, 24);
            }

            switch (selected)
            {
                case LooseLootSpawn spawn:
                    BuildLooseLootSpawn(spawn);
                    break;
                case LootZone zone:
                    BuildLootZone(zone);
                    break;
                case StaticObject obj:
                    BuildStaticObject(obj);
                    break;
                case WTTQuestZone zone:
                    BuildWTTQuestZone(zone);
                    break;
                case WTTStaticObject obj:
                    BuildWTTStaticObject(obj);
                    break;
                case InteractiveObject obj:
                    BuildInteractiveObject(obj);
                    break;
                case ExtractZone zone:
                    BuildExtractZone(zone);
                    break;
                case BotSpawnPoint point:
                    BuildBotSpawnPoint(point);
                    break;
                case BotSpawnZone zone:
                    BuildBotSpawnZone(zone);
                    break;
                case PmcSpawnZone zone:
                    BuildPmcSpawnZone(zone);
                    break;
                case LightZone zone:
                    BuildLightZone(zone);
                    break;
                case TriggerZone zone:
                    BuildTriggerZone(zone);
                    break;
                case BotActivityZone baz:
                    BuildBotActivityZone(baz);
                    break;
                case OcclusionRepairVolume orv:
                    BuildOcclusionRepairVolume(orv);
                    break;
                case CutVolume cv:
                    BuildCutVolume(cv);
                    break;
                case Blocker blocker:
                    BuildBlocker(blocker);
                    break;
                case NavMeshArea nma:
                    BuildNavMeshArea(nma);
                    break;
            }

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Random", () =>
            {
                if (selected is LooseLootSpawn s && s.items != null && s.items.Count > 0)
                    previews.SpawnAtMarker(s, UnityEngine.Random.Range(0, s.items.Count));
                else if (selected is LootZone z)
                    previews.SpawnAtZoneCenter(z);
            }, 110, 22);
            if (selected is LootZone lz)
                UIBuilder.CreateButton(previewRow, "Preview Random In Zone", () => previews.SpawnAllInZone(lz), 140, 22);
        }

        private void BuildVanillaInspector(MarkerBase selected)
        {
            UIBuilder.CreateText(_inspectorContent, "Vanilla reference (read-only)", 12, new Color(0.85f, 0.7f, 0.4f, 1f), FontStyle.Bold);

            BuildReadOnlyLabel(_inspectorContent, "Name", selected.name);
            BuildReadOnlyLabel(_inspectorContent, "Group", selected.group ?? "");
            BuildReadOnlyLabel(_inspectorContent, "Kind", selected.Kind.ToString());
            BuildReadOnlyLabel(_inspectorContent, "Position", $"{selected.position.x:F3}, {selected.position.y:F3}, {selected.position.z:F3}");
            BuildReadOnlyLabel(_inspectorContent, "Rotation", $"{selected.rotation.x:F3}, {selected.rotation.y:F3}, {selected.rotation.z:F3}");

            switch (selected)
            {
                case LooseLootSpawn spawn:
                    BuildReadOnlyLabel(_inspectorContent, "Spawn Chance", $"{spawn.spawnChance:F2}%");
                    BuildReadOnlyLabel(_inspectorContent, "Forced", spawn.forced.ToString());
                    BuildReadOnlyLabel(_inspectorContent, "Use Gravity", spawn.useGravity.ToString());
                    BuildVanillaItemList(spawn.items);
                    break;
                case InteractiveObject obj:
                    BuildReadOnlyLabel(_inspectorContent, "Container Template", FormatContainerTemplate(obj.containerTemplate));
                    BuildReadOnlyLabel(_inspectorContent, "Loot Mode", obj.lootMode.ToString());
                    BuildReadOnlyLabel(_inspectorContent, "Min Items (0 = vanilla)", obj.itemCountMin.ToString());
                    BuildReadOnlyLabel(_inspectorContent, "Max Items (0 = vanilla)", obj.itemCountMax.ToString());
                    BuildReadOnlyLabel(_inspectorContent, "Spawn Chance", $"{obj.spawnChance:F2}%");
                    BuildVanillaItemList(obj.items);
                    break;
                case LootZone zone:
                    BuildReadOnlyLabel(_inspectorContent, "Shape", zone.shape.ToString());
                    BuildReadOnlyLabel(_inspectorContent, "Radius", zone.radius.ToString("F2"));
                    BuildVanillaItemList(zone.items);
                    break;
                case StaticObject obj:
                    BuildReadOnlyLabel(_inspectorContent, "Prefab Path", obj.prefabPath ?? "");
                    break;
                case WTTQuestZone zone:
                    BuildReadOnlyLabel(_inspectorContent, "Zone Id", zone.zoneId ?? "");
                    BuildReadOnlyLabel(_inspectorContent, "Zone Type", zone.zoneType ?? "");
                    break;
                case WTTStaticObject obj:
                    BuildReadOnlyLabel(_inspectorContent, "Spawn Type", obj.spawnType ?? "");
                    BuildReadOnlyLabel(_inspectorContent, "Bundle Name", obj.bundleName ?? "");
                    BuildReadOnlyLabel(_inspectorContent, "Prefab Name", obj.prefabName ?? "");
                    break;
                case ExtractZone zone:
                    BuildReadOnlyLabel(_inspectorContent, "Shape", zone.shape.ToString());
                    BuildReadOnlyLabel(_inspectorContent, "Exit Name", zone.exitName ?? "");
                    BuildReadOnlyLabel(_inspectorContent, "Exfil Time", zone.exfiltrationTime.ToString("F1"));
                    BuildReadOnlyLabel(_inspectorContent, "Spawn Chance", $"{zone.spawnChance:F2}%");
                    break;
            }

            var actionRow = UIBuilder.CreatePanel("VanillaActions1", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(actionRow, 4, 2, false, false);
            UIBuilder.AddLayoutElement(actionRow, null, 26, null, 26, null, 0);

            var sourceObj = selected as IHasSourceObject;
            if (sourceObj != null && !string.IsNullOrEmpty(sourceObj.sourceObjectName))
            {
                UIBuilder.CreateButton(actionRow, "Go To", () =>
                {
                    var src = previews?.FindSourceObject(sourceObj.sourceObjectName, sourceObj.sourceObjectPosition.ToVector3());
                    if (src != null)
                        controller?.GoToSceneObject(src);
                }, 50, 22);
            }

            var actionRow2 = UIBuilder.CreatePanel("VanillaActions2", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(actionRow2, 4, 2, false, false);
            UIBuilder.AddLayoutElement(actionRow2, null, 26, null, 26, null, 0);
            UIBuilder.CreateButton(actionRow2, "Write Mode", () =>
            {
                var clone = manager.CloneVanillaToPack(selected);
                if (clone != null)
                {
                    manager.Selected = clone;
                    RequestInspectorRefresh();
                    RequestHierarchyRefresh();
                    renderer.Rebuild();
                    previews?.SpawnPreviewForMarker(clone);
                }
            }, 80, 22);
            UIBuilder.CreateButton(actionRow2, "Remove", () =>
            {
                manager.RemoveVanillaMarker(selected);
                manager.Selected = null;
                RequestInspectorRefresh();
                RequestHierarchyRefresh();
                renderer.Rebuild();
            }, 60, 22);
        }

        private void BuildVanillaItemList(List<LootItem> items)
        {
            if (items == null || items.Count == 0)
            {
                BuildReadOnlyLabel(_inspectorContent, "Items", "0");
                return;
            }

            BuildReadOnlyLabel(_inspectorContent, "Items", items.Count.ToString());

            const int LabelThreshold = 100;
            if (items.Count > LabelThreshold)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var label = ItemNameResolver.GetNameOrId(item.template ?? "");
                    sb.AppendLine($"  {i + 1}. {label}  ({item.chance:F2}%)");
                }

                var text = UIBuilder.CreateText(_inspectorContent, sb.ToString(), 11, new Color(0.72f, 0.72f, 0.72f, 1f));
                text.alignment = TextAnchor.UpperLeft;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                var le = text.GetComponent<LayoutElement>();
                if (le != null)
                    le.preferredHeight = -1f;
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var label = ItemNameResolver.GetNameOrId(item.template ?? "");
                BuildReadOnlyLabel(_inspectorContent, $"  Item {i + 1}", label);
            }
        }

        private static string FormatContainerTemplate(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return "";
            var match = LootContainerTemplates.FirstOrDefault(t => t.id.Equals(templateId, StringComparison.OrdinalIgnoreCase));
            if (match.name != null)
                return $"{match.name} ({templateId})";
            return ItemNameResolver.GetNameOrId(templateId);
        }

        private void BuildReadOnlyLabel(RectTransform parent, string label, string value)
        {
            var row = UIBuilder.CreatePanel("ReadOnlyLabel", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, true, true);
            UIBuilder.AddLayoutElement(row, null, 22, null, null, null, 0);
            var text = UIBuilder.CreateText(row, $"{Locale.Get(label)}: {value ?? ""}", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
            text.alignment = TextAnchor.MiddleLeft;
            // Long values wrap onto extra lines (row grows) instead of drawing past the panel.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.GetComponent<LayoutElement>().preferredHeight = -1f;
            text.GetComponent<LayoutElement>().minHeight = 18f;
        }

        private void BuildLooseLootSpawn(LooseLootSpawn spawn)
        {
            BuildFloatField(_inspectorContent, "Spawn Chance", spawn.spawnChance, (v) => { spawn.spawnChance = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Respawnable", spawn.respawnable, (v) => { spawn.respawnable = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Use Gravity", spawn.useGravity, (v) => { spawn.useGravity = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Quest only", spawn.questOnly, (v) => { spawn.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", spawn.questCompleted, (v) => { spawn.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (spawn.questOnly || spawn.questCompleted)
            {
                BuildStringField(_inspectorContent, "Quest ID", spawn.questId ?? "", (v) => { spawn.questId = v; manager.IsDirty = true; });
            }
            BuildLootGroupControls(spawn);
            BuildItemsList(spawn.items, false, (i) => previews.SpawnAtMarker(spawn, i));
        }

        private void BuildLootGroupControls(LooseLootSpawn spawn)
        {
            BuildToggleField(_inspectorContent, "Part of Loot Group", spawn.lootGroupEnabled, (v) =>
            {
                spawn.lootGroupEnabled = v;
                if (v && spawn.lootGroupWeight <= 0f)
                    spawn.lootGroupWeight = 1f;
                manager.IsDirty = true;
                RefreshInspector();
            });

            if (!spawn.lootGroupEnabled)
                return;

            Text idWarning = null;
            Action updateIdWarning = () =>
            {
                if (idWarning != null)
                    idWarning.gameObject.SetActive(string.IsNullOrWhiteSpace(spawn.lootGroupId));
            };

            BuildStringField(_inspectorContent, "Loot Group", spawn.lootGroupId ?? "", (v) => { spawn.lootGroupId = v; manager.IsDirty = true; updateIdWarning(); });
            idWarning = UIBuilder.CreateWrappedText(_inspectorContent, "Warning: Part of Loot Group is on but Loot Group is empty (spawn is treated as normal loot).", 11);
            idWarning.color = new Color(1f, 0.75f, 0.2f, 1f);
            updateIdWarning();

            BuildFloatField(_inspectorContent, "Selection Weight", spawn.lootGroupWeight, (v) =>
            {
                if (v > 0f)
                {
                    spawn.lootGroupWeight = v;
                    manager.IsDirty = true;
                }
            });

            var groupCounts = manager.Data.lootSpawns
                .Where(s => s.lootGroupEnabled && !string.IsNullOrWhiteSpace(s.lootGroupId))
                .GroupBy(s => s.lootGroupId.Trim())
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            if (!string.IsNullOrWhiteSpace(spawn.lootGroupId))
            {
                var key = spawn.lootGroupId.Trim();
                var members = groupCounts.FirstOrDefault(g => g.Key == key)?.Count() ?? 0;
                BuildReadOnlyLabel(_inspectorContent, "Members in this group", members.ToString());
                if (members == 1)
                {
                    var single = UIBuilder.CreateWrappedText(_inspectorContent, "Warning: only one member, it will always spawn.", 11);
                    single.color = new Color(1f, 0.75f, 0.2f, 1f);
                }
            }

            var note = UIBuilder.CreateWrappedText(_inspectorContent, "Exactly one member per group spawns each raid. Spawn Chance and Forced are ignored; only the first eligible item spawns.", 10);
            note.color = new Color(0.72f, 0.72f, 0.72f, 1f);

            if (groupCounts.Count > 0)
            {
                BuildReadOnlyLabel(_inspectorContent, "Existing loot groups", "");
                foreach (var g in groupCounts)
                {
                    var id = g.Key;
                    UIBuilder.CreateButton(_inspectorContent, $"{id} ({g.Count()})", () =>
                    {
                        spawn.lootGroupId = id;
                        manager.IsDirty = true;
                        RefreshInspector();
                    }, 200, 20, 10);
                }
            }
        }

        private string _lootZoneBakeStatus;

        private void BuildLootZone(LootZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { zone.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)zone.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildFloatField(_inspectorContent, "Radius", zone.radius, (v) => { zone.radius = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", zone.scale.ToVector3(), (v) => { zone.scale = TransformData.FromVector3(v); manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Use Gravity", zone.useGravity, (v) => { zone.useGravity = v; manager.IsDirty = true; });

            BuildFloatField(_inspectorContent, "Bake points", zone.bakeCount > 0 ? zone.bakeCount : LootZoneSurfaceBaker.DefaultBakeCount, (v) => { zone.bakeCount = Mathf.Max(1, Mathf.RoundToInt(v)); manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Min spacing (m)", zone.minSpacing > 0f ? zone.minSpacing : LootZoneSurfaceBaker.DefaultMinSpacing, (v) => { zone.minSpacing = Mathf.Max(0.05f, v); manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Max slope (deg)", zone.maxSlope > 0f ? zone.maxSlope : LootZoneSurfaceBaker.DefaultMaxSlope, (v) => { zone.maxSlope = Mathf.Clamp(v, 5f, 80f); manager.IsDirty = true; });
            var bakeRow = UIBuilder.CreatePanel("BakeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(bakeRow, 4, 2, false, false);
            UIBuilder.AddLayoutElement(bakeRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(bakeRow, "Bake Surface Points", () =>
            {
                _lootZoneBakeStatus = LootZoneSurfaceBaker.Bake(zone);
                manager.IsDirty = true;
                RefreshInspector();
            }, 130, 22);
            UIBuilder.CreateButton(bakeRow, "Clear", () =>
            {
                zone.surfacePoints = new List<TransformData>();
                LootZoneSurfaceBaker.ClearVisualization();
                _lootZoneBakeStatus = null;
                manager.IsDirty = true;
                RefreshInspector();
            }, 50, 22);
            var baked = zone.surfacePoints?.Count ?? 0;
            UIBuilder.CreateWrappedText(_inspectorContent,
                baked > 0
                    ? $"{baked} baked surface points. In raid each item takes a different one. {_lootZoneBakeStatus}"
                    : "No baked points: items scatter at the zone's Y (legacy). Bake with the zone over the area to ground loot on floors, shelves and tables. " + _lootZoneBakeStatus,
                11, new Color(0.68f, 0.75f, 0.8f, 1f));

            BuildToggleField(_inspectorContent, "Quest only", zone.questOnly, (v) => { zone.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", zone.questCompleted, (v) => { zone.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.questOnly || zone.questCompleted)
            {
                BuildStringField(_inspectorContent, "Quest ID", zone.questId ?? "", (v) => { zone.questId = v; manager.IsDirty = true; });
            }
            BuildItemsList(zone.items, true, (i) => previews.SpawnAtZoneCenter(zone, i));
        }

        private void BuildStaticObject(StaticObject obj)
        {
            BuildStringField(_inspectorContent, "Prefab Path", obj.prefabPath ?? "", (v) => { obj.prefabPath = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", obj.scale.ToVector3(), (v) => { obj.scale = TransformData.FromVector3(v); manager.IsDirty = true; });
            BuildSourceObjectControls(obj);
            BuildToggleField(_inspectorContent, "Quest only", obj.questOnly, (v) => { obj.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", obj.questCompleted, (v) => { obj.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (obj.questOnly || obj.questCompleted)
            {
                BuildStringField(_inspectorContent, "Quest ID", obj.questId ?? "", (v) => { obj.questId = v; manager.IsDirty = true; });
            }
            UIBuilder.CreateButton(_inspectorContent, "Preview Object", () => previews.SpawnStaticPreview(obj), 100, 24);
        }

        private void BuildSourceObjectControls(IHasSourceObject obj)
        {
            if (_isPickingSource && _pickingSourceTarget == obj)
            {
                var pickRow = UIBuilder.CreatePanel("SourcePickRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(pickRow, 2, 2, false, false);
                UIBuilder.AddLayoutElement(pickRow, null, 22, null, 22, null, 0);
                UIBuilder.CreateLabel(pickRow, "Click object in world...", 11, 132, 22);
                UIBuilder.CreateButton(pickRow, "Cancel", () => { SetPickingSource(false, null); RefreshInspector(); }, 54, 22);
            }
            else
            {
                // Row 1: Pick from Scene + Use Parent
                var row1 = UIBuilder.CreatePanel("SourceRow1", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row1, 4, 2, false, false);
                UIBuilder.AddLayoutElement(row1, null, 22, null, 22, null, 0);
                UIBuilder.CreateButton(row1, "Pick from Scene", () => { SetPickingSource(true, obj); RefreshInspector(); }, 100, 22);
                UIBuilder.CreateToggle(row1, "Use Parent", _pickUseParent, (v) => _pickUseParent = v, 18);

                // Row 2: From List
                var row2 = UIBuilder.CreatePanel("SourceRow2", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row2, 4, 2, false, false);
                UIBuilder.AddLayoutElement(row2, null, 22, null, 22, null, 0);
                UIBuilder.CreateButton(row2, "From List", () => { _goListTarget = obj; SelectBottomTab(3); RefreshGOActionRow(); }, 68, 22);

                // Row 3: Clear Source (only when a source is set)
                if (!string.IsNullOrEmpty(obj.sourceObjectName))
                {
                    var row3 = UIBuilder.CreatePanel("SourceRow3", _inspectorContent, new Color(0, 0, 0, 0));
                    UIBuilder.AddHorizontalLayout(row3, 4, 2, false, false);
                    UIBuilder.AddLayoutElement(row3, null, 22, null, 22, null, 0);
                    UIBuilder.CreateButton(row3, "Clear Source", () => { obj.sourceObjectName = ""; obj.sourceObjectPosition = new TransformData(); obj.sourceWorldObjectId = ""; manager.IsDirty = true; RefreshInspector(); }, 90, 22);
                    UIBuilder.CreateWrappedText(_inspectorContent, $"Source: {obj.sourceObjectName} @ {obj.sourceObjectPosition.x:F2}, {obj.sourceObjectPosition.y:F2}, {obj.sourceObjectPosition.z:F2}", 11, new Color(0.68f, 0.75f, 0.8f, 1f));
                }
            }

            BuildRootDoorControls(obj);
        }

        // Shown only when the copied source object is itself a door (door component on its root).
        private void BuildRootDoorControls(IHasSourceObject src)
        {
            if (string.IsNullOrEmpty(src.sourceObjectName))
                return;

            string key, state;
            Action<string> setKey, setState;
            if (src is StaticObject so)
            {
                key = so.rootKeyId; state = so.rootDoorState;
                setKey = v => so.rootKeyId = v; setState = v => so.rootDoorState = v;
            }
            else if (src is WTTStaticObject wtt && wtt.spawnType == "clone")
            {
                key = wtt.rootKeyId; state = wtt.rootDoorState;
                setKey = v => wtt.rootKeyId = v; setState = v => wtt.rootDoorState = v;
            }
            else
                return;

            var source = previews?.FindSourceObject(src.sourceObjectName, src.sourceObjectPosition.ToVector3());
            var vanilla = source != null ? source.GetComponent<WorldInteractiveObject>() : null;
            if (vanilla == null)
                return;

            var vanillaKey = vanilla.KeyId;
            Text warn = null;
            Action update = () =>
            {
                if (warn == null) return;
                bool locked = string.Equals(state, "Locked", StringComparison.OrdinalIgnoreCase);
                bool hasKey = !string.IsNullOrWhiteSpace(key) || !string.IsNullOrWhiteSpace(vanillaKey);
                warn.gameObject.SetActive(locked && !hasKey);
            };

            UIBuilder.CreateText(_inspectorContent, "Door (copied source)", 10, new Color(0.75f, 0.75f, 0.75f, 1f));
            BuildStringField(_inspectorContent, "Key Id", key ?? "", v => { key = v; setKey(v); manager.IsDirty = true; update(); });
            BuildDropdownField(_inspectorContent, "Initial Door State", string.IsNullOrWhiteSpace(state) ? "Default" : state, new[] { "Default", "Locked", "Shut", "Open" }, v =>
            {
                state = v == "Default" ? "" : v;
                setState(state);
                manager.IsDirty = true;
                update();
            });
            warn = UIBuilder.CreateWrappedText(_inspectorContent, "Warning: door is Locked but has no key id; no key can unlock it.", 11);
            warn.color = new Color(1f, 0.75f, 0.2f, 1f);
            update();
        }

        private void BuildWTTQuestZone(WTTQuestZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildStringField(_inspectorContent, "Zone Id", zone.zoneId ?? "", (v) => { zone.zoneId = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Zone Name", zone.zoneName ?? "", (v) => { zone.zoneName = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Zone Location", zone.zoneLocation ?? "", (v) => { zone.zoneLocation = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Zone Type", zone.zoneType ?? "", new[] { "placeitem", "visit", "flarezone", "botkillzone", "salvage" }, (v) => { zone.zoneType = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Flare Type", zone.flareType ?? "", new[] { "", "Light", "Airdrop", "ExitActivate", "Quest", "AIFollowEvent" }, (v) => { zone.flareType = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", zone.scale.ToVector3(), (v) => { zone.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            UIBuilder.CreateButton(_inspectorContent, "Copy Zone Data", () =>
            {
                var json = JsonConvert.SerializeObject(new
                {
                    ZoneId = zone.zoneId,
                    ZoneName = zone.zoneName,
                    ZoneLocation = zone.zoneLocation,
                    ZoneType = zone.zoneType,
                    FlareType = zone.flareType,
                    Position = new { X = zone.position.x.ToString("F4", CultureInfo.InvariantCulture), Y = zone.position.y.ToString("F4", CultureInfo.InvariantCulture), Z = zone.position.z.ToString("F4", CultureInfo.InvariantCulture) },
                    Rotation = new { X = zone.rotation.x.ToString("F4", CultureInfo.InvariantCulture), Y = zone.rotation.y.ToString("F4", CultureInfo.InvariantCulture), Z = zone.rotation.z.ToString("F4", CultureInfo.InvariantCulture) },
                    Scale = new { X = zone.scale.x.ToString("F4", CultureInfo.InvariantCulture), Y = zone.scale.y.ToString("F4", CultureInfo.InvariantCulture), Z = zone.scale.z.ToString("F4", CultureInfo.InvariantCulture) }
                }, Formatting.Indented);
                GUIUtility.systemCopyBuffer = json;
                _fieldClipboard = json;
            }, 120, 24);
        }

        private void BuildWTTStaticObject(WTTStaticObject obj)
        {
            if (obj.scale == null)
                obj.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildDropdownField(_inspectorContent, "Spawn Type", obj.spawnType ?? "bundle", new[] { "bundle", "clone" }, (v) => { obj.spawnType = v; manager.IsDirty = true; RefreshInspector(); });

            if (obj.spawnType == "clone")
            {
                BuildSourceObjectControls(obj);
            }
            else
            {
                BuildStringField(_inspectorContent, "Bundle Name", obj.bundleName ?? "", (v) => { obj.bundleName = v; manager.IsDirty = true; });
                BuildStringField(_inspectorContent, "Prefab Name", obj.prefabName ?? "", (v) => { obj.prefabName = v; manager.IsDirty = true; });
            }

            BuildVector3Field(_inspectorContent, "Scale", obj.scale.ToVector3(), (v) => { obj.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Quest only", obj.questOnly, (v) => { obj.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", obj.questCompleted, (v) => { obj.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            BuildStringField(_inspectorContent, "Quest Id", obj.questId ?? "", (v) => { obj.questId = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Required Statuses", string.Join(",", obj.requiredQuestStatuses ?? new System.Collections.Generic.List<string>()), (v) => { obj.requiredQuestStatuses = new System.Collections.Generic.List<string>(v.Split(',')); manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Excluded Statuses", string.Join(",", obj.excludedQuestStatuses ?? new System.Collections.Generic.List<string>()), (v) => { obj.excludedQuestStatuses = new System.Collections.Generic.List<string>(v.Split(',')); manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Quest Must Exist", obj.questMustExist, (v) => { obj.questMustExist = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Linked Quest Id", obj.linkedQuestId ?? "", (v) => { obj.linkedQuestId = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Required Item", obj.requiredItemInInventory ?? "", (v) => { obj.requiredItemInInventory = v; manager.IsDirty = true; });
            BuildIntField(_inspectorContent, "Required Level", obj.requiredLevel, (v) => { obj.requiredLevel = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Required Faction", obj.requiredFaction ?? "", (v) => { obj.requiredFaction = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Required Boss", obj.requiredBossSpawned ?? "", (v) => { obj.requiredBossSpawned = v; manager.IsDirty = true; });

            if (obj.spawnType == "clone" || (!string.IsNullOrEmpty(obj.bundleName) && !string.IsNullOrEmpty(obj.prefabName)))
            {
                UIBuilder.CreateButton(_inspectorContent, "Preview Object", () => previews.SpawnWTTStaticPreview(obj), 100, 24);
            }

            UIBuilder.CreateButton(_inspectorContent, "Copy WTT Static Data", () =>
            {
                var data = WttStaticDataConverter.ToWttConfig(obj, manager.Data.map);
                var json = JsonConvert.SerializeObject(data, Formatting.Indented, PackData.InvariantSettings);
                GUIUtility.systemCopyBuffer = json;
                _fieldClipboard = json;
            }, 120, 24);
        }

        private List<ChildInteractableData> GetChildInteractables(MarkerBase marker)
        {
            if (marker is StaticObject so)
                return so.childInteractables;
            if (marker is WTTStaticObject wtt)
                return wtt.childInteractables;
            return null;
        }

        private void BuildChildInspector(MarkerBase marker, string childPath, bool isDoor, bool isContainer)
        {
            UIBuilder.CreateText(_inspectorContent, "Child Object", 12, Color.white, FontStyle.Bold);
            UIBuilder.CreateWrappedText(_inspectorContent, childPath, 10, new Color(0.75f, 0.75f, 0.75f, 1f));
            UIBuilder.CreateText(_inspectorContent, isDoor ? "Door" : isContainer ? "Container" : "Object", 10, new Color(0.75f, 0.75f, 0.75f, 1f));

            var sourceObj = marker as IHasSourceObject;
            Transform child = manager.SelectedChild;
            if (child == null && sourceObj != null && !string.IsNullOrEmpty(sourceObj.sourceObjectName))
            {
                var source = previews?.FindSourceObject(sourceObj.sourceObjectName, sourceObj.sourceObjectPosition.ToVector3());
                child = source?.transform.Find(childPath);
            }

            if (child == null)
            {
                UIBuilder.CreateText(_inspectorContent, "Child not found in preview or source.", 10, new Color(0.7f, 0.3f, 0.3f, 1f));
            }
            else
            {
                var comps = child.GetComponents<Component>();
                var names = new List<string>();
                foreach (var comp in comps)
                {
                    if (comp == null)
                        continue;
                    var n = comp.GetType().Name;
                    if (n == "Transform")
                        continue;
                    names.Add(n);
                }
                if (names.Count > 0)
                    UIBuilder.CreateWrappedText(_inspectorContent, "Components: " + string.Join(", ", names.ToArray()), 10, new Color(0.75f, 0.75f, 0.75f, 1f));

                var overrides = GetChildInteractables(marker);
                if (overrides != null)
                {
                    ChildInteractableData data = overrides.FirstOrDefault(x => x.childPath == childPath);
                    var currentPos = data?.position != null ? data.position.ToVector3() : child.localPosition;
                    var currentRot = data?.rotation != null ? data.rotation.ToVector3() : child.localEulerAngles;
                    var currentScl = data?.scale != null ? data.scale.ToVector3() : child.localScale;

                    BuildVector3Field(_inspectorContent, "Local Position", currentPos, (v) =>
                    {
                        if (data == null) { data = new ChildInteractableData { childPath = childPath }; overrides.Add(data); }
                        data.position = TransformData.FromVector3(v);
                        manager.IsDirty = true;
                        RequestInspectorRefresh();
                    });
                    BuildVector3Field(_inspectorContent, "Local Rotation", currentRot, (v) =>
                    {
                        if (data == null) { data = new ChildInteractableData { childPath = childPath }; overrides.Add(data); }
                        data.rotation = TransformData.FromVector3(v);
                        manager.IsDirty = true;
                        RequestInspectorRefresh();
                    });
                    BuildVector3Field(_inspectorContent, "Local Scale", currentScl, (v) =>
                    {
                        if (data == null) { data = new ChildInteractableData { childPath = childPath }; overrides.Add(data); }
                        data.scale = TransformData.FromVector3(v);
                        manager.IsDirty = true;
                        RequestInspectorRefresh();
                    });
                }
            }

            var overrides2 = GetChildInteractables(marker);
            if (overrides2 != null && (isDoor || isContainer))
            {
                ChildInteractableData data = overrides2.FirstOrDefault(x => x.childPath == childPath);

                if (isDoor)
                {
                    var vanillaKey = child != null ? child.GetComponent<WorldInteractiveObject>()?.KeyId : null;
                    Text childLockWarning = null;
                    Action updateChildLockWarning = () =>
                    {
                        if (childLockWarning == null) return;
                        var locked = string.Equals(data?.initialDoorState, "Locked", StringComparison.OrdinalIgnoreCase);
                        var hasKey = !string.IsNullOrWhiteSpace(data?.keyId) || !string.IsNullOrWhiteSpace(vanillaKey);
                        childLockWarning.gameObject.SetActive(locked && !hasKey);
                    };

                    var value = data?.keyId ?? "";
                    BuildStringField(_inspectorContent, "Key Id", value, (v) =>
                    {
                        if (data == null) { data = new ChildInteractableData { childPath = childPath }; overrides2.Add(data); }
                        data.keyId = v;
                        manager.IsDirty = true;
                        updateChildLockWarning();
                    });

                    // "Default" leaves the vanilla state untouched; "Shut" is the unlocked, closed state.
                    var stateValue = string.IsNullOrWhiteSpace(data?.initialDoorState) ? "Default" : data.initialDoorState;
                    BuildDropdownField(_inspectorContent, "Initial Door State", stateValue, new[] { "Default", "Locked", "Shut", "Open" }, (v) =>
                    {
                        if (data == null) { data = new ChildInteractableData { childPath = childPath }; overrides2.Add(data); }
                        data.initialDoorState = v == "Default" ? "" : v;
                        manager.IsDirty = true;
                        updateChildLockWarning();
                    });
                    childLockWarning = UIBuilder.CreateWrappedText(_inspectorContent, "Warning: door is Locked but has no key id (override or vanilla); no key can unlock it.", 11);
                    childLockWarning.color = new Color(1f, 0.75f, 0.2f, 1f);
                    updateChildLockWarning();
                }

                if (isContainer)
                {
                    var value = data?.containerId ?? "";
                    BuildStringField(_inspectorContent, "Container Id", value, (v) =>
                    {
                        if (data == null) { data = new ChildInteractableData { childPath = childPath }; overrides2.Add(data); }
                        data.containerId = v;
                        manager.IsDirty = true;
                    });
                }
            }

            if (child != null)
            {
                var addOverrides = GetChildInteractables(marker);
                ChildInteractableData addData = addOverrides?.FirstOrDefault(x => x.childPath == childPath);
                if (addData == null && addOverrides != null)
                {
                    addData = new ChildInteractableData { childPath = childPath };
                    addOverrides.Add(addData);
                }
                if (addData != null)
                    UIBuilder.CreateButton(_inspectorContent, "Add Component", () => ShowAddComponentDialog(child.gameObject, addData.addedComponents), 110, 24);
            }

            UIBuilder.CreateButton(_inspectorContent, "Delete Child", () =>
            {
                var delOverrides = GetChildInteractables(marker);
                if (delOverrides != null)
                {
                    ChildInteractableData delData = delOverrides.FirstOrDefault(x => x.childPath == childPath);
                    if (delData == null)
                    {
                        delData = new ChildInteractableData { childPath = childPath };
                        delOverrides.Add(delData);
                    }
                    delData.deleted = true;
                }
                if (manager.SelectedChild != null)
                    manager.SelectedChild.gameObject.SetActive(false);
                manager.IsDirty = true;
                _selectedChildMarker = null;
                _selectedChildPath = null;
                manager.SelectedChild = null;
                manager.SelectedChildPath = null;
                RequestInspectorRefresh();
                RequestHierarchyRefresh();
            }, 90, 24);

            UIBuilder.CreateButton(_inspectorContent, "Back to Parent", () =>
            {
                _selectedChildMarker = null;
                _selectedChildPath = null;
                manager.SelectedChild = null;
                manager.SelectedChildPath = null;
                RequestInspectorRefresh();
                RequestHierarchyRefresh();
            }, 110, 24);
        }

        private static bool IsContainerLockedWithoutKey(InteractiveObject obj)
        {
            return string.Equals(obj.initialDoorState, "Locked", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(obj.keyId);
        }

        private void BuildInteractiveObject(InteractiveObject obj)
        {
            if (obj.scale == null)
                obj.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildDropdownField(_inspectorContent, "Type", obj.interactiveType.ToString(), new[] { "Door", "Container", "StationaryWeapon", "Switch" }, (v) =>
            {
                obj.interactiveType = (InteractiveObjectType)System.Enum.Parse(typeof(InteractiveObjectType), v);
                manager.IsDirty = true;
                RefreshInspector();
            });

            BuildSourceObjectControls(obj);
            BuildVector3Field(_inspectorContent, "Scale", obj.scale.ToVector3(), (v) => { obj.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            if (obj.interactiveType == InteractiveObjectType.Door)
            {
                if (string.IsNullOrEmpty(obj.initialDoorState))
                    obj.initialDoorState = "Shut";
                Text doorLockWarning = null;
                Action updateDoorLockWarning = () =>
                {
                    if (doorLockWarning != null)
                        doorLockWarning.gameObject.SetActive(IsContainerLockedWithoutKey(obj));
                };
                BuildStringField(_inspectorContent, "Key Template Id", obj.keyId ?? "", (v) => { obj.keyId = v; manager.IsDirty = true; updateDoorLockWarning(); });
                BuildDropdownField(_inspectorContent, "Initial Door State", obj.initialDoorState, new[] { "Locked", "Shut", "Open" }, (v) => { obj.initialDoorState = v; manager.IsDirty = true; updateDoorLockWarning(); });
                doorLockWarning = UIBuilder.CreateWrappedText(_inspectorContent, "Warning: door is Locked but has no Key Template Id; no key can unlock it.", 11);
                doorLockWarning.color = new Color(1f, 0.75f, 0.2f, 1f);
                updateDoorLockWarning();
                BuildToggleField(_inspectorContent, "Can Breach", obj.canBreach, (v) => { obj.canBreach = v; manager.IsDirty = true; });
            }
            else if (obj.interactiveType == InteractiveObjectType.Container)
            {
                BuildStringFieldWithButton(_inspectorContent, "Container Id", obj.containerId ?? "", (v) => { obj.containerId = v; manager.IsDirty = true; }, "Gen", () =>
                {
                    obj.containerId = GenerateHexId();
                    manager.IsDirty = true;
                    RequestInspectorRefresh();
                });
                BuildContainerTemplateDropdown(obj);
                Text lockWarning = null;
                Action updateLockWarning = () =>
                {
                    if (lockWarning != null)
                        lockWarning.gameObject.SetActive(IsContainerLockedWithoutKey(obj));
                };
                BuildStringField(_inspectorContent, "Key Template Id (optional)", obj.keyId ?? "", (v) => { obj.keyId = v; manager.IsDirty = true; updateLockWarning(); });
                BuildDropdownField(_inspectorContent, "Initial Door State", string.Equals(obj.initialDoorState, "Locked", StringComparison.OrdinalIgnoreCase) ? "Locked" : "Shut", new[] { "Shut", "Locked" }, (v) => { obj.initialDoorState = v; manager.IsDirty = true; updateLockWarning(); });
                lockWarning = UIBuilder.CreateWrappedText(_inspectorContent, "Warning: container is Locked but has no Key Template Id.", 11);
                lockWarning.color = new Color(1f, 0.75f, 0.2f, 1f);
                updateLockWarning();
                BuildDropdownField(_inspectorContent, "Loot Mode", obj.lootMode.ToString(), new[] { "Default", "Hybrid", "Custom" }, (v) =>
                {
                    obj.lootMode = (ContainerLootMode)System.Enum.Parse(typeof(ContainerLootMode), v);
                    manager.IsDirty = true;
                    RefreshInspector();
                });
                BuildIntField(_inspectorContent, "Min Items (0 = vanilla)", obj.itemCountMin, (v) => { obj.itemCountMin = Math.Max(0, v); manager.IsDirty = true; });
                BuildIntField(_inspectorContent, "Max Items (0 = vanilla)", obj.itemCountMax, (v) => { obj.itemCountMax = Math.Max(0, v); manager.IsDirty = true; });
                BuildFloatField(_inspectorContent, "Spawn Chance", obj.spawnChance, (v) => { obj.spawnChance = v; manager.IsDirty = true; });
                BuildToggleField(_inspectorContent, "Quest only", obj.questOnly, (v) => { obj.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
                BuildToggleField(_inspectorContent, "Quest completed", obj.questCompleted, (v) => { obj.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
                if (obj.questOnly || obj.questCompleted)
                {
                    BuildStringField(_inspectorContent, "Quest ID", obj.questId ?? "", (v) => { obj.questId = v; manager.IsDirty = true; });
                }
                BuildItemsList(obj.items, false, (i) => { });
            }
            else if (obj.interactiveType == InteractiveObjectType.StationaryWeapon)
            {
                BuildWeaponTemplateDropdown(obj);
            }
            else if (obj.interactiveType == InteractiveObjectType.Switch)
            {
                BuildToggleField(_inspectorContent, "Start On", obj.switchInitialState, (v) => { obj.switchInitialState = v; manager.IsDirty = true; });
                if (obj.linkedLightZoneNames == null)
                    obj.linkedLightZoneNames = new List<string>();
                BuildStringList(_inspectorContent, "Linked Light Zones", obj.linkedLightZoneNames, "lightZoneName");
                if (obj.linkedExtractNames == null)
                    obj.linkedExtractNames = new List<string>();
                BuildStringList(_inspectorContent, "Linked Extract Zones", obj.linkedExtractNames, "extractName");
            }

            UIBuilder.CreateButton(_inspectorContent, "Preview Object", () => previews.SpawnInteractivePreview(obj), 100, 24);
        }

        private void BuildExtractZone(ExtractZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildStringField(_inspectorContent, "Exit Name", zone.exitName ?? "", (v) => { zone.exitName = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Exfil Time", zone.exfiltrationTime, (v) => { zone.exfiltrationTime = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Exfil Type", zone.exfiltrationType ?? "Individual", new[] { "Individual", "SharedTimer", "Manual" }, (v) => { zone.exfiltrationType = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Spawn Chance", zone.spawnChance, (v) => { zone.spawnChance = v; manager.IsDirty = true; });

            BuildDropdownField(_inspectorContent, "Side", zone.side ?? "Pmc", new[] { "Pmc", "Coop", "Scav" }, (v) => { zone.side = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Passage Requirement", zone.passageRequirement ?? "None", new[] { "None", "Empty", "TransferItem", "WorldEvent", "HasItem", "EmptyOrSize", "SkillLevel", "Reference", "ScavCooperation", "Train", "Timer", "SecretTransferItem" }, (v) => { zone.passageRequirement = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Requirement Tip", zone.requirementTip ?? "", (v) => { zone.requirementTip = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Required Slot", zone.requiredSlot ?? "FirstPrimaryWeapon", (v) => { zone.requiredSlot = v; manager.IsDirty = true; });
            BuildIntField(_inspectorContent, "Count", zone.count, (v) => { zone.count = v; manager.IsDirty = true; });
            BuildIntField(_inspectorContent, "Players Count", zone.playersCount, (v) => { zone.playersCount = v; manager.IsDirty = true; });

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { zone.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)zone.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildFloatField(_inspectorContent, "Radius", zone.radius, (v) => { zone.radius = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", zone.scale.ToVector3(), (v) => { zone.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Quest only", zone.questOnly, (v) => { zone.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", zone.questCompleted, (v) => { zone.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.questOnly || zone.questCompleted)
            {
                BuildStringField(_inspectorContent, "Quest ID", zone.questId ?? "", (v) => { zone.questId = v; manager.IsDirty = true; });
            }

            BuildToggleField(_inspectorContent, "Link Lights", zone.linkLights, (v) => { zone.linkLights = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.linkLights)
            {
                if (zone.lightZoneNames == null)
                    zone.lightZoneNames = new List<string>();
                var actionNames = new[] { "Toggle", "Enable", "Disable" };
                BuildDropdownField(_inspectorContent, "Light Action", actionNames[(int)zone.lightAction], actionNames, (v) =>
                {
                    zone.lightAction = (TriggerLightAction)Array.IndexOf(actionNames, v);
                    manager.IsDirty = true;
                });
                BuildStringList(_inspectorContent, "Light Zone Names", zone.lightZoneNames, "light_zone");
            }

            BuildExtractZoneRequirements(zone);
        }

        private void BuildExtractZoneRequirements(ExtractZone zone)
        {
            if (zone.requirements == null)
                zone.requirements = new List<ExtractZoneRequirement>();

            UIBuilder.CreateText(_inspectorContent, "Requirements:", 11, Color.white, FontStyle.Bold);

            for (int i = 0; i < zone.requirements.Count; i++)
            {
                var req = zone.requirements[i];
                int idx = i;

                var row = UIBuilder.CreatePanel("ReqRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
                UIBuilder.CreateLabel(row, $"Req {i + 1}", 11, 38, 20);

                BuildDropdownField(row, "", req.type ?? "None", new[] { "None", "TransferItem", "HasItem", "WearsItem", "QuestActive", "QuestCompleted" }, (v) =>
                {
                    req.type = v;
                    manager.IsDirty = true;
                    RefreshInspector();
                });

                UIBuilder.CreateButton(row, "X", () => { zone.requirements.RemoveAt(idx); manager.IsDirty = true; RefreshInspector(); }, 30, 20, 10);

                if (req.type == "TransferItem" || req.type == "HasItem" || req.type == "WearsItem")
                {
                    BuildStringField(_inspectorContent, "Item Tpl", req.templateId ?? "", (v) => { req.templateId = v; manager.IsDirty = true; });
                    BuildIntField(_inspectorContent, "Count", req.count, (v) => { req.count = v; manager.IsDirty = true; });
                }
                else if (req.type == "QuestActive" || req.type == "QuestCompleted")
                {
                    BuildStringField(_inspectorContent, "Quest ID", req.templateId ?? "", (v) => { req.templateId = v; manager.IsDirty = true; });
                }
            }

            UIBuilder.CreateButton(_inspectorContent, "Add Requirement", () => { zone.requirements.Add(new ExtractZoneRequirement()); manager.IsDirty = true; RefreshInspector(); }, 100, 22);
        }

        private void BuildBotActivityZone(BotActivityZone zone)
        {
            UIBuilder.CreateText(_inspectorContent, "Bot Activity Zone", 12, Color.white, FontStyle.Bold);
            UIBuilder.CreateWrappedText(_inspectorContent,
                "Activity boundary for custom bot spawn points. Moving this zone never moves spawn points assigned to it.",
                11, new Color(0.72f, 0.72f, 0.72f, 1f));
            UIBuilder.CreateWrappedText(_inspectorContent, $"Zone ID: {zone.zoneId}", 11, new Color(0.8f, 0.95f, 0.4f, 1f));

            BuildToggleField(_inspectorContent, "Enabled", zone.enabled, (v) => { zone.enabled = v; manager.IsDirty = true; });

            var shapeRow = UIBuilder.CreatePanel("ZoneShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 22);
            var shapes = new[] { BotActivityZoneShape.Circle, BotActivityZoneShape.Square };
            foreach (var shape in shapes)
            {
                var captured = shape;
                var btn = UIBuilder.CreateButton(shapeRow, shape.ToString(), () =>
                {
                    zone.shape = captured;
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 70, 22, 11);
                if (zone.shape == shape)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            var leashRow = UIBuilder.CreatePanel("ZoneLeashModeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(leashRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(leashRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(leashRow, "Leash", 11, 44, 22);
            var currentLeash = BotActivityZone.NormalizeLeashMode(zone.leashMode);
            foreach (var mode in new[] { BotActivityZone.LeashModeSoft, BotActivityZone.LeashModeStrong })
            {
                var capturedMode = mode;
                var modeBtn = UIBuilder.CreateButton(leashRow, mode + " Leash", () =>
                {
                    zone.leashMode = capturedMode;
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 100, 22, 11);
                if (currentLeash == mode)
                    modeBtn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }
            if (currentLeash == BotActivityZone.LeashModeStrong)
                UIBuilder.CreateWrappedText(_inspectorContent, "Strong Leash: stricter boundary, shorter combat holdoff, re-plans before the edge. SAIN combat movement can still leave the zone briefly.", 11, new Color(1f, 0.7f, 0.2f, 1f));

            _zoneRadiusInput = null;
            _zoneWidthInput = null;
            _zoneDepthInput = null;
            _zoneSizeInputZone = zone;
            var zoneIsCircle = zone.shape == BotActivityZoneShape.Circle;
            if (zoneIsCircle)
            {
                var radiusSnapshotTaken = false;
                _zoneRadiusInput = BuildFloatField(_inspectorContent, "Radius (m)", zone.radius, (v) =>
                {
                    var clamped = BotActivityZone.ClampRadius(v);
                    if (Mathf.Approximately(zone.radius, clamped))
                        return;
                    if (!radiusSnapshotTaken)
                    {
                        manager.Snapshot();
                        radiusSnapshotTaken = true;
                    }
                    manager.SetActivityZoneSize(zone, v, out _);
                });
                _zoneRadiusInput.onEndEdit.AddListener((_) =>
                {
                    radiusSnapshotTaken = false;
                    SyncZoneSizeField(zone, true);
                });
                UIBuilder.CreateWrappedText(_inspectorContent, $"Radius is measured in meters from the zone center to the edge ({BotActivityZone.MinSize:0.#}-{BotActivityZone.MaxRadius:0.#} m). Drag the radius handle in the viewport to resize.", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
            }
            else
            {
                var widthSnapshotTaken = false;
                var depthSnapshotTaken = false;
                _zoneWidthInput = BuildFloatField(_inspectorContent, "Width (m)", zone.EffectiveWidth, (v) =>
                {
                    if (!Mathf.Approximately(zone.EffectiveWidth, BotActivityZone.ClampDimension(v)))
                    {
                        if (!widthSnapshotTaken) { manager.Snapshot(); widthSnapshotTaken = true; }
                        manager.SetActivityZoneDimension(zone, true, v);
                    }
                });
                _zoneDepthInput = BuildFloatField(_inspectorContent, "Depth (m)", zone.EffectiveDepth, (v) =>
                {
                    if (!Mathf.Approximately(zone.EffectiveDepth, BotActivityZone.ClampDimension(v)))
                    {
                        if (!depthSnapshotTaken) { manager.Snapshot(); depthSnapshotTaken = true; }
                        manager.SetActivityZoneDimension(zone, false, v);
                    }
                });
                _zoneWidthInput.onEndEdit.AddListener((_) => { widthSnapshotTaken = false; SyncZoneSizeField(zone, true); });
                _zoneDepthInput.onEndEdit.AddListener((_) => { depthSnapshotTaken = false; SyncZoneSizeField(zone, true); });
                UIBuilder.CreateWrappedText(_inspectorContent, $"Width and depth are independent full dimensions ({BotActivityZone.MinSize:0.#}-{BotActivityZone.MaxSideLength:0.#} m). The footprint follows the zone's Y rotation; drag a corner or edge handle to resize one or both dimensions while anchoring the opposite side.", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
            }

            UIBuilder.CreateText(_inspectorContent, "Patrol Route", 12, Color.white, FontStyle.Bold);
            BuildToggleField(_inspectorContent, "Enable Patrol", zone.patrolEnabled, (value) =>
            {
                zone.patrolEnabled = value;
                manager.IsDirty = true;
                RefreshInspector();
            });
            UIBuilder.CreateWrappedText(_inspectorContent, "Patrol points belong to this activity zone. Drag a visible point marker to move it, or use the viewport placement button. Dwell timers pause during combat, and Strong Leash limits movement to the configured route and destination area.", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
            if (controller.IsPlacingPatrolPoint)
            {
                UIBuilder.CreateButton(_inspectorContent, "Cancel Point Placement (Esc)", controller.CancelPatrolPointPlacement, 200, 22);
            }
            else
            {
                UIBuilder.CreateButton(_inspectorContent, "Place New Patrol Point", () => controller.BeginAddPatrolPoint(zone), 180, 22);
            }

            zone.patrolPoints ??= new List<BotActivityPatrolPoint>();
            var destinationOptions = new[] { "" }.Concat(manager.GetActivityZones().Where(candidate => candidate != null && !string.Equals(candidate.zoneId, zone.zoneId, StringComparison.Ordinal))
                .Select(candidate => candidate.zoneId)).ToArray();
            for (var index = 0; index < zone.patrolPoints.Count; index++)
            {
                var point = zone.patrolPoints[index];
                if (point == null)
                    continue;
                point.position ??= new TransformData();
                UIBuilder.CreateText(_inspectorContent, $"{index + 1}. {point.name} ({point.id})", 11, new Color(0.9f, 0.78f, 0.38f, 1f), FontStyle.Bold);
                BuildStringField(_inspectorContent, "Point Name", point.name ?? "", value => { point.name = value; manager.IsDirty = true; });
                BuildToggleField(_inspectorContent, "Point Enabled", point.enabled, (value) => { point.enabled = value; manager.IsDirty = true; });
                BuildDropdownField(_inspectorContent, "Destination Zone", point.destinationZoneId ?? "", destinationOptions, (value) =>
                {
                    point.destinationZoneId = value;
                    var destination = manager.FindActivityZone(value);
                    if (destination != null)
                        point.position = TransformData.FromVector3(destination.position.ToVector3());
                    manager.IsDirty = true;
                });
                var patrolPosition = point.position.ToVector3();
                BuildFloatField(_inspectorContent, "Point X", patrolPosition.x, value => { point.position.x = value; manager.IsDirty = true; });
                BuildFloatField(_inspectorContent, "Point Y", patrolPosition.y, value => { point.position.y = value; manager.IsDirty = true; });
                BuildFloatField(_inspectorContent, "Point Z", patrolPosition.z, value => { point.position.z = value; manager.IsDirty = true; });
                BuildFloatField(_inspectorContent, "Dwell (sec)", point.dwellSeconds, value =>
                {
                    point.dwellSeconds = Mathf.Clamp(value, 1f, 3600f);
                    manager.IsDirty = true;
                });
                UIBuilder.CreateButton(_inspectorContent,
                    controller.IsPatrolPointBeingPlaced(point) ? "Cancel Viewport Placement (Esc)" : "Place Point in Viewport",
                    () =>
                    {
                        if (controller.IsPatrolPointBeingPlaced(point))
                            controller.CancelPatrolPointPlacement();
                        else
                            controller.BeginMovePatrolPoint(zone, point);
                    }, 200, 22);
                var pointIndex = index;
                var controls = UIBuilder.CreatePanel("PatrolPointControls", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(controls, 2, 2, false, false);
                UIBuilder.CreateButton(controls, "Move Up", () =>
                {
                    if (pointIndex <= 0) return;
                    var moved = zone.patrolPoints[pointIndex];
                    zone.patrolPoints.RemoveAt(pointIndex);
                    zone.patrolPoints.Insert(pointIndex - 1, moved);
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 70, 22, 10);
                UIBuilder.CreateButton(controls, "Move Down", () =>
                {
                    if (pointIndex >= zone.patrolPoints.Count - 1) return;
                    var moved = zone.patrolPoints[pointIndex];
                    zone.patrolPoints.RemoveAt(pointIndex);
                    zone.patrolPoints.Insert(pointIndex + 1, moved);
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 70, 22, 10);
                UIBuilder.CreateButton(controls, "Remove", () =>
                {
                    zone.patrolPoints.Remove(point);
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 64, 22, 10);
            }

            int users = manager.Data?.botSpawnPoints?.Count(p => p != null && p.botActivityZoneId == zone.zoneId) ?? 0;
            UIBuilder.CreateWrappedText(_inspectorContent, $"Assigned spawn points: {users}", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
        }

        // Keeps the Inspector size field in step with viewport resizing; never overwrites a field being typed in.
        public void SyncZoneSizeField(BotActivityZone zone, bool force = false)
        {
            if (zone == null || !ReferenceEquals(zone, _zoneSizeInputZone))
                return;
            if (_zoneRadiusInput != null && (force || !_zoneRadiusInput.isFocused))
                _zoneRadiusInput.SetTextWithoutNotify(zone.radius.ToString("F3", CultureInfo.InvariantCulture));
            if (_zoneWidthInput != null && (force || !_zoneWidthInput.isFocused))
                _zoneWidthInput.SetTextWithoutNotify(zone.EffectiveWidth.ToString("F3", CultureInfo.InvariantCulture));
            if (_zoneDepthInput != null && (force || !_zoneDepthInput.isFocused))
                _zoneDepthInput.SetTextWithoutNotify(zone.EffectiveDepth.ToString("F3", CultureInfo.InvariantCulture));
        }

        private InputField _zoneRadiusInput;
        private InputField _zoneWidthInput;
        private InputField _zoneDepthInput;
        private BotActivityZone _zoneSizeInputZone;

        private void BuildBotActivityZoneAssignment(BotSpawnPoint point)
        {
            UIBuilder.CreateText(_inspectorContent, "Bot Activity Zone", 12, Color.white, FontStyle.Bold);

            const string unassigned = "(none)";
            var zones = manager.GetActivityZones().ToList();
            // Labels carry the ID suffix so duplicate zone names stay distinguishable.
            string LabelFor(BotActivityZone z) => $"{(string.IsNullOrEmpty(z.name) ? "zone" : z.name)} [{z.zoneId}]";

            var labels = new List<string> { unassigned };
            labels.AddRange(zones.Select(LabelFor));

            var assigned = manager.FindActivityZone(point.botActivityZoneId);
            string current = assigned != null ? LabelFor(assigned) : unassigned;
            bool missing = !string.IsNullOrEmpty(point.botActivityZoneId) && assigned == null;

            BuildDropdownField(_inspectorContent, "Zone", current, labels.ToArray(), (v) =>
            {
                var index = labels.IndexOf(v);
                point.botActivityZoneId = index > 0 ? zones[index - 1].zoneId : "";
                manager.IsDirty = true;
                RefreshInspector();
            });

            if (assigned != null)
                UIBuilder.CreateWrappedText(_inspectorContent, $"Assigned: {LabelFor(assigned)}{(assigned.enabled ? "" : " (zone disabled)")}", 11, new Color(0.8f, 0.95f, 0.4f, 1f));
            else if (missing)
                UIBuilder.CreateWrappedText(_inspectorContent, $"Warning: zone ID \"{point.botActivityZoneId}\" no longer exists. It was not reassigned; pick another zone or clear it.", 11, new Color(1f, 0.7f, 0.2f, 1f));
            else
                UIBuilder.CreateWrappedText(_inspectorContent, "No activity zone assigned.", 11, new Color(0.72f, 0.72f, 0.72f, 1f));

            if (!string.IsNullOrEmpty(point.botActivityZoneId))
            {
                UIBuilder.CreateButton(_inspectorContent, "Clear Zone", () =>
                {
                    point.botActivityZoneId = "";
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 100, 22);
            }
            UIBuilder.CreateWrappedText(_inspectorContent, "Editor data only. Bots are not yet limited to the zone.", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
        }

        private void BuildDeploymentSection(string currentId, Action<string> setId)
        {
            UIBuilder.CreateText(_inspectorContent, "NPC Deployment", 12, Color.white, FontStyle.Bold);
            if (manager.Data.deployments == null)
                manager.Data.deployments = new List<NpcDeployment>();
            var deployments = manager.Data.deployments;

            const string none = "None";
            string LabelFor(NpcDeployment d) => $"{(string.IsNullOrEmpty(d.name) ? "deployment" : d.name)} [{d.id}]";
            var labels = new List<string> { none };
            labels.AddRange(deployments.Select(LabelFor));
            var assigned = string.IsNullOrEmpty(currentId) ? null : deployments.FirstOrDefault(d => d.id == currentId);
            var current = string.IsNullOrEmpty(currentId) ? none : (assigned != null ? LabelFor(assigned) : currentId);
            BuildDropdownField(_inspectorContent, "Deployment", current, labels.ToArray(), (v) =>
            {
                var i = labels.IndexOf(v);
                setId(i > 0 ? deployments[i - 1].id : "");
                manager.IsDirty = true;
            });
            if (!string.IsNullOrEmpty(currentId) && assigned == null)
                UIBuilder.CreateWrappedText(_inspectorContent, $"Warning: deployment \"{currentId}\" does not exist on this map. The reference is kept, but this entry will not spawn until it is fixed.", 11, new Color(1f, 0.7f, 0.2f, 1f));
            else if (assigned != null)
                UIBuilder.CreateWrappedText(_inspectorContent, "Spawns only while the deployment is active (checked at raid start, together with the quest settings above).", 11, new Color(0.72f, 0.72f, 0.72f, 1f));

            for (int i = 0; i < deployments.Count; i++)
            {
                var d = deployments[i];
                UIBuilder.CreateText(_inspectorContent, $"Deployment [{d.id}]", 11, new Color(0.8f, 0.95f, 0.4f, 1f));
                BuildStringField(_inspectorContent, "Name", d.name ?? "", (v) => { d.name = v; manager.IsDirty = true; });
                BuildStringField(_inspectorContent, "Activation Quest ID", d.activationQuestId ?? "", (v) => { d.activationQuestId = v?.Trim() ?? ""; manager.IsDirty = true; });
                BuildStringField(_inspectorContent, "Withdraw Quest ID", d.deactivationQuestId ?? "", (v) => { d.deactivationQuestId = v?.Trim() ?? ""; manager.IsDirty = true; });
                var users = CountDeploymentUsers(d.id);
                UIBuilder.CreateButton(_inspectorContent, $"Delete ({users} entries use it)", () =>
                {
                    deployments.Remove(d);
                    manager.IsDirty = true;
                    RefreshInspector();
                }, 160, 22);
            }
            UIBuilder.CreateButton(_inspectorContent, "Add Deployment", () =>
            {
                deployments.Add(new NpcDeployment { id = "dep_" + Guid.NewGuid().ToString("N").Substring(0, 8), name = "New Deployment" });
                manager.IsDirty = true;
                RefreshInspector();
            }, 130, 22);
        }

        private int CountDeploymentUsers(string id)
        {
            var data = manager.Data;
            return (data.botSpawnPoints?.Count(p => p != null && p.deploymentId == id) ?? 0)
                 + (data.botSpawnZones?.Count(z => z != null && z.deploymentId == id) ?? 0);
        }

        private void BuildBotSpawnPoint(BotSpawnPoint point)
        {
            BuildBotActivityZoneAssignment(point);
            BuildStringField(_inspectorContent, "Bot Spawn Group ID", point.botSpawnGroupId ?? "", (v) =>
            {
                point.botSpawnGroupId = v?.Trim() ?? "";
                manager.IsDirty = true;
            });
            UIBuilder.CreateButton(_inspectorContent, "New Bot Spawn Group ID", () =>
            {
                point.botSpawnGroupId = Guid.NewGuid().ToString("N");
                manager.IsDirty = true;
                RefreshInspector();
            }, 180, 22);
            BuildReadOnlyLabel(_inspectorContent, "Group behavior", "Same ID = one spawn event at one eligible point.");

            BuildBotTypeSelector(point.preset, point.wildSpawnType, preset =>
            {
                point.preset = preset;
                BotSpawnPresetMapping.ApplyPreset(preset, point);
                manager.IsDirty = true;
            }, option =>
            {
                point.preset = BotSpawnPreset.BotGen;
                point.wildSpawnType = option.Id;
                ApplyNpcOption(option, side => point.side = side, category => point.category = category);
                manager.IsDirty = true;
            });
            BuildReadOnlyLabel(_inspectorContent, "Side", point.side.ToString());
            BuildReadOnlyLabel(_inspectorContent, "Category", point.category.ToString());
            if (GetEffectiveBotSpawnPreset(point.preset, point.wildSpawnType) != BotSpawnPreset.BotGen)
                BuildStringField(_inspectorContent, "Wild Type", point.wildSpawnType ?? "", (v) => { point.wildSpawnType = v?.Trim() ?? ""; manager.IsDirty = true; });
            if (GetEffectiveBotSpawnPreset(point.preset, point.wildSpawnType) == BotSpawnPreset.Boss && string.IsNullOrWhiteSpace(point.wildSpawnType))
                BuildReadOnlyLabel(_inspectorContent, "WARNING", "Generic Boss needs a Wild Type (e.g. bossBully) or pick Reshala");
            BuildToggleField(_inspectorContent, "Force Player Spawn (off = bot/any)", point.forcePlayerSpawn, (v) => { point.forcePlayerSpawn = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Spawn Mode", point.spawnMode ?? "Forced", new[] { "Forced", "Potential" }, (v) =>
            {
                point.spawnMode = v;
                manager.IsDirty = true;
                RefreshInspector();
            });
            BuildFloatField(_inspectorContent, "Spawn Chance", point.spawnChance, (v) => { point.spawnChance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Bot Spawn Chance", point.botSpawnChance, (v) => { point.botSpawnChance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Delay", point.delayToCanSpawnSec, (v) => { point.delayToCanSpawnSec = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Radius", point.radius, (v) => { point.radius = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Bot Zone Name", point.botZoneName ?? "", (v) => { point.botZoneName = v; manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Trigger activated", point.triggerActivated, (v) => { point.triggerActivated = v; manager.IsDirty = true; RefreshInspector(); });
            if (point.triggerActivated)
                BuildStringField(_inspectorContent, "Trigger Zone Name", point.triggerZoneName ?? "", (v) => { point.triggerZoneName = v; manager.IsDirty = true; });

            BuildStringList(_inspectorContent, "Random Types (per bot)", point.randomSpawnTypes, "assault");

            BuildToggleField(_inspectorContent, "Followers enabled", point.followersEnabled, (v) => { point.followersEnabled = v; manager.IsDirty = true; RefreshInspector(); });
            if (point.followersEnabled)
            {
                BuildIntField(_inspectorContent, "Min Followers", point.minFollowers, (v) => { point.minFollowers = Math.Max(0, v); point.maxFollowers = Math.Max(point.minFollowers, point.maxFollowers); manager.IsDirty = true; });
                BuildIntField(_inspectorContent, "Max Followers", point.maxFollowers, (v) => { point.maxFollowers = Math.Max(point.minFollowers, v); manager.IsDirty = true; });
            }

            BuildToggleField(_inspectorContent, "Quest only", point.questOnly, (v) => { point.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", point.questCompleted, (v) => { point.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (point.questOnly || point.questCompleted)
                BuildStringField(_inspectorContent, "Quest ID", point.questId ?? "", (v) => { point.questId = v; manager.IsDirty = true; });
            BuildDeploymentSection(point.deploymentId, v => point.deploymentId = v);

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Spawn", () => previews.SpawnBotSpawnPreview(point), 100, 22);
        }

        private void BuildBotSpawnZone(BotSpawnZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildBotTypeSelector(zone.preset, zone.wildSpawnType, preset =>
            {
                zone.preset = preset;
                BotSpawnPresetMapping.ApplyPreset(preset, zone);
                manager.IsDirty = true;
            }, option =>
            {
                zone.preset = BotSpawnPreset.BotGen;
                zone.wildSpawnType = option.Id;
                ApplyNpcOption(option, side => zone.side = side, category => zone.category = category);
                manager.IsDirty = true;
            });
            BuildReadOnlyLabel(_inspectorContent, "Side", zone.side.ToString());
            BuildReadOnlyLabel(_inspectorContent, "Category", zone.category.ToString());
            if (GetEffectiveBotSpawnPreset(zone.preset, zone.wildSpawnType) != BotSpawnPreset.BotGen)
                BuildStringField(_inspectorContent, "Wild Type", zone.wildSpawnType ?? "", (v) => { zone.wildSpawnType = v?.Trim() ?? ""; manager.IsDirty = true; });
            if (GetEffectiveBotSpawnPreset(zone.preset, zone.wildSpawnType) == BotSpawnPreset.Boss && string.IsNullOrWhiteSpace(zone.wildSpawnType))
                BuildReadOnlyLabel(_inspectorContent, "WARNING", "Generic Boss needs a Wild Type (e.g. bossBully) or pick Reshala");
            BuildDropdownField(_inspectorContent, "Spawn Mode", zone.spawnMode ?? "Forced", new[] { "Forced", "Potential" }, (v) =>
            {
                zone.spawnMode = v;
                manager.IsDirty = true;
                RefreshInspector();
            });
            BuildIntField(_inspectorContent, "Spawn Count", zone.spawnCount, (v) => { zone.spawnCount = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Spawn Chance", zone.spawnChance, (v) => { zone.spawnChance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Bot Spawn Chance", zone.botSpawnChance, (v) => { zone.botSpawnChance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Delay", zone.delayToCanSpawnSec, (v) => { zone.delayToCanSpawnSec = v; manager.IsDirty = true; });

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { zone.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)zone.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildFloatField(_inspectorContent, "Radius", zone.radius, (v) => { zone.radius = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", zone.scale.ToVector3(), (v) => { zone.scale = TransformData.FromVector3(v); manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Bot Zone Name", zone.botZoneName ?? "", (v) => { zone.botZoneName = v; manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Trigger activated", zone.triggerActivated, (v) => { zone.triggerActivated = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.triggerActivated)
                BuildStringField(_inspectorContent, "Trigger Zone Name", zone.triggerZoneName ?? "", (v) => { zone.triggerZoneName = v; manager.IsDirty = true; });

            BuildStringList(_inspectorContent, "Random Types (per bot)", zone.randomSpawnTypes, "assault");
            BuildBotSpawnGroupsList(zone);

            BuildToggleField(_inspectorContent, "Followers enabled (per boss)", zone.followersEnabled, (v) => { zone.followersEnabled = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.followersEnabled)
            {
                BuildIntField(_inspectorContent, "Min Followers", zone.minFollowers, (v) => { zone.minFollowers = Math.Max(0, v); zone.maxFollowers = Math.Max(zone.minFollowers, zone.maxFollowers); manager.IsDirty = true; });
                BuildIntField(_inspectorContent, "Max Followers", zone.maxFollowers, (v) => { zone.maxFollowers = Math.Max(zone.minFollowers, v); manager.IsDirty = true; });
            }

            BuildToggleField(_inspectorContent, "Quest only", zone.questOnly, (v) => { zone.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", zone.questCompleted, (v) => { zone.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.questOnly || zone.questCompleted)
                BuildStringField(_inspectorContent, "Quest ID", zone.questId ?? "", (v) => { zone.questId = v; manager.IsDirty = true; });
            BuildDeploymentSection(zone.deploymentId, v => zone.deploymentId = v);

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Spawns", () => previews.SpawnBotSpawnZonePreview(zone), 110, 22);
        }

        private void BuildPmcSpawnZone(PmcSpawnZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildReadOnlyLabel(_inspectorContent, "Side", zone.side.ToString());
            BuildReadOnlyLabel(_inspectorContent, "Category", zone.category.ToString());
            BuildToggleField(_inspectorContent, "Force Player Spawn (off = bot/any)", zone.forcePlayerSpawn, (v) => { zone.forcePlayerSpawn = v; manager.IsDirty = true; });
            BuildIntField(_inspectorContent, "Min Group Size", zone.minGroupSize, (v) => { zone.minGroupSize = Math.Max(0, v); manager.IsDirty = true; });
            BuildIntField(_inspectorContent, "Max Group Size", zone.maxGroupSize, (v) => { zone.maxGroupSize = Math.Max(zone.minGroupSize, v); manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Spawn Chance", zone.spawnChance, (v) => { zone.spawnChance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Delay", zone.delayToCanSpawnSec, (v) => { zone.delayToCanSpawnSec = v; manager.IsDirty = true; });

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { zone.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)zone.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildFloatField(_inspectorContent, "Radius", zone.radius, (v) => { zone.radius = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", zone.scale.ToVector3(), (v) => { zone.scale = TransformData.FromVector3(v); manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Bot Zone Name", zone.botZoneName ?? "", (v) => { zone.botZoneName = v; manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Quest only", zone.questOnly, (v) => { zone.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", zone.questCompleted, (v) => { zone.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.questOnly || zone.questCompleted)
                BuildStringField(_inspectorContent, "Quest ID", zone.questId ?? "", (v) => { zone.questId = v; manager.IsDirty = true; });

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Spawns", () => previews.SpawnPmcSpawnZonePreview(zone), 110, 22);
        }

        private void BuildLightZone(LightZone zone)
        {
            if (zone.color == null)
                zone.color = new LightColorData { r = 1f, g = 1f, b = 1f, a = 1f };

            BuildFloatField(_inspectorContent, "Color R", zone.color.r, (v) => { zone.color.r = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Color G", zone.color.g, (v) => { zone.color.g = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Color B", zone.color.b, (v) => { zone.color.b = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Color A", zone.color.a, (v) => { zone.color.a = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Intensity", zone.intensity, (v) => { zone.intensity = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Range", zone.range, (v) => { zone.range = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Type", zone.lightType ?? "Point", new[] { "Point", "Spot", "Directional" }, (v) => { zone.lightType = v; manager.IsDirty = true; });
            if ((zone.lightType ?? "Point") == "Spot")
                BuildFloatField(_inspectorContent, "Spot Angle", zone.spotAngle, (v) => { zone.spotAngle = v; manager.IsDirty = true; });
            BuildDropdownField(_inspectorContent, "Shadows", zone.shadows ?? "Soft", new[] { "None", "Hard", "Soft" }, (v) => { zone.shadows = v; manager.IsDirty = true; previews.UpdateForMarker(zone); });
            BuildFloatField(_inspectorContent, "Shadow Strength", zone.shadowStrength, (v) => { zone.shadowStrength = v; manager.IsDirty = true; previews.UpdateForMarker(zone); });
            BuildFloatField(_inspectorContent, "Shadow Bias", zone.shadowBias, (v) => { zone.shadowBias = v; manager.IsDirty = true; previews.UpdateForMarker(zone); });
            BuildFloatField(_inspectorContent, "Shadow Normal Bias", zone.shadowNormalBias, (v) => { zone.shadowNormalBias = v; manager.IsDirty = true; previews.UpdateForMarker(zone); });
            BuildFloatField(_inspectorContent, "Spawn Chance", zone.spawnChance, (v) => { zone.spawnChance = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Enabled", zone.enabled, (v) => { zone.enabled = v; manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Quest only", zone.questOnly, (v) => { zone.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", zone.questCompleted, (v) => { zone.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.questOnly || zone.questCompleted)
                BuildStringField(_inspectorContent, "Quest ID", zone.questId ?? "", (v) => { zone.questId = v; manager.IsDirty = true; });

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Light", () => previews.SpawnLightPreview(zone), 100, 22);
            UIBuilder.CreateButton(previewRow, "Force Shadows", () => RuntimeLightZoneSpawner.ForceShadowCastersInRange(zone.position.ToVector3(), zone.range), 110, 22);

            previews.SpawnLightPreview(zone);
        }

        private void BuildTriggerZone(TriggerZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };
            if (zone.lightZoneNames == null)
                zone.lightZoneNames = new List<string>();

            BuildStringField(_inspectorContent, "Trigger Zone Name", zone.name ?? "", (v) => { zone.name = v; manager.IsDirty = true; });

            var modeNames = new[] { "One Time", "Repeatable", "Once Per Player" };
            BuildDropdownField(_inspectorContent, "Mode", modeNames[(int)zone.triggerMode], modeNames, (v) =>
            {
                zone.triggerMode = (TriggerMode)Array.IndexOf(modeNames, v);
                manager.IsDirty = true;
                RefreshInspector();
            });

            BuildFloatField(_inspectorContent, "Trigger Chance", zone.triggerChance, (v) => { zone.triggerChance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Delay (sec)", zone.delaySeconds, (v) => { zone.delaySeconds = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Cooldown (sec)", zone.cooldownSeconds, (v) => { zone.cooldownSeconds = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Min Raid Time", zone.minRaidTime, (v) => { zone.minRaidTime = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Max Raid Time", zone.maxRaidTime, (v) => { zone.maxRaidTime = v; manager.IsDirty = true; });

            var sideNames = new[] { "Any", "PMC", "Scav" };
            BuildDropdownField(_inspectorContent, "Allowed Side", sideNames[(int)zone.allowedSide], sideNames, (v) =>
            {
                zone.allowedSide = (TriggerSide)Array.IndexOf(sideNames, v);
                manager.IsDirty = true;
            });

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { zone.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)zone.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildVector3Field(_inspectorContent, "Scale", zone.scale.ToVector3(), (v) => { zone.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            UIBuilder.CreateText(_inspectorContent, "Light Zone Actions", 11, Color.white, FontStyle.Bold);
            var actionNames = new[] { "Toggle", "Enable", "Disable" };
            BuildDropdownField(_inspectorContent, "Light Action", actionNames[(int)zone.lightAction], actionNames, (v) =>
            {
                zone.lightAction = (TriggerLightAction)Array.IndexOf(actionNames, v);
                manager.IsDirty = true;
            });
            BuildStringList(_inspectorContent, "Light Zone Names", zone.lightZoneNames, "light_zone");
        }

        private void BuildOcclusionRepairVolume(OcclusionRepairVolume orv)
        {
            if (orv.scale == null)
                orv.scale = new TransformData { x = 10f, y = 10f, z = 10f };

            BuildStringField(_inspectorContent, "Volume Name", orv.name ?? "", (v) => { orv.name = v; manager.IsDirty = true; });

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { orv.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)orv.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildVector3Field(_inspectorContent, "Scale", orv.scale.ToVector3(), (v) => { orv.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            BuildToggleField(_inspectorContent, "Disable Camera Occlusion", orv.disableCameraOcclusion, (v) => { orv.disableCameraOcclusion = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Manage Renderers", orv.manageRenderers, (v) => { orv.manageRenderers = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Renderer Radius", orv.rendererRadius, (v) => { orv.rendererRadius = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Max Visible Distance", orv.maxVisibleDistance, (v) => { orv.maxVisibleDistance = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Check Interval", orv.checkInterval, (v) => { orv.checkInterval = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Raycast Cull", orv.raycastCull, (v) => { orv.raycastCull = v; manager.IsDirty = true; });
            BuildStringField(_inspectorContent, "Raycast Mask", orv.raycastMask ?? "", (v) => { orv.raycastMask = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Disable Culling Objects", orv.disableCullingObjects, (v) => { orv.disableCullingObjects = v; manager.IsDirty = true; });
            BuildFloatField(_inspectorContent, "Culling Object Radius", orv.cullingObjectRadius, (v) => { orv.cullingObjectRadius = v; manager.IsDirty = true; });
        }

        private void BuildCutVolume(CutVolume cv)
        {
            if (cv.scale == null)
                cv.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildStringField(_inspectorContent, "Cut Volume Name", cv.name ?? "", (v) => { cv.name = v; manager.IsDirty = true; });
            BuildSourceObjectControls(cv);

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { cv.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)cv.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildVector3Field(_inspectorContent, "Scale", cv.scale.ToVector3(), (v) => { cv.scale = TransformData.FromVector3(v); manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Invert (keep inside)", cv.invert, (v) => { cv.invert = v; manager.IsDirty = true; });
        }

        private void BuildBlocker(Blocker blocker)
        {
            if (blocker.scale == null)
                blocker.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildStringField(_inspectorContent, "Blocker Name", blocker.name ?? "", (v) => { blocker.name = v; manager.IsDirty = true; });

            var shapeRow = UIBuilder.CreatePanel("ShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { blocker.shape = (ZoneShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)blocker.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildVector3Field(_inspectorContent, "Scale", blocker.scale.ToVector3(), (v) => { blocker.scale = TransformData.FromVector3(v); manager.IsDirty = true; });
        }

        private void BuildNavMeshArea(NavMeshArea nma)
        {
            if (nma.scale == null)
                nma.scale = new TransformData { x = 2f, y = 0.1f, z = 2f };

            var shapeRow = UIBuilder.CreatePanel("NavMeshShapeRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(shapeRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(shapeRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(shapeRow, "Shape", 11, 44, 20);
            var shapes = new[] { "Box", "Capsule" };
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(shapeRow, shapes[idx], () => { nma.shape = (NavMeshAreaShape)idx; manager.IsDirty = true; RefreshInspector(); }, 52, 20, 10);
                if ((int)nma.shape == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }

            BuildVector3Field(_inspectorContent, "Scale", nma.scale.ToVector3(), (v) => { nma.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            var areaRow = UIBuilder.CreatePanel("NavMeshAreaRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(areaRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(areaRow, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(areaRow, "Area Type", 11, 60, 20);
            var areaTypes = new[] { "Walkable", "Not Walkable" };
            for (int i = 0; i < areaTypes.Length; i++)
            {
                int idx = i;
                var btn = UIBuilder.CreateButton(areaRow, areaTypes[idx], () => { nma.area = idx; nma.walkable = (idx == 0); manager.IsDirty = true; RefreshInspector(); }, 70, 20, 10);
                if (nma.area == idx)
                    btn.GetComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 1f);
            }
        }

        public static readonly (string id, string name)[] LootContainerTemplates = new (string, string)[]
        {
            ("566966cd4bdc2d0c4c8b4578", "Box full of junk"),
            ("5d6d2bb386f774785b07a77a", "Buried barrel cache"),
            ("578f879c24597735401e6bc6", "Cash register"),
            ("5ad74cf586f774391278f6f0", "Cash register TAR2-2"),
            ("5d07b91b86f7745a077a9432", "Common fund stash"),
            ("5909e4b686f7747f5b744fa4", "Dead Scav"),
            ("658420d8085fea07e674cdb6", "Civilian body"),
            ("578f87b7245977356274f2cd", "Drawer"),
            ("578f87a3245977356274f2cb", "Duffle bag"),
            ("5909d36d86f774660f0bb900", "Grenade box"),
            ("5d6d2b5486f774785c2ba8ea", "Ground cache"),
            ("578f8778245977358849a9b5", "Jacket"),
            ("5914944186f774189e5e76c2", "Jacket 2"),
            ("5937ef2b86f77408a47244b3", "Jacket 3"),
            ("59387ac686f77401442ddd61", "Jacket 4"),
            ("5909d24f86f77466f56e6855", "Medbag SMU06"),
            ("5909d4c186f7746ad34e805a", "Medcase"),
            ("5d6fe50986f77449d97f7463", "Medical supply crate"),
            ("59139c2186f77411564f8e42", "PC block"),
            ("5c052cea86f7746b2101e8d8", "Plastic suitcase"),
            ("5d6fd13186f77424ad2a8c69", "Ration supply crate"),
            ("578f8782245977354405a1e3", "Safe"),
            ("5d6fd45b86f774317075ed43", "Technical supply crate"),
            ("5909d50c86f774659e6aaebe", "Toolbox"),
            ("5909d5ef86f77467974efbd8", "Weapon box"),
            ("5909d76c86f77471e53d2adf", "Weapon box 2"),
            ("5909d7cf86f77470ee57d75a", "Weapon box 3"),
            ("5909d89086f77472591234a0", "Weapon box 4"),
            ("5909d45286f77465a8136dc6", "Wooden ammo box"),
            ("578f87ad245977356274f2cc", "Wooden crate")
        };

        public static readonly (string id, string name)[] WeaponTemplates = new (string, string)[]
        {
            ("5cdeb229d7f00c000e7ce174", "NSV Utes"),
            ("5d52cc5ba4b9367408500062", "AGS-30")
        };

        private void BuildContainerTemplateDropdown(InteractiveObject obj)
        {
            var options = LootContainerTemplates.Select(t => t.name).ToArray();
            var current = LootContainerTemplates.FirstOrDefault(t => t.id == obj.containerTemplate).name;
            if (string.IsNullOrEmpty(current))
                current = obj.containerTemplate;

            BuildDropdownField(_inspectorContent, "Container Template", current, options, (v) =>
            {
                var selected = LootContainerTemplates.FirstOrDefault(t => t.name == v);
                obj.containerTemplate = selected.id;
                manager.IsDirty = true;
            });
        }

        private void BuildWeaponTemplateDropdown(InteractiveObject obj)
        {
            var options = WeaponTemplates.Select(t => t.name).ToArray();
            var current = WeaponTemplates.FirstOrDefault(t => t.id == obj.weaponTemplate).name;
            if (string.IsNullOrEmpty(current))
                current = obj.weaponTemplate;

            BuildDropdownField(_inspectorContent, "Weapon Template", current, options, (v) =>
            {
                var selected = WeaponTemplates.FirstOrDefault(t => t.name == v);
                obj.weaponTemplate = selected.id;
                manager.IsDirty = true;
            });
        }

        private void BuildBotTypeSelector(BotSpawnPreset preset, string wildSpawnType, Action<BotSpawnPreset> onPresetChanged, Action<NpcOption> onBotGenSelected)
        {
            var effectivePreset = GetEffectiveBotSpawnPreset(preset, wildSpawnType);
            var presetNames = BotSpawnPresetMapping.PresetNames.OrderBy(kvp => (int)kvp.Key).Select(kvp => kvp.Value).ToArray();
            var currentName = BotSpawnPresetMapping.PresetNames.ContainsKey(effectivePreset)
                ? BotSpawnPresetMapping.PresetNames[effectivePreset]
                : effectivePreset.ToString();
            BuildDropdownField(_inspectorContent, "Bot Type", currentName, presetNames, value =>
            {
                var selected = BotSpawnPresetMapping.PresetNames.FirstOrDefault(kvp => kvp.Value == value).Key;
                onPresetChanged?.Invoke(selected);
                RefreshInspector();
            });

            if (effectivePreset == BotSpawnPreset.BotGen)
                BuildBotGenNpcSelector(wildSpawnType, onBotGenSelected);
        }

        private static BotSpawnPreset GetEffectiveBotSpawnPreset(BotSpawnPreset preset, string wildSpawnType)
        {
            return BotSpawnPresetMapping.ParsePreset(wildSpawnType) == BotSpawnPreset.BotGen
                ? BotSpawnPreset.BotGen
                : preset;
        }

        private void BuildBotGenNpcSelector(string storedRole, Action<NpcOption> onPick)
        {
            var provider = (storedRole != null ? NpcProviderRegistry.FindOwner(storedRole) as BotGenNpcProvider : null)
                ?? NpcProviderRegistry.All.OfType<BotGenNpcProvider>().FirstOrDefault();
            if (provider == null)
            {
                UIBuilder.CreateWrappedText(_inspectorContent,
                    "BotGen provider is unavailable. Vanilla NPCs remain available; any stored BotGen role is preserved and will not be replaced.",
                    11, new Color(1f, 0.7f, 0.3f, 1f));
                if (!string.IsNullOrWhiteSpace(storedRole))
                    BuildReadOnlyLabel(_inspectorContent, "Stored BotGen role", storedRole);
                return;
            }

            UIBuilder.CreateButton(_inspectorContent, provider.IsLoading ? "Refreshing BotGen..." : "Refresh BotGen Registry", () =>
            {
                if (!provider.IsLoading)
                {
                    StartCoroutine(NpcProviderRegistry.RefreshAll(_ => RequestInspectorRefresh()));
                    RequestInspectorRefresh();
                }
            }, 170, 22);

            if (provider.IsLoading || provider.IsStale || !provider.Detected)
            {
                var detail = provider.StatusNote ?? "no successful registry response";
                UIBuilder.CreateWrappedText(_inspectorContent,
                    "BotGen registry is " + (provider.IsLoading ? "refreshing" : provider.IsStale ? "stale" : "unavailable") + " (" + detail +
                    "). Cached roles are not selectable or spawnable until a refresh succeeds. Saved role IDs are preserved.",
                    11, new Color(1f, 0.7f, 0.3f, 1f));
                if (!string.IsNullOrWhiteSpace(storedRole))
                {
                    var known = provider.FindKnown(storedRole);
                    var savedLabel = known == null ? storedRole : known.Label + " (" + known.Id + ")";
                    BuildReadOnlyLabel(_inspectorContent, "Unavailable BotGen role", savedLabel);
                }
                return;
            }

            var options = provider.Options.OrderBy(o => o.Kind).ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
            var optionLabels = options.Select(o =>
                (o.Kind == NpcKind.Follower ? "[BotGen Follower] " : "[BotGen NPC] ") +
                (o.Label ?? o.Id) + " (" + o.Id + ")").ToArray();
            var selected = provider.Find(storedRole);
            var current = selected == null
                ? (string.IsNullOrWhiteSpace(storedRole)
                    ? (options.Count == 0 ? "(no active BotGen NPCs)" : "(select BotGen NPC)")
                    : "[unavailable] " + (provider.FindKnown(storedRole)?.Label ?? storedRole) + " (" + storedRole + ")")
                : optionLabels[options.IndexOf(selected)];

            if (options.Count > 0)
            {
                BuildDropdownField(_inspectorContent, "BotGen NPC", current, optionLabels, value =>
                {
                    var index = Array.IndexOf(optionLabels, value);
                    if (index >= 0)
                        onPick?.Invoke(options[index]);
                    RefreshInspector();
                });
            }
            else
                UIBuilder.CreateWrappedText(_inspectorContent,
                    "BotGen successfully reported no active NPCs. Create or activate an NPC in BotGen, then refresh this registry.",
                    11, new Color(0.72f, 0.72f, 0.72f, 1f));

            if (!string.IsNullOrWhiteSpace(storedRole))
            {
                string reason;
                if (!provider.IsAvailable(storedRole, out reason))
                    UIBuilder.CreateWrappedText(_inspectorContent, "Stored role will not spawn: " + reason + ". Its exact identifier is preserved.", 11, new Color(1f, 0.7f, 0.3f, 1f));
            }
        }

        private static void ApplyNpcOption(NpcOption option, Action<BotSpawnSide> setSide, Action<BotSpawnCategory> setCategory)
        {
            if (string.Equals(option?.Category, "pmc", StringComparison.OrdinalIgnoreCase))
            {
                setSide(BotSpawnSide.Pmc);
                setCategory(BotSpawnCategory.BotPmc);
                return;
            }
            setSide(BotSpawnSide.Savage);
            setCategory(string.Equals(option?.Category, "boss", StringComparison.OrdinalIgnoreCase)
                ? BotSpawnCategory.Boss
                : BotSpawnCategory.Bot);
        }

        private void BuildDropdownField(RectTransform parent, string label, string value, string[] options, UnityAction<string> onChanged)
        {
            var row = UIBuilder.CreatePanel("DropdownField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            int dropdownLabelWidth = FieldLabelWidth(label);
            int dropdownWidth = 94;
            if (!string.IsNullOrEmpty(label))
                dropdownWidth = dropdownLabelWidth + 100;
            UIBuilder.AddLayoutElement(row, null, 24, dropdownWidth, 24, null, 0);
            if (!string.IsNullOrEmpty(label))
                UIBuilder.CreateLabel(row, label, 11, dropdownLabelWidth, 24);
            var display = value ?? "";
            if (string.IsNullOrEmpty(display))
                display = "(none)";
            var currentBtn = UIBuilder.CreateButton(row, display, () => { }, 90, 22, 10);
            currentBtn.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.18f, 1f);

            var trigger = currentBtn.gameObject.GetComponent<EventTrigger>() ?? currentBtn.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener((data) =>
            {
                var ped = data as PointerEventData;
                if (ped == null) return;
                ShowContextMenu(ped.position, options.Select(o =>
                {
                    var opt = o;
                    return (string.IsNullOrEmpty(opt) ? "(none)" : opt, (Action)(() =>
                    {
                        onChanged?.Invoke(opt);
                        RefreshInspector();
                    }));
                }).ToList());
            });
            trigger.triggers.Add(entry);
        }

        private void BuildItemsList(List<LootItem> items, bool showRotation, System.Action<int> onPreview)
        {
            if (items == null)
                return;

            UIBuilder.CreateText(_inspectorContent, "Items (chance does not need to add to 100):", 11, Color.white, FontStyle.Bold);

            const int pageSize = 50;
            int start = 0;
            int end = items.Count;
            int pageCount = 1;

            if (items.Count > pageSize)
            {
                if (_itemsListPageTarget != items)
                {
                    _itemsListPageTarget = items;
                    _itemsListPage = 0;
                }
                pageCount = (items.Count + pageSize - 1) / pageSize;
                _itemsListPage = Mathf.Clamp(_itemsListPage, 0, pageCount - 1);
                start = _itemsListPage * pageSize;
                end = Mathf.Min(start + pageSize, items.Count);

                var pageRow = UIBuilder.CreatePanel("ItemsPageRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(pageRow, 4, 2, false, false);
                UIBuilder.AddLayoutElement(pageRow, null, 22, null, 22, null, 0);
                UIBuilder.CreateButton(pageRow, "<", () => { _itemsListPage--; RefreshInspector(); }, 30, 22, 10);
                UIBuilder.CreateButton(pageRow, ">", () => { _itemsListPage++; RefreshInspector(); }, 30, 22, 10);
                UIBuilder.CreateText(_inspectorContent, string.Format(Locale.Get("Page {0} / {1} ({2} items)"), _itemsListPage + 1, pageCount, items.Count), 11, Color.white);
            }

            for (int i = start; i < end; i++)
            {
                var item = items[i];
                if (item.rotation == null)
                    item.rotation = new TransformData();

                int idx = i;
                var row = UIBuilder.CreatePanel("ItemRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);

                UIBuilder.CreateLabel(row, "Tpl", 11, 26, 20);
                var tplField = BuildInputFieldInline(row, item.template ?? "", (v) => { item.template = v; manager.IsDirty = true; }, 68, 20);
                UIBuilder.CreateButton(row, "Item", () => ShowItemPicker(item), 32, 20, 10);
                tplField.onEndEdit.AddListener(_ =>
                {
                    if (ItemNeedsStackFields(item.template))
                    {
                        var stackMax = ItemNameResolver.GetStackMaxSize(item.template);
                        if (item.maxCount <= 1)
                        {
                            item.minCount = 1;
                            item.maxCount = stackMax;
                        }
                    }
                    RequestInspectorRefresh();
                });
                UIBuilder.CreateLabel(row, "%", 11, 18, 20);
                BuildInputFieldInline(row, item.chance.ToString("F1", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) item.chance = r; manager.IsDirty = true; }, 40, 20);
                if (onPreview != null)
                    UIBuilder.CreateButton(row, "Prev", () => onPreview(idx), 36, 20, 10);
                UIBuilder.CreateButton(row, "-", () => { items.RemoveAt(idx); manager.IsDirty = true; RefreshInspector(); }, 24, 20, 10);

                var name = GetItemName(item.template);
                if (!string.IsNullOrEmpty(name))
                    UIBuilder.CreateWrappedText(_inspectorContent, $"  {name}", 11, new Color(0.6f, 0.6f, 0.6f, 1f));

                if (item.children != null && item.children.Count > 0)
                {
                    var labels = item.children.Take(5).Select(c => GetItemName(c.template) ?? c.template ?? "?");
                    var childText = $"  +{item.children.Count} nested: {string.Join(", ", labels)}";
                    if (item.children.Count > 5)
                        childText += ", ...";
                    UIBuilder.CreateWrappedText(_inspectorContent, childText, 10, new Color(0.5f, 0.5f, 0.5f, 1f));
                }

                if (ItemNeedsStackFields(item.template))
                {
                    var countRow = UIBuilder.CreatePanel("ItemCountRow", _inspectorContent, new Color(0, 0, 0, 0));
                    UIBuilder.AddHorizontalLayout(countRow, 2, 2, false, false);
                    UIBuilder.AddLayoutElement(countRow, null, 22, null, 22, null, 0);
                    UIBuilder.CreateLabel(countRow, "Min", 11, 26, 20);
                    BuildInputFieldInline(countRow, item.minCount.ToString(), (v) => { if (int.TryParse(v, out var r)) { item.minCount = Math.Max(r, 1); manager.IsDirty = true; } }, 36, 20);
                    UIBuilder.CreateLabel(countRow, "Max", 11, 30, 20);
                    BuildInputFieldInline(countRow, item.maxCount.ToString(), (v) => { if (int.TryParse(v, out var r)) { item.maxCount = Math.Max(r, 1); manager.IsDirty = true; } }, 40, 20);
                }

                var questOnlyRow = UIBuilder.CreatePanel("QuestOnlyRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(questOnlyRow, 2, 2, false, false);
                UIBuilder.AddLayoutElement(questOnlyRow, null, 22, null, 22, null, 0);
                UIBuilder.CreateToggle(questOnlyRow, "Quest only", item.questOnly, (v) => { item.questOnly = v; manager.IsDirty = true; RefreshInspector(); }, 18);

                var questCompletedRow = UIBuilder.CreatePanel("QuestCompletedRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(questCompletedRow, 2, 2, false, false);
                UIBuilder.AddLayoutElement(questCompletedRow, null, 22, null, 22, null, 0);
                UIBuilder.CreateToggle(questCompletedRow, "Quest completed", item.questCompleted, (v) => { item.questCompleted = v; manager.IsDirty = true; RefreshInspector(); }, 18);

                if (item.questOnly || item.questCompleted)
                {
                    var questIdRow = UIBuilder.CreatePanel("QuestIdRow", _inspectorContent, new Color(0, 0, 0, 0));
                    UIBuilder.AddHorizontalLayout(questIdRow, 2, 2, false, false);
                    UIBuilder.AddLayoutElement(questIdRow, null, 22, null, 22, null, 0);
                    UIBuilder.CreateLabel(questIdRow, "Quest ID", 11, 48, 20);
                    BuildInputFieldInline(questIdRow, item.questId ?? "", (v) => { item.questId = v; manager.IsDirty = true; }, 120, 20);
                }

                if (showRotation)
                {
                    var rotRow = UIBuilder.CreatePanel("RotRow", _inspectorContent, new Color(0, 0, 0, 0));
                    UIBuilder.AddHorizontalLayout(rotRow, 2, 2, false, false);
                    UIBuilder.AddLayoutElement(rotRow, null, 22, null, 22, null, 0);
                    UIBuilder.CreateToggle(rotRow, "Random Rotation", item.randomRotation, (v) => { item.randomRotation = v; manager.IsDirty = true; RefreshInspector(); }, 18);
                    if (!item.randomRotation)
                        BuildVector3FieldInline(rotRow, "Rot", item.rotation.ToVector3(), (v) => { item.rotation = TransformData.FromVector3(v); manager.IsDirty = true; });

                    var yRow = UIBuilder.CreatePanel("YOffsetRow", _inspectorContent, new Color(0, 0, 0, 0));
                    UIBuilder.AddHorizontalLayout(yRow, 2, 2, false, false);
                    UIBuilder.AddLayoutElement(yRow, null, 22, null, 22, null, 0);
                    UIBuilder.CreateLabel(yRow, "Y Offset", 11, 52, 20);
                    BuildInputFieldInline(yRow, item.yOffset.ToString("F2", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) { item.yOffset = r; manager.IsDirty = true; } }, 52, 20);
                }

                BuildChildEditor(item, 1);
            }

            UIBuilder.CreateButton(_inspectorContent, "Add Item", () => { items.Add(new LootItem()); manager.IsDirty = true; RefreshInspector(); }, 80, 22);
        }

        private void BuildChildEditor(LootItem parent, int depth)
        {
            if (parent.children == null)
                parent.children = new List<LootItem>();

            if (parent.children.Count > 0)
            {
                UIBuilder.CreateText(_inspectorContent, $"  Children ({parent.children.Count}):", 11, new Color(0.7f, 0.7f, 0.7f, 1f));
            }

            for (int i = 0; i < parent.children.Count; i++)
            {
                var child = parent.children[i];
                if (child == null)
                    continue;

                int idx = i;
                var row = UIBuilder.CreatePanel("ChildRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);

                var spacer = new GameObject("ChildSpacer", typeof(RectTransform));
                spacer.transform.SetParent(row.transform, false);
                UIBuilder.AddLayoutElement(spacer, depth * 12, 20, depth * 12, 20, 0, 0);

                UIBuilder.CreateLabel(row, "Tpl", 11, 26, 20);
                BuildInputFieldInline(row, child.template ?? "", (v) => { child.template = v; manager.IsDirty = true; }, 80, 20);

                UIBuilder.CreateLabel(row, "Slot", 11, 30, 20);
                BuildInputFieldInline(row, child.slotId ?? "", (v) => { child.slotId = v; manager.IsDirty = true; }, 70, 20);

                UIBuilder.CreateButton(row, "X", () => { parent.children.RemoveAt(idx); manager.IsDirty = true; RefreshInspector(); }, 22, 20, 10);

                BuildChildEditor(child, depth + 1);
            }

            var addRow = UIBuilder.CreatePanel("AddChildRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(addRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(addRow, null, 22, null, 22, null, 1);
            var addSpacer = new GameObject("AddChildSpacer", typeof(RectTransform));
            addSpacer.transform.SetParent(addRow.transform, false);
            UIBuilder.AddLayoutElement(addSpacer, depth * 12, 20, depth * 12, 20, 0, 0);
            UIBuilder.CreateButton(addRow, "+ Child", () => { parent.children.Add(new LootItem()); manager.IsDirty = true; RefreshInspector(); }, 60, 22, 10);
        }

        private void RefreshPrefabs()
        {
            if (_prefabsContent == null)
                return;
            ClearChildren(_prefabsContent);
            var names = PrefabStorage.ListPrefabNames();
            if (names.Count == 0)
            {
                UIBuilder.CreateText(_prefabsContent, "No prefabs saved yet.", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
                return;
            }
            foreach (var prefabName in names)
            {
                var capturedName = prefabName;
                var row = UIBuilder.CreatePanel("PrefabRow", _prefabsContent, new Color(0.17f, 0.17f, 0.17f, 1f));
                UIBuilder.AddHorizontalLayout(row, 2, 2, true, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);

                var placeBtn = UIBuilder.CreateButton(row, capturedName, () => controller.PlacePrefab(capturedName), 0, 20, 11);
                UIBuilder.AddLayoutElement(placeBtn.gameObject, null, 20, null, 20, 1, 0);

                UIBuilder.CreateButton(row, "Ren", () =>
                {
                    var renRow = UIBuilder.CreatePanel("RenameRow", _prefabsContent, new Color(0.2f, 0.2f, 0.2f, 1f));
                    UIBuilder.AddHorizontalLayout(renRow, 2, 2, true, true);
                    UIBuilder.AddLayoutElement(renRow, null, 22, null, 22, null, 0);
                    renRow.transform.SetSiblingIndex(row.transform.GetSiblingIndex() + 1);
                    var input = UIBuilder.CreateInputField(renRow, "New name", capturedName, null, 0, 20);
                    UIBuilder.AddLayoutElement(input.gameObject, null, 20, null, 20, 1, 0);
                    UIBuilder.CreateButton(renRow, "OK", () =>
                    {
                        var newName = input.text.Trim();
                        if (!string.IsNullOrEmpty(newName) && newName != capturedName)
                        {
                            var data = PrefabStorage.Load(capturedName);
                            if (data != null) { data.name = newName; PrefabStorage.Save(data); System.IO.File.Delete(PrefabStorage.PrefabPath(capturedName)); }
                        }
                        RequestRefresh();
                    }, 36, 20);
                    UIBuilder.CreateButton(renRow, "✕", () => { Destroy(renRow.gameObject); }, 22, 20);
                }, 30, 20, 10);

                UIBuilder.CreateButton(row, "Del", () =>
                {
                    System.IO.File.Delete(PrefabStorage.PrefabPath(capturedName));
                    RequestRefresh();
                }, 30, 20, 10);
            }
        }

        private void RefreshGroups()
        {
            if (_groupsContent == null)
                return;
            ClearChildren(_groupsContent);
            var groups = manager.GetGroups().ToList();
            if (!groups.Any(group => string.Equals(group, _selectedGroupName, StringComparison.OrdinalIgnoreCase)))
                _selectedGroupName = null;
            if (groups.Count == 0)
            {
                UIBuilder.CreateText(_groupsContent, "No groups.", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
                return;
            }
            var groupCounts = manager.GetAllMarkers()
                .Where(marker => !string.IsNullOrWhiteSpace(marker.group))
                .GroupBy(marker => marker.group, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                var capturedGroup = group;
                bool selected = string.Equals(capturedGroup, _selectedGroupName, StringComparison.OrdinalIgnoreCase);
                var row = UIBuilder.CreatePanel("GroupRow", _groupsContent,
                    selected ? new Color(0.2f, 0.3f, 0.45f, 0.8f) : new Color(0.15f, 0.15f, 0.15f, 0.25f));
                UIBuilder.AddHorizontalLayout(row, 3, 2, true, false);
                UIBuilder.AddLayoutElement(row, null, 18, null, 18, null, 0);
                var rowButton = row.gameObject.AddComponent<Button>();
                rowButton.targetGraphic = row.GetComponent<Image>();
                rowButton.transition = Selectable.Transition.None;
                rowButton.onClick.AddListener(() =>
                {
                    _selectedGroupName = capturedGroup;
                    _newGroupName = capturedGroup;
                    if (_groupInput != null)
                        _groupInput.text = capturedGroup;
                    RefreshGroups();
                });

                var label = UIBuilder.CreateLabel(row, capturedGroup, 11, 0, 16);
                label.color = selected ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f);
                var count = UIBuilder.CreateLabel(row, groupCounts[capturedGroup].ToString(), 10, 36, 16);
                count.alignment = TextAnchor.MiddleRight;
                count.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            }
        }

        private string GenerateHexId()
        {
            const string chars = "0123456789abcdef";
            var sb = new System.Text.StringBuilder(24);
            for (int i = 0; i < 24; i++)
                sb.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
            return sb.ToString();
        }

        // Label column for inspector rows: 60px minimum, grows (up to 110px) for longer labels so they are not clipped.
        private static readonly Dictionary<string, int> _labelWidthCache = new Dictionary<string, int>();

        private static int FieldLabelWidth(string label, int fontSize = 11)
        {
            if (string.IsNullOrEmpty(label))
                return 60;
            var key = fontSize + "|" + label;
            if (_labelWidthCache.TryGetValue(key, out var cached))
                return cached;

            var probe = new GameObject("LabelProbe", typeof(RectTransform)).AddComponent<Text>();
            int result;
            try
            {
                probe.font = UIBuilder.GetFont();
                probe.fontSize = fontSize;
                var width = UIBuilder.MeasureTextWidth(probe, Locale.Get(label));
                result = Mathf.Clamp(Mathf.CeilToInt(width) + 6, 60, 110);
            }
            finally
            {
                UnityEngine.Object.Destroy(probe.gameObject);
            }
            _labelWidthCache[key] = result;
            return result;
        }

        private void BuildStringField(RectTransform parent, string label, string value, UnityAction<string> onChanged)
        {
            var row = UIBuilder.CreatePanel("StringField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, true, true);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, FieldLabelWidth(label), 22);
            var inp = UIBuilder.CreateInputField(row, label, value, (v) => onChanged?.Invoke(v), 0, 22);
            UIBuilder.AddLayoutElement(inp.gameObject, null, null, null, null, 1, null);
            AddInputFieldContextMenu(inp);
        }

        private void BuildStringFieldWithButton(RectTransform parent, string label, string value, UnityAction<string> onChanged, string buttonLabel, UnityAction onButtonClick)
        {
            var row = UIBuilder.CreatePanel("StringFieldWithButton", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, true, true);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, FieldLabelWidth(label), 22);
            var inp = UIBuilder.CreateInputField(row, label, value, (v) => onChanged?.Invoke(v), 0, 22);
            UIBuilder.AddLayoutElement(inp.gameObject, null, null, null, null, 1, null);
            AddInputFieldContextMenu(inp);
            UIBuilder.CreateButton(row, buttonLabel, onButtonClick, 40, 22);
        }

        private InputField BuildFloatField(RectTransform parent, string label, float value, UnityAction<float> onChanged)
        {
            var row = UIBuilder.CreatePanel("FloatField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, FieldLabelWidth(label), 22);
            var inp = UIBuilder.CreateInputField(row, label, value.ToString("F3", CultureInfo.InvariantCulture), (v) =>
            {
                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                    onChanged?.Invoke(r);
            }, 80, 22);
            AddInputFieldContextMenu(inp);
            return inp;
        }

        private void BuildIntField(RectTransform parent, string label, int value, UnityAction<int> onChanged)
        {
            var row = UIBuilder.CreatePanel("IntField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, FieldLabelWidth(label), 22);
            var inp = UIBuilder.CreateInputField(row, label, value.ToString(CultureInfo.InvariantCulture), (v) =>
            {
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                    onChanged?.Invoke(r);
            }, 80, 22);
            AddInputFieldContextMenu(inp);
        }

        private void BuildVector3Field(RectTransform parent, string label, Vector3 value, UnityAction<Vector3> onChanged)
        {
            var row = UIBuilder.CreatePanel("Vector3Field", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, 60, 22);
            BuildInputFieldInline(row, value.x.ToString("F3", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) onChanged?.Invoke(new Vector3(r, value.y, value.z)); }, 60, 22);
            BuildInputFieldInline(row, value.y.ToString("F3", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) onChanged?.Invoke(new Vector3(value.x, r, value.z)); }, 60, 22);
            BuildInputFieldInline(row, value.z.ToString("F3", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) onChanged?.Invoke(new Vector3(value.x, value.y, r)); }, 60, 22);
        }

        private void BuildVector3FieldInline(RectTransform parent, string label, Vector3 value, UnityAction<Vector3> onChanged)
        {
            var row = UIBuilder.CreatePanel("Vector3FieldInline", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 20, null, 20, null, 0);
            UIBuilder.CreateLabel(row, label, 11, 30, 20);
            BuildInputFieldInline(row, value.x.ToString("F3", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) onChanged?.Invoke(new Vector3(r, value.y, value.z)); }, 52, 20);
            BuildInputFieldInline(row, value.y.ToString("F3", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) onChanged?.Invoke(new Vector3(value.x, r, value.z)); }, 52, 20);
            BuildInputFieldInline(row, value.z.ToString("F3", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) onChanged?.Invoke(new Vector3(value.x, value.y, r)); }, 52, 20);
        }

        private InputField BuildInputFieldInline(RectTransform parent, string value, UnityAction<string> onChanged, int width, int height)
        {
            var input = UIBuilder.CreateInputField(parent, "", value, (v) => onChanged?.Invoke(v), width, height);
            AddInputFieldContextMenu(input);
            return input;
        }

        private void AddInputFieldContextMenu(InputField input)
        {
            if (input == null) return;
            var trigger = input.gameObject.GetComponent<EventTrigger>() ?? input.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener((data) =>
            {
                var ped = data as PointerEventData;
                if (ped != null && ped.button == PointerEventData.InputButton.Right)
                {
                    ShowContextMenu(ped.position, new List<(string, Action)>
                    {
                        ("Copy", () => { _fieldClipboard = input.text; GUIUtility.systemCopyBuffer = input.text; }),
                        ("Paste", () =>
                        {
                            var text = !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer) ? GUIUtility.systemCopyBuffer : _fieldClipboard;
                            input.text = text;
                            input.onValueChanged.Invoke(text);
                        })
                    });
                }
            });
            trigger.triggers.Add(entry);
        }

        private void BuildToggleField(RectTransform parent, string label, bool value, UnityAction<bool> onChanged)
        {
            UIBuilder.CreateToggle(parent, label, value, (v) => onChanged?.Invoke(v), 20);
        }

        private void BuildStringList(RectTransform parent, string title, List<string> list, string placeholder = "wildSpawnType")
        {
            UIBuilder.CreateText(parent, title, 11, Color.white, FontStyle.Bold);
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var row = UIBuilder.CreatePanel("StringListRow", parent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
                var input = BuildInputFieldInline(row, list[idx], (v) => { list[idx] = v; manager.IsDirty = true; }, 140, 20);
                input.onEndEdit.AddListener(_ => RequestInspectorRefresh());
                UIBuilder.CreateButton(row, "-", () => { list.RemoveAt(idx); manager.IsDirty = true; RefreshInspector(); }, 24, 20, 10);
            }
            UIBuilder.CreateButton(parent, "Add", () => { list.Add(placeholder); manager.IsDirty = true; RefreshInspector(); }, 80, 22);
        }

        private void BuildBotSpawnGroupsList(BotSpawnZone zone)
        {
            if (zone.randomGroups == null)
                zone.randomGroups = new List<BotSpawnGroup>();

            UIBuilder.CreateText(_inspectorContent, "Random Groups (one picked per raid):", 11, Color.white, FontStyle.Bold);

            for (int i = 0; i < zone.randomGroups.Count; i++)
            {
                int idx = i;
                var group = zone.randomGroups[idx];

                var row = UIBuilder.CreatePanel("GroupRow", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
                UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
                UIBuilder.CreateLabel(row, $"Group {idx + 1}", 11, 60, 22);
                UIBuilder.CreateButton(row, "-", () => { zone.randomGroups.RemoveAt(idx); manager.IsDirty = true; RefreshInspector(); }, 24, 20, 10);

                BuildIntField(_inspectorContent, "Count", group.spawnCount, (v) => { group.spawnCount = v; manager.IsDirty = true; });
                BuildBotTypeSelector(group.preset, group.wildSpawnType, preset =>
                {
                    group.preset = preset;
                    BotSpawnPresetMapping.ApplyPreset(preset, group);
                    manager.IsDirty = true;
                }, option =>
                {
                    group.preset = BotSpawnPreset.BotGen;
                    group.wildSpawnType = option.Id;
                    ApplyNpcOption(option, side => group.side = side, category => group.category = category);
                    manager.IsDirty = true;
                });
                if (GetEffectiveBotSpawnPreset(group.preset, group.wildSpawnType) != BotSpawnPreset.BotGen)
                    BuildStringField(_inspectorContent, "Wild Type", group.wildSpawnType ?? "", (v) => { group.wildSpawnType = v; manager.IsDirty = true; });
            }

            UIBuilder.CreateButton(_inspectorContent, "Add Group", () => { zone.randomGroups.Add(new BotSpawnGroup { id = Guid.NewGuid().ToString("N").Substring(0, 8) }); manager.IsDirty = true; RefreshInspector(); }, 100, 22);
        }

        private void ClearChildren(RectTransform parent)
        {
            if (parent == null)
                return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private bool MatchesSearch(MarkerBase marker)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
                return true;
            var term = _searchText.Trim();
            return (marker.name?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                || (marker.group?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                || (marker.Kind.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string GetItemName(string template)
        {
            if (string.IsNullOrEmpty(template))
                return null;
            if (_itemNameCache.TryGetValue(template, out var cached))
                return cached;

            var factory = Singleton<ItemFactory>.Instance;
            if (factory == null || !factory.ItemTemplates.TryGetValue(template, out var itemTemplate))
            {
                _itemNameCache[template] = null;
                return null;
            }

            var name = GetTemplateName(itemTemplate);
            _itemNameCache[template] = name;
            return name;
        }

        private static string GetTemplateName(object itemTemplate)
        {
            if (itemTemplate == null)
                return null;

            var type = itemTemplate.GetType();
            var prop = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (prop != null)
                return prop.GetValue(itemTemplate) as string;

            var field = type.GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(itemTemplate) as string;

            return null;
        }

        private string GetItemParent(string template)
        {
            if (string.IsNullOrEmpty(template))
                return null;

            var factory = Singleton<ItemFactory>.Instance;
            if (factory == null || !factory.ItemTemplates.TryGetValue(template, out var itemTemplate))
                return null;

            var type = itemTemplate.GetType();
            foreach (var propName in new[] { "ParentId", "Parent", "_parent" })
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (prop != null)
                {
                    var v = prop.GetValue(itemTemplate);
                    if (v != null)
                        return v.ToString();
                }
            }

            foreach (var fieldName in new[] { "ParentId", "Parent", "_parent" })
            {
                var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var v = field.GetValue(itemTemplate);
                    if (v != null)
                        return v.ToString();
                }
            }

            return null;
        }

        private bool ItemNeedsStackFields(string template)
        {
            if (string.IsNullOrEmpty(template))
                return false;

            if (string.Equals(template, "5d235b4d86f7742e017bc88a", StringComparison.OrdinalIgnoreCase))
                return true;

            var moneyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "5449016a4bdc2d6f028b456f",
                "5696686a4bdc2da3298b456a",
                "569668774bdc2da2298b4568"
            };
            if (moneyIds.Contains(template))
                return true;

            var parent = ItemNameResolver.GetParent(template) ?? GetItemParent(template);
            if (parent == "543be5dd4bdc2deb348b4569" || parent == "5485a8684bdc2da71d8b4567")
                return true;

            return false;
        }

        public void RequestDelete()
        {
            if (manager == null || manager.Selected == null)
                return;
            EnsureDeleteConfirm();
            if (_deleteConfirmPanel == null)
                return;
            _isDeletePending = true;
            _deleteConfirmPanel.gameObject.SetActive(true);
            _deleteConfirmPanel.SetAsLastSibling();
            if (_deleteConfirmText != null)
                _deleteConfirmText.text = string.Format(Locale.Get("Delete {0} marker(s)?"), manager.SelectedIds.Count);
        }

        private void ConfirmDelete()
        {
            _isDeletePending = false;
            _isDeleteConfirmed = true;
            if (_deleteConfirmPanel != null)
                _deleteConfirmPanel.gameObject.SetActive(false);
        }

        private void CancelDelete()
        {
            _isDeletePending = false;
            _isDeleteConfirmed = false;
            if (_deleteConfirmPanel != null)
                _deleteConfirmPanel.gameObject.SetActive(false);
        }

        public void ClearDeletePending()
        {
            _isDeletePending = false;
            _isDeleteConfirmed = false;
            if (_deleteConfirmPanel != null)
                _deleteConfirmPanel.gameObject.SetActive(false);
        }

        public IHasSourceObject PickingSourceTarget => _pickingSourceTarget;

        public void ClearPickingSource()
        {
            _isPickingSource = false;
            _pickingSourceTarget = null;
            RefreshInspector();
        }

        public void OnScatterObjectPicked(string objectName)
        {
            _scatterPrefabPath = objectName;
            _isPickingScatter = false;
            RefreshScatterPickRow();
        }

        public void ClearPickingScatter()
        {
            _isPickingScatter = false;
            RefreshScatterPickRow();
        }

        private void RefreshScatterPickRow()
        {
            if (_scatterPickRow == null) return;
            ClearChildren(_scatterPickRow);
            if (_isPickingScatter)
            {
                UIBuilder.CreateLabel(_scatterPickRow, "Click object in world...", 11, 136, 22);
                UIBuilder.CreateButton(_scatterPickRow, "Cancel", () => { _isPickingScatter = false; RefreshScatterPickRow(); }, 54, 22);
            }
            else
            {
                UIBuilder.CreateButton(_scatterPickRow, "Pick from Scene", () => { _isPickingScatter = true; RefreshScatterPickRow(); }, 100, 22);
                UIBuilder.CreateToggle(_scatterPickRow, "Use Parent", _pickUseParent, (v) => _pickUseParent = v, 18);
                if (!string.IsNullOrEmpty(_scatterPrefabPath))
                    UIBuilder.AddEllipsis(UIBuilder.CreateText(_scatterPickRow, $"  {_scatterPrefabPath}", 10, new Color(0.5f, 0.85f, 0.5f, 1f)));
            }
        }

        public GameObject TryPickSourceSceneObject() => PickSceneObjectAtMouse();

        public GameObject TryPickSourceSceneObject(out RaycastHit selectedHit) =>
            PickSceneObjectAtMouse(false, out selectedHit);

        public bool TryGetWorldSurfacePoint(Vector2 screenPosition, out Vector3 position)
        {
            position = default;
            var camera = Camera.main;
            if (camera == null)
                return false;

            var hits = Physics.RaycastAll(
                camera.ScreenPointToRay(screenPosition),
                1000f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (var hit in hits)
            {
                if (hit.collider == null || IsEditorObject(hit.collider.gameObject) ||
                    IsCharacterOrLooseLoot(hit.collider.gameObject))
                    continue;

                var point = hit.point;
                if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                    float.IsNaN(point.y) || float.IsInfinity(point.y) ||
                    float.IsNaN(point.z) || float.IsInfinity(point.z))
                    continue;

                position = point;
                return true;
            }
            return false;
        }

        public GameObject GetSceneObjectHover() => PickSceneObjectAtMouse(true);

        public void LogWorldSurfaceHit(RaycastHit hit, GameObject selectedGameObject)
        {
            if (TryCreateWorldSurfaceHit(hit, selectedGameObject, out var surfaceHit, out var failure))
            {
                LogWorldSurfaceHitDetails(surfaceHit);
                return;
            }

            if (Time.realtimeSinceStartup - _lastM1AnyLogTime >= 1f)
            {
                _lastM1AnyLogTime = Time.realtimeSinceStartup;
                var hitName = hit.collider != null ? hit.collider.gameObject.name : "unavailable";
                Plugin.Log.LogWarning($"[M1] World surface pick rejected: selected='{(selectedGameObject != null ? selectedGameObject.name : "none")}' hit='{hitName}': {failure}");
            }
        }

        private bool TryCreateWorldSurfaceHit(RaycastHit hit, GameObject selectedGo, out WorldSurfaceHit surfaceHit, out string failure)
        {
            surfaceHit = null;
            failure = null;
            if (selectedGo == null)
            {
                failure = "existing scene picker found no candidate";
                return false;
            }
            if (hit.collider == null || hit.transform == null)
            {
                failure = "existing scene picker returned no raycast collider or transform";
                return false;
            }
            if (hit.transform != selectedGo.transform && !hit.transform.IsChildOf(selectedGo.transform))
            {
                failure = "raycast collider is not part of the selected GameObject hierarchy";
                return false;
            }

            var hitGo = hit.transform.gameObject;
            if (hit.collider.isTrigger)
            {
                failure = "selected hit uses a trigger collider";
                return false;
            }
            if (IsEditorObject(hitGo) || IsEditorObject(selectedGo))
            {
                failure = "selected hit is a MEL-owned, preview, or player object";
                return false;
            }
            if (IsCharacterOrLooseLoot(hitGo) || IsCharacterOrLooseLoot(selectedGo))
            {
                failure = "selected hit belongs to a character or loose-loot object";
                return false;
            }
            if (!IsValidPick(selectedGo))
            {
                failure = "existing scene-pick filters rejected the selected object";
                return false;
            }
            if (!TryResolveSurfaceRenderer(hit, selectedGo.transform, out var targetRenderer, out var rendererFailure))
            {
                failure = rendererFailure;
                return false;
            }

            var mesh = targetRenderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null && targetRenderer is SkinnedMeshRenderer skinnedRenderer)
                mesh = skinnedRenderer.sharedMesh;
            if (mesh == null && hit.collider is MeshCollider meshCollider)
                mesh = meshCollider.sharedMesh;

            surfaceHit = new WorldSurfaceHit
            {
                HitGameObject = hitGo,
                SelectedGameObject = selectedGo,
                HitCollider = hit.collider,
                HitTransform = hit.transform,
                Point = hit.point,
                Normal = hit.normal,
                TriangleIndex = hit.triangleIndex,
                Renderer = targetRenderer,
                Mesh = mesh,
                Materials = targetRenderer.sharedMaterials
            };
            return true;
        }

        private static bool TryResolveSurfaceRenderer(RaycastHit hit, Transform target, out Renderer renderer, out string failure)
        {
            renderer = null;
            failure = null;

            for (var current = hit.transform; current != null; current = current.parent)
            {
                if (current == target.parent)
                    break;
                var candidate = current.GetComponent<Renderer>();
                if (IsUsableSurfaceRenderer(candidate))
                {
                    renderer = candidate;
                    return true;
                }
                if (current == target)
                    break;
            }

            var childRenderers = target.GetComponentsInChildren<Renderer>(true)
                .Where(IsUsableSurfaceRenderer)
                .Distinct()
                .ToArray();
            if (childRenderers.Length == 1)
            {
                renderer = childRenderers[0];
                return true;
            }

            failure = childRenderers.Length == 0
                ? "no enabled renderer with a material"
                : $"renderer association ambiguous ({childRenderers.Length} enabled child renderers): " +
                  FormatRendererCandidates(hit, childRenderers);
            return false;
        }

        private static string FormatRendererCandidates(RaycastHit hit, Renderer[] renderers)
        {
            return string.Join(" | ", renderers.Take(32).Select(candidate =>
            {
                var candidateTransform = candidate.transform;
                var relation = candidateTransform == hit.transform
                    ? "same"
                    : candidateTransform.IsChildOf(hit.transform)
                        ? "descendant-of-hit"
                        : hit.transform.IsChildOf(candidateTransform)
                            ? "ancestor-of-hit"
                            : "separate-branch";
                var mesh = candidate.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null && candidate is SkinnedMeshRenderer skinned)
                    mesh = skinned.sharedMesh;
                var closestDistance = Vector3.Distance(candidate.bounds.ClosestPoint(hit.point), hit.point);
                var materialNames = string.Join(",", candidate.sharedMaterials
                    .Where(material => material != null)
                    .Select(material => $"{material.name}:{(material.shader != null ? material.shader.name : "shader-unavailable")}")
                    .Take(4));
                return $"'{GetGameObjectPath(candidate.gameObject)}' type={candidate.GetType().Name} relation={relation} " +
                       $"mesh={(mesh != null ? mesh.name : "unavailable")} boundsDistance={closestDistance:F3} materials=[{materialNames}]";
            }));
        }

        private static bool IsUsableSurfaceRenderer(Renderer renderer)
        {
            return renderer != null &&
                   renderer.enabled &&
                   renderer.gameObject.activeInHierarchy &&
                   !renderer.forceRenderingOff &&
                   renderer.sharedMaterials.Any(material => material != null);
        }

        private static bool IsCharacterOrLooseLoot(GameObject go)
        {
            for (var current = go != null ? go.transform : null; current != null; current = current.parent)
            {
                foreach (var component in current.GetComponents<Component>())
                {
                    if (component == null)
                        continue;
                    var typeName = component.GetType().FullName ?? component.GetType().Name;
                    if (typeName.IndexOf("EFT.Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("BotOwner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("ObservedPlayer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeName.IndexOf("LootItem", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        private static void LogWorldSurfaceHitDetails(WorldSurfaceHit hit)
        {
            var key = hit.SelectedGameObject.GetInstanceID();
            var now = Time.realtimeSinceStartup;
            if ((key == _lastM1LoggedObjectId && now - _lastM1LogTime < 2f) ||
                now - _lastM1AnyLogTime < 1f)
                return;
            _lastM1LoggedObjectId = key;
            _lastM1LogTime = now;
            _lastM1AnyLogTime = now;

            var collider = hit.HitCollider;
            var root = hit.SelectedGameObject.transform.root;
            Plugin.Log.LogInfo(
                $"[M1] Surface hit selected='{hit.SelectedGameObject.name}' path='{GetGameObjectPath(hit.SelectedGameObject)}' " +
                $"hitGO='{hit.HitGameObject.name}' hitTransform='{GetGameObjectPath(hit.HitTransform.gameObject)}' " +
                $"collider='{collider.GetType().FullName}' trigger={collider.isTrigger} layer={collider.gameObject.layer} " +
                $"point={hit.Point} normal={hit.Normal} triangle={(hit.TriangleIndex >= 0 ? hit.TriangleIndex.ToString() : "unavailable")} " +
                $"root='{GetGameObjectPath(root.gameObject)}' rootPosition={root.position} rootRotation={root.rotation.eulerAngles} rootScale={root.lossyScale}");

            var worldObject = hit.SelectedGameObject.GetComponentInParent<WorldInteractiveObject>() ??
                              hit.SelectedGameObject.GetComponentInChildren<WorldInteractiveObject>(true);
            Plugin.Log.LogInfo(
                $"[M1] Renderer='{GetGameObjectPath(hit.Renderer.gameObject)}' type='{hit.Renderer.GetType().FullName}' " +
                $"mesh={(hit.Mesh != null ? $"'{hit.Mesh.name}' vertices={hit.Mesh.vertexCount} submeshes={hit.Mesh.subMeshCount} readable={hit.Mesh.isReadable}" : "unavailable")} " +
                $"WorldInteractiveObject.Id={(worldObject != null && !string.IsNullOrEmpty(worldObject.Id) ? worldObject.Id : "unavailable")}");

            for (var index = 0; index < hit.Materials.Length; index++)
            {
                var material = hit.Materials[index];
                Plugin.Log.LogInfo(material != null
                    ? $"[M1] material[{index}]='{material.name}' shader='{(material.shader != null ? material.shader.name : "unavailable")}'"
                    : $"[M1] material[{index}]=unavailable");
            }
        }

        private static int _lastM1LoggedObjectId;
        private static float _lastM1LogTime;
        private static float _lastM1AnyLogTime;

        public void SetPickingSource(bool picking, IHasSourceObject target = null)
        {
            _isPickingSource = picking;
            _pickingSourceTarget = target;
            if (picking && !_scanning)
            {
                if (_sceneObjectCache.Count == 0)
                    ScanSceneObjects();
                else
                    renderer?.SetSceneObjects(_sceneObjectCache, _selectedSceneGO);
            }
        }

        private bool IsMouseOverPanel(RectTransform panel)
        {
            if (panel == null)
                return false;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, Input.mousePosition, null, out var localPoint))
                return panel.rect.Contains(localPoint);
            return false;
        }

        public bool IsMouseOverMainWindow()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private GameObject PickSceneObjectAtMouse(bool silent = false) =>
            PickSceneObjectAtMouse(silent, out _);

        private GameObject PickSceneObjectAtMouse(bool silent, out RaycastHit selectedHit)
        {
            selectedHit = default;

            // Clicking again at the same spot cycles to the next candidate behind the previous one.
            var cycle = 0;
            if (!silent)
            {
                var mousePos = (Vector2)Input.mousePosition;
                if (_cycleCount > 1 && Time.unscaledTime - _cycleTime < 6f && (mousePos - _cycleMouse).sqrMagnitude < 36f)
                    cycle = (_cycleIndex + 1) % _cycleCount;
                _cycleMouse = mousePos;
                _cycleTime = Time.unscaledTime;
            }

            // A click confirms what the hover highlight is showing, so hover and click cannot disagree.
            if (!silent && cycle == 0 && _hoverDisplayed != null &&
                Time.unscaledTime - _hoverDisplayedTime < 0.3f &&
                ((Vector2)Input.mousePosition - _hoverMouse).sqrMagnitude < 16f)
            {
                var confirmed = _hoverDisplayed;
                _cycleIndex = 0;
                _cycleCount = _lastPickCount;
                if (Plugin.EnableScenePickDiagnostics != null && Plugin.EnableScenePickDiagnostics.Value)
                    Plugin.Log.LogInfo($"[PICK] click confirmed hovered object {GetGameObjectPath(confirmed)}");
                return confirmed;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                if (!silent)
                    Plugin.Log.LogWarning("No main camera found for scene picking.");
                return null;
            }

            var diag = !silent && Plugin.EnableScenePickDiagnostics != null && Plugin.EnableScenePickDiagnostics.Value;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var picked = ResolveViewportPick(ray, 100f, diag, out selectedHit, cycle);

            if (!silent)
            {
                _cycleIndex = cycle;
                _cycleCount = _lastPickCount;
            }
            if (picked == null && !silent)
                Plugin.Log.LogWarning("Scene picker found no selectable object under the cursor.");
            else if (picked != null && !silent)
                Plugin.Log.LogInfo($"Picked scene object: {GetGameObjectPath(picked)} at {picked.transform.position}" +
                                   (_lastPickCount > 1 ? $" (#{cycle + 1} of {_lastPickCount}; click again to cycle)" : ""));
            return picked;
        }

        private int _cycleIndex;
        private int _cycleCount;
        private float _cycleTime;
        private Vector2 _cycleMouse;

        // Diagnostic-only: runs the existing resolver once with its diagnostics block enabled (no selection side effects),
        // then dumps door hierarchies near the ray. Triggered by a key press, never by hover.
        public void CaptureDoorDiagnostics()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    Plugin.Log.LogWarning("[DOOR-DIAG] no main camera available.");
                    return;
                }

                var ray = cam.ScreenPointToRay(Input.mousePosition);
                var finalPick = ResolveViewportPick(ray, 100f, true, out _);
                DoorDiagnostics.Capture(ray, finalPick,
                    t => renderer != null ? renderer.FindSceneObjectAncestor(t) : null,
                    go =>
                    {
                        if (go == null) return "no object";
                        if (IsEditorObject(go)) return "editor object";
                        if (IsCharacterOrLooseLoot(go)) return "character/loot";
                        if (ShouldIgnorePick(go)) return FilterMatchReason(go);
                        return null;
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[DOOR-DIAG] capture failed: {ex.Message}");
            }
        }

        // ── Hybrid viewport picking ───────────────────────────────────────────
        // Candidates come from two sources, both measured as distance along the same camera ray:
        //   1. physics hits (accurate surface distance), resolved to the scanned scene object that owns the collider;
        //   2. renderer bounds of scanned objects (works without colliders, e.g. foliage), refined per renderer.
        // The nearest valid candidate wins; characters/loose loot/editor objects occlude everything behind them.
        private const int InitialPickHitBuffer = 64;
        private const int MaxPickHitBuffer = 2048;
        private const float PickTieDistance = 0.1f;
        private RaycastHit[] _pickHitBuffer = new RaycastHit[InitialPickHitBuffer];
        private readonly List<MarkerRenderer.SceneRayCandidate> _rendererCandidates = new List<MarkerRenderer.SceneRayCandidate>(64);
        private readonly List<Renderer> _pickRendererBuffer = new List<Renderer>(32);
        private readonly System.Text.StringBuilder _pickDiag = new System.Text.StringBuilder();

        private struct PickChoice
        {
            public GameObject go;
            public float distance;
            public float volume;
            public bool fromCollider;
            // True when the ray actually hits one of the object's own renderers (not just a collider volume).
            public bool visible;
            public RaycastHit hit;
        }

        private readonly List<PickChoice> _pickCandidates = new List<PickChoice>(16);
        private int _lastPickCount;

        private void AddPickCandidate(PickChoice choice)
        {
            for (int i = 0; i < _pickCandidates.Count; i++)
            {
                var existing = _pickCandidates[i];
                if (existing.go != choice.go)
                    continue;
                var visible = existing.visible || choice.visible;
                if (IsBetterPick(choice, existing))
                    existing = choice;
                existing.visible = visible;
                _pickCandidates[i] = existing;
                return;
            }
            _pickCandidates.Add(choice);
        }

        // Visible renderer hits rank above collider-only volumes, then nearest/tie rules apply.
        // A candidate whose own renderer the ray hits beats a collider-only one when it is at most
        // VisiblePreferenceWindow farther; beyond that, plain nearest-first applies so distant objects cannot jump a wall.
        private const float VisiblePreferenceWindow = 1.5f;

        private static bool RanksBefore(PickChoice a, PickChoice b)
        {
            if (a.visible != b.visible)
            {
                var v = a.visible ? a : b;
                var o = a.visible ? b : a;
                if (v.distance <= o.distance + VisiblePreferenceWindow)
                    return a.visible;
            }
            return IsBetterPick(a, b);
        }

        private GameObject ResolveViewportPick(Ray ray, float maxDistance, bool diag, out RaycastHit winningHit, int cycleIndex = 0)
        {
            winningHit = default;
            _pickCandidates.Clear();
            _lastPickCount = 0;
            if (diag)
            {
                _pickDiag.Length = 0;
                _pickDiag.AppendLine("[PICK] --- viewport pick ---");
            }

            // Physics candidates. A full buffer is grown and re-queried so a closer hit is never silently dropped.
            int count;
            while (true)
            {
                count = Physics.RaycastNonAlloc(ray, _pickHitBuffer, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < _pickHitBuffer.Length || _pickHitBuffer.Length >= MaxPickHitBuffer)
                    break;
                _pickHitBuffer = new RaycastHit[_pickHitBuffer.Length * 2];
            }
            if (count >= _pickHitBuffer.Length && diag)
                _pickDiag.AppendLine($"[PICK] hit buffer saturated at {count}; NonAlloc may keep an arbitrary subset");

            var occluder = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var hit = _pickHitBuffer[i];
                if (hit.collider == null)
                    continue;
                var hitGo = hit.collider.gameObject;
                var hitName = diag ? GetGameObjectPath(hitGo) : null;

                if (hit.collider.GetType().Name == "TerrainCollider")
                {
                    if (diag) _pickDiag.AppendLine($"[PICK] collider {hitName} d={hit.distance:F2} rejected: terrain");
                    continue;
                }

                // Characters, loose loot and editor objects are not selectable and block anything behind them.
                if (IsEditorObject(hitGo) || IsCharacterOrLooseLoot(hitGo))
                {
                    occluder = Mathf.Min(occluder, hit.distance);
                    if (diag) _pickDiag.AppendLine($"[PICK] collider {hitName} d={hit.distance:F2} rejected: character/loot/editor object (occludes)");
                    continue;
                }

                var owner = ResolveSelectable(hit.transform, out var viaCache);
                if (owner == null)
                {
                    if (diag) _pickDiag.AppendLine($"[PICK] collider {hitName} d={hit.distance:F2} rejected: no selectable owner");
                    continue;
                }
                if (!IsValidPick(owner))
                {
                    if (diag) _pickDiag.AppendLine($"[PICK] collider {hitName} -> {GetGameObjectPath(owner)} d={hit.distance:F2} rejected: {InvalidPickReason(owner)}");
                    continue;
                }
                // Collider-only blockers outside the scanned set that render nothing are not "visible objects".
                if (!viaCache && owner.GetComponentInChildren<Renderer>(true) == null)
                {
                    if (diag) _pickDiag.AppendLine($"[PICK] collider {hitName} d={hit.distance:F2} rejected: no visible renderer");
                    continue;
                }

                var choice = new PickChoice { go = owner, distance = hit.distance, volume = ColliderVolume(hit.collider), fromCollider = true, visible = false, hit = hit };
                if (diag) _pickDiag.AppendLine($"[PICK] collider {hitName} -> {GetGameObjectPath(owner)} d={hit.distance:F2} candidate");
                AddPickCandidate(choice);
            }

            // Renderer-bounds candidates (collider-less foliage, rocks, static meshes).
            if (renderer != null)
            {
                _rendererCandidates.Clear();
                MarkerRenderer.SceneRayCandidate.RejectedRefine = 0;
                renderer.CollectRendererRayCandidates(ray, maxDistance, _rendererCandidates, _pickRendererBuffer, 256);
                if (diag && MarkerRenderer.SceneRayCandidate.RejectedRefine > 0)
                    _pickDiag.AppendLine($"[PICK] {MarkerRenderer.SceneRayCandidate.RejectedRefine} combined-box hit(s) rejected: ray misses every renderer's own bounds");

                for (int i = 0; i < _rendererCandidates.Count; i++)
                {
                    var rc = _rendererCandidates[i];
                    var owner = rc.go;
                    if (_pickUseParent && owner.transform.parent != null && !IsEditorObject(owner.transform.parent.gameObject))
                        owner = owner.transform.parent.gameObject;

                    if (!IsValidPick(owner))
                    {
                        if (diag) _pickDiag.AppendLine($"[PICK] renderer {GetGameObjectPath(rc.go)} d={rc.distance:F2} rejected: {InvalidPickReason(owner)}");
                        continue;
                    }

                    var choice = new PickChoice { go = owner, distance = rc.distance, volume = rc.volume, fromCollider = false, visible = true };
                    if (diag) _pickDiag.AppendLine($"[PICK] renderer {GetGameObjectPath(rc.go)} -> {GetGameObjectPath(owner)} d={rc.distance:F2} candidate");
                    AddPickCandidate(choice);
                }
            }

            // Drop candidates behind an occluder, then order the rest (insertion sort; the list is small).
            for (int i = _pickCandidates.Count - 1; i >= 0; i--)
            {
                if (_pickCandidates[i].distance > occluder)
                {
                    if (diag) _pickDiag.AppendLine($"[PICK] {GetGameObjectPath(_pickCandidates[i].go)} d={_pickCandidates[i].distance:F2} discarded: behind occluder at d={occluder:F2}");
                    _pickCandidates.RemoveAt(i);
                }
            }
            for (int i = 1; i < _pickCandidates.Count; i++)
            {
                var item = _pickCandidates[i];
                int j = i - 1;
                while (j >= 0 && RanksBefore(item, _pickCandidates[j]))
                {
                    _pickCandidates[j + 1] = _pickCandidates[j];
                    j--;
                }
                _pickCandidates[j + 1] = item;
            }
            _lastPickCount = _pickCandidates.Count;

            if (_pickCandidates.Count == 0)
            {
                if (diag)
                {
                    _pickDiag.AppendLine("[PICK] FINAL none");
                    Plugin.Log.LogInfo(_pickDiag.ToString());
                }
                return null;
            }

            var best = _pickCandidates[cycleIndex % _pickCandidates.Count];
            if (diag)
            {
                for (int i = 0; i < _pickCandidates.Count; i++)
                    _pickDiag.AppendLine($"[PICK] rank {i + 1}: {GetGameObjectPath(_pickCandidates[i].go)} d={_pickCandidates[i].distance:F2} visible={_pickCandidates[i].visible}");
                _pickDiag.AppendLine($"[PICK] FINAL (#{cycleIndex % _pickCandidates.Count + 1} of {_pickCandidates.Count}) {(best.fromCollider ? "collider" : "renderer-bounds")} {GetGameObjectPath(best.go)} d={best.distance:F2}");
                Plugin.Log.LogInfo(_pickDiag.ToString());
            }

            if (best.fromCollider)
                winningHit = best.hit;
            return best.go;
        }

        // Existing resolution rules: Use Parent takes the hit's parent, otherwise the nearest ancestor in the scan cache,
        // otherwise the raw hit object (so picking still works before any scan).
        private GameObject ResolveSelectable(Transform hitTransform, out bool viaCache)
        {
            viaCache = false;
            var picked = hitTransform.gameObject;
            if (_pickUseParent && picked.transform.parent != null)
            {
                var parent = picked.transform.parent.gameObject;
                if (!IsEditorObject(parent))
                    picked = parent;
                return picked;
            }

            var cachedAncestor = renderer?.FindSceneObjectAncestor(hitTransform);
            if (cachedAncestor != null)
            {
                viaCache = true;
                return cachedAncestor;
            }
            return picked;
        }

        private static float ColliderVolume(Collider c)
        {
            var s = c.bounds.size;
            return s.x * s.y * s.z;
        }

        // Nearest wins. Within PickTieDistance: a descendant beats its ancestor (the more specific owner of the surface),
        // collider beats bounds for the same object, then smaller volume, then instance ID so the result is deterministic.
        private static bool IsBetterPick(PickChoice a, PickChoice b)
        {
            if (a.go == b.go)
                return a.fromCollider && !b.fromCollider ? true : (a.fromCollider == b.fromCollider && a.distance < b.distance);

            var diff = a.distance - b.distance;
            if (diff < -PickTieDistance) return true;
            if (diff > PickTieDistance) return false;

            if (a.go.transform.IsChildOf(b.go.transform)) return true;
            if (b.go.transform.IsChildOf(a.go.transform)) return false;
            if (!Mathf.Approximately(a.volume, b.volume)) return a.volume < b.volume;
            return a.go.GetInstanceID() < b.go.GetInstanceID();
        }

        // ── Hover (throttled, with hysteresis) ────────────────────────────────
        private const float HoverInterval = 0.06f;
        private const int HoverSwitchFrames = 2;
        private GameObject _hoverDisplayed;
        private float _hoverDisplayedTime;
        private Vector2 _hoverMouse;
        private GameObject _hoverPending;
        private int _hoverPendingCount;
        private float _nextHoverTime;

        public void ClearHover()
        {
            _hoverDisplayed = null;
            _hoverPending = null;
            _hoverPendingCount = 0;
        }

        // Called every frame while a picking mode is active; does real work at ~16 Hz. A new target must win on
        // consecutive evaluations before it replaces the shown one, which stops flicker between touching meshes.
        public GameObject UpdateHover()
        {
            if (_hoverDisplayed != null && !_hoverDisplayed)
                ClearHover();

            var mouse = (Vector2)Input.mousePosition;
            if (Time.unscaledTime < _nextHoverTime)
            {
                if (_hoverDisplayed != null && (mouse - _hoverMouse).sqrMagnitude > 16f)
                    _hoverDisplayedTime = 0f;
                return _hoverDisplayed;
            }
            _nextHoverTime = Time.unscaledTime + HoverInterval;

            var cam = Camera.main;
            GameObject candidate = null;
            if (cam != null)
                candidate = ResolveViewportPick(cam.ScreenPointToRay(Input.mousePosition), 100f, false, out _);

            if (candidate == _hoverDisplayed)
            {
                _hoverPending = null;
                _hoverPendingCount = 0;
            }
            else if (candidate == null || _hoverDisplayed == null)
            {
                // Appearing or disappearing is immediate; only target-to-target switches are debounced.
                _hoverDisplayed = candidate;
                _hoverPending = null;
                _hoverPendingCount = 0;
            }
            else if (candidate == _hoverPending)
            {
                if (++_hoverPendingCount >= HoverSwitchFrames)
                {
                    _hoverDisplayed = candidate;
                    _hoverPending = null;
                    _hoverPendingCount = 0;
                }
            }
            else
            {
                _hoverPending = candidate;
                _hoverPendingCount = 1;
            }

            _hoverMouse = mouse;
            _hoverDisplayedTime = Time.unscaledTime;
            return _hoverDisplayed;
        }

        private bool IsValidPick(GameObject go)
        {
            return go != null &&
                   !IsEditorObject(go) &&
                   !IsCharacterOrLooseLoot(go) &&
                   !ShouldIgnorePick(go);
        }

        private bool ShouldIgnorePick(GameObject go) => FilterMatchReason(go) != null;

        // Returns null when the object is not filtered, otherwise which filter matched and on which path segment.
        private string FilterMatchReason(GameObject go)
        {
            if (_pickFilterConfig == null || !_pickFilterConfig.enabled)
                return null;
            if (go == null)
                return "null object";
            var path = GetGameObjectPath(go);
            foreach (var filter in _pickFilterConfig.filters)
            {
                if (string.IsNullOrWhiteSpace(filter))
                    continue;
                if (PickFilterMatcher.TryMatch(path, filter, out var segment))
                    return $"filter '{filter}' matched path segment '{segment}'";
            }
            return null;
        }

        private string InvalidPickReason(GameObject go)
        {
            if (go == null) return "no object";
            if (IsEditorObject(go)) return "editor object";
            if (IsCharacterOrLooseLoot(go)) return "character/loot";
            return FilterMatchReason(go) ?? "valid";
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null)
                return string.Empty;
            var t = go.transform;
            var sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return "/" + sb.ToString();
        }

        public static bool IsEditorObject(GameObject go)
        {
            if (go == null)
                return true;
            var t = go.transform;
            while (t != null)
            {
                var name = t.name;
                if (name.StartsWith("MLE_", StringComparison.Ordinal))
                    return true;
                if (name.Equals("Player", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (t.GetComponent<PreviewLootMarker>() != null)
                    return true;
                t = t.parent;
            }
            return false;
        }

        public static bool IsScreenPointOverEditorUI(Vector2 screenPoint)
        {
            var ui = _outputInstance;
            if (ui == null || !ui._isVisible || ui._canvas == null)
                return false;

            if (IsPointOver(ui._topPanel, screenPoint)) return true;
            if (IsPointOver(ui._hierarchyPanel, screenPoint)) return true;
            if (IsPointOver(ui._inspectorPanel, screenPoint)) return true;
            if (IsPointOver(ui._bottomPanel, screenPoint)) return true;
            if (IsPointOver(ui._workspaceResizeHandle, screenPoint)) return true;
            if (IsPointOver(ui._scenePickerNotice, screenPoint)) return true;
            if (IsPointOver(ui._scenePickerCursorTooltip, screenPoint)) return true;
            if (IsPointOver(ui._deleteConfirmPanel, screenPoint)) return true;
            if (IsPointOver(ui._exportDialogPanel, screenPoint)) return true;
            if (IsPointOver(ui._renderDistanceDialogPanel, screenPoint)) return true;
            if (IsPointOver(ui._pickFiltersDialogPanel, screenPoint)) return true;
            if (IsPointOver(ui._itemPickerPanel, screenPoint)) return true;
            if (IsPointOver(ui._contextMenuPanel, screenPoint)) return true;
            if (IsPointOver(ui._addComponentDialogPanel, screenPoint)) return true;
            return false;
        }

        private static bool IsPointOver(RectTransform rt, Vector2 screenPoint)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy)
                return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);
        }

        private void LoadPickFilters()
        {
            var configDir = Path.GetDirectoryName(Plugin.Instance.Config.ConfigFilePath);
            _pickFiltersPath = Path.Combine(configDir, "MapEditorLite", "scenePickFilters.json");
            if (File.Exists(_pickFiltersPath))
            {
                try
                {
                    var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
                    _pickFilterConfig = JsonConvert.DeserializeObject<ScenePickFilterConfig>(File.ReadAllText(_pickFiltersPath), settings);
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"Failed to load scene pick filters: {ex.Message}");
                }
            }
            if (_pickFilterConfig == null)
                _pickFilterConfig = new ScenePickFilterConfig();
            _pickFilterConfig.filters = _pickFilterConfig.filters.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void SavePickFilters()
        {
            try
            {
                if (!string.IsNullOrEmpty(_pickFiltersPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_pickFiltersPath));
                    File.WriteAllText(_pickFiltersPath, JsonConvert.SerializeObject(_pickFilterConfig, Formatting.Indented, PackData.InvariantSettings));
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to save scene pick filters: {ex.Message}");
            }
        }

        private void BuildPickFiltersDialog()
        {
            _pickFiltersDialogPanel = UIBuilder.CreatePanel("PickFiltersDialog", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.95f));
            _pickFiltersDialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _pickFiltersDialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _pickFiltersDialogPanel.pivot = new Vector2(0.5f, 0.5f);
            _pickFiltersDialogPanel.sizeDelta = new Vector2(360, 420);
            UIBuilder.AddVerticalLayout(_pickFiltersDialogPanel, 12, 8, true, true);

            UIBuilder.CreateText(_pickFiltersDialogPanel, "Scene Pick Filters", 13, Color.white, FontStyle.Bold);

            var toggleRow = UIBuilder.CreatePanel("ToggleRow", _pickFiltersDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(toggleRow, 4, 4, false, false);
            UIBuilder.AddLayoutElement(toggleRow, null, 24, null, 24, null, 0);
            _pickFiltersEnabledToggle = UIBuilder.CreateToggle(toggleRow, "Filters Enabled", true, null, 22);

            var addRow = UIBuilder.CreatePanel("AddRow", _pickFiltersDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(addRow, 4, 4, false, false);
            UIBuilder.AddLayoutElement(addRow, null, 26, null, 26, null, 0);
            _pickFiltersInput = UIBuilder.CreateInputField(addRow, "new filter keyword...", "", null, 220, 22);
            UIBuilder.CreateButton(addRow, "Add", () => AddPickFilter(), 60, 22);

            var scroll = UIBuilder.CreateScrollView(_pickFiltersDialogPanel, out _pickFiltersContent, out _, 330, 250);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, 250, null, 250, null, 1);
            UIBuilder.AddVerticalLayout(_pickFiltersContent, 4, 4, true, false);

            var btnRow = UIBuilder.CreatePanel("BtnRow", _pickFiltersDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(btnRow, 8, 8, false, false);
            UIBuilder.AddLayoutElement(btnRow, null, 32, null, 32, null, 0);
            UIBuilder.CreateButton(btnRow, "Save", () => ConfirmPickFilters(), 100, 28);
            UIBuilder.CreateButton(btnRow, "Cancel", () => CancelPickFilters(), 100, 28);

            _pickFiltersDialogPanel.gameObject.SetActive(false);
        }

        private void ShowPickFiltersDialog()
        {
            CloseAllMenus();
            if (_pickFiltersDialogPanel == null || _pickFilterConfig == null) return;
            _pickFiltersEnabledToggle.isOn = _pickFilterConfig.enabled;
            _pickFiltersInput.text = "";
            RefreshPickFiltersList();
            _pickFiltersDialogPanel.gameObject.SetActive(true);
        }

        private void RefreshPickFiltersList()
        {
            if (_pickFiltersContent == null || _pickFilterConfig == null) return;
            ClearChildren(_pickFiltersContent);
            foreach (var filter in _pickFilterConfig.filters)
            {
                if (string.IsNullOrWhiteSpace(filter)) continue;
                var row = UIBuilder.CreatePanel("FilterRow", _pickFiltersContent, new Color(1f, 1f, 1f, 0.04f));
                UIBuilder.AddHorizontalLayout(row, 4, 4, true, false);
                UIBuilder.AddLayoutElement(row, null, 20, null, 20, null, 0);
                UIBuilder.CreateLabel(row, filter, 11, 200, 20);
                var captured = filter;
                UIBuilder.CreateButton(row, "X", () => RemovePickFilter(captured), 24, 20);
            }
        }

        private void AddPickFilter()
        {
            if (_pickFilterConfig == null || _pickFiltersInput == null) return;
            var text = _pickFiltersInput.text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!_pickFilterConfig.filters.Any(f => f.Equals(text, StringComparison.OrdinalIgnoreCase)))
            {
                _pickFilterConfig.filters.Add(text);
                _pickFiltersInput.text = "";
                RefreshPickFiltersList();
            }
        }

        private void RemovePickFilter(string filter)
        {
            if (_pickFilterConfig == null || string.IsNullOrEmpty(filter)) return;
            _pickFilterConfig.filters.RemoveAll(f => f.Equals(filter, StringComparison.OrdinalIgnoreCase));
            RefreshPickFiltersList();
        }

        private void ConfirmPickFilters()
        {
            if (_pickFilterConfig == null || _pickFiltersEnabledToggle == null || _pickFiltersDialogPanel == null) return;
            _pickFilterConfig.enabled = _pickFiltersEnabledToggle.isOn;
            SavePickFilters();
            _pickFiltersDialogPanel.gameObject.SetActive(false);
        }

        private void CancelPickFilters()
        {
            LoadPickFilters();
            if (_pickFiltersDialogPanel != null)
                _pickFiltersDialogPanel.gameObject.SetActive(false);
        }

        private void BuildItemPickerDialog()
        {
            _itemPickerPanel = UIBuilder.CreatePanel("ItemPickerDialog", _canvas.transform, new Color(0.08f, 0.08f, 0.08f, 0.97f));
            _itemPickerPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _itemPickerPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _itemPickerPanel.pivot = new Vector2(0.5f, 0.5f);
            _itemPickerPanel.sizeDelta = new Vector2(440, 480);
            UIBuilder.AddVerticalLayout(_itemPickerPanel, 12, 8, true, true);

            UIBuilder.CreateText(_itemPickerPanel, "Choose Item", 13, Color.white, FontStyle.Bold);

            _itemPickerSearch = UIBuilder.CreateInputField(_itemPickerPanel, "Search by name or template ID...", "", _ => RefreshItemPickerList(), 410, 22);
            UIBuilder.AddLayoutElement(_itemPickerSearch.gameObject, null, 24, null, 24, null, 0);

            _itemPickerStatus = UIBuilder.CreateText(_itemPickerPanel, "", 10, new Color(0.6f, 0.6f, 0.6f, 1f));

            var scroll = UIBuilder.CreateScrollView(_itemPickerPanel, out _itemPickerContent, out _, 410, 340);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, 340, null, 340, null, 1);
            UIBuilder.AddVerticalLayout(_itemPickerContent, 2, 2, true, false);

            var btnRow = UIBuilder.CreatePanel("BtnRow", _itemPickerPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(btnRow, 8, 8, false, false);
            UIBuilder.AddLayoutElement(btnRow, null, 32, null, 32, null, 0);
            UIBuilder.CreateButton(btnRow, "Cancel", () => CloseItemPicker(), 100, 28);

            _itemPickerPanel.gameObject.SetActive(false);
        }

        private void ShowItemPicker(LootItem item)
        {
            if (_itemPickerPanel == null || item == null) return;
            CloseAllMenus();
            _itemPickerTarget = item;
            _itemPickerSearch.text = "";
            RefreshItemPickerList();
            _itemPickerPanel.gameObject.SetActive(true);
            _itemPickerPanel.SetAsLastSibling();
        }

        private void CloseItemPicker()
        {
            _itemPickerTarget = null;
            if (_itemPickerPanel != null)
                _itemPickerPanel.gameObject.SetActive(false);
        }

        private void RefreshItemPickerList()
        {
            if (_itemPickerContent == null || _itemPickerSearch == null) return;
            ClearChildren(_itemPickerContent);

            var term = _itemPickerSearch.text;
            var results = ItemCatalog.Search(term, ItemPickerMaxResults + 1).ToList();
            var truncated = results.Count > ItemPickerMaxResults;
            if (truncated)
                results.RemoveAt(results.Count - 1);

            var currentId = _itemPickerTarget?.template;
            foreach (var entry in results)
            {
                var captured = entry;
                var label = string.IsNullOrEmpty(entry.Category) ? $"{entry.Name}  [{entry.Id}]" : $"{entry.Name}  [{entry.Id}]  ({entry.Category})";
                var btn = UIBuilder.CreateButton(_itemPickerContent, label, () => ApplyPickedItem(captured), 0, 22, 10);
                UIBuilder.AddLayoutElement(btn.gameObject, null, 22, null, 22, null, 0);
                if (!string.IsNullOrEmpty(currentId) && string.Equals(currentId, entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var img = btn.GetComponent<Image>();
                    if (img != null)
                        img.color = new Color(0.2f, 0.45f, 0.25f, 1f);
                }
            }

            _itemPickerStatus.text = results.Count == 0
                ? "No matching items."
                : (truncated ? $"Showing first {ItemPickerMaxResults} matches - refine search." : $"{results.Count} match(es).");
        }

        private void ApplyPickedItem(ItemCatalogEntry entry)
        {
            var item = _itemPickerTarget;
            if (item == null || entry == null) return;

            item.template = entry.Id;
            if (ItemNeedsStackFields(item.template) && item.maxCount <= 1)
            {
                item.minCount = 1;
                item.maxCount = ItemNameResolver.GetStackMaxSize(item.template);
            }
            manager.IsDirty = true;
            CloseItemPicker();
            RefreshInspector();
        }

        public void UpdateGizmoButtons()
        {
            foreach (var btn in _gizmoButtons)
            {
                if (btn == null)
                    continue;
                var isSelected = false;
                if (btn == _translateButton && controller.GizmoMode == GizmoMode.Translate) isSelected = true;
                if (btn == _rotateButton && controller.GizmoMode == GizmoMode.Rotate) isSelected = true;
                if (btn == _scaleButton && controller.GizmoMode == GizmoMode.Scale) isSelected = true;
                ApplyButtonAccent(btn, isSelected
                    ? new Color(0.15f, 0.43f, 0.55f, 1f)
                    : new Color(0.24f, 0.24f, 0.24f, 1f));
            }
        }

        public void UpdateEditorModeButton()
        {
            if (_editorModeButton != null)
            {
                var text = _editorModeButton.GetComponentInChildren<Text>();
                if (text != null)
                    text.text = Locale.Get(controller.IsFreeCam ? "Exit Editor Mode" : "Editor Mode");
            }
        }

        // ── Menu System ─────────────────────────────────────────────────

        private readonly List<RectTransform> _openMenus = new List<RectTransform>();

        private struct MenuItem
        {
            public string Label;
            public System.Action Action;
            public List<MenuItem> SubItems;
            public bool IsHeader;

            public MenuItem(string label, System.Action action = null, List<MenuItem> subItems = null, bool isHeader = false)
            {
                Label = label;
                Action = action;
                SubItems = subItems;
                IsHeader = isHeader;
            }
        }

        private void CloseAllMenus()
        {
            foreach (var menu in _openMenus)
            {
                if (menu != null)
                    Destroy(menu.gameObject);
            }
            _openMenus.Clear();
        }

        private Button BuildMenuButton(Transform parent, string label, List<MenuItem> items, int width, int height)
        {
            Button btn = null;
            btn = UIBuilder.CreateButton(parent, label, () => ShowMenu(btn.GetComponent<RectTransform>(), Vector2.zero, items, false), width, height, 11);
            return btn;
        }

        private void ShowMenu(RectTransform anchor, Vector2 offset, List<MenuItem> items, bool isSubMenu)
        {
            if (!isSubMenu)
                CloseAllMenus();
            else
                CloseChildMenus(anchor);

            var canvasRect = _canvas.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            Vector2 localPos;
            if (isSubMenu)
            {
                anchor.GetWorldCorners(corners);
                var topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, topRight, null, out localPos);
            }
            else
            {
                anchor.GetWorldCorners(corners);
                var bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, bottomLeft, null, out localPos);
            }

            // localPos is in canvas-local space (origin at screen centre). Convert to top-left anchored offset.
            var anchorOffset = new Vector2(canvasRect.rect.width / 2f, -canvasRect.rect.height / 2f);
            var menuPos = localPos + anchorOffset + offset;

            if (!isSubMenu)
            {
                var overlay = UIBuilder.CreatePanel("MenuOverlay", _canvas.transform, new Color(0, 0, 0, 0));
                overlay.anchorMin = Vector2.zero;
                overlay.anchorMax = Vector2.one;
                overlay.offsetMin = Vector2.zero;
                overlay.offsetMax = Vector2.zero;
                var overlayBtn = overlay.gameObject.AddComponent<Button>();
                overlayBtn.targetGraphic = overlay.GetComponent<Image>();
                overlayBtn.onClick.AddListener(CloseAllMenus);
                overlay.transform.SetAsLastSibling();
                _openMenus.Add(overlay);
            }

            var menu = UIBuilder.CreatePanel("Menu", _canvas.transform, new Color(0.12f, 0.12f, 0.12f, 0.98f));
            menu.anchorMin = new Vector2(0, 1);
            menu.anchorMax = new Vector2(0, 1);
            menu.pivot = new Vector2(0, 1);
            menu.anchoredPosition = menuPos;
            menu.sizeDelta = new Vector2(160, 0);
            menu.transform.SetAsLastSibling();
            UIBuilder.AddVerticalLayout(menu, 2, 1, true, true);
            UIBuilder.AddContentSizeFitter(menu, ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize);

            foreach (var item in items)
            {
                if (item.IsHeader)
                {
                    var header = UIBuilder.CreateText(menu, item.Label, 10, new Color(0.6f, 0.6f, 0.6f, 1f), FontStyle.Bold);
                    if (header != null)
                    {
                        header.alignment = TextAnchor.MiddleLeft;
                        var headerRt = header.rectTransform;
                        UIBuilder.AddLayoutElement(headerRt, null, 16, null, 16, null, 0);
                    }
                    continue;
                }

                var row = UIBuilder.CreatePanel("MenuItem", menu, new Color(0.14f, 0.14f, 0.14f, 1f));
                UIBuilder.AddHorizontalLayout(row, 4, 0, true, false);
                UIBuilder.AddLayoutElement(row, null, 20, null, 20, null, 0);

                var hasSubItems = item.SubItems != null && item.SubItems.Count > 0;
                var btn = UIBuilder.CreateButton(row, item.Label, () => { }, 0, 18, 10);
                UIBuilder.AddLayoutElement(btn.gameObject, null, 18, null, 18, 1, 0);
                btn.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                var btnColors = btn.colors;
                btnColors.normalColor = Color.white;
                btnColors.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
                btnColors.pressedColor = new Color(0.7f, 0.8f, 1f, 1f);
                btn.colors = btnColors;
                var lbl = btn.GetComponentInChildren<Text>();
                if (lbl != null)
                {
                    lbl.alignment = TextAnchor.MiddleLeft;
                    lbl.fontSize = 10;
                }

                if (hasSubItems)
                {
                    var arrow = UIBuilder.CreateLabel(row, "►", 10, 14, 18);
                    if (arrow != null)
                        arrow.alignment = TextAnchor.MiddleRight;
                    btn.onClick.AddListener(() => ShowMenu(row, Vector2.zero, item.SubItems, true));
                }
                else if (item.Action != null)
                {
                    btn.onClick.AddListener(() =>
                    {
                        item.Action();
                        CloseAllMenus();
                    });
                }
            }

            _openMenus.Add(menu);
        }

        private void CloseChildMenus(RectTransform anchor)
        {
            var parentMenu = anchor.parent?.GetComponent<RectTransform>();
            if (parentMenu == null)
            {
                CloseAllMenus();
                return;
            }
            int parentIndex = _openMenus.IndexOf(parentMenu);
            if (parentIndex < 0) parentIndex = 0;
            for (int i = _openMenus.Count - 1; i > parentIndex; i--)
            {
                var menu = _openMenus[i];
                if (menu != null && menu.name != "MenuOverlay")
                    Destroy(menu.gameObject);
                _openMenus.RemoveAt(i);
            }
        }

        private const int AddComponentMaxResults = 200;
        private const float AddComponentDialogWidth = 380f;
        private const float AddComponentDialogHeight = 520f;
        private ScrollRect _addComponentScroll;
        private Text _addComponentStatus;

        // Canvas size in canvas units (screen size / UI scale).
        private Vector2 CanvasSize()
        {
            var rt = _canvas != null ? _canvas.transform as RectTransform : null;
            return rt != null && rt.rect.width > 1f ? rt.rect.size : new Vector2(Screen.width, Screen.height);
        }

        private void FitAddComponentDialog()
        {
            if (_addComponentDialogPanel == null)
                return;
            var canvasSize = CanvasSize();
            var fitted = new Vector2(
                Mathf.Min(AddComponentDialogWidth, Mathf.Max(220f, canvasSize.x - 24f)),
                Mathf.Min(AddComponentDialogHeight, Mathf.Max(180f, canvasSize.y - 24f)));
            if (_addComponentDialogPanel.sizeDelta != fitted)
                _addComponentDialogPanel.sizeDelta = fitted;
        }

        private void CloseAddComponentDialog()
        {
            if (_addComponentDialogPanel != null)
                Destroy(_addComponentDialogPanel.gameObject);
            _addComponentDialogPanel = null;
            _addComponentListPanel = null;
            _addComponentScroll = null;
            _addComponentStatus = null;
        }

        private void ShowAddComponentDialog(GameObject target, List<string> targetList)
        {
            if (target == null)
                return;

            CloseAddComponentDialog();

            _addComponentTarget = target;
            _addComponentTargetList = targetList;
            _addComponentSearch = "";

            var panel = UIBuilder.CreatePanel("AddComponentDialog", _canvas.transform, new Color(0.15f, 0.15f, 0.15f, 1f));
            _addComponentDialogPanel = panel.GetComponent<RectTransform>();
            _addComponentDialogPanel.anchorMin = new Vector2(0.5f, 0.5f);
            _addComponentDialogPanel.anchorMax = new Vector2(0.5f, 0.5f);
            _addComponentDialogPanel.pivot = new Vector2(0.5f, 0.5f);
            _addComponentDialogPanel.anchoredPosition = Vector2.zero;
            FitAddComponentDialog();

            // Header, search and footer are fixed-size; only the results scroll view takes the remaining height.
            UIBuilder.AddVerticalLayout(_addComponentDialogPanel, 8, 6, true, true);

            UIBuilder.CreateText(_addComponentDialogPanel, "Add Component", 13, Color.white, FontStyle.Bold);

            var search = UIBuilder.CreateInputField(_addComponentDialogPanel, "Search...", _addComponentSearch, (v) =>
            {
                _addComponentSearch = v;
                RebuildAddComponentList();
            }, 300, 22);
            UIBuilder.AddLayoutElement(search.gameObject, null, 24, null, 24, 1, 0);
            AddInputFieldContextMenu(search);

            _addComponentStatus = UIBuilder.CreateText(_addComponentDialogPanel, "", 10, new Color(0.6f, 0.6f, 0.6f, 1f));

            _addComponentScroll = UIBuilder.CreateScrollView(_addComponentDialogPanel, out var content, out _, 0, 0, 14);
            _addComponentListPanel = content;
            UIBuilder.AddLayoutElement(_addComponentScroll.gameObject, null, 80, null, 80, null, 1);
            UIBuilder.AddVerticalLayout(_addComponentListPanel, 2, 2, true, true);

            var close = UIBuilder.CreateButton(_addComponentDialogPanel, "Close", CloseAddComponentDialog, 80, 24);
            UIBuilder.AddLayoutElement(close.gameObject, null, 24, null, 24, null, 0);

            RebuildAddComponentList();
            _addComponentDialogPanel.SetAsLastSibling();
            search.ActivateInputField();
        }

        private void RebuildAddComponentList()
        {
            if (_addComponentListPanel == null)
                return;
            ClearChildren(_addComponentListPanel);
            _addComponentListPanel.anchoredPosition = Vector2.zero;

            var search = _addComponentSearch ?? "";
            var all = ComponentHelper.GetAllComponentTypes();
            var filtered = all.Where(t => string.IsNullOrEmpty(search) ||
                                          t.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          (t.Namespace != null && t.Namespace.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                          t.FullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                              .ToList();
            var matches = filtered.Take(AddComponentMaxResults).ToList();

            if (_addComponentStatus != null)
            {
                _addComponentStatus.text = filtered.Count > matches.Count
                    ? $"Showing first {matches.Count:N0} of {filtered.Count:N0} - type to narrow"
                    : $"{filtered.Count:N0} component(s)";
            }

            if (matches.Count == 0)
            {
                UIBuilder.CreateText(_addComponentListPanel, "No matching components.", 10, new Color(0.6f, 0.6f, 0.6f, 1f));
                return;
            }

            foreach (var type in matches)
            {
                var captured = type;
                var label = string.IsNullOrEmpty(captured.Namespace) ? captured.Name : $"{captured.Namespace}.{captured.Name}";
                var btn = UIBuilder.CreateButton(_addComponentListPanel, label, () =>
                {
                    AddSelectedComponent(captured);
                }, 0, 20, 10);
                UIBuilder.AddLayoutElement(btn.gameObject, null, 20, null, 20, 1, 0);
                var text = btn.GetComponentInChildren<Text>();
                if (text != null)
                    text.alignment = TextAnchor.MiddleLeft;
            }
        }

        private void AddSelectedComponent(Type type)
        {
            if (_addComponentTarget == null || type == null)
                return;

            var comp = ComponentHelper.AddComponentByName(_addComponentTarget, type.AssemblyQualifiedName);
            if (comp == null)
                return;

            if (_addComponentTargetList != null && !_addComponentTargetList.Contains(type.AssemblyQualifiedName))
                _addComponentTargetList.Add(type.AssemblyQualifiedName);

            manager.IsDirty = true;

            if (_addComponentDialogPanel != null)
                Destroy(_addComponentDialogPanel.gameObject);
            _addComponentDialogPanel = null;
            _addComponentListPanel = null;

            RequestInspectorRefresh();
            RequestHierarchyRefresh();
        }
    }
}
