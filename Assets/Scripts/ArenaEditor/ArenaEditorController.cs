using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeonStrikers.Arenas;
using UnityEngine;

/// <summary>
/// In-game arena editor (ArenaEditor scene). Everything is drawn with IMGUI so it works in builds with
/// mouse or touchpad:
///  * left: palette of objects to add, grid options
///  * centre: the arena (same units as the game, 19 x 10), select / move / resize / rotate / box-select
///  * right: inspector of the selection and the live "Arena check" (validation) list
///  * top: name, New / Open / Save, Undo / Redo, Playtest, Use as 4th Arena, Help
/// The edited arena lives in <see cref="CustomArenaSession.EditorDocument"/> so a playtest never loses it.
/// </summary>
public class ArenaEditorController : MonoBehaviour
{
    enum DragMode { None, Move, Resize, Rotate, Marquee }

    enum DialogKind { None, Browser, ConfirmDiscard, Message, ConfirmPlaytest, ConfirmDeleteArena, Help }

    struct PaletteItem
    {
        public string Label;
        public string Tooltip;
        public ArenaObjectType Type;
        public bool PortalPair;
        public Color Color;
    }

    const float TopBarHeight = 52f;
    const float LeftPanelWidth = 200f;
    const float RightPanelWidth = 330f;
    const float StatusBarHeight = 30f;
    const float Gap = 14f;
    const float HandleSize = 11f;
    const float RotateHandleDistance = 30f;
    const float DuplicateOffset = 0.5f;
    const float SnapStep = 0.25f;
    const float RotationStep = 15f;
    const float DragThreshold = 3f;

    static readonly int CanvasControlHint = "NeonStrikersArenaCanvas".GetHashCode();

    static readonly PaletteItem[] Palette =
    {
        new PaletteItem { Label = "Wall", Type = ArenaObjectType.Wall, Color = new Color(0.95f, 0.95f, 0.95f),
            Tooltip = "Wall: solid, blocks players and the ball. Resize it to any length." },
        new PaletteItem { Label = "Platform", Type = ArenaObjectType.Platform, Color = new Color(0.3f, 0.95f, 1f),
            Tooltip = "Platform: solid ledge to stand and bounce on (same physics as walls)." },
        new PaletteItem { Label = "Portal Pair", Type = ArenaObjectType.Portal, PortalPair = true, Color = new Color(0.75f, 0.35f, 1f),
            Tooltip = "Portal Pair: two connected portals. The arrow shows where players and the ball come out." },
        new PaletteItem { Label = "Single Portal", Type = ArenaObjectType.Portal, Color = new Color(0.6f, 0.6f, 0.6f),
            Tooltip = "Single Portal: one unconnected portal. Link it to another portal in the right panel." },
        new PaletteItem { Label = "Blue Goal", Type = ArenaObjectType.BlueGoal, Color = new Color(0.25f, 0.55f, 1f),
            Tooltip = "Blue Goal: the goal Blue defends. Red scores when the ball enters it." },
        new PaletteItem { Label = "Red Goal", Type = ArenaObjectType.RedGoal, Color = new Color(1f, 0.3f, 0.35f),
            Tooltip = "Red Goal: the goal Red defends. Blue scores when the ball enters it." },
        new PaletteItem { Label = "Slow Zone", Type = ArenaObjectType.SlowZone, Color = new Color(0.2f, 1f, 0.85f),
            Tooltip = "Slow Zone: slows the ball and players slightly and slows time while the ball is inside." },
        new PaletteItem { Label = "Ball", Type = ArenaObjectType.Ball, Color = Color.white,
            Tooltip = "Ball: where the ball starts every round. There is only one; placing it again moves it." },
        new PaletteItem { Label = "Blue Spawn", Type = ArenaObjectType.BlueSpawn, Color = new Color(0.2f, 0.55f, 1f),
            Tooltip = "Blue Spawn: where Blue starts every round (only one; placing again moves it)." },
        new PaletteItem { Label = "Red Spawn", Type = ArenaObjectType.RedSpawn, Color = new Color(1f, 0.3f, 0.35f),
            Tooltip = "Red Spawn: where Red starts every round (only one; placing again moves it)." }
    };

    static readonly Color[] PortalPairColors =
    {
        new Color(0.75f, 0.35f, 1f), new Color(1f, 0.6f, 0.1f), new Color(0.3f, 1f, 0.45f),
        new Color(1f, 0.95f, 0.25f), new Color(0.2f, 0.85f, 1f), new Color(1f, 0.4f, 0.75f)
    };

    static readonly Color BackgroundColor = new Color(0.02f, 0.025f, 0.06f, 1f);
    static readonly Color ArenaColor = new Color(0.085f, 0.079f, 0.079f, 1f);
    static readonly Color SelectionColor = new Color(1f, 0.88f, 0.2f, 1f);
    static readonly Color ProblemColor = new Color(1f, 0.2f, 0.25f, 1f);
    static readonly Color UnpairedPortalColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    static readonly Vector2Int[] BoxHandles =
    {
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(1, 0),
        new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 0)
    };

    static readonly Vector2Int[] CircleHandles =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1)
    };

    // ------------------------------------------------------------------ state

    ArenaEditorDocument doc;
    ArenaStorage storage;
    ArenaValidationReport report;
    int reportRevision = -1;
    string flowArenaId = "";
    string flowArenaName = "";

    int paletteIndex = -1;
    int linkSourceId;
    bool snapToGrid = true;
    bool showGrid = true;

    DragMode dragMode;
    bool dragMoved;
    Vector2 dragStartGui;
    Vector2 dragStartWorld;
    int dragObjectId;
    ArenaObjectData dragStartObject;
    Vector2Int dragHandle;
    readonly Dictionary<int, Vector2> dragStartPositions = new Dictionary<int, Vector2>();
    readonly List<int> marqueeBaseSelection = new List<int>();
    Vector2 marqueeEndGui;

    float uiScale = 1f;
    float screenW;
    float screenH;
    Rect canvasRect;
    float ppu = 50f;
    Vector2 mouseGui;
    bool uiEnabled = true;

    DialogKind dialog;
    string dialogTitle = "";
    string dialogBody = "";
    readonly List<string> dialogLines = new List<string>();
    Action discardContinuation;
    string pendingDeleteArenaId = "";
    string pendingDeleteArenaName = "";
    List<ArenaFileEntry> browserEntries = new List<ArenaFileEntry>();
    readonly Dictionary<string, ArenaValidationReport> browserReports = new Dictionary<string, ArenaValidationReport>();
    Vector2 browserScroll;
    Vector2 issuesScroll;
    Vector2 inspectorScroll;
    Vector2 dialogScroll;

    string toast = "";
    float toastUntil;
    bool toastIsWarning;
    string hoverTooltip = "";

    readonly List<Action> deferred = new List<Action>();
    readonly Dictionary<string, string> fieldBuffers = new Dictionary<string, string>();

    Texture2D circleTexture;
    readonly List<Texture2D> ownedTextures = new List<Texture2D>();
    bool stylesReady;
    GUIStyle panelStyle;
    GUIStyle dialogStyle;
    GUIStyle buttonStyle;
    GUIStyle accentButtonStyle;
    GUIStyle dangerButtonStyle;
    GUIStyle smallButtonStyle;
    GUIStyle paletteStyle;
    GUIStyle paletteActiveStyle;
    GUIStyle headerStyle;
    GUIStyle titleStyle;
    GUIStyle labelStyle;
    GUIStyle smallLabelStyle;
    GUIStyle wrapLabelStyle;
    GUIStyle okLabelStyle;
    GUIStyle warnLabelStyle;
    GUIStyle errorLabelStyle;
    GUIStyle canvasLabelStyle;
    GUIStyle statusStyle;
    GUIStyle statusRightStyle;
    GUIStyle textFieldStyle;
    GUIStyle toggleStyle;
    GUIStyle toastStyle;
    GUIStyle toastWarnStyle;
    GUIStyle rowStyle;
    GUIStyle[] issueStyles;

    // ------------------------------------------------------------------ public API (also used by tests)

    public ArenaEditorDocument Document => doc;
    public ArenaValidationReport Report => report;
    public ArenaStorage Storage => storage;
    public bool HasOpenDialog => dialog != DialogKind.None;
    public string LastMessage => toast;

    void Start()
    {
        Time.timeScale = 1f;
        storage = ArenaStorage.Default;
        RefreshFlowInfo();

        ArenaEditorDocument existing = CustomArenaSession.EditorDocument;
        bool firstVisit = existing == null;
        SetDocument(firstVisit ? NewDocument(true) : existing);

        if (!string.IsNullOrEmpty(CustomArenaSession.PendingEditorMessage))
        {
            ShowToast(CustomArenaSession.PendingEditorMessage, false);
            CustomArenaSession.PendingEditorMessage = null;
        }

        if (firstVisit)
        {
            RefreshBrowser();
            if (browserEntries.Count > 0)
                dialog = DialogKind.Browser;
            else
                ShowToast("A starter arena is ready. Change it, or press Help to learn the controls.", false);
        }
    }

    void Update()
    {
        if (doc == null)
            return;

        if (!doc.IsInteracting && doc.Revision != reportRevision)
            Validate();

        // Mouse released outside the window: finish the drag anyway.
        if (dragMode != DragMode.None && !Input.GetMouseButton(0))
            FinishDrag();
    }

    void OnDestroy()
    {
        foreach (Texture2D texture in ownedTextures)
        {
            if (texture != null)
                Destroy(texture);
        }

        ownedTextures.Clear();
    }

    void SetDocument(ArenaEditorDocument document)
    {
        doc = document;
        CustomArenaSession.EditorDocument = document;
        paletteIndex = -1;
        linkSourceId = 0;
        dragMode = DragMode.None;
        fieldBuffers.Clear();
        Validate();
    }

    void Validate()
    {
        report = ArenaValidator.Validate(doc.Data);
        reportRevision = doc.Revision;
    }

    void RefreshFlowInfo()
    {
        try
        {
            flowArenaId = storage.GetGameFlowArenaId();
            flowArenaName = "";
            if (!string.IsNullOrEmpty(flowArenaId))
            {
                flowArenaName = storage.TryLoad(flowArenaId, out ArenaData data, out _) ? data.DisplayName : "(unreadable file)";
            }
        }
        catch (Exception e)
        {
            flowArenaId = "";
            flowArenaName = "";
            Debug.LogWarning("[ArenaEditor] Couldn't read the game flow settings: " + e.Message);
        }
    }

    void RefreshBrowser()
    {
        browserReports.Clear();
        try
        {
            browserEntries = storage.ListArenas();
        }
        catch (Exception e)
        {
            browserEntries = new List<ArenaFileEntry>();
            ShowMessage("Couldn't read your arenas", e.Message, null);
        }

        foreach (ArenaFileEntry entry in browserEntries)
        {
            if (!entry.IsBroken)
                browserReports[entry.Id] = ArenaValidator.Validate(entry.Data);
        }

        RefreshFlowInfo();
    }

    bool IsCurrentInFlow => !doc.IsNew && doc.Data.id == flowArenaId;

    // ------------------------------------------------------------------ actions

    void Defer(Action action)
    {
        if (action != null)
            deferred.Add(action);
    }

    void RunDeferred()
    {
        if (deferred.Count == 0)
            return;

        List<Action> actions = new List<Action>(deferred);
        deferred.Clear();
        foreach (Action action in actions)
            action();
    }

    void ShowToast(string message, bool warning)
    {
        toast = message;
        toastIsWarning = warning;
        toastUntil = Time.unscaledTime + 4f;
    }

    void ShowMessage(string title, string body, IEnumerable<string> lines)
    {
        dialogTitle = title;
        dialogBody = body;
        dialogLines.Clear();
        if (lines != null)
            dialogLines.AddRange(lines);
        dialogScroll = Vector2.zero;
        dialog = DialogKind.Message;
    }

    static IEnumerable<string> IssueLines(ArenaValidationReport validation, bool includeErrors)
    {
        return validation.Issues
            .Where(i => i.Severity == ArenaIssueSeverity.Blocker || (includeErrors && i.Severity == ArenaIssueSeverity.Error))
            .Select(i => i.Message);
    }

    void ConfirmDiscardThen(Action continuation)
    {
        CancelModes();
        if (!doc.IsDirty)
        {
            dialog = DialogKind.None;
            continuation?.Invoke();
            return;
        }

        discardContinuation = continuation;
        dialog = DialogKind.ConfirmDiscard;
    }

    void CancelModes()
    {
        if (dragMode != DragMode.None)
        {
            doc.CancelInteraction();
            dragMode = DragMode.None;
        }

        paletteIndex = -1;
        linkSourceId = 0;
    }

    /// <summary>A fresh arena counts as "nothing to lose" until the user changes it (no discard prompt).</summary>
    static ArenaEditorDocument NewDocument(bool starterLayout)
    {
        ArenaData data = starterLayout ? ArenaFactory.CreateStarter() : ArenaFactory.CreateEmpty();
        return new ArenaEditorDocument(data, true);
    }

    public void NewArena(bool starterLayout)
    {
        SetDocument(NewDocument(starterLayout));
        dialog = DialogKind.None;
        ShowToast(starterLayout ? "New arena created from the starter layout." : "New empty arena created.", false);
    }

    public bool OpenArena(string id)
    {
        if (!storage.TryLoad(id, out ArenaData data, out string error))
        {
            ShowMessage("This arena can't be opened", error, null);
            return false;
        }

        SetDocument(new ArenaEditorDocument(data, true));
        dialog = DialogKind.None;
        ShowToast("Opened '" + data.DisplayName + "'.", false);
        return true;
    }

    /// <summary>Writes the arena to disk. Returns false (and explains why) on failure.</summary>
    public bool SaveCurrent(bool announce)
    {
        CancelModes();
        try
        {
            ArenaData stored = storage.Save(doc.Data.Clone());
            doc.MarkSaved(stored);
            Validate();

            if (stored.id == flowArenaId && !report.CanUseInGameFlow)
            {
                ShowToast("Saved. This is the 4th arena but it has problems, so the game skips it until they are fixed.", true);
            }
            else if (announce)
            {
                ShowToast("Saved '" + stored.DisplayName + "'. It stays available after restarting the game.", false);
            }

            RefreshFlowInfo();
            return true;
        }
        catch (Exception e)
        {
            ShowMessage("Couldn't save the arena", e.Message, null);
            return false;
        }
    }

    public void Undo()
    {
        dragMode = DragMode.None;
        string label = doc.UndoLabel;
        if (doc.Undo())
            ShowToast("Undo: " + label, false);
    }

    public void Redo()
    {
        dragMode = DragMode.None;
        string label = doc.RedoLabel;
        if (doc.Redo())
            ShowToast("Redo: " + label, false);
    }

    public void DuplicateSelection()
    {
        if (doc.Selection.Count == 0)
            return;

        List<ArenaObjectData> copies = doc.DuplicateSelection(new Vector2(DuplicateOffset, -DuplicateOffset));
        int unpaired = copies.Count(c => c != null && c.IsPortal && c.linkedPortalId == 0);
        ShowToast(unpaired > 0
            ? "Duplicated. " + unpaired + " portal copy(s) are not connected yet - see Arena check."
            : "Duplicated " + copies.Count + " object(s).", unpaired > 0);
    }

    public void DeleteSelection()
    {
        int count = doc.DeleteSelection();
        if (count > 0)
            ShowToast("Deleted " + count + " object(s). Ctrl+Z brings them back.", false);
    }

    /// <summary>Playtest button: blockers prevent it, errors ask for confirmation.</summary>
    public void Playtest()
    {
        CancelModes();
        Validate();

        if (!report.CanPlaytest)
        {
            ShowMessage("Fix these before playtesting", "The arena can't be played yet:", IssueLines(report, false));
            return;
        }

        if (report.HasErrors)
        {
            dialogScroll = Vector2.zero;
            dialog = DialogKind.ConfirmPlaytest;
            return;
        }

        StartPlaytest();
    }

    public void StartPlaytest()
    {
        CancelModes();
        doc.EndInteraction();
        CustomArenaSession.EditorDocument = doc;
        ArenaEditorNavigation.StartPlaytest(doc.Data);
    }

    /// <summary>Saves (if needed) and makes the current arena the 4th arena of the game flow.</summary>
    public bool UseCurrentInGameFlow()
    {
        CancelModes();
        Validate();

        if (!report.CanUseInGameFlow)
        {
            ShowMessage("Fix these problems first", "Only arenas without problems can be added to the game:", IssueLines(report, true));
            return false;
        }

        if ((doc.IsDirty || doc.IsNew) && !SaveCurrent(false))
            return false;

        return AssignToGameFlow(doc.Data.id, doc.Data.DisplayName);
    }

    bool AssignToGameFlow(string id, string name)
    {
        try
        {
            string previousName = flowArenaName;
            string previousId = flowArenaId;
            storage.SetGameFlowArena(id);
            RefreshFlowInfo();

            string body = "'" + name + "' is now the 4th arena. It is played after Arena 3 in Two Players and Vs Computer.";
            if (!string.IsNullOrEmpty(previousId) && previousId != id)
                body += "\n\nIt replaces '" + previousName + "', which is still saved and can be opened any time.";

            ShowMessage("Added to the game", body, null);
            return true;
        }
        catch (Exception e)
        {
            ShowMessage("Couldn't update the game flow", e.Message, null);
            return false;
        }
    }

    public void RemoveFromGameFlow()
    {
        try
        {
            storage.ClearGameFlowArena();
            RefreshFlowInfo();
            ShowToast("The game is back to the original three arenas. Your arena is still saved.", false);
        }
        catch (Exception e)
        {
            ShowMessage("Couldn't update the game flow", e.Message, null);
        }
    }

    void BackToMenu()
    {
        ConfirmDiscardThen(ArenaEditorNavigation.LeaveEditorToMainMenu);
    }

    void OpenBrowser()
    {
        CancelModes();
        RefreshBrowser();
        browserScroll = Vector2.zero;
        dialog = DialogKind.Browser;
    }

    void DeleteArenaFile(string id)
    {
        try
        {
            storage.Delete(id);
            if (doc.Data.id == id)
            {
                doc.ForgetFile();
                ShowToast("Arena file deleted. The open copy is now unsaved.", true);
            }
            else
            {
                ShowToast("Arena deleted.", false);
            }
        }
        catch (Exception e)
        {
            ShowMessage("Couldn't delete the arena", e.Message, null);
            return;
        }

        RefreshBrowser();
        dialog = DialogKind.Browser;
    }

    void PlaceFromPalette(Vector2 world, bool keepTool)
    {
        PaletteItem item = Palette[paletteIndex];
        Vector2 p = ClampToBounds(snapToGrid ? ArenaRules.Snap(world, SnapStep) : world);

        if (item.PortalPair)
        {
            Vector2 other = Mathf.Abs(p.x) < 1.5f ? p + new Vector2(3f, 0f) : new Vector2(-p.x, p.y);
            other = ClampToBounds(other);
            float firstRotation = p.x <= other.x ? -90f : 90f;
            doc.AddPortalPair(p, other, firstRotation, -firstRotation);
            ShowToast("Portal pair added. Arrows show where things come out; rotate a portal to change it.", false);
        }
        else
        {
            doc.Place(item.Type, p);
        }

        if (!keepTool)
            paletteIndex = -1;
    }

    static Vector2 ClampToBounds(Vector2 p)
    {
        Rect b = ArenaRules.Bounds;
        return new Vector2(Mathf.Clamp(p.x, b.xMin, b.xMax), Mathf.Clamp(p.y, b.yMin, b.yMax));
    }

    void ApplyField(int id, string field, float value, bool merge)
    {
        if (!ArenaRules.IsFinite(value) || doc.Find(id) == null)
            return;

        doc.Modify("Change " + FieldLabel(field), d =>
        {
            ArenaObjectData o = d.Find(id);
            if (o == null)
                return;

            switch (field)
            {
                case "x":
                    o.x = Mathf.Clamp(value, -ArenaRules.MaxCoordinate, ArenaRules.MaxCoordinate);
                    break;
                case "y":
                    o.y = Mathf.Clamp(value, -ArenaRules.MaxCoordinate, ArenaRules.MaxCoordinate);
                    break;
                case "w":
                    o.width = Mathf.Clamp(value, ArenaRules.MinSize, ArenaRules.MaxSize);
                    if (ArenaRules.IsCircular(o.Kind))
                        o.height = o.width;
                    break;
                case "h":
                    o.height = Mathf.Clamp(value, ArenaRules.MinSize, ArenaRules.MaxSize);
                    break;
                case "r":
                    o.rotation = ArenaEditorDocument.NormalizeAngle(value);
                    break;
            }
        }, merge ? "field:" + id + ":" + field : null);
    }

    static string FieldLabel(string field)
    {
        switch (field)
        {
            case "x": return "X";
            case "y": return "Y";
            case "w": return "width";
            case "h": return "height";
            case "r": return "rotation";
            default: return field;
        }
    }

    static float GetField(ArenaObjectData obj, string field)
    {
        switch (field)
        {
            case "x": return obj.x;
            case "y": return obj.y;
            case "w": return obj.width;
            case "h": return obj.height;
            case "r": return obj.rotation;
            default: return 0f;
        }
    }

    // ------------------------------------------------------------------ OnGUI

    void OnGUI()
    {
        if (doc == null)
            return;

        EnsureResources();

        uiScale = Mathf.Clamp(Screen.height / 820f, 0.7f, 3f);
        screenW = Screen.width / uiScale;
        screenH = Screen.height / uiScale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));

        // First control of every pass, so its id stays the same while panels change (drags keep working).
        int canvasId = GUIUtility.GetControlID(CanvasControlHint, FocusType.Passive);

        Event e = Event.current;
        mouseGui = e.mousePosition;
        LayoutCanvas();

        // Clicking anywhere releases a focused text field; a clicked text field takes the focus back below.
        if (e.type == EventType.MouseDown)
        {
            GUIUtility.keyboardControl = 0;
            doc.BreakMerge();
        }

        bool modal = dialog != DialogKind.None;
        uiEnabled = !modal;

        if (e.type == EventType.Repaint)
        {
            Fill(new Rect(0f, 0f, screenW, screenH), BackgroundColor);
            DrawCanvas();
        }

        GUI.enabled = uiEnabled;
        DrawTopBar();
        DrawLeftPanel();
        DrawRightPanel();
        DrawStatusBar();
        if (e.type == EventType.Repaint)
            hoverTooltip = GUI.tooltip;
        GUI.enabled = true;

        if (!modal)
        {
            HandleCanvasInput(canvasId);
            HandleKeyboard();
        }

        if (modal)
            DrawDialog();

        DrawToast();

        GUI.matrix = Matrix4x4.identity;
        RunDeferred();
    }

    void LayoutCanvas()
    {
        float left = LeftPanelWidth + Gap;
        float right = screenW - RightPanelWidth - Gap;
        float top = TopBarHeight + Gap;
        float bottom = screenH - StatusBarHeight - Gap;
        float availableW = Mathf.Max(120f, right - left);
        float availableH = Mathf.Max(70f, bottom - top);
        Rect b = ArenaRules.Bounds;
        ppu = Mathf.Min(availableW / b.width, availableH / b.height);
        float w = b.width * ppu;
        float h = b.height * ppu;
        canvasRect = new Rect(left + (availableW - w) * 0.5f, top + (availableH - h) * 0.5f, w, h);
    }

    Vector2 WorldToGui(Vector2 world)
    {
        Rect b = ArenaRules.Bounds;
        return new Vector2(canvasRect.x + (world.x - b.xMin) * ppu, canvasRect.y + (b.yMax - world.y) * ppu);
    }

    Vector2 GuiToWorld(Vector2 gui)
    {
        Rect b = ArenaRules.Bounds;
        return new Vector2(b.xMin + (gui.x - canvasRect.x) / ppu, b.yMax - (gui.y - canvasRect.y) / ppu);
    }

    // ------------------------------------------------------------------ canvas drawing

    void DrawCanvas()
    {
        Fill(canvasRect, ArenaColor);

        if (showGrid)
            DrawGrid();

        DrawGuides();

        Dictionary<int, int> pairs = CustomArenaBuilder.PortalPairColors(doc.Data);
        List<ArenaObjectData> ordered = DrawOrder();

        foreach (ArenaObjectData obj in ordered)
            DrawObject(obj, pairs);

        foreach (ArenaObjectData portal in ordered.Where(o => o.IsPortal))
        {
            ArenaObjectData partner = doc.PartnerOf(portal);
            if (partner != null && portal.id < partner.id)
                DrawDashedLine(WorldToGui(portal.Position), WorldToGui(partner.Position), 1.5f, WithAlpha(PortalColor(portal, pairs), 0.55f));
        }

        if (linkSourceId != 0)
        {
            ArenaObjectData source = doc.Find(linkSourceId);
            if (source != null)
                DrawDashedLine(WorldToGui(source.Position), mouseGui, 2f, SelectionColor);
        }

        foreach (ArenaObjectData selected in doc.SelectedObjects())
            DrawShapeOutline(selected, SelectionColor, 2f, 4f);

        if (doc.Selection.Count == 1 && dragMode != DragMode.Marquee)
        {
            ArenaObjectData single = doc.Find(doc.PrimarySelection);
            if (single != null)
                DrawHandles(single);
        }

        if (paletteIndex >= 0 && canvasRect.Contains(mouseGui))
            DrawGhost();

        if (dragMode == DragMode.Marquee)
        {
            Rect marquee = GuiRect(dragStartGui, marqueeEndGui);
            Fill(marquee, new Color(1f, 0.88f, 0.2f, 0.08f));
            DrawRectOutline(marquee, SelectionColor, 1f);
        }

        DrawRectOutline(canvasRect, new Color(1f, 1f, 1f, 0.35f), 2f);
    }

    static Rect GuiRect(Vector2 a, Vector2 b)
    {
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    void DrawGrid()
    {
        Rect b = ArenaRules.Bounds;
        for (int x = Mathf.CeilToInt(b.xMin); x <= Mathf.FloorToInt(b.xMax); x++)
        {
            Color c = x == 0 ? new Color(1f, 1f, 1f, 0.13f) : new Color(1f, 1f, 1f, 0.045f);
            DrawLine(WorldToGui(new Vector2(x, b.yMin)), WorldToGui(new Vector2(x, b.yMax)), 1f, c);
        }

        for (int y = Mathf.CeilToInt(b.yMin); y <= Mathf.FloorToInt(b.yMax); y++)
        {
            Color c = y == 0 ? new Color(1f, 1f, 1f, 0.13f) : new Color(1f, 1f, 1f, 0.045f);
            DrawLine(WorldToGui(new Vector2(b.xMin, y)), WorldToGui(new Vector2(b.xMax, y)), 1f, c);
        }
    }

    void DrawGuides()
    {
        Rect b = ArenaRules.Bounds;
        Color guide = new Color(1f, 0.95f, 0.55f, 0.22f);
        foreach (float x in new[] { -ArenaRules.SixteenByNineHalfWidth, ArenaRules.SixteenByNineHalfWidth })
            DrawDashedLine(WorldToGui(new Vector2(x, b.yMin)), WorldToGui(new Vector2(x, b.yMax)), 1f, guide);

        Vector2 labelPos = WorldToGui(new Vector2(ArenaRules.SixteenByNineHalfWidth, b.yMax));
        GUI.color = new Color(1f, 0.95f, 0.55f, 0.5f);
        GUI.Label(new Rect(labelPos.x - 124f, labelPos.y + 2f, 120f, 16f), "16:9 screen edge", smallLabelStyle);
        GUI.color = Color.white;
    }

    List<ArenaObjectData> DrawOrder()
    {
        return doc.Data.objects.Where(o => o != null)
            .Select((o, index) => new { o, index })
            .OrderBy(p => Layer(p.o.Kind)).ThenBy(p => p.index)
            .Select(p => p.o).ToList();
    }

    static int Layer(ArenaObjectType kind)
    {
        switch (kind)
        {
            case ArenaObjectType.SlowZone: return 0;
            case ArenaObjectType.BlueGoal:
            case ArenaObjectType.RedGoal: return 1;
            case ArenaObjectType.Portal: return 2;
            case ArenaObjectType.Wall:
            case ArenaObjectType.Platform: return 3;
            case ArenaObjectType.BlueSpawn:
            case ArenaObjectType.RedSpawn: return 4;
            case ArenaObjectType.Ball: return 5;
            default: return 6;
        }
    }

    Color PortalColor(ArenaObjectData portal, Dictionary<int, int> pairs)
    {
        return pairs.TryGetValue(portal.id, out int index) ? PortalPairColors[index % PortalPairColors.Length] : UnpairedPortalColor;
    }

    static Color WithAlpha(Color c, float a)
    {
        return new Color(c.r, c.g, c.b, a);
    }

    static bool IsFiniteObject(ArenaObjectData obj)
    {
        return ArenaRules.IsFinite(obj.x) && ArenaRules.IsFinite(obj.y) && ArenaRules.IsFinite(obj.width) &&
               ArenaRules.IsFinite(obj.height) && ArenaRules.IsFinite(obj.rotation);
    }

    void DrawObject(ArenaObjectData obj, Dictionary<int, int> pairs)
    {
        if (!IsFiniteObject(obj))
            return;

        bool problem = report != null && report.Mentions(obj.id);
        Vector2 center = WorldToGui(obj.Position);

        switch (obj.Kind)
        {
            case ArenaObjectType.Wall:
                DrawBoxFill(ArenaGeometry.BoxOf(obj), new Color(0.09f, 0.085f, 0.085f, 1f));
                DrawBoxOutline(ArenaGeometry.BoxOf(obj), new Color(0.95f, 0.95f, 0.95f, 1f), 2f);
                break;

            case ArenaObjectType.Platform:
                DrawBoxFill(ArenaGeometry.BoxOf(obj), new Color(0.09f, 0.085f, 0.085f, 1f));
                DrawBoxOutline(ArenaGeometry.BoxOf(obj), new Color(0.3f, 0.95f, 1f, 1f), 2f);
                break;

            case ArenaObjectType.BlueGoal:
            case ArenaObjectType.RedGoal:
            {
                bool blue = obj.Kind == ArenaObjectType.BlueGoal;
                Color c = blue ? new Color(0.25f, 0.55f, 1f, 1f) : new Color(1f, 0.3f, 0.35f, 1f);
                DrawBoxFill(ArenaGeometry.BoxOf(obj), WithAlpha(c, 0.35f));
                DrawBoxOutline(ArenaGeometry.BoxOf(obj), c, 2f);
                CanvasLabel(center, blue ? "BLUE\nGOAL" : "RED\nGOAL", Color.white);
                break;
            }

            case ArenaObjectType.Portal:
            {
                Color c = PortalColor(obj, pairs);
                OrientedBox box = ArenaGeometry.BoxOf(obj);
                DrawBoxFill(box, WithAlpha(c, 0.3f));
                DrawBoxOutline(box, c, 2f);
                Vector2 faceA = WorldToGui(box.ToWorld(new Vector2(-box.HalfSize.x, box.HalfSize.y)));
                Vector2 faceB = WorldToGui(box.ToWorld(new Vector2(box.HalfSize.x, box.HalfSize.y)));
                DrawLine(faceA, faceB, 4f, c);
                Vector2 exit = WorldToGui(ArenaGeometry.PortalExitPoint(obj));
                Vector2 landing = WorldToGui(ArenaGeometry.PortalLandingPoint(obj));
                DrawArrow(center, landing, 2f, WithAlpha(c, 0.95f));
                DrawCircleOutline(exit, 3f, 1.5f, c);
                ArenaObjectData partner = doc.PartnerOf(obj);
                string label = partner != null && pairs.TryGetValue(obj.id, out int pairIndex) ? "P" + (pairIndex + 1) : "!";
                CanvasLabel(center, label, partner != null ? Color.white : ProblemColor);
                break;
            }

            case ArenaObjectType.SlowZone:
            {
                float r = ArenaGeometry.CircleRadius(obj) * ppu;
                Color c = new Color(0.2f, 1f, 0.85f, 1f);
                DrawCircleFill(center, r, WithAlpha(c, 0.12f));
                DrawCircleOutline(center, r, 1.5f, WithAlpha(c, 0.75f));
                CanvasLabel(center, "SLOW", WithAlpha(c, 0.9f));
                break;
            }

            case ArenaObjectType.Ball:
            {
                float r = Mathf.Max(6f, ArenaRules.BallRadius * ppu);
                DrawCircleFill(center, r + 2f, new Color(0f, 0f, 0f, 0.9f));
                DrawCircleFill(center, r, new Color(0.95f, 0.95f, 0.95f, 1f));
                CanvasLabel(center + new Vector2(0f, -r - 9f), "BALL", Color.white);
                break;
            }

            case ArenaObjectType.BlueSpawn:
            case ArenaObjectType.RedSpawn:
            {
                bool blue = obj.Kind == ArenaObjectType.BlueSpawn;
                Color c = blue ? new Color(0.2f, 0.55f, 1f, 1f) : new Color(1f, 0.3f, 0.35f, 1f);
                OrientedBox body = ArenaGeometry.PlayerBody(obj.Position);
                DrawBoxFill(body, WithAlpha(c, 0.85f));
                DrawBoxOutline(body, Color.white, 1.5f);
                DrawCircleFill(center, 3f, Color.white);
                CanvasLabel(WorldToGui(body.Center + new Vector2(0f, body.HalfSize.y)) + new Vector2(0f, -9f), blue ? "BLUE" : "RED", c);
                break;
            }

            default:
            {
                OrientedBox box = ArenaGeometry.BoxOf(obj);
                DrawBoxOutline(box, ProblemColor, 2f);
                CanvasLabel(center, "?", ProblemColor);
                break;
            }
        }

        if (problem)
            DrawShapeOutline(obj, WithAlpha(ProblemColor, 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 2f, 1f)), 2f, 7f);
    }

    void DrawShapeOutline(ArenaObjectData obj, Color color, float width, float paddingPx)
    {
        if (!IsFiniteObject(obj))
            return;

        float pad = paddingPx / ppu;
        if (obj.Kind == ArenaObjectType.Ball)
        {
            DrawCircleOutline(WorldToGui(obj.Position), Mathf.Max(6f, ArenaRules.BallRadius * ppu) + paddingPx, width, color);
        }
        else if (obj.Kind == ArenaObjectType.SlowZone)
        {
            DrawCircleOutline(WorldToGui(obj.Position), ArenaGeometry.CircleRadius(obj) * ppu + paddingPx, width, color);
        }
        else
        {
            DrawBoxOutline(ArenaGeometry.BoxOf(obj).Expanded(pad), color, width);
        }
    }

    List<KeyValuePair<Vector2Int, Vector2>> HandlePositions(ArenaObjectData obj)
    {
        var result = new List<KeyValuePair<Vector2Int, Vector2>>();
        if (!ArenaRules.IsResizable(obj.Kind) || !IsFiniteObject(obj))
            return result;

        if (ArenaRules.IsCircular(obj.Kind))
        {
            float r = ArenaGeometry.CircleRadius(obj);
            foreach (Vector2Int h in CircleHandles)
                result.Add(new KeyValuePair<Vector2Int, Vector2>(h, WorldToGui(obj.Position + new Vector2(h.x, h.y) * r)));
            return result;
        }

        OrientedBox box = ArenaGeometry.BoxOf(obj);
        foreach (Vector2Int h in BoxHandles)
            result.Add(new KeyValuePair<Vector2Int, Vector2>(h, WorldToGui(box.ToWorld(new Vector2(h.x * box.HalfSize.x, h.y * box.HalfSize.y)))));
        return result;
    }

    bool TryRotateHandle(ArenaObjectData obj, out Vector2 anchor, out Vector2 handle)
    {
        anchor = Vector2.zero;
        handle = Vector2.zero;
        if (!ArenaRules.IsRotatable(obj.Kind) || !IsFiniteObject(obj))
            return false;

        OrientedBox box = ArenaGeometry.BoxOf(obj);
        anchor = WorldToGui(box.ToWorld(new Vector2(0f, box.HalfSize.y)));
        Vector2 up = WorldToGui(box.Center + box.AxisY) - WorldToGui(box.Center);
        if (up.sqrMagnitude < 0.0001f)
            return false;

        handle = anchor + up.normalized * RotateHandleDistance;
        return true;
    }

    void DrawHandles(ArenaObjectData obj)
    {
        if (TryRotateHandle(obj, out Vector2 anchor, out Vector2 handle))
        {
            DrawLine(anchor, handle, 1.5f, SelectionColor);
            DrawCircleFill(handle, 7f, new Color(0f, 0f, 0f, 0.9f));
            DrawCircleFill(handle, 5.5f, SelectionColor);
        }

        foreach (KeyValuePair<Vector2Int, Vector2> h in HandlePositions(obj))
        {
            Rect r = new Rect(h.Value.x - HandleSize * 0.5f, h.Value.y - HandleSize * 0.5f, HandleSize, HandleSize);
            Fill(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f), new Color(0f, 0f, 0f, 0.9f));
            Fill(r, Color.white);
        }
    }

    void DrawGhost()
    {
        PaletteItem item = Palette[paletteIndex];
        Vector2 world = ClampToBounds(snapToGrid ? ArenaRules.Snap(GuiToWorld(mouseGui), SnapStep) : GuiToWorld(mouseGui));
        ArenaObjectData ghost = ArenaFactory.Create(item.Type, -1, world);
        if (item.PortalPair)
        {
            Vector2 other = Mathf.Abs(world.x) < 1.5f ? world + new Vector2(3f, 0f) : new Vector2(-world.x, world.y);
            ghost.rotation = world.x <= other.x ? -90f : 90f;
            ArenaObjectData second = ArenaFactory.Create(item.Type, -2, ClampToBounds(other));
            second.rotation = -ghost.rotation;
            DrawShapeOutline(second, WithAlpha(item.Color, 0.6f), 1.5f, 0f);
        }

        DrawShapeOutline(ghost, WithAlpha(item.Color, 0.9f), 2f, 0f);
        if (ArenaGeometry.IsCircle(ghost))
            DrawCircleFill(WorldToGui(world), Mathf.Max(6f, ArenaGeometry.CircleRadius(ghost) * ppu), WithAlpha(item.Color, 0.15f));
        else
            DrawBoxFill(ArenaGeometry.BoxOf(ghost), WithAlpha(item.Color, 0.15f));
    }

    void CanvasLabel(Vector2 gui, string text, Color color)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.8f * color.a);
        GUI.Label(new Rect(gui.x - 59f, gui.y - 15f, 120f, 32f), text, canvasLabelStyle);
        GUI.color = color;
        GUI.Label(new Rect(gui.x - 60f, gui.y - 16f, 120f, 32f), text, canvasLabelStyle);
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------------ primitives

    void Fill(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static void DrawRotated(Vector2 center, Vector2 size, float angleDegrees, Color color, Texture texture)
    {
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = previous * Matrix4x4.TRS(new Vector3(center.x, center.y, 0f), Quaternion.Euler(0f, 0f, angleDegrees), Vector3.one);
        GUI.color = color;
        GUI.DrawTexture(new Rect(-size.x * 0.5f, -size.y * 0.5f, size.x, size.y), texture);
        GUI.color = Color.white;
        GUI.matrix = previous;
    }

    static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 0.01f)
            return;

        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        DrawRotated((a + b) * 0.5f, new Vector2(length + width, width), angle, color, Texture2D.whiteTexture);
    }

    static void DrawDashedLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 0.01f)
            return;

        Vector2 dir = d / length;
        const float dash = 8f;
        const float space = 6f;
        for (float t = 0f; t < length; t += dash + space)
            DrawLine(a + dir * t, a + dir * Mathf.Min(length, t + dash), width, color);
    }

    static void DrawArrow(Vector2 from, Vector2 to, float width, Color color)
    {
        DrawLine(from, to, width, color);
        Vector2 d = to - from;
        if (d.sqrMagnitude < 1f)
            return;

        Vector2 dir = d.normalized;
        Vector2 side = new Vector2(-dir.y, dir.x);
        DrawLine(to, to - dir * 9f + side * 5f, width, color);
        DrawLine(to, to - dir * 9f - side * 5f, width, color);
    }

    void DrawRectOutline(Rect r, Color color, float width)
    {
        Fill(new Rect(r.x, r.y, r.width, width), color);
        Fill(new Rect(r.x, r.yMax - width, r.width, width), color);
        Fill(new Rect(r.x, r.y, width, r.height), color);
        Fill(new Rect(r.xMax - width, r.y, width, r.height), color);
    }

    void DrawBoxFill(OrientedBox box, Color color)
    {
        Vector2 center = WorldToGui(box.Center);
        float angle = -Mathf.Atan2(box.AxisX.y, box.AxisX.x) * Mathf.Rad2Deg;
        DrawRotated(center, box.HalfSize * 2f * ppu, angle, color, Texture2D.whiteTexture);
    }

    void DrawBoxOutline(OrientedBox box, Color color, float width)
    {
        Vector2[] corners = box.Corners();
        for (int i = 0; i < corners.Length; i++)
            DrawLine(WorldToGui(corners[i]), WorldToGui(corners[(i + 1) % corners.Length]), width, color);
    }

    void DrawCircleFill(Vector2 center, float radius, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), circleTexture);
        GUI.color = Color.white;
    }

    static void DrawCircleOutline(Vector2 center, float radius, float width, Color color)
    {
        int segments = Mathf.Clamp(Mathf.CeilToInt(radius * 0.8f), 12, 64);
        Vector2 previous = center + new Vector2(radius, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector2 next = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            DrawLine(previous, next, width, color);
            previous = next;
        }
    }

    // ------------------------------------------------------------------ canvas input

    int HitTest(Vector2 gui)
    {
        Vector2 world = GuiToWorld(gui);
        float tolerance = 4f / ppu;
        List<ArenaObjectData> ordered = DrawOrder();
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            if (HitObject(ordered[i], world, tolerance))
                return ordered[i].id;
        }

        return 0;
    }

    bool HitObject(ArenaObjectData obj, Vector2 world, float tolerance)
    {
        if (!IsFiniteObject(obj))
            return false;

        switch (obj.Kind)
        {
            case ArenaObjectType.Ball:
                return Vector2.Distance(world, obj.Position) <= Mathf.Max(ArenaRules.BallRadius, 8f / ppu) + tolerance;
            case ArenaObjectType.SlowZone:
                return Vector2.Distance(world, obj.Position) <= ArenaGeometry.CircleRadius(obj) + tolerance;
            default:
                return ArenaGeometry.BoxOf(obj).Expanded(tolerance).Contains(world);
        }
    }

    void HandleCanvasInput(int controlId)
    {
        Event e = Event.current;
        Rect interactive = new Rect(canvasRect.x - 16f, canvasRect.y - 16f, canvasRect.width + 32f, canvasRect.height + 32f);

        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (!interactive.Contains(e.mousePosition))
                    return;

                GUIUtility.keyboardControl = 0;
                doc.BreakMerge();

                if (e.button == 1)
                {
                    if (dragMode != DragMode.None)
                        CancelDrag();
                    else if (paletteIndex >= 0 || linkSourceId != 0)
                        CancelModes();
                    else
                        doc.ClearSelection();
                    e.Use();
                    return;
                }

                if (e.button != 0)
                    return;

                OnCanvasMouseDown(e.mousePosition, e.shift, e.control || e.command);
                if (dragMode != DragMode.None)
                    GUIUtility.hotControl = controlId;
                e.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlId)
                    return;

                OnCanvasDrag(e.mousePosition, e.shift);
                e.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != controlId)
                    return;

                GUIUtility.hotControl = 0;
                FinishDrag();
                e.Use();
                break;
        }
    }

    void OnCanvasMouseDown(Vector2 gui, bool shift, bool ctrl)
    {
        Vector2 world = GuiToWorld(gui);

        if (paletteIndex >= 0)
        {
            if (canvasRect.Contains(gui))
                PlaceFromPalette(world, shift);
            return;
        }

        if (linkSourceId != 0)
        {
            int target = HitTest(gui);
            ArenaObjectData targetObj = doc.Find(target);
            if (targetObj != null && targetObj.IsPortal && target != linkSourceId)
            {
                doc.Link(linkSourceId, target);
                doc.SetSelection(new[] { linkSourceId, target });
                ShowToast("Portals connected.", false);
            }
            else
            {
                ShowToast("Linking cancelled (click a different portal to connect).", true);
            }

            linkSourceId = 0;
            return;
        }

        if (doc.Selection.Count == 1)
        {
            ArenaObjectData selected = doc.Find(doc.PrimarySelection);
            if (selected != null)
            {
                if (TryRotateHandle(selected, out _, out Vector2 rotateHandle) && Vector2.Distance(gui, rotateHandle) <= 9f)
                {
                    BeginObjectDrag(DragMode.Rotate, selected, gui, world, "Rotate " + ArenaRules.DisplayName(selected.Kind));
                    return;
                }

                foreach (KeyValuePair<Vector2Int, Vector2> h in HandlePositions(selected))
                {
                    if (Mathf.Abs(gui.x - h.Value.x) <= HandleSize && Mathf.Abs(gui.y - h.Value.y) <= HandleSize)
                    {
                        dragHandle = h.Key;
                        BeginObjectDrag(DragMode.Resize, selected, gui, world, "Resize " + ArenaRules.DisplayName(selected.Kind));
                        return;
                    }
                }
            }
        }

        int hit = HitTest(gui);
        if (hit != 0)
        {
            if (shift || ctrl)
            {
                doc.ToggleSelection(hit);
                return;
            }

            if (!doc.IsSelected(hit))
                doc.Select(hit);

            ArenaObjectData hitObj = doc.Find(hit);
            BeginObjectDrag(DragMode.Move, hitObj, gui, world, doc.Selection.Count > 1 ? "Move " + doc.Selection.Count + " objects" : "Move " + ArenaRules.DisplayName(hitObj.Kind));
            return;
        }

        marqueeBaseSelection.Clear();
        if (shift || ctrl)
            marqueeBaseSelection.AddRange(doc.Selection);
        else
            doc.ClearSelection();

        dragMode = DragMode.Marquee;
        dragStartGui = gui;
        marqueeEndGui = gui;
    }

    void BeginObjectDrag(DragMode mode, ArenaObjectData obj, Vector2 gui, Vector2 world, string label)
    {
        dragMode = mode;
        dragMoved = false;
        dragStartGui = gui;
        dragStartWorld = world;
        dragObjectId = obj.id;
        dragStartObject = obj.Clone();
        dragStartPositions.Clear();
        foreach (ArenaObjectData selected in doc.SelectedObjects())
            dragStartPositions[selected.id] = selected.Position;

        doc.BeginInteraction(label);
    }

    void OnCanvasDrag(Vector2 gui, bool shift)
    {
        if (dragMode == DragMode.None)
            return;

        if (!dragMoved && Vector2.Distance(gui, dragStartGui) < DragThreshold)
            return;

        dragMoved = true;
        Vector2 world = GuiToWorld(gui);

        switch (dragMode)
        {
            case DragMode.Move:
                DragMove(world);
                break;
            case DragMode.Resize:
                DragResize(world);
                break;
            case DragMode.Rotate:
                DragRotate(world, shift);
                break;
            case DragMode.Marquee:
                marqueeEndGui = gui;
                UpdateMarqueeSelection();
                break;
        }
    }

    void DragMove(Vector2 world)
    {
        Vector2 delta = world - dragStartWorld;
        if (snapToGrid && dragStartPositions.TryGetValue(dragObjectId, out Vector2 primaryStart))
            delta = ArenaRules.Snap(primaryStart + delta, SnapStep) - primaryStart;

        var starts = new Dictionary<int, Vector2>(dragStartPositions);
        doc.UpdateInteraction(d =>
        {
            foreach (KeyValuePair<int, Vector2> start in starts)
            {
                ArenaObjectData o = d.Find(start.Key);
                if (o == null)
                    continue;
                o.x = start.Value.x + delta.x;
                o.y = start.Value.y + delta.y;
            }
        });
    }

    void DragResize(Vector2 world)
    {
        ArenaObjectData start = dragStartObject;
        int id = dragObjectId;

        if (ArenaRules.IsCircular(start.Kind))
        {
            float diameter = 2f * Vector2.Distance(world, start.Position);
            if (snapToGrid)
                diameter = ArenaRules.SnapValue(diameter, SnapStep);
            diameter = Mathf.Clamp(diameter, ArenaRules.MinSize, ArenaRules.MaxSize);
            doc.UpdateInteraction(d =>
            {
                ArenaObjectData o = d.Find(id);
                if (o == null)
                    return;
                o.width = diameter;
                o.height = diameter;
            });
            return;
        }

        OrientedBox box = ArenaGeometry.BoxOf(start);
        Vector2 local = box.ToLocal(world);
        float newWidth = Mathf.Abs(start.width);
        float newHeight = Mathf.Abs(start.height);
        float centerX = 0f;
        float centerY = 0f;

        if (dragHandle.x != 0)
        {
            float fixedEdge = -dragHandle.x * box.HalfSize.x;
            float size = (local.x - fixedEdge) * dragHandle.x;
            if (snapToGrid)
                size = ArenaRules.SnapValue(size, SnapStep);
            newWidth = Mathf.Clamp(size, ArenaRules.MinSize, ArenaRules.MaxSize);
            centerX = fixedEdge + dragHandle.x * newWidth * 0.5f;
        }

        if (dragHandle.y != 0)
        {
            float fixedEdge = -dragHandle.y * box.HalfSize.y;
            float size = (local.y - fixedEdge) * dragHandle.y;
            if (snapToGrid)
                size = ArenaRules.SnapValue(size, SnapStep);
            newHeight = Mathf.Clamp(size, ArenaRules.MinSize, ArenaRules.MaxSize);
            centerY = fixedEdge + dragHandle.y * newHeight * 0.5f;
        }

        Vector2 newCenter = box.ToWorld(new Vector2(centerX, centerY));
        doc.UpdateInteraction(d =>
        {
            ArenaObjectData o = d.Find(id);
            if (o == null)
                return;
            o.width = newWidth;
            o.height = newHeight;
            o.x = newCenter.x;
            o.y = newCenter.y;
        });
    }

    void DragRotate(Vector2 world, bool free)
    {
        ArenaObjectData start = dragStartObject;
        Vector2 dir = world - start.Position;
        if (dir.sqrMagnitude < 0.0001f)
            return;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        if (snapToGrid && !free)
            angle = Mathf.Round(angle / RotationStep) * RotationStep;
        angle = ArenaEditorDocument.NormalizeAngle(angle);

        int id = dragObjectId;
        doc.UpdateInteraction(d =>
        {
            ArenaObjectData o = d.Find(id);
            if (o != null)
                o.rotation = angle;
        });
    }

    void UpdateMarqueeSelection()
    {
        Vector2 a = GuiToWorld(dragStartGui);
        Vector2 b = GuiToWorld(marqueeEndGui);
        Rect area = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        var ids = new List<int>(marqueeBaseSelection);
        foreach (ArenaObjectData obj in doc.Data.objects)
        {
            if (obj != null && IsFiniteObject(obj) && area.Contains(obj.Position) && !ids.Contains(obj.id))
                ids.Add(obj.id);
        }

        doc.SetSelection(ids);
    }

    void FinishDrag()
    {
        switch (dragMode)
        {
            case DragMode.Move:
                if (!dragMoved && doc.Selection.Count > 1)
                    doc.Select(dragObjectId);
                doc.EndInteraction();
                break;
            case DragMode.Resize:
            case DragMode.Rotate:
                doc.EndInteraction();
                break;
        }

        dragMode = DragMode.None;
        if (GUIUtility.hotControl != 0)
            GUIUtility.hotControl = 0;
    }

    void CancelDrag()
    {
        if (dragMode == DragMode.Marquee)
            doc.SetSelection(marqueeBaseSelection);
        else if (dragMode != DragMode.None)
            doc.CancelInteraction();

        dragMode = DragMode.None;
        GUIUtility.hotControl = 0;
    }

    void HandleKeyboard()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
            return;

        KeyCode key = e.keyCode;
        bool command = e.control || e.command;

        if (key == KeyCode.Escape)
        {
            e.Use();
            if (GUIUtility.keyboardControl != 0)
            {
                GUIUtility.keyboardControl = 0;
                return;
            }

            Defer(() =>
            {
                if (dragMode != DragMode.None)
                    CancelDrag();
                else if (paletteIndex >= 0 || linkSourceId != 0)
                    CancelModes();
                else
                    doc.ClearSelection();
            });
            return;
        }

        // Typing in a text field: leave the keys to it (Enter confirms and leaves the field).
        if (GUIUtility.keyboardControl != 0)
        {
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                GUIUtility.keyboardControl = 0;
                doc.BreakMerge();
                e.Use();
            }

            return;
        }

        bool used = true;
        if (command && key == KeyCode.Z)
        {
            if (e.shift)
                Defer(Redo);
            else
                Defer(Undo);
        }
        else if (command && key == KeyCode.Y)
            Defer(Redo);
        else if (command && key == KeyCode.D)
            Defer(DuplicateSelection);
        else if (command && key == KeyCode.S)
            Defer(() => SaveCurrent(true));
        else if (command && key == KeyCode.A)
            Defer(() => doc.SelectAll());
        else if (key == KeyCode.Delete || key == KeyCode.Backspace)
            Defer(DeleteSelection);
        else if (key == KeyCode.LeftArrow || key == KeyCode.RightArrow || key == KeyCode.UpArrow || key == KeyCode.DownArrow)
        {
            float step = (snapToGrid ? SnapStep : 0.05f) * (e.shift ? 4f : 1f);
            Vector2 delta = key == KeyCode.LeftArrow ? new Vector2(-step, 0f)
                : key == KeyCode.RightArrow ? new Vector2(step, 0f)
                : key == KeyCode.UpArrow ? new Vector2(0f, step)
                : new Vector2(0f, -step);
            Defer(() => doc.MoveBy(new List<int>(doc.Selection), delta, "Nudge"));
        }
        else if (key == KeyCode.Q && !command)
            Defer(() => doc.RotateBy(new List<int>(doc.Selection), RotationStep));
        else if (key == KeyCode.E && !command)
            Defer(() => doc.RotateBy(new List<int>(doc.Selection), -RotationStep));
        else if (key == KeyCode.F1)
            Defer(() => dialog = DialogKind.Help);
        else
            used = false;

        if (used)
            e.Use();
    }

    // ------------------------------------------------------------------ panels

    bool Button(string text, string tooltip, float width, GUIStyle style = null)
    {
        return GUILayout.Button(new GUIContent(text, tooltip), style ?? buttonStyle, GUILayout.Width(width));
    }

    void DrawTopBar()
    {
        Rect bar = new Rect(0f, 0f, screenW, TopBarHeight);
        GUI.Box(bar, GUIContent.none, panelStyle);

        GUILayout.BeginArea(new Rect(8f, 9f, screenW - 16f, TopBarHeight - 14f));
        GUILayout.BeginHorizontal();

        if (Button("< Menu", "Back to the main menu", 78f))
            Defer(BackToMenu);

        GUILayout.Space(6f);
        GUILayout.Label("Name", labelStyle, GUILayout.Width(42f));
        GUI.SetNextControlName("ArenaName");
        string currentName = doc.Data.name ?? "";
        string newName = GUILayout.TextField(currentName, ArenaData.MaxNameLength, textFieldStyle, GUILayout.Width(200f));
        if (newName != currentName)
            doc.Rename(newName);

        GUILayout.Space(6f);
        if (Button("New", "Start a new arena (starter layout)", 54f))
            Defer(() => ConfirmDiscardThen(() => NewArena(true)));
        if (Button("Open", "Open, delete or choose your saved arenas", 62f))
            Defer(OpenBrowser);
        if (Button("Save", "Save this arena (Ctrl+S). Saved arenas stay after restarting the game.", 58f))
            Defer(() => SaveCurrent(true));

        GUILayout.Space(6f);
        GUI.enabled = uiEnabled && doc.CanUndo;
        if (Button("Undo", doc.CanUndo ? "Undo: " + doc.UndoLabel + " (Ctrl+Z)" : "Nothing to undo", 58f))
            Defer(Undo);
        GUI.enabled = uiEnabled && doc.CanRedo;
        if (Button("Redo", doc.CanRedo ? "Redo: " + doc.RedoLabel + " (Ctrl+Y)" : "Nothing to redo", 58f))
            Defer(Redo);
        GUI.enabled = uiEnabled;

        GUILayout.Space(6f);
        if (Button("Playtest", "Play this layout now in Two Players mode, then come back with Esc", 84f, accentButtonStyle))
            Defer(Playtest);

        if (IsCurrentInFlow)
        {
            if (Button("Remove from Game", "Go back to the original three arenas (the arena stays saved)", 150f))
                Defer(RemoveFromGameFlow);
        }
        else if (Button("Use as 4th Arena", "Save this arena and play it after Arena 3 in Two Players and Vs Computer", 150f))
        {
            Defer(() => UseCurrentInGameFlow());
        }

        GUILayout.FlexibleSpace();
        string saveState = doc.IsNew ? "Not saved yet" : doc.IsDirty ? "Unsaved changes" : "Saved";
        GUILayout.Label(saveState, doc.IsNew || doc.IsDirty ? warnLabelStyle : okLabelStyle);
        GUILayout.Space(6f);
        if (Button("Help", "How to use the arena editor (F1)", 56f))
            Defer(() => dialog = DialogKind.Help);

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawLeftPanel()
    {
        Rect panel = new Rect(0f, TopBarHeight, LeftPanelWidth, screenH - TopBarHeight - StatusBarHeight);
        GUI.Box(panel, GUIContent.none, panelStyle);

        GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, panel.height - 20f));
        GUILayout.Label("ADD TO ARENA", headerStyle);
        GUILayout.Label("Pick an item, then click in the arena.", smallLabelStyle);
        GUILayout.Space(4f);

        if (GUILayout.Button(new GUIContent("Select / Move", "Select, move, resize and rotate objects (Esc)"),
                paletteIndex < 0 ? paletteActiveStyle : paletteStyle))
        {
            Defer(() =>
            {
                paletteIndex = -1;
                linkSourceId = 0;
            });
        }

        GUILayout.Space(4f);
        for (int i = 0; i < Palette.Length; i++)
        {
            int index = i;
            PaletteItem item = Palette[i];
            GUIContent content = new GUIContent("      " + item.Label, item.Tooltip);
            Rect r = GUILayoutUtility.GetRect(content, paletteStyle);
            if (GUI.Button(r, content, paletteIndex == i ? paletteActiveStyle : paletteStyle))
            {
                Defer(() =>
                {
                    linkSourceId = 0;
                    paletteIndex = paletteIndex == index ? -1 : index;
                });
            }

            if (Event.current.type == EventType.Repaint)
                Fill(new Rect(r.x + 9f, r.y + r.height * 0.5f - 6f, 12f, 12f), item.Color);
        }

        GUILayout.Space(12f);
        GUILayout.Label("OPTIONS", headerStyle);
        snapToGrid = GUILayout.Toggle(snapToGrid, new GUIContent(" Snap to grid", "Positions and sizes snap to 0.25, rotation to 15 degrees"), toggleStyle);
        showGrid = GUILayout.Toggle(showGrid, new GUIContent(" Show grid", "Grid lines every 1 unit"), toggleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Tip: Shift+click places several objects of the same kind.", wrapLabelStyle);
        GUILayout.EndArea();
    }

    void DrawRightPanel()
    {
        Rect panel = new Rect(screenW - RightPanelWidth, TopBarHeight, RightPanelWidth, screenH - TopBarHeight - StatusBarHeight);
        GUI.Box(panel, GUIContent.none, panelStyle);

        float inspectorHeight = Mathf.Floor(panel.height * 0.5f);
        GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, inspectorHeight - 12f));
        inspectorScroll = GUILayout.BeginScrollView(inspectorScroll);
        DrawInspector();
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        Fill(new Rect(panel.x + 10f, panel.y + inspectorHeight, panel.width - 20f, 1f), new Color(1f, 1f, 1f, 0.15f));

        GUILayout.BeginArea(new Rect(panel.x + 10f, panel.y + inspectorHeight + 8f, panel.width - 20f, panel.height - inspectorHeight - 16f));
        DrawIssues();
        GUILayout.EndArea();
    }

    void DrawInspector()
    {
        List<ArenaObjectData> selected = doc.SelectedObjects();

        if (selected.Count == 0)
        {
            GUILayout.Label("SELECTION", headerStyle);
            GUILayout.Label("Nothing selected.", labelStyle);
            GUILayout.Space(4f);
            GUILayout.Label(
                "Click an object to select it and drag to move it.\n" +
                "Drag the white squares to resize and the round handle to rotate.\n" +
                "Shift+click or drag a box on empty space to select several objects.",
                wrapLabelStyle);
            GUILayout.Space(6f);
            GUILayout.Label(doc.Data.objects.Count + " objects in this arena.", smallLabelStyle);
            GUILayout.Label(string.IsNullOrEmpty(flowArenaId)
                ? "4th arena: none (the game uses the original 3 arenas)."
                : "4th arena: " + flowArenaName + (IsCurrentInFlow ? " (this arena)" : ""), wrapLabelStyle);
            return;
        }

        if (selected.Count == 1)
        {
            DrawSingleInspector(selected[0]);
            return;
        }

        GUILayout.Label(selected.Count + " OBJECTS SELECTED", headerStyle);
        string summary = string.Join(", ", selected.GroupBy(o => o.Kind)
            .Select(g => g.Count() + " " + ArenaRules.DisplayName(g.Key)).ToArray());
        GUILayout.Label(summary, wrapLabelStyle);
        GUILayout.Label("Drag any selected object to move them all together.", smallLabelStyle);
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Duplicate", "Copy the selection (Ctrl+D). Connected portals copied together stay connected to each other."), buttonStyle))
            Defer(DuplicateSelection);
        if (GUILayout.Button(new GUIContent("Delete", "Delete the selection (Del)"), dangerButtonStyle))
            Defer(DeleteSelection);
        GUILayout.EndHorizontal();

        List<ArenaObjectData> portals = selected.Where(o => o.IsPortal).ToList();
        if (portals.Count == 2)
        {
            int a = portals[0].id;
            int b = portals[1].id;
            bool alreadyLinked = portals[0].linkedPortalId == b && portals[1].linkedPortalId == a;
            if (!alreadyLinked && GUILayout.Button(new GUIContent("Link these 2 portals", "Connect the two selected portals to each other"), accentButtonStyle))
                Defer(() =>
                {
                    doc.Link(a, b);
                    ShowToast("Portals connected.", false);
                });
        }

        if (selected.Any(o => ArenaRules.IsRotatable(o.Kind)))
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Rotate -15", "Rotate clockwise (E)"), buttonStyle))
                Defer(() => doc.RotateBy(new List<int>(doc.Selection), -RotationStep));
            if (GUILayout.Button(new GUIContent("Rotate +15", "Rotate counter-clockwise (Q)"), buttonStyle))
                Defer(() => doc.RotateBy(new List<int>(doc.Selection), RotationStep));
            GUILayout.EndHorizontal();
        }
    }

    void DrawSingleInspector(ArenaObjectData obj)
    {
        int id = obj.id;
        GUILayout.Label(ArenaRules.Describe(obj).ToUpperInvariant(), headerStyle);
        GUILayout.Label(Describe(obj.Kind), wrapLabelStyle);
        GUILayout.Space(4f);

        FloatRow("X", "x", obj, SnapStep);
        FloatRow("Y", "y", obj, SnapStep);

        if (ArenaRules.IsResizable(obj.Kind))
        {
            if (ArenaRules.IsCircular(obj.Kind))
            {
                FloatRow("Diameter", "w", obj, SnapStep);
            }
            else
            {
                FloatRow("Width", "w", obj, SnapStep);
                FloatRow(obj.IsPortal ? "Depth" : "Height", "h", obj, SnapStep);
            }
        }

        if (ArenaRules.IsRotatable(obj.Kind))
        {
            FloatRow("Rotation", "r", obj, RotationStep);
            GUILayout.BeginHorizontal();
            GUILayout.Space(74f);
            if (GUILayout.Button(new GUIContent("+90", "Turn a quarter counter-clockwise"), smallButtonStyle, GUILayout.Width(52f)))
                Defer(() => doc.RotateBy(new[] { id }, 90f));
            if (GUILayout.Button(new GUIContent("-90", "Turn a quarter clockwise"), smallButtonStyle, GUILayout.Width(52f)))
                Defer(() => doc.RotateBy(new[] { id }, -90f));
            if (GUILayout.Button(new GUIContent("0", "Reset rotation"), smallButtonStyle, GUILayout.Width(40f)))
                Defer(() => ApplyField(id, "r", 0f, false));
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6f);

        if (obj.IsPortal)
        {
            ArenaObjectData partner = doc.PartnerOf(obj);
            if (partner != null)
            {
                GUILayout.Label("Connected to Portal #" + partner.id + ".", okLabelStyle);
                GUILayout.BeginHorizontal();
                int partnerId = partner.id;
                if (GUILayout.Button(new GUIContent("Select partner", "Select the connected portal"), buttonStyle))
                    Defer(() => doc.Select(partnerId));
                if (GUILayout.Button(new GUIContent("Unlink", "Disconnect the two portals"), buttonStyle))
                    Defer(() => doc.Unlink(id));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Not connected: this portal does nothing yet.", errorLabelStyle);
                if (GUILayout.Button(new GUIContent("Link to...", "Then click the portal it should connect to"), accentButtonStyle))
                {
                    Defer(() =>
                    {
                        paletteIndex = -1;
                        linkSourceId = id;
                    });
                }
            }

            GUILayout.Label("Entering this portal takes you out of its partner, along the partner's arrow.", smallLabelStyle);
        }
        else if (obj.IsGoal)
        {
            GUILayout.Label(obj.Kind == ArenaObjectType.BlueGoal ? "Red scores when the ball enters this goal." : "Blue scores when the ball enters this goal.", labelStyle);
            if (GUILayout.Button(new GUIContent("Swap team", "Turn it into the other team's goal"), buttonStyle))
                Defer(() => doc.SwapGoalTeam(id));
        }
        else if (ArenaRules.IsUniqueType(obj.Kind))
        {
            GUILayout.Label("There is only one. Picking it in the Add panel again moves it.", smallLabelStyle);
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("Duplicate", "Copy this object (Ctrl+D)"), buttonStyle))
            Defer(DuplicateSelection);
        if (GUILayout.Button(new GUIContent("Delete", "Delete this object (Del)"), dangerButtonStyle))
            Defer(DeleteSelection);
        GUILayout.EndHorizontal();
    }

    static string Describe(ArenaObjectType kind)
    {
        switch (kind)
        {
            case ArenaObjectType.Wall: return "Solid. Blocks players and the ball (same as the walls of the other arenas).";
            case ArenaObjectType.Platform: return "Solid ledge to stand on and bounce off.";
            case ArenaObjectType.Portal: return "Teleports the ball and players to its partner. The thick edge and arrow mark where things come out.";
            case ArenaObjectType.BlueGoal: return "The goal Blue defends.";
            case ArenaObjectType.RedGoal: return "The goal Red defends.";
            case ArenaObjectType.SlowZone: return "Slows the ball and players slightly; time slows down while the ball is inside.";
            case ArenaObjectType.Ball: return "Where the ball is placed at every kick-off.";
            case ArenaObjectType.BlueSpawn: return "Where Blue stands at every kick-off (the box shows the player's size).";
            case ArenaObjectType.RedSpawn: return "Where Red stands at every kick-off (the box shows the player's size).";
            default: return "Unknown object from a damaged file. Delete it.";
        }
    }

    void FloatRow(string label, string field, ArenaObjectData obj, float step)
    {
        int id = obj.id;
        float value = GetField(obj, field);

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(70f));

        string controlName = "field_" + field + "_" + id;
        GUI.SetNextControlName(controlName);
        bool focused = GUI.GetNameOfFocusedControl() == controlName;
        string shown = focused && fieldBuffers.TryGetValue(controlName, out string buffer) ? buffer : FormatFloat(value);
        string edited = GUILayout.TextField(shown, 12, textFieldStyle, GUILayout.Width(84f));

        if (focused)
        {
            fieldBuffers[controlName] = edited;
            if (edited != shown && TryParseFloat(edited, out float parsed) && !Mathf.Approximately(parsed, value))
                ApplyField(id, field, parsed, true);
        }
        else
        {
            fieldBuffers.Remove(controlName);
        }

        if (GUILayout.Button(new GUIContent("-", "Decrease by " + FormatFloat(step)), smallButtonStyle, GUILayout.Width(30f)))
            Defer(() => ApplyField(id, field, value - step, false));
        if (GUILayout.Button(new GUIContent("+", "Increase by " + FormatFloat(step)), smallButtonStyle, GUILayout.Width(30f)))
            Defer(() => ApplyField(id, field, value + step, false));
        GUILayout.EndHorizontal();
    }

    static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               ArenaRules.IsFinite(value);
    }

    void DrawIssues()
    {
        GUILayout.Label("ARENA CHECK", headerStyle);

        if (report == null)
            return;

        GUIStyle summaryStyle = report.CanUseInGameFlow ? okLabelStyle : report.CanPlaytest ? warnLabelStyle : errorLabelStyle;
        string summary = report.Summary;
        if (report.CanUseInGameFlow)
            summary += " - can be playtested and added to the game.";
        else if (report.CanPlaytest)
            summary += " - can be playtested, fix them to add it to the game.";
        else
            summary += " - fix the MUST FIX items to playtest.";
        GUILayout.Label(summary, summaryStyle);

        issuesScroll = GUILayout.BeginScrollView(issuesScroll);
        if (report.Issues.Count == 0)
            GUILayout.Label("Everything looks good! Playtest it or press 'Use as 4th Arena'.", wrapLabelStyle);

        foreach (ArenaIssue issue in report.Issues)
        {
            string tag = issue.Severity == ArenaIssueSeverity.Blocker ? "MUST FIX" : issue.Severity == ArenaIssueSeverity.Error ? "FIX" : "TIP";
            string tooltip = issue.ObjectIds.Count > 0 ? "Click to select the objects involved" : "";
            if (GUILayout.Button(new GUIContent(tag + ": " + issue.Message, tooltip), issueStyles[(int)issue.Severity]))
            {
                int[] ids = issue.ObjectIds.ToArray();
                if (ids.Length > 0)
                {
                    Defer(() =>
                    {
                        paletteIndex = -1;
                        linkSourceId = 0;
                        doc.SetSelection(ids);
                    });
                }
            }
        }

        GUILayout.EndScrollView();
    }

    void DrawStatusBar()
    {
        Rect bar = new Rect(0f, screenH - StatusBarHeight, screenW, StatusBarHeight);
        GUI.Box(bar, GUIContent.none, panelStyle);

        string hint;
        if (!string.IsNullOrEmpty(hoverTooltip))
            hint = hoverTooltip;
        else if (paletteIndex >= 0)
            hint = "Click in the arena to place: " + Palette[paletteIndex].Label + ". Shift+click places several. Right-click or Esc to stop.";
        else if (linkSourceId != 0)
            hint = "Click the portal that Portal #" + linkSourceId + " should connect to. Esc cancels.";
        else if (dragMode != DragMode.None)
            hint = "Release the mouse to finish. Esc cancels.";
        else
            hint = "Click: select   Drag: move   Shift+click / box: multi-select   Del: delete   Ctrl+D: duplicate   Ctrl+Z / Ctrl+Y: undo / redo   Arrows: nudge   Q/E: rotate";

        GUI.Label(new Rect(10f, bar.y + 6f, screenW * 0.66f, 20f), hint, statusStyle);

        string right = "";
        if (canvasRect.Contains(mouseGui))
        {
            Vector2 w = GuiToWorld(mouseGui);
            right = "x " + w.x.ToString("0.00", CultureInfo.InvariantCulture) + "  y " + w.y.ToString("0.00", CultureInfo.InvariantCulture) + "    ";
        }

        right += string.IsNullOrEmpty(flowArenaId) ? "Game flow: 3 stock arenas" : "4th arena: " + flowArenaName;
        GUI.Label(new Rect(screenW * 0.66f, bar.y + 6f, screenW * 0.34f - 10f, 20f), right, statusRightStyle);
    }

    void DrawToast()
    {
        if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil)
            return;

        GUIStyle style = toastIsWarning ? toastWarnStyle : toastStyle;
        GUIContent content = new GUIContent(toast);
        float width = Mathf.Min(screenW - 40f, Mathf.Max(260f, style.CalcSize(content).x + 30f));
        Rect r = new Rect((screenW - width) * 0.5f, TopBarHeight + 8f, width, 34f);
        GUI.Label(r, content, style);
    }

    // ------------------------------------------------------------------ dialogs

    void DrawDialog()
    {
        if (Event.current.type == EventType.Repaint)
            Fill(new Rect(0f, 0f, screenW, screenH), new Color(0f, 0f, 0f, 0.6f));

        float width = dialog == DialogKind.Browser || dialog == DialogKind.Help ? 680f : 540f;
        float height = dialog == DialogKind.Browser || dialog == DialogKind.Help ? Mathf.Min(screenH - 60f, 600f) : Mathf.Min(screenH - 60f, 360f);
        Rect box = new Rect((screenW - width) * 0.5f, (screenH - height) * 0.5f, width, height);
        GUI.Box(box, GUIContent.none, dialogStyle);

        GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 16f, box.width - 36f, box.height - 32f));
        switch (dialog)
        {
            case DialogKind.Browser:
                DrawBrowser();
                break;
            case DialogKind.ConfirmDiscard:
                DrawConfirmDiscard();
                break;
            case DialogKind.Message:
                DrawMessage();
                break;
            case DialogKind.ConfirmPlaytest:
                DrawConfirmPlaytest();
                break;
            case DialogKind.ConfirmDeleteArena:
                DrawConfirmDelete();
                break;
            case DialogKind.Help:
                DrawHelp();
                break;
        }

        GUILayout.EndArea();
    }

    void DrawMessage()
    {
        GUILayout.Label(dialogTitle, titleStyle);
        GUILayout.Space(6f);
        if (!string.IsNullOrEmpty(dialogBody))
            GUILayout.Label(dialogBody, wrapLabelStyle);

        dialogScroll = GUILayout.BeginScrollView(dialogScroll);
        foreach (string line in dialogLines)
            GUILayout.Label("- " + line, wrapLabelStyle);
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("OK", accentButtonStyle, GUILayout.Width(110f)))
            Defer(() => dialog = DialogKind.None);
        GUILayout.EndHorizontal();
    }

    void DrawConfirmDiscard()
    {
        GUILayout.Label("Unsaved changes", titleStyle);
        GUILayout.Space(6f);
        GUILayout.Label("'" + doc.Data.DisplayName + "' has changes that are not saved. Save them first?", wrapLabelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save", accentButtonStyle, GUILayout.Width(110f)))
        {
            Defer(() =>
            {
                Action continuation = discardContinuation;
                discardContinuation = null;
                dialog = DialogKind.None;
                if (SaveCurrent(false))
                    continuation?.Invoke();
            });
        }

        if (GUILayout.Button("Don't Save", dangerButtonStyle, GUILayout.Width(110f)))
        {
            Defer(() =>
            {
                Action continuation = discardContinuation;
                discardContinuation = null;
                dialog = DialogKind.None;
                continuation?.Invoke();
            });
        }

        if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Width(110f)))
        {
            Defer(() =>
            {
                discardContinuation = null;
                dialog = DialogKind.None;
            });
        }

        GUILayout.EndHorizontal();
    }

    void DrawConfirmPlaytest()
    {
        GUILayout.Label("Playtest with problems?", titleStyle);
        GUILayout.Space(6f);
        GUILayout.Label("You can playtest the arena now, but these problems must be fixed before it can be added to the game:", wrapLabelStyle);
        dialogScroll = GUILayout.BeginScrollView(dialogScroll);
        foreach (ArenaIssue issue in report.OfSeverity(ArenaIssueSeverity.Error))
            GUILayout.Label("- " + issue.Message, wrapLabelStyle);
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Playtest anyway", accentButtonStyle, GUILayout.Width(150f)))
            Defer(() =>
            {
                dialog = DialogKind.None;
                StartPlaytest();
            });
        if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Width(110f)))
            Defer(() => dialog = DialogKind.None);
        GUILayout.EndHorizontal();
    }

    void DrawConfirmDelete()
    {
        GUILayout.Label("Delete arena?", titleStyle);
        GUILayout.Space(6f);
        string body = "'" + pendingDeleteArenaName + "' will be deleted permanently.";
        if (pendingDeleteArenaId == flowArenaId)
            body += " It is the 4th arena, so the game goes back to the original three arenas.";
        GUILayout.Label(body, wrapLabelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Delete", dangerButtonStyle, GUILayout.Width(110f)))
        {
            string id = pendingDeleteArenaId;
            Defer(() => DeleteArenaFile(id));
        }

        if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Width(110f)))
            Defer(() => dialog = DialogKind.Browser);
        GUILayout.EndHorizontal();
    }

    void DrawBrowser()
    {
        GUILayout.Label("YOUR ARENAS", titleStyle);
        GUILayout.Label("Saved in " + storage.RootDirectory, smallLabelStyle);
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("New Arena", "Start from a ready-made layout"), accentButtonStyle))
            Defer(() => ConfirmDiscardThen(() => NewArena(true)));
        if (GUILayout.Button(new GUIContent("New Empty Arena", "Only the outer walls, the ball and the spawns"), buttonStyle))
            Defer(() => ConfirmDiscardThen(() => NewArena(false)));
        if (GUILayout.Button("Close", buttonStyle, GUILayout.Width(90f)))
            Defer(() => dialog = DialogKind.None);
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        if (string.IsNullOrEmpty(flowArenaId))
        {
            GUILayout.Label("Game flow: Arena 1 > 2 > 3 (no 4th arena).", labelStyle);
        }
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("4th arena: " + flowArenaName, okLabelStyle);
            if (GUILayout.Button(new GUIContent("Use original 3 arenas", "Remove the 4th arena from the game (it stays saved)"), buttonStyle, GUILayout.Width(190f)))
                Defer(() =>
                {
                    RemoveFromGameFlow();
                    RefreshBrowser();
                });
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(4f);
        browserScroll = GUILayout.BeginScrollView(browserScroll);
        if (browserEntries.Count == 0)
            GUILayout.Label("No saved arenas yet. Create one, then press Save.", wrapLabelStyle);

        foreach (ArenaFileEntry entry in browserEntries)
        {
            GUILayout.BeginVertical(rowStyle);
            string id = entry.Id;
            string name = entry.Name;
            string badges = "";
            if (id == flowArenaId)
                badges += "   [4TH ARENA]";
            if (!doc.IsNew && id == doc.Data.id)
                badges += "   [OPEN]";
            GUILayout.Label(name + badges, headerStyle);

            if (entry.IsBroken)
            {
                GUILayout.Label("Broken file: " + entry.Error, errorLabelStyle);
            }
            else
            {
                browserReports.TryGetValue(id, out ArenaValidationReport entryReport);
                string status = entryReport == null ? "" : entryReport.CanUseInGameFlow ? "Ready to play" : entryReport.Summary;
                GUILayout.Label(entry.ObjectCount + " objects   saved " + entry.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + "   " + status,
                    entryReport != null && entryReport.CanUseInGameFlow ? smallLabelStyle : warnLabelStyle);
            }

            GUILayout.BeginHorizontal();
            if (!entry.IsBroken)
            {
                if (GUILayout.Button(new GUIContent("Open", "Edit this arena"), accentButtonStyle, GUILayout.Width(90f)))
                    Defer(() => ConfirmDiscardThen(() => OpenArena(id)));

                if (id != flowArenaId &&
                    GUILayout.Button(new GUIContent("Use as 4th Arena", "Play it after Arena 3 (replaces the current 4th arena, which stays saved)"), buttonStyle, GUILayout.Width(150f)))
                {
                    ArenaValidationReport entryReport = browserReports.TryGetValue(id, out ArenaValidationReport found) ? found : null;
                    Defer(() =>
                    {
                        if (entryReport != null && entryReport.CanUseInGameFlow)
                            AssignToGameFlow(id, name);
                        else
                            ShowMessage("'" + name + "' has problems", "Open it and fix these first:",
                                entryReport != null ? IssueLines(entryReport, true) : null);
                    });
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent(entry.IsBroken ? "Delete file" : "Delete", "Delete this arena permanently"), dangerButtonStyle, GUILayout.Width(100f)))
            {
                Defer(() =>
                {
                    pendingDeleteArenaId = id;
                    pendingDeleteArenaName = name;
                    dialog = DialogKind.ConfirmDeleteArena;
                });
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        GUILayout.EndScrollView();
    }

    void DrawHelp()
    {
        GUILayout.Label("HOW TO BUILD AN ARENA", titleStyle);
        dialogScroll = GUILayout.BeginScrollView(dialogScroll);
        GUILayout.Label(HelpText, wrapLabelStyle);
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Got it", accentButtonStyle, GUILayout.Width(120f)))
            Defer(() => dialog = DialogKind.None);
        GUILayout.EndHorizontal();
    }

    const string HelpText =
        "BUILDING\n" +
        "- Pick an item in the left panel, then click inside the arena to place it (Shift+click places several).\n" +
        "- Click an object to select it and drag it to move it. Drag the white squares to resize it and the round handle to rotate it.\n" +
        "- Shift+click (or Ctrl+click) adds or removes objects from the selection; dragging on empty space selects everything in the box.\n" +
        "- Delete / Backspace deletes the selection, Ctrl+D duplicates it, arrow keys nudge it, Q / E rotate it.\n" +
        "- Ctrl+Z undoes and Ctrl+Y (or Ctrl+Shift+Z) redoes. Undo also brings back portal connections.\n" +
        "- The right panel shows exact numbers for the selected object; type a value or use - / +.\n\n" +
        "OBJECTS\n" +
        "- Walls and platforms are solid, exactly like in the other arenas.\n" +
        "- Portals work in connected pairs. The thick edge and the arrow show where players and the ball come out of a portal. " +
        "Duplicating both portals of a pair creates a new connected pair; duplicating one portal gives an unconnected copy that you must link.\n" +
        "- Blue Goal is the goal Blue defends (Red scores there); Red Goal is the goal Red defends.\n" +
        "- Slow zones slow the ball and players a little, and time slows down while the ball is inside (like the zones in front of the stock goals).\n" +
        "- Ball, Blue Spawn and Red Spawn exist once; placing them again moves them.\n\n" +
        "CHECKING AND PLAYING\n" +
        "- The Arena check panel explains every problem. MUST FIX items block playtesting; FIX items keep the arena out of the game. Click a problem to select the objects involved.\n" +
        "- Playtest plays the current layout in Two Players mode (Blue: WASD + Space, Red: Arrows + Enter), even if it isn't saved. Press Esc or 'Back to Editor' to return; nothing is lost.\n" +
        "- Save keeps the arena on this computer, also after restarting the game. Open lists all saved arenas.\n" +
        "- 'Use as 4th Arena' adds the arena after Arena 3 in Two Players and Vs Computer. Choosing another arena replaces it; the previous one stays saved. " +
        "'Use original 3 arenas' (in Open) goes back to the normal game.";

    // ------------------------------------------------------------------ resources

    void EnsureResources()
    {
        if (stylesReady)
            return;

        circleTexture = MakeCircleTexture(128);

        panelStyle = new GUIStyle();
        panelStyle.normal.background = Solid(new Color(0.045f, 0.055f, 0.11f, 0.98f));

        dialogStyle = new GUIStyle();
        dialogStyle.normal.background = Solid(new Color(0.06f, 0.075f, 0.15f, 1f));

        buttonStyle = MakeButton(new Color(0.13f, 0.16f, 0.3f), new Color(0.2f, 0.25f, 0.46f), new Color(0.3f, 0.36f, 0.62f), 13, 32f);
        accentButtonStyle = MakeButton(new Color(0.55f, 0.12f, 0.45f), new Color(0.72f, 0.2f, 0.6f), new Color(0.85f, 0.3f, 0.7f), 13, 32f);
        dangerButtonStyle = MakeButton(new Color(0.42f, 0.1f, 0.12f), new Color(0.6f, 0.15f, 0.18f), new Color(0.75f, 0.22f, 0.25f), 13, 32f);
        smallButtonStyle = MakeButton(new Color(0.13f, 0.16f, 0.3f), new Color(0.2f, 0.25f, 0.46f), new Color(0.3f, 0.36f, 0.62f), 13, 24f);

        paletteStyle = MakeButton(new Color(0.09f, 0.11f, 0.2f), new Color(0.16f, 0.2f, 0.36f), new Color(0.25f, 0.32f, 0.55f), 13, 30f);
        paletteStyle.alignment = TextAnchor.MiddleLeft;
        paletteActiveStyle = MakeButton(new Color(0.24f, 0.4f, 0.7f), new Color(0.3f, 0.48f, 0.8f), new Color(0.35f, 0.55f, 0.9f), 13, 30f);
        paletteActiveStyle.alignment = TextAnchor.MiddleLeft;

        titleStyle = MakeLabel(18, FontStyle.Bold, new Color(1f, 0.55f, 0.85f), false);
        headerStyle = MakeLabel(13, FontStyle.Bold, new Color(0.55f, 0.9f, 1f), false);
        labelStyle = MakeLabel(13, FontStyle.Normal, new Color(0.88f, 0.92f, 1f), false);
        labelStyle.alignment = TextAnchor.MiddleLeft;
        smallLabelStyle = MakeLabel(11, FontStyle.Normal, new Color(0.65f, 0.72f, 0.85f), true);
        wrapLabelStyle = MakeLabel(12, FontStyle.Normal, new Color(0.85f, 0.9f, 1f), true);
        okLabelStyle = MakeLabel(12, FontStyle.Bold, new Color(0.4f, 1f, 0.55f), true);
        warnLabelStyle = MakeLabel(12, FontStyle.Bold, new Color(1f, 0.82f, 0.3f), true);
        errorLabelStyle = MakeLabel(12, FontStyle.Bold, new Color(1f, 0.4f, 0.42f), true);
        canvasLabelStyle = MakeLabel(11, FontStyle.Bold, Color.white, false);
        canvasLabelStyle.alignment = TextAnchor.MiddleCenter;
        canvasLabelStyle.padding = new RectOffset(0, 0, 0, 0);
        statusStyle = MakeLabel(12, FontStyle.Normal, new Color(0.8f, 0.86f, 1f), false);
        statusStyle.clipping = TextClipping.Clip;
        statusRightStyle = MakeLabel(12, FontStyle.Normal, new Color(0.6f, 0.9f, 1f), false);
        statusRightStyle.alignment = TextAnchor.UpperRight;
        statusRightStyle.clipping = TextClipping.Clip;

        textFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 13, fixedHeight = 26f, alignment = TextAnchor.MiddleLeft };
        textFieldStyle.margin = new RectOffset(3, 3, 6, 3);
        toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
        toggleStyle.normal.textColor = new Color(0.88f, 0.92f, 1f);
        toggleStyle.onNormal.textColor = new Color(0.88f, 0.92f, 1f);
        toggleStyle.hover.textColor = Color.white;
        toggleStyle.onHover.textColor = Color.white;

        toastStyle = MakeLabel(13, FontStyle.Bold, Color.white, false);
        toastStyle.alignment = TextAnchor.MiddleCenter;
        toastStyle.normal.background = Solid(new Color(0.1f, 0.35f, 0.25f, 0.95f));
        toastWarnStyle = MakeLabel(13, FontStyle.Bold, Color.white, false);
        toastWarnStyle.alignment = TextAnchor.MiddleCenter;
        toastWarnStyle.normal.background = Solid(new Color(0.45f, 0.3f, 0.05f, 0.95f));

        rowStyle = new GUIStyle();
        rowStyle.normal.background = Solid(new Color(0.1f, 0.12f, 0.22f, 1f));
        rowStyle.padding = new RectOffset(10, 10, 8, 8);

        issueStyles = new GUIStyle[3];
        issueStyles[(int)ArenaIssueSeverity.Warning] = MakeIssueStyle(new Color(0.12f, 0.2f, 0.3f), new Color(0.75f, 0.88f, 1f));
        issueStyles[(int)ArenaIssueSeverity.Error] = MakeIssueStyle(new Color(0.32f, 0.22f, 0.05f), new Color(1f, 0.88f, 0.55f));
        issueStyles[(int)ArenaIssueSeverity.Blocker] = MakeIssueStyle(new Color(0.36f, 0.08f, 0.1f), new Color(1f, 0.75f, 0.75f));

        stylesReady = true;
    }

    Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        ownedTextures.Add(texture);
        return texture;
    }

    Texture2D MakeCircleTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        float radius = size * 0.5f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        ownedTextures.Add(texture);
        return texture;
    }

    GUIStyle MakeButton(Color normal, Color hover, Color active, int fontSize, float height)
    {
        var style = new GUIStyle
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = height,
            padding = new RectOffset(8, 8, 4, 4),
            margin = new RectOffset(3, 3, 3, 3),
            clipping = TextClipping.Clip
        };

        style.normal.background = Solid(normal);
        style.hover.background = Solid(hover);
        style.active.background = Solid(active);
        style.focused.background = style.normal.background;
        style.normal.textColor = Color.white;
        style.hover.textColor = Color.white;
        style.active.textColor = Color.white;
        style.focused.textColor = Color.white;
        return style;
    }

    static GUIStyle MakeLabel(int fontSize, FontStyle fontStyle, Color color, bool wrap)
    {
        var style = new GUIStyle
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            wordWrap = wrap,
            padding = new RectOffset(2, 2, 2, 2),
            margin = new RectOffset(2, 2, 2, 2)
        };

        style.normal.textColor = color;
        return style;
    }

    GUIStyle MakeIssueStyle(Color background, Color text)
    {
        var style = new GUIStyle
        {
            fontSize = 12,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 6, 6),
            margin = new RectOffset(0, 0, 2, 2)
        };

        style.normal.background = Solid(background);
        style.hover.background = Solid(background * 1.35f);
        style.active.background = style.hover.background;
        style.normal.textColor = text;
        style.hover.textColor = Color.white;
        style.active.textColor = Color.white;
        return style;
    }
}
