using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Comfort.Common;
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
        private RectTransform _deleteConfirmPanel;
        private RectTransform _exportDialogPanel;
        private RectTransform _contextMenuPanel;
        private string _fieldClipboard = "";

        private RectTransform _hierarchyContent;
        private RectTransform _inspectorContent;
        private RectTransform _prefabsContent;
        private RectTransform _groupsContent;

        private Text _titleText;
        private Text _deleteConfirmText;
        private InputField _exportPackInput;
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

        private bool _isVisible;
        private bool _isDeletePending;
        private bool _isDeleteConfirmed;
        private bool _isPickingSource;
        private StaticObject _pickingSourceTarget;
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
        private readonly List<GameObject> _sceneObjectCache = new List<GameObject>();
        private GameObject _selectedSceneGO;
        private StaticObject _goListTarget;

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
        public bool IsDeletePending => _isDeletePending;
        public bool IsDeleteConfirmed => _isDeleteConfirmed;
        public bool IsVisible => _isVisible;
        public string PackName => _packName;

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
            BuildUI();
            Hide();
        }

        private void Start()
        {
            UIBuilder.EnsureEventSystem(transform);
        }

        private void Update()
        {
            if (!_isVisible)
                return;

            UpdateCanvasScale();
            UpdateGizmoButtons();
            UpdateEditorModeButton();

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

            BuildTopPanel();
            BuildHierarchyPanel();
            BuildInspectorPanel();
            BuildBottomPanel();
            BuildDeleteConfirm();
            BuildExportDialog();
            BuildContextMenu();
            BuildResizeHandles();

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
                new MenuItem("Export As", () => ShowExportDialog())
            }, 40, 22);
            BuildMenuButton(row1, "Add Spawn", new List<MenuItem>
            {
                new MenuItem("Common", subItems: new List<MenuItem>
                {
                    new MenuItem("Loot Spawn", () => controller.CreateLootSpawn()),
                    new MenuItem("Loot Zone", () => controller.CreateLootZone()),
                    new MenuItem("Static Object", () => controller.CreateStaticObject())
                }),
                new MenuItem("WTT", subItems: new List<MenuItem>
                {
                    new MenuItem("WTT Quest Area", () => controller.CreateWTTQuestZone()),
                    new MenuItem("WTT Static Object", () => { Plugin.Log.LogInfo("WTT Static Object selected (placeholder)"); })
                })
            }, 84, 22);
            UIBuilder.CreateButton(row1, "Snap",         () => controller.SnapSelected(),     46, 22);
            UIBuilder.CreateButton(row1, "Duplicate",    () => controller.DuplicateSelected(),68, 22);
            UIBuilder.CreateButton(row1, "Delete",       () => RequestDelete(),               54, 22);
            UIBuilder.CreateButton(row1, "Clear Prev",   () => controller.ClearPreviews(),    76, 22);
            _editorModeButton = UIBuilder.CreateButton(row1, "Editor Mode", () => controller.ToggleFreeCam(), 92, 22);
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

            // Scroll — second child, fills remaining height
            var scroll = UIBuilder.CreateScrollView(_hierarchyPanel, out _hierarchyContent, out _, 0, 0, 14);
            UIBuilder.AddLayoutElement(scroll.gameObject, null, null, null, null, null, 1);
            UIBuilder.AddVerticalLayout(_hierarchyContent, 1, 2, true, true);
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
            _prefabsTabButton  = UIBuilder.CreateButton(tabBar, "Prefabs",      () => SelectBottomTab(0), 70, 22);
            _scatterTabButton  = UIBuilder.CreateButton(tabBar, "Scatter",      () => SelectBottomTab(1), 70, 22);
            _groupsTabButton   = UIBuilder.CreateButton(tabBar, "Groups",       () => SelectBottomTab(2), 70, 22);
            _objectsTabButton  = UIBuilder.CreateButton(tabBar, "GameObjects",  () => SelectBottomTab(3), 90, 22);

            var tabContent = UIBuilder.CreatePanel("TabContent", _bottomPanel, new Color(0, 0, 0, 0));
            tabContent.anchorMin = Vector2.zero;
            tabContent.anchorMax = Vector2.one;
            tabContent.offsetMin = Vector2.zero;
            tabContent.offsetMax = Vector2.zero;
            UIBuilder.AddLayoutElement(tabContent, null, null, null, null, null, 1);

            BuildPrefabsTab(tabContent);
            BuildScatterTab(tabContent);
            BuildGroupsTab(tabContent);
            BuildObjectsTab(tabContent);

            SelectBottomTab(0);
        }

        private void SelectBottomTab(int index)
        {
            _activeBottomTab = index;
            _prefabsTab.gameObject.SetActive(index == 0);
            _scatterTab.gameObject.SetActive(index == 1);
            _groupsTab.gameObject.SetActive(index == 2);
            if (_objectsTab != null) _objectsTab.gameObject.SetActive(index == 3);

            UpdateTabButton(_prefabsTabButton, index == 0);
            UpdateTabButton(_scatterTabButton, index == 1);
            UpdateTabButton(_groupsTabButton, index == 2);
            if (_objectsTabButton != null) UpdateTabButton(_objectsTabButton, index == 3);
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

        private void RefreshGOActionRow()
        {
            if (_goActionBtnRow == null) return;
            ClearChildren(_goActionBtnRow);

            if (_selectedSceneGO == null)
            {
                UIBuilder.CreateLabel(_goActionBtnRow, "Select an object", 10, 120, 22);
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
            }
            else
            {
                UIBuilder.CreateButton(_goActionBtnRow, "Place Here", () =>
                {
                    controller?.PlaceStaticFromSceneGO(_selectedSceneGO);
                }, 80, 22);
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

                if (!go.name.StartsWith("MLE_") && go.GetComponent<PreviewLootMarker>() == null)
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
            RefreshTitle();
            RefreshHierarchy();
            RefreshInspector();
            RefreshPrefabs();
            RefreshGroups();
            UpdateGizmoButtons();
            UpdateEditorModeButton();
            _lastMarkerCount = manager.GetAllMarkers().Count();
        }

        private void RefreshTitle()
        {
            var map = manager.Data?.map ?? "none";
            var count = manager.GetAllMarkers().Count();
            _titleText.text = $"Map Loot Editor Lite - {map} ({count} markers)";
        }

        private void RefreshHierarchy()
        {
            if (_hierarchyContent == null || manager == null)
                return;
            ClearChildren(_hierarchyContent);

            var allMarkers = manager.GetAllMarkers().Where(MatchesSearch).ToList();

            // Grouped sections (collapsible)
            var grouped = allMarkers
                .Where(m => !string.IsNullOrWhiteSpace(m.group))
                .GroupBy(m => m.group)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var grp in grouped)
            {
                var key = grp.Key;
                bool collapsed = _collapsedGroups.Contains(key);

                var grpHdr = UIBuilder.CreatePanel("GrpHdr", _hierarchyContent, new Color(0.18f, 0.2f, 0.28f, 0.9f));
                UIBuilder.AddHorizontalLayout(grpHdr, 3, 1, false, false);
                UIBuilder.AddLayoutElement(grpHdr, null, 18, null, 18, null, 0);
                UIBuilder.CreateLabel(grpHdr, collapsed ? "\u25ba" : "\u25bc", 10, 12, 14);
                UIBuilder.CreateLabel(grpHdr, $"{key}  ({grp.Count()})", 10, 0, 14);
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

                if (!collapsed)
                    foreach (var m in grp)
                        BuildHierarchyRow(m, indent: true);
            }

            // Ungrouped items
            foreach (var m in allMarkers.Where(m => string.IsNullOrWhiteSpace(m.group)))
                BuildHierarchyRow(m, indent: false);
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
                $"{marker.Kind} | {marker.name}",
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
                UIBuilder.CreateText(_inspectorContent, "No marker selected.", 12, new Color(0.6f, 0.6f, 0.6f, 1f));
                return;
            }

            var selectedCount = manager.SelectedIds.Count;
            UIBuilder.CreateText(_inspectorContent, selectedCount > 1 ? $"Selected {selectedCount} markers (primary: {selected.name})" : $"Selected: {selected.Kind} - {selected.name}", 12, Color.white, FontStyle.Bold);

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

        private void BuildLooseLootSpawn(LooseLootSpawn spawn)
        {
            BuildToggleField(_inspectorContent, "Respawnable", spawn.respawnable, (v) => { spawn.respawnable = v; manager.IsDirty = true; });
            BuildToggleField(_inspectorContent, "Use Gravity", spawn.useGravity, (v) => { spawn.useGravity = v; manager.IsDirty = true; });
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
            BuildItemsList(zone.items, true, (i) => previews.SpawnAtZoneCenter(zone, i));
        }

        private void BuildStaticObject(StaticObject obj)
        {
            BuildStringField(_inspectorContent, "Prefab Path", obj.prefabPath ?? "", (v) => { obj.prefabPath = v; manager.IsDirty = true; });
            BuildVector3Field(_inspectorContent, "Scale", obj.scale.ToVector3(), (v) => { obj.scale = TransformData.FromVector3(v); manager.IsDirty = true; });

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

            UIBuilder.CreateButton(_inspectorContent, "Preview Object", () => previews.SpawnStaticPreview(obj), 100, 24);
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

        private void BuildDropdownField(RectTransform parent, string label, string value, string[] options, UnityAction<string> onChanged)
        {
            var row = UIBuilder.CreatePanel("DropdownField", parent, new Color(0, 0, 0, 0));
            UIBuilder.AddHorizontalLayout(row, 2, 2, false, false);
            UIBuilder.AddLayoutElement(row, null, 24, null, 24, null, 0);
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

            for (int i = 0; i < items.Count; i++)
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

        private void ClearChildren(RectTransform parent)
        {
            if (parent == null)
                return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
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
            _isDeletePending = true;
            if (_deleteConfirmPanel != null)
                _deleteConfirmPanel.gameObject.SetActive(true);
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

        public StaticObject PickingSourceTarget => _pickingSourceTarget;

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

        public void SetPickingSource(bool picking, StaticObject target = null)
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
                    picked = picked.transform.parent.gameObject;
                Plugin.Log.LogInfo($"Picked scene object: {picked.name} at {picked.transform.position}");
                return picked;
            }
            Plugin.Log.LogWarning("Scene picker raycast did not hit anything.");
            return null;
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
