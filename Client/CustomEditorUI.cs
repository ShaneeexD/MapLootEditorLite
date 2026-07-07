using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MapLootEditorLite.Client
{
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
        private const int TopPanelHeight = 56;
        private int _hierarchyWidth = 240;
        private int _inspectorWidth = 280;
        private const int BottomPanelHeight = 180;

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
        private bool _scanning;
        private Text _goPreviewNameText;
        private RectTransform _goActionBtnRow;
        private RectTransform _goPickRow;
        private Text _goDetailsText;
        private bool _isPickingSceneGO;

        // Removed vanilla objects tab
        private RectTransform _removedTab;
        private Button _removedTabButton;
        private RectTransform _removedContent;
        private readonly Dictionary<string, GameObject> _removedObjectInstances = new Dictionary<string, GameObject>();

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
        public bool IsDeletePending => _isDeletePending;
        public bool IsDeleteConfirmed => _isDeleteConfirmed;
        public bool IsVisible => _isVisible;
        public string PackName => _packName;

        public void SetPickingSceneGO(bool picking)
        {
            _isPickingSceneGO = picking;
            if (_scenePickerNotice != null)
                _scenePickerNotice.gameObject.SetActive(picking);
            RefreshGOPickRow();
        }

        public void SelectSceneGO(GameObject go)
        {
            if (go != null && manager != null)
                manager.ClearSelection();
            _selectedSceneGO = go;
            if (_goPreviewNameText != null)
                _goPreviewNameText.text = go != null ? go.name : "No selection";
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
            CloseAllMenus();
            HideContextMenu();
            if (_canvas != null)
                _canvas.SetActive(false);
            if (_deleteConfirmPanel != null)
                _deleteConfirmPanel.gameObject.SetActive(false);
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
            SafeBuild("ContextMenu", BuildContextMenu);
            SafeBuild("ResizeHandles", BuildResizeHandles);

            if (manager != null && controller != null)
                RefreshAll();
        }

        private void BuildTopPanel()
        {
            _topPanel = UIBuilder.CreatePanel("TopPanel", _mainWindow, new Color(0.08f, 0.08f, 0.08f, 0.97f));
            // Anchor to top edge, full width
            _topPanel.anchorMin = new Vector2(0f, 1f);
            _topPanel.anchorMax = new Vector2(1f, 1f);
            _topPanel.pivot     = new Vector2(0f, 1f);
            _topPanel.offsetMin = new Vector2(0, -TopPanelHeight);
            _topPanel.offsetMax = Vector2.zero;
            UIBuilder.AddVerticalLayout(_topPanel, 2, 2, true, true);

            // Row 1 – action buttons
            var row1 = UIBuilder.CreatePanel("TopRow1", _topPanel, new Color(0, 0, 0, 0));
            var hlg1 = UIBuilder.AddHorizontalLayout(row1, 3, 3, false, false);
            hlg1.childAlignment = TextAnchor.MiddleLeft;
            UIBuilder.AddLayoutElement(row1, null, 28, null, 28, null, 0);

            _titleText = UIBuilder.CreateText(row1, "Map Loot Editor Lite", 12, Color.white, FontStyle.Bold);
            _titleText.rectTransform.sizeDelta = new Vector2(300, 22);
            // Small gap between title and buttons
            var sep = UIBuilder.CreatePanel("TitleSep", row1, new Color(0, 0, 0, 0));
            sep.GetComponent<Image>().raycastTarget = false;
            sep.sizeDelta = new Vector2(6, 22);
            BuildMenuButton(row1, "File", new List<MenuItem>
            {
                new MenuItem("Export", () => DirectExport()),
                new MenuItem("Export As", () => ShowExportDialog()),
                new MenuItem("Import Vanilla Loot", () => controller.ImportVanillaLoot())
            }, 40, 22);
            BuildMenuButton(row1, "View", new List<MenuItem>
            {
                new MenuItem("Hierarchy", subItems: new List<MenuItem>
                {
                    new MenuItem("Toggle Vanilla Gizmos", () => controller.ToggleVanillaGizmos()),
                    new MenuItem("Toggle Pack Gizmos", () => controller.TogglePackGizmos())
                }),
                new MenuItem("Vanilla Render Distance", () => ShowRenderDistanceDialog())
            }, 40, 22);
            BuildMenuButton(row1, "Tools", new List<MenuItem>
            {
                new MenuItem("Debug", subItems: new List<MenuItem>
                {
                    new MenuItem("Dump door IDs", () => controller.DumpDoorIds()),
                    new MenuItem("Dump keyed door IDs", () => controller.DumpKeyedDoorIds())
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
                    new MenuItem("Light Zone", () => controller.CreateLightZone()),
                    new MenuItem("Trigger Zone", () => controller.CreateTriggerZone())
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
                })
            }, 84, 22);
            UIBuilder.CreateButton(row1, "Snap",         () => controller.SnapSelected(),     46, 22);
            UIBuilder.CreateButton(row1, "Duplicate",    () => controller.DuplicateSelected(),68, 22);
            UIBuilder.CreateButton(row1, "Delete",       () => RequestDelete(),               54, 22);
            UIBuilder.CreateButton(row1, "Clear Prev",   () => controller.ClearPreviews(),    76, 22);
            _editorModeButton = UIBuilder.CreateButton(row1, "Editor Mode", () =>
            {
                if (controller.IsFreeCam)
                    controller.CloseEditor();
                else
                    controller.ToggleFreeCam();
            }, 92, 22);
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
            hints.rectTransform.anchorMin = Vector2.zero;
            hints.rectTransform.anchorMax = Vector2.one;
            hints.rectTransform.offsetMin = Vector2.zero;
            hints.rectTransform.offsetMax = Vector2.zero;
        }

        private void BuildHierarchyPanel()
        {
            _hierarchyPanel = UIBuilder.CreatePanel("HierarchyPanel", _mainWindow, new Color(0.12f, 0.12f, 0.12f, 0.97f));
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
                ? new Color(0.25f, 0.45f, 0.75f, 1f)
                : new Color(0.24f, 0.24f, 0.24f, 1f);
            _vanillaTabButton.GetComponent<Image>().color = _vanillaHierarchyActive
                ? new Color(0.25f, 0.45f, 0.75f, 1f)
                : new Color(0.24f, 0.24f, 0.24f, 1f);
        }

        private void BuildInspectorPanel()
        {
            _inspectorPanel = UIBuilder.CreatePanel("InspectorPanel", _mainWindow, new Color(0.12f, 0.12f, 0.12f, 0.97f));
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
        }

        private void UpdateResizeHandles()
        {
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

        private void AddDragResize(RectTransform handle, bool isHierarchy)
        {
            var et = handle.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            entry.callback.AddListener((data) =>
            {
                var scale = _canvasScaler != null ? _canvasScaler.scaleFactor : 1f;
                var dx = Mathf.RoundToInt(((PointerEventData)data).delta.x / scale);
                if (isHierarchy)
                {
                    _hierarchyWidth = Mathf.Clamp(_hierarchyWidth + dx, 140, 500);
                    _hierarchyPanel.offsetMax = new Vector2(_hierarchyWidth, -TopPanelHeight);
                }
                else
                {
                    _inspectorWidth = Mathf.Clamp(_inspectorWidth - dx, 160, 500);
                    _inspectorPanel.offsetMin = new Vector2(-_inspectorWidth, BottomPanelHeight);
                }
                UpdateResizeHandles();
            });
            et.triggers.Add(entry);
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

            var tabBar = UIBuilder.CreatePanel("TabBar", _bottomPanel, new Color(0.08f, 0.08f, 0.08f, 1f));
            UIBuilder.AddHorizontalLayout(tabBar, 4, 4, false, false);
            UIBuilder.AddLayoutElement(tabBar, null, 26, null, 26, null, 0);
            _prefabsTabButton  = UIBuilder.CreateButton(tabBar, "Prefabs",      () => SelectBottomTab(0), 60, 22);
            _scatterTabButton  = UIBuilder.CreateButton(tabBar, "Scatter",      () => SelectBottomTab(1), 60, 22);
            _groupsTabButton   = UIBuilder.CreateButton(tabBar, "Groups",       () => SelectBottomTab(2), 60, 22);
            _objectsTabButton  = UIBuilder.CreateButton(tabBar, "GameObjects",  () => SelectBottomTab(3), 76, 22);
            _removedTabButton  = UIBuilder.CreateButton(tabBar, "Removed",      () => SelectBottomTab(4), 60, 22);
            _outputTabButton   = UIBuilder.CreateButton(tabBar, "Output",       () => SelectBottomTab(5), 60, 22);

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

        private void SelectBottomTab(int index)
        {
            if (index == 5)
                EnsureOutputTab();
            _activeBottomTab = index;
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
            btn.GetComponent<Image>().color = active
                ? new Color(0.25f, 0.45f, 0.75f, 1f)
                : new Color(0.24f, 0.24f, 0.24f, 1f);
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
            _groupInput = UIBuilder.CreateInputField(row, "Group", _newGroupName, (v) => _newGroupName = v, 80, 22);
            UIBuilder.CreateButton(row, "Assign", () => { if (manager.Selected != null) { manager.SetGroupOnSelection(_newGroupName); RequestRefresh(); } }, 60, 22);
            UIBuilder.CreateButton(row, "Clear", () => { if (manager.Selected != null) manager.SetGroupOnSelection(""); }, 50, 22);

            var groupsScroll = UIBuilder.CreateScrollView(_groupsTab, out _groupsContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(groupsScroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_groupsContent, 4, 2, true, false);
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
                (v) => { _objectsSearchText = v; RefreshObjectsList(); }, 110, 22);
            UIBuilder.CreateButton(searchRow, "Scan Scene", () => { ScanSceneObjects(); RefreshObjectsList(); }, 80, 22);

            _goPickRow = UIBuilder.CreatePanel("GOPick", leftCol, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(_goPickRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(_goPickRow, null, 22, null, 22, null, 0);
            RefreshGOPickRow();

            var scroll = UIBuilder.CreateScrollView(leftCol, out _objectsContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_objectsContent, 1, 2, true, true);

            // ── Right: name label + action buttons ────────────────────────────
            var rightCol = UIBuilder.CreatePanel("GORight", _objectsTab, new Color(0.1f, 0.1f, 0.1f, 1f));
            UIBuilder.AddLayoutElement(rightCol, 120, null, 120, null, 0, 1);
            UIBuilder.AddVerticalLayout(rightCol, 4, 4, true, true);

            _goPreviewNameText = UIBuilder.CreateText(rightCol, "No selection", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
            if (_goPreviewNameText != null)
            {
                _goPreviewNameText.alignment = TextAnchor.UpperCenter;
                _goPreviewNameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            _goActionBtnRow = UIBuilder.CreatePanel("GOActions", rightCol, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(_goActionBtnRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(_goActionBtnRow, null, 22, null, 22, null, 0);
            RefreshGOActionRow();

            _goDetailsText = UIBuilder.CreateText(rightCol, "Select an object to view details.", 10, new Color(0.7f, 0.7f, 0.7f, 1f));
            if (_goDetailsText != null)
            {
                _goDetailsText.alignment = TextAnchor.UpperLeft;
                _goDetailsText.horizontalOverflow = HorizontalWrapMode.Wrap;
                _goDetailsText.verticalOverflow = VerticalWrapMode.Overflow;
                var detailsRt = _goDetailsText.GetComponent<RectTransform>();
                if (detailsRt != null)
                    detailsRt.sizeDelta = new Vector2(110, 0);
                UIBuilder.AddLayoutElement(_goDetailsText.gameObject, null, null, null, null, 1, 1);
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
                UIBuilder.CreateText(_outputContent, msg, 11, Color.white);
            }
        }

        private void RefreshObjectsList()
        {
            if (_objectsContent == null) return;
            ClearChildren(_objectsContent);

            if (_scanning)
            {
                UIBuilder.CreateText(_objectsContent, "Scanning scene... please wait", 11, new Color(0.7f, 0.85f, 0.7f, 1f));
                return;
            }

            var filter = _objectsSearchText?.Trim() ?? "";
            var filtered = string.IsNullOrEmpty(filter)
                ? _sceneObjectCache
                : _sceneObjectCache.Where(g => g != null && g.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            const int maxShown = 150;
            bool truncated = filtered.Count > maxShown;
            var list = truncated ? filtered.Take(maxShown).ToList() : filtered;

            if (list.Count == 0)
            {
                UIBuilder.CreateText(_objectsContent,
                    _sceneObjectCache.Count == 0 ? "Click 'Scan Scene' to populate the list." : "No objects match the filter.",
                    11, new Color(0.5f, 0.5f, 0.5f, 1f));
                return;
            }

            if (truncated)
                UIBuilder.CreateText(_objectsContent, $"Showing {maxShown} of {filtered.Count} — refine filter", 10, new Color(0.6f, 0.6f, 0.4f, 1f));

            foreach (var go in list)
            {
                if (go == null) continue;
                var captured = go;
                bool isSelected = _selectedSceneGO == go;

                var row = UIBuilder.CreatePanel("GORow", _objectsContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row, 1, 1, true, false);
                UIBuilder.AddLayoutElement(row, null, 14, null, 14, null, 0);
                row.GetComponent<Image>().color = isSelected
                    ? new Color(0.2f, 0.3f, 0.45f, 0.6f)
                    : new Color(0.13f, 0.13f, 0.13f, 0.3f);

                var btn = UIBuilder.CreateButton(row, go.name, () =>
                {
                    _selectedSceneGO = captured;
                    if (_goPreviewNameText != null)
                        _goPreviewNameText.text = captured.name;
                    RefreshObjectsList();
                    RefreshGOActionRow();
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

        private void RefreshGOPickRow()
        {
            if (_goPickRow == null) return;
            ClearChildren(_goPickRow);
            if (_isPickingSceneGO)
            {
                UIBuilder.CreateLabel(_goPickRow, "Click object in world...", 11, 136, 22);
                UIBuilder.CreateButton(_goPickRow, "Cancel", () => { _isPickingSceneGO = false; RefreshGOPickRow(); }, 54, 22);
            }
            else
            {
                UIBuilder.CreateButton(_goPickRow, "Pick from Scene", () => { _isPickingSceneGO = true; RefreshGOPickRow(); }, 100, 22);
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
                return "Select an object to view details.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Name: {go.name}");
            sb.AppendLine($"Path: {GetFullPath(go.transform)}");
            sb.AppendLine($"Pos: {go.transform.position}");
            sb.AppendLine($"Rot: {go.transform.rotation.eulerAngles}");
            sb.AppendLine($"Scale: {go.transform.localScale}");

            var wio = go.GetComponentInChildren<WorldInteractiveObject>(true);
            if (wio != null)
            {
                sb.AppendLine("--- WorldInteractiveObject ---");
                sb.AppendLine($"Id: {wio.Id}");
                sb.AppendLine($"KeyId: {wio.KeyId}");
                sb.AppendLine($"DoorState: {wio.DoorState}");
                sb.AppendLine($"Initial: {wio.InitialDoorState}");
                sb.AppendLine($"Fallback: {wio.FallbackState}");
            }

            var container = wio as LootableContainer ?? go.GetComponentInChildren<LootableContainer>(true);
            if (container != null)
            {
                sb.AppendLine("--- LootableContainer ---");
                sb.AppendLine($"Id: {container.Id}");
                sb.AppendLine($"Template: {container.Template}");
            }

            var sw = go.GetComponentInChildren<StationaryWeapon>(true);
            if (sw != null)
            {
                sb.AppendLine("--- StationaryWeapon ---");
                sb.AppendLine($"IdEditable: {sw.IdEditable}");
                sb.AppendLine($"Template: {sw.Template}");
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
            var removed = new RemovedObject
            {
                id = System.Guid.NewGuid().ToString("N"),
                name = go.name,
                path = GetFullPath(go.transform),
                position = TransformData.FromVector3(go.transform.position),
                rotation = TransformData.FromVector3(go.transform.rotation.eulerAngles),
                scale = TransformData.FromVector3(go.transform.localScale)
            };
            _removedObjectInstances[removed.id] = go;
            manager.Data.removedObjects.Add(removed);
            go.SetActive(false);
            _sceneObjectCache.Remove(go);
            _selectedSceneGO = null;
            manager.IsDirty = true;
            if (_goPreviewNameText != null)
                _goPreviewNameText.text = "No selection";
            RefreshObjectsList();
            RefreshGOActionRow();
            RefreshRemovedObjectsList();
            Plugin.Log.LogInfo($"Marked scene object for removal: {removed.name} at {removed.position}");
        }

        private void RestoreRemovedObject(string id)
        {
            var removed = manager.Data?.removedObjects?.FirstOrDefault(r => r.id == id);
            if (removed == null) return;
            if (_removedObjectInstances.TryGetValue(id, out var go) && go != null)
                go.SetActive(true);
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
            foreach (var removed in manager.Data.removedObjects.ToList())
            {
                if (_removedObjectInstances.TryGetValue(removed.id, out var go) && go != null)
                    go.SetActive(true);
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
                    _removedObjectInstances[removed.id] = go;
                    go.SetActive(false);
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
                    var rt = label.GetComponent<RectTransform>();
                    if (rt != null) rt.sizeDelta = new Vector2(180, 18);
                }
                var captured = removed;
                UIBuilder.CreateButton(row, "Restore", () => RestoreRemovedObject(captured.id), 60, 18);
            }
        }

        private void ScanSceneObjects()
        {
            if (_scanning) return;
            StartCoroutine(ScanSceneObjectsCoroutine());
        }

        private IEnumerator ScanSceneObjectsCoroutine()
        {
            _scanning = true;
            _sceneObjectCache.Clear();
            RefreshObjectsList();
            yield return null;

            var seen = new HashSet<int>();
            var stack = new Stack<Transform>();

            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    stack.Push(root.transform);
            }

            int processed = 0;
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                var go = t.gameObject;

                // Skip inactive objects — they're not visible and we skip their entire subtree
                if (!go.activeSelf)
                {
                    if (++processed % 300 == 0) yield return null;
                    continue;
                }

                if (!go.name.StartsWith("MLE_", StringComparison.Ordinal) && go.GetComponent<PreviewLootMarker>() == null)
                {
                    var lod = go.GetComponent<LODGroup>();
                    if (lod != null)
                    {
                        // Only add LOD objects whose LOD group is enabled and have at least one enabled renderer
                        if (lod.enabled)
                        {
                            var mr = go.GetComponentInChildren<MeshRenderer>();
                            if (mr != null && mr.enabled && seen.Add(go.GetInstanceID()))
                                _sceneObjectCache.Add(go);
                        }
                        // Never push children — sub-meshes belong to this LOD object
                    }
                    else
                    {
                        for (int i = 0; i < t.childCount; i++)
                            stack.Push(t.GetChild(i));

                        // Only add if the renderer is enabled and has an actual mesh assigned
                        var mr = go.GetComponent<MeshRenderer>();
                        if (mr != null && mr.enabled)
                        {
                            var mf = go.GetComponent<MeshFilter>();
                            if ((mf == null || mf.sharedMesh != null) && seen.Add(go.GetInstanceID()))
                                _sceneObjectCache.Add(go);
                        }
                    }
                }

                if (++processed % 300 == 0)
                    yield return null;
            }

            _sceneObjectCache.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            _scanning = false;
            RefreshObjectsList();
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
            UIBuilder.CreateText(_exportDialogPanel, "Enter pack name:", 11, new Color(0.7f, 0.7f, 0.7f, 1f));
            _exportPackInput = UIBuilder.CreateInputField(_exportDialogPanel, "Pack name", _packName, null, 240, 22);

            var row = UIBuilder.CreatePanel("ExportDialogRow", _exportDialogPanel, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 8, 8, false, false);
            UIBuilder.AddLayoutElement(row, null, 32, null, 32, null, null);

            UIBuilder.CreateButton(row, "Export", () => ConfirmExport(), 100, 28);
            UIBuilder.CreateButton(row, "Cancel", () => CancelExport(), 100, 28);

            _exportDialogPanel.gameObject.SetActive(false);
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
            _renderDistanceDialogPanel.sizeDelta = new Vector2(340, 140);
            UIBuilder.AddVerticalLayout(_renderDistanceDialogPanel, 12, 8, true, true);

            UIBuilder.CreateText(_renderDistanceDialogPanel, "Vanilla Render Distance", 13, Color.white, FontStyle.Bold);
            UIBuilder.CreateText(_renderDistanceDialogPanel, "Max distance to render vanilla gizmos (0 = unlimited):", 11, new Color(0.7f, 0.7f, 0.7f, 1f));
            _renderDistanceInput = UIBuilder.CreateInputField(_renderDistanceDialogPanel, "meters", (Plugin.VanillaRenderDistance?.Value ?? 50f).ToString("F0", CultureInfo.InvariantCulture), null, 240, 22);

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
            if (_renderDistanceDialogPanel == null || _renderDistanceInput == null) return;
            _renderDistanceInput.text = (Plugin.VanillaRenderDistance?.Value ?? 50f).ToString("F0", CultureInfo.InvariantCulture);
            _renderDistanceDialogPanel.gameObject.SetActive(true);
        }

        private void ConfirmRenderDistance()
        {
            if (_renderDistanceDialogPanel == null || _renderDistanceInput == null) return;
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
            _titleText.text = $"Map Loot Editor Lite - {map} ({count} markers)";
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
                        entries.Add(new MarkerEntry(m, indent: true));
                }
            }

            // Ungrouped items
            foreach (var m in allMarkers.Where(m => string.IsNullOrWhiteSpace(m.group)))
                entries.Add(new MarkerEntry(m, indent: false));

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
                UIBuilder.CreateText(pageRow, $"Page {_hierarchyPage + 1} / {pageCount} ({entries.Count} rows)", 11, Color.white);
                UIBuilder.CreateButton(pageRow, ">", () => { _hierarchyPage++; RequestHierarchyRefresh(); }, 30, 22, 10);
            }

            for (int i = start; i < end; i++)
            {
                if (entries[i] is GroupHeaderEntry g)
                    BuildHierarchyGroupHeader(g);
                else if (entries[i] is MarkerEntry m)
                    BuildHierarchyRow(m.Marker, m.Indent);
            }
        }

        private void BuildHierarchyGroupHeader(GroupHeaderEntry entry)
        {
            var key = entry.Key;
            var grpHdr = UIBuilder.CreatePanel("GrpHdr", _hierarchyContent, new Color(0.18f, 0.2f, 0.28f, 0.9f));
            UIBuilder.AddHorizontalLayout(grpHdr, 3, 1, false, false);
            UIBuilder.AddLayoutElement(grpHdr, null, 18, null, 18, null, 0);
            UIBuilder.CreateLabel(grpHdr, entry.Collapsed ? "\u25ba" : "\u25bc", 10, 12, 14);
            UIBuilder.CreateLabel(grpHdr, $"{key}  ({entry.Count})", 10, 0, 14);
            var capturedKey = key;
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

            UIBuilder.CreateButton(row, "Go", () => controller.GoToMarker(marker), 24, 16, 10);

            bool selected = manager.IsSelected(marker);
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

        private void RefreshInspector()
        {
            if (_inspectorContent == null || manager == null)
                return;
            ClearChildren(_inspectorContent);
            _lastSelected = manager.Selected;
            _lastSelectedCount = manager.SelectedIds.Count;

            var selected = manager.Selected;
            if (selected == null)
            {
                if (_selectedSceneGO != null)
                {
                    UIBuilder.CreateText(_inspectorContent, "Scene Object", 12, Color.white, FontStyle.Bold);

                    var detailsPanel = UIBuilder.CreatePanel("SceneGODetails", _inspectorContent, new Color(0, 0, 0, 0));
                    detailsPanel.GetComponent<Image>().raycastTarget = false;
                    UIBuilder.AddVerticalLayout(detailsPanel, 2, 4, true, true);
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
                            var rt = t.GetComponent<RectTransform>();
                            if (rt != null) rt.sizeDelta = new Vector2(0, 14);
                        }
                    }

                    var actionRow = UIBuilder.CreatePanel("SceneGOActions", _inspectorContent, new Color(0, 0, 0, 0));
                    actionRow.GetComponent<Image>().raycastTarget = false;
                    UIBuilder.AddHorizontalLayout(actionRow, 4, 2, false, false);
                    UIBuilder.AddLayoutElement(actionRow.gameObject, null, 26, null, 26, null, 0);
                    UIBuilder.CreateButton(actionRow, "Place Here", () => controller?.PlaceStaticFromSceneGO(_selectedSceneGO), 80, 22);
                    UIBuilder.CreateButton(actionRow, "Remove", () => RemoveSelectedSceneGO(), 60, 22);
                }
                else
                {
                    UIBuilder.CreateText(_inspectorContent, "No marker selected.", 12, new Color(0.6f, 0.6f, 0.6f, 1f));
                }
                return;
            }

            var selectedCount = manager.SelectedIds.Count;
            UIBuilder.CreateText(_inspectorContent, selectedCount > 1 ? $"Selected {selectedCount} markers (primary: {selected.name})" : $"Selected: {selected.Kind} - {selected.name}", 12, Color.white, FontStyle.Bold);

            if (selected.isVanilla)
            {
                BuildVanillaInspector(selected);
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
                case LightZone zone:
                    BuildLightZone(zone);
                    break;
                case TriggerZone zone:
                    BuildTriggerZone(zone);
                    break;
            }

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Item", () =>
            {
                if (selected is LooseLootSpawn s)
                    previews.SpawnAtMarker(s);
                else if (selected is LootZone z)
                    previews.SpawnAtZoneCenter(z);
            }, 90, 22);
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
                var le = text.GetComponent<LayoutElement>();
                if (le != null)
                    le.preferredHeight = items.Count * 14f;
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
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            var text = UIBuilder.CreateText(row, $"{label}: {value ?? ""}", 11, new Color(0.72f, 0.72f, 0.72f, 1f));
            text.alignment = TextAnchor.MiddleLeft;
        }

        private void BuildLooseLootSpawn(LooseLootSpawn spawn)
        {
            BuildToggleField(_inspectorContent, "Respawnable", spawn.respawnable, (v) => { spawn.respawnable = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Use Gravity", spawn.useGravity, (v) => { spawn.useGravity = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Quest only", spawn.questOnly, (v) => { spawn.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", spawn.questCompleted, (v) => { spawn.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (spawn.questOnly || spawn.questCompleted)
            {
                BuildStringField(_inspectorContent, "Quest ID", spawn.questId ?? "", (v) => { spawn.questId = v; manager.IsDirty = true; });
            }
            BuildItemsList(spawn.items, false, (i) => previews.SpawnAtMarker(spawn, i));
        }

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
                UIBuilder.CreateButton(pickRow, "Cancel", () => { _isPickingSource = false; _pickingSourceTarget = null; RefreshInspector(); }, 54, 22);
            }
            else
            {
                // Row 1: Pick from Scene + Use Parent
                var row1 = UIBuilder.CreatePanel("SourceRow1", _inspectorContent, new Color(0, 0, 0, 0));
                UIBuilder.AddHorizontalLayout(row1, 4, 2, false, false);
                UIBuilder.AddLayoutElement(row1, null, 22, null, 22, null, 0);
                UIBuilder.CreateButton(row1, "Pick from Scene", () => { _isPickingSource = true; _pickingSourceTarget = obj; RefreshInspector(); }, 100, 22);
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
                    UIBuilder.CreateButton(row3, "Clear Source", () => { obj.sourceObjectName = ""; obj.sourceObjectPosition = new TransformData(); manager.IsDirty = true; RefreshInspector(); }, 90, 22);
                    UIBuilder.CreateText(_inspectorContent, $"Source: {obj.sourceObjectName} @ {obj.sourceObjectPosition.x:F2}, {obj.sourceObjectPosition.y:F2}, {obj.sourceObjectPosition.z:F2}", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
                }
            }
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
                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                GUIUtility.systemCopyBuffer = json;
                _fieldClipboard = json;
            }, 120, 24);
        }

        private void BuildInteractiveObject(InteractiveObject obj)
        {
            if (obj.scale == null)
                obj.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            BuildDropdownField(_inspectorContent, "Type", obj.interactiveType.ToString(), new[] { "Door", "Container", "StationaryWeapon" }, (v) =>
            {
                obj.interactiveType = (InteractiveObjectType)System.Enum.Parse(typeof(InteractiveObjectType), v);
                manager.IsDirty = true;
                RefreshInspector();
            });

            BuildSourceObjectControls(obj);
            BuildVector3Field(_inspectorContent, "Scale", obj.scale.ToVector3(), (v) => { obj.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

            if (obj.interactiveType == InteractiveObjectType.Door)
            {
                BuildStringField(_inspectorContent, "Key Template Id", obj.keyId ?? "", (v) => { obj.keyId = v; manager.IsDirty = true; });
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
                BuildDropdownField(_inspectorContent, "Loot Mode", obj.lootMode.ToString(), new[] { "Default", "Hybrid", "Custom" }, (v) =>
                {
                    obj.lootMode = (ContainerLootMode)System.Enum.Parse(typeof(ContainerLootMode), v);
                    manager.IsDirty = true;
                    RefreshInspector();
                });
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

        private void BuildBotSpawnPoint(BotSpawnPoint point)
        {
            var presetNames = BotSpawnPresetMapping.PresetNames.OrderBy(kvp => (int)kvp.Key).Select(kvp => kvp.Value).ToArray();
            var currentName = BotSpawnPresetMapping.PresetNames.ContainsKey(point.preset) ? BotSpawnPresetMapping.PresetNames[point.preset] : point.preset.ToString();
            BuildDropdownField(_inspectorContent, "Bot Type", currentName, presetNames, (v) =>
            {
                var selected = BotSpawnPresetMapping.PresetNames.FirstOrDefault(kvp => kvp.Value == v).Key;
                point.preset = selected;
                BotSpawnPresetMapping.ApplyPreset(selected, point);
                manager.IsDirty = true;
                RefreshInspector();
            });
            BuildReadOnlyLabel(_inspectorContent, "Side", point.side.ToString());
            BuildReadOnlyLabel(_inspectorContent, "Category", point.category.ToString());
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

            BuildToggleField(_inspectorContent, "Quest only", point.questOnly, (v) => { point.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", point.questCompleted, (v) => { point.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (point.questOnly || point.questCompleted)
                BuildStringField(_inspectorContent, "Quest ID", point.questId ?? "", (v) => { point.questId = v; manager.IsDirty = true; });

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Spawn", () => previews.SpawnBotSpawnPreview(point), 100, 22);
        }

        private void BuildBotSpawnZone(BotSpawnZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            var presetNames = BotSpawnPresetMapping.PresetNames.OrderBy(kvp => (int)kvp.Key).Select(kvp => kvp.Value).ToArray();
            var currentName = BotSpawnPresetMapping.PresetNames.ContainsKey(zone.preset) ? BotSpawnPresetMapping.PresetNames[zone.preset] : zone.preset.ToString();
            BuildDropdownField(_inspectorContent, "Bot Type", currentName, presetNames, (v) =>
            {
                var selected = BotSpawnPresetMapping.PresetNames.FirstOrDefault(kvp => kvp.Value == v).Key;
                zone.preset = selected;
                BotSpawnPresetMapping.ApplyPreset(selected, zone);
                manager.IsDirty = true;
                RefreshInspector();
            });
            BuildReadOnlyLabel(_inspectorContent, "Side", zone.side.ToString());
            BuildReadOnlyLabel(_inspectorContent, "Category", zone.category.ToString());
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

            BuildToggleField(_inspectorContent, "Quest only", zone.questOnly, (v) => { zone.questOnly = v; manager.IsDirty = true; RefreshInspector(); });
            BuildToggleField(_inspectorContent, "Quest completed", zone.questCompleted, (v) => { zone.questCompleted = v; manager.IsDirty = true; RefreshInspector(); });
            if (zone.questOnly || zone.questCompleted)
                BuildStringField(_inspectorContent, "Quest ID", zone.questId ?? "", (v) => { zone.questId = v; manager.IsDirty = true; });

            var previewRow = UIBuilder.CreatePanel("PreviewRow", _inspectorContent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(previewRow, 2, 2, false, false);
            UIBuilder.AddLayoutElement(previewRow, null, 22, null, 22, null, 0);
            UIBuilder.CreateButton(previewRow, "Preview Spawns", () => previews.SpawnBotSpawnZonePreview(zone), 110, 22);
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

        public static readonly (string id, string name)[] LootContainerTemplates = new (string, string)[]
        {
            ("566966cd4bdc2d0c4c8b4578", "Box full of junk"),
            ("5d6d2bb386f774785b07a77a", "Buried barrel cache"),
            ("578f879c24597735401e6bc6", "Cash register"),
            ("5ad74cf586f774391278f6f0", "Cash register TAR2-2"),
            ("5d07b91b86f7745a077a9432", "Common fund stash"),
            ("5909e4b686f7747f5b744fa4", "Dead Scav"),
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

        private void BuildDropdownField(RectTransform parent, string label, string value, string[] options, UnityAction<string> onChanged)
        {
            var row = UIBuilder.CreatePanel("DropdownField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            int dropdownWidth = 94;
            if (!string.IsNullOrEmpty(label))
                dropdownWidth = 160;
            UIBuilder.AddLayoutElement(row, null, 24, dropdownWidth, 24, null, 0);
            if (!string.IsNullOrEmpty(label))
                UIBuilder.CreateLabel(row, label, 11, 60, 24);
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
                UIBuilder.CreateText(_inspectorContent, $"Page {_itemsListPage + 1} / {pageCount} ({items.Count} items)", 11, Color.white);
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
                var tplField = BuildInputFieldInline(row, item.template ?? "", (v) => { item.template = v; manager.IsDirty = true; }, 100, 20);
                tplField.onEndEdit.AddListener(_ => RequestInspectorRefresh());
                UIBuilder.CreateLabel(row, "%", 11, 18, 20);
                BuildInputFieldInline(row, item.chance.ToString("F1", CultureInfo.InvariantCulture), (v) => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) item.chance = r; manager.IsDirty = true; }, 40, 20);
                if (onPreview != null)
                    UIBuilder.CreateButton(row, "Prev", () => onPreview(idx), 36, 20, 10);
                UIBuilder.CreateButton(row, "-", () => { items.RemoveAt(idx); manager.IsDirty = true; RefreshInspector(); }, 24, 20, 10);

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

                var name = GetItemName(item.template);
                if (!string.IsNullOrEmpty(name))
                    UIBuilder.CreateText(_inspectorContent, $"  {name}", 11, new Color(0.6f, 0.6f, 0.6f, 1f));

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
            }

            UIBuilder.CreateButton(_inspectorContent, "Add Item", () => { items.Add(new LootItem()); manager.IsDirty = true; RefreshInspector(); }, 80, 22);
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
            if (groups.Count == 0)
            {
                UIBuilder.CreateText(_groupsContent, "No groups.", 11, new Color(0.6f, 0.6f, 0.6f, 1f));
                return;
            }
            foreach (var group in groups)
            {
                var capturedGroup = group;
                var row = UIBuilder.CreatePanel("GroupRow", _groupsContent, new Color(0.15f, 0.15f, 0.15f, 0.4f));
                UIBuilder.AddHorizontalLayout(row, 2, 2, true, false);
                UIBuilder.AddLayoutElement(row, null, 20, null, 20, null, 0);
                var lbl = UIBuilder.CreateLabel(row, capturedGroup, 11, 0, 18);
                UIBuilder.AddLayoutElement(lbl.gameObject, null, 18, null, 18, 1, 0);
                UIBuilder.CreateButton(row, "Select", () => { manager.SelectGroup(capturedGroup); RequestInspectorRefresh(); }, 50, 18, 10);
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

        private void BuildStringField(RectTransform parent, string label, string value, UnityAction<string> onChanged)
        {
            var row = UIBuilder.CreatePanel("StringField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, true, true);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, 60, 22);
            var inp = UIBuilder.CreateInputField(row, label, value, (v) => onChanged?.Invoke(v), 0, 22);
            UIBuilder.AddLayoutElement(inp.gameObject, null, null, null, null, 1, null);
            AddInputFieldContextMenu(inp);
        }

        private void BuildStringFieldWithButton(RectTransform parent, string label, string value, UnityAction<string> onChanged, string buttonLabel, UnityAction onButtonClick)
        {
            var row = UIBuilder.CreatePanel("StringFieldWithButton", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, true, true);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, 60, 22);
            var inp = UIBuilder.CreateInputField(row, label, value, (v) => onChanged?.Invoke(v), 0, 22);
            UIBuilder.AddLayoutElement(inp.gameObject, null, null, null, null, 1, null);
            AddInputFieldContextMenu(inp);
            UIBuilder.CreateButton(row, buttonLabel, onButtonClick, 40, 22);
        }

        private void BuildFloatField(RectTransform parent, string label, float value, UnityAction<float> onChanged)
        {
            var row = UIBuilder.CreatePanel("FloatField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, 60, 22);
            var inp = UIBuilder.CreateInputField(row, label, value.ToString("F3", CultureInfo.InvariantCulture), (v) =>
            {
                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                    onChanged?.Invoke(r);
            }, 80, 22);
            AddInputFieldContextMenu(inp);
        }

        private void BuildIntField(RectTransform parent, string label, int value, UnityAction<int> onChanged)
        {
            var row = UIBuilder.CreatePanel("IntField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 22, null, 22, null, 0);
            UIBuilder.CreateLabel(row, label, 11, 60, 22);
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
                var presetNames = BotSpawnPresetMapping.PresetNames.OrderBy(kvp => (int)kvp.Key).Select(kvp => kvp.Value).ToArray();
                var currentName = BotSpawnPresetMapping.PresetNames.ContainsKey(group.preset) ? BotSpawnPresetMapping.PresetNames[group.preset] : group.preset.ToString();
                BuildDropdownField(_inspectorContent, "Preset", currentName, presetNames, (v) =>
                {
                    var selected = BotSpawnPresetMapping.PresetNames.FirstOrDefault(kvp => kvp.Value == v).Key;
                    group.preset = selected;
                    BotSpawnPresetMapping.ApplyPreset(selected, group);
                    manager.IsDirty = true;
                    RefreshInspector();
                });
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

            var factory = Singleton<ItemFactoryClass>.Instance;
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
                _deleteConfirmText.text = $"Delete {manager.SelectedIds.Count} marker(s)?";
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
                    UIBuilder.CreateText(_scatterPickRow, $"  {_scatterPrefabPath}", 10, new Color(0.5f, 0.85f, 0.5f, 1f));
            }
        }

        public GameObject TryPickSourceSceneObject() => PickSceneObjectAtMouse();

        public void SetPickingSource(bool picking, IHasSourceObject target = null)
        {
            _isPickingSource = picking;
            _pickingSourceTarget = target;
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

        private GameObject PickSceneObjectAtMouse()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Plugin.Log.LogWarning("No main camera found for scene picking.");
                return null;
            }
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                var picked = hit.transform.gameObject;
                if (_pickUseParent && picked.transform.parent != null)
                {
                    var parent = picked.transform.parent.gameObject;
                    if (!IsEditorObject(parent))
                        picked = parent;
                }

                if (IsEditorObject(picked))
                {
                    Plugin.Log.LogInfo($"Scene picker ignored editor-owned object '{picked.name}'.");
                    return null;
                }

                Plugin.Log.LogInfo($"Picked scene object: {picked.name} at {picked.transform.position}");
                return picked;
            }
            Plugin.Log.LogWarning("Scene picker raycast did not hit anything.");
            return null;
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
                btn.GetComponent<Image>().color = isSelected ? new Color(0.25f, 0.45f, 0.75f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);
            }
        }

        public void UpdateEditorModeButton()
        {
            if (_editorModeButton != null)
            {
                var text = _editorModeButton.GetComponentInChildren<Text>();
                if (text != null)
                    text.text = controller.IsFreeCam ? "Exit Editor Mode" : "Editor Mode";
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
    }
}
