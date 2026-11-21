using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tool xếp map road bằng lưới. Vẽ ô Đường → tool tự nhận mảnh (thẳng / góc / ngã ba / ngã tư / cụt) theo ô xung quanh.
/// Ô Cột = 1 ô xe; các ô Cột liền nhau theo chiều dọc = 1 cột. Ô Lối ra = ô đường, cạnh quay ra ngoài lưới để hở viền.
/// Bake → RoadMapBuilder trong scene dựng road thật; sau đó kéo chấm trong Scene để chỉnh bo từng góc.
/// </summary>
public class RoadMapWindow : OdinEditorWindow
{
    private const string MapFolder = "Assets/00_Game/Data/RoadMaps";

    [MenuItem("Tools/Road Map Editor")]
    private static void Open() => GetWindow<RoadMapWindow>("Road Map").Show();

    [BoxGroup("Map"), HorizontalGroup("Map/Row"), HideLabel, AssetsOnly]
    [SerializeField, OnValueChanged(nameof(OnMapChanged))] private RoadMapSO map;

    [HorizontalGroup("Map/Row", Width = 90), Button("Tạo mới")]
    private void CreateMap()
    {
        if (!AssetDatabase.IsValidFolder(MapFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/00_Game/Data")) AssetDatabase.CreateFolder("Assets/00_Game", "Data");
            AssetDatabase.CreateFolder("Assets/00_Game/Data", "RoadMaps");
        }
        string path = EditorUtility.SaveFilePanelInProject("Tạo road map", "RoadMap_01", "asset", "", MapFolder);
        if (string.IsNullOrEmpty(path)) return;
        var so = CreateInstance<RoadMapSO>();
        so.Resize(newWidth, newHeight);
        AssetDatabase.CreateAsset(so, path);
        AssetDatabase.SaveAssets();
        map = so;
        OnMapChanged();
    }

    [BoxGroup("Lưới"), HorizontalGroup("Lưới/Size"), LabelText("Rộng"), LabelWidth(40)]
    [SerializeField, Min(1)] private int newWidth = 10;
    [HorizontalGroup("Lưới/Size"), LabelText("Cao"), LabelWidth(30)]
    [SerializeField, Min(1)] private int newHeight = 10;

    [HorizontalGroup("Lưới/Size", Width = 90), Button("Đổi cỡ"), EnableIf(nameof(HasMap))]
    private void Resize()
    {
        Record("Đổi cỡ lưới");
        map.Resize(newWidth, newHeight);
        Changed();
    }

    [BoxGroup("Lưới"), LabelText("Cỡ ô hiển thị"), PropertyRange(16, 64)]
    [SerializeField] private int cellPx = 36;

    [BoxGroup("Vẽ"), EnumToggleButtons, HideLabel]
    [SerializeField] private Brush brush = Brush.Đường;

    private enum Brush { Đường, Cột, Lối_ra, Xoá }

    // Nét vẽ cột đang kéo: x → y của ô đầu tiên trong nét (ô bắt đầu = đầu RA, ô kết thúc = đầu VÀO).
    private readonly Dictionary<int, int> strokeStartY = new();

    [BoxGroup("Vẽ"), LabelText("Tự bake khi vẽ")]
    [SerializeField] private bool liveBake = true;

    [BoxGroup("Mẫu nhanh"), HorizontalGroup("Mẫu nhanh/Row"), Button("Vòng chữ nhật"), EnableIf(nameof(HasMap))]
    private void TemplateRing()
    {
        Record("Mẫu vòng");
        map.Clear();
        for (int x = 0; x < map.Width; x++) { map.Set(x, 0, RoadCell.Road); map.Set(x, map.Height - 1, RoadCell.Road); }
        for (int y = 0; y < map.Height; y++) { map.Set(0, y, RoadCell.Road); map.Set(map.Width - 1, y, RoadCell.Road); }
        Changed();
    }

    [HorizontalGroup("Mẫu nhanh/Row"), Button("+ Đường giữa"), EnableIf(nameof(HasMap))]
    private void TemplateMiddle()
    {
        Record("Đường giữa");
        int y = map.Height / 2;
        for (int x = 0; x < map.Width; x++) map.Set(x, y, RoadCell.Road);
        Changed();
    }

    [HorizontalGroup("Mẫu nhanh/Row"), Button("Lấp cột vào đảo"), EnableIf(nameof(HasMap))]
    private void FillColumns()
    {
        // Ô trống không thông ra mép lưới (bị đường bao kín) → ô Cột.
        Record("Lấp cột");
        int w = map.Width, h = map.Height;
        var reach = new bool[w, h];
        var q = new Queue<Vector2Int>();
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            Seed(c.x + 1, c.y); Seed(c.x - 1, c.y); Seed(c.x, c.y + 1); Seed(c.x, c.y - 1);
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (!reach[x, y] && map.Get(x, y) == RoadCell.Empty) map.Set(x, y, RoadCell.Column);
        Changed();

        void Seed(int x, int y)
        {
            if (!map.InBounds(x, y) || reach[x, y] || map.IsRoad(x, y)) return;
            reach[x, y] = true;
            q.Enqueue(new Vector2Int(x, y));
        }
    }

    [HorizontalGroup("Mẫu nhanh/Row"), Button("Xoá hết"), EnableIf(nameof(HasMap))]
    private void ClearAll()
    {
        Record("Xoá map");
        map.Clear();
        Changed();
    }

    private bool HasMap => map != null;
    private string hover = "";

    // ================= GRID =================

    private static readonly Color ColEmpty = new(0.36f, 0.36f, 0.48f);
    private static readonly Color ColRoad = new(0.16f, 0.16f, 0.22f);
    private static readonly Color ColColumn = new(0.25f, 0.7f, 0.35f);
    private static readonly Color ColExit = new(1f, 0.8f, 0.2f);
    private static readonly Color ColLine = new(1f, 1f, 1f, 0.08f);

    [OnInspectorGUI, PropertyOrder(100)]
    private void DrawGrid()
    {
        if (map == null)
        {
            EditorGUILayout.HelpBox("Chọn hoặc tạo 1 RoadMap để bắt đầu.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Chuột trái = vẽ, chuột phải = xoá. Kéo để vẽ liên tục.", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Cột: ô bắt đầu kéo = đầu RA (cam), ô kết thúc = đầu VÀO (xanh). Kéo lại trên cột để đổi hướng.", EditorStyles.miniLabel);

        int w = map.Width, h = map.Height;
        Rect area = GUILayoutUtility.GetRect(w * cellPx, h * cellPx, GUILayout.ExpandWidth(false));
        var columns = map.GetColumns();
        var slotIndex = new Dictionary<Vector2Int, int>();
        foreach (RoadMapSO.ColumnRun c in columns)
            for (int s = 0; s < c.count; s++) slotIndex[c.Cell(s)] = s;

        if (Event.current.type == EventType.Repaint)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Rect r = new Rect(area.x + x * cellPx, area.y + y * cellPx, cellPx, cellPx);
                DrawCell(r, x, y, slotIndex);
            }
        }

        // Hướng cột: mũi tên vào (xanh) / ra (cam) ở 2 đầu cột. Phía vào/ra phải giáp đường, không thì vạch đỏ + cảnh báo.
        var problems = new List<string>();
        foreach (RoadMapSO.ColumnRun c in columns)
        {
            int topY = c.TopY;
            bool topRoad = map.IsRoad(c.x, topY - 1), botRoad = map.IsRoad(c.x, c.bottomY + 1);
            bool RoadAt(ColumnSide s) => s == ColumnSide.Top ? topRoad : s == ColumnSide.Bottom ? botRoad : true;
            bool entryOk = RoadAt(c.entry), exitOk = c.exit != ColumnSide.None && RoadAt(c.exit);
            if (!entryOk) problems.Add($"Cột x={c.x}, y={topY}..{c.bottomY}: phía VÀO ({Side(c.entry)}) không giáp đường");
            if (!exitOk) problems.Add($"Cột x={c.x}, y={topY}..{c.bottomY}: phía RA ({Side(c.exit)}) không giáp đường");
            if (Event.current.type != EventType.Repaint) continue;

            Rect topCell = new Rect(area.x + c.x * cellPx, area.y + topY * cellPx, cellPx, cellPx);
            Rect botCell = new Rect(area.x + c.x * cellPx, area.y + c.bottomY * cellPx, cellPx, cellPx);
            if (c.entry != ColumnSide.None && c.entry != c.exit) DrawFlowMark(c.entry == ColumnSide.Top ? topCell : botCell, c.entry == ColumnSide.Top, true, entryOk);
            if (c.exit != ColumnSide.None) DrawFlowMark(c.exit == ColumnSide.Top ? topCell : botCell, c.exit == ColumnSide.Top, false, exitOk);
        }

        HandleInput(area, w, h);

        EditorGUILayout.LabelField(hover, EditorStyles.miniBoldLabel);
        int slots = 0;
        foreach (RoadMapSO.ColumnRun c in columns) slots += c.count;
        EditorGUILayout.LabelField($"{columns.Count} cột · {slots} ô xe", EditorStyles.miniLabel);
        if (problems.Count > 0)
            EditorGUILayout.HelpBox("Hướng cột chưa hợp lệ (vạch đỏ):\n" + string.Join("\n", problems), MessageType.Warning);

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Bake vào scene", GUILayout.Height(30))) Bake(true);
    }

    private void DrawCell(Rect r, int x, int y, Dictionary<Vector2Int, int> slotIndex)
    {
        RoadCell cell = map.Get(x, y);
        EditorGUI.DrawRect(r, ColEmpty);

        if (cell == RoadCell.Road || cell == RoadCell.Exit)
        {
            // Mảnh ghép: lõi giữa + tay nối sang ô đường bên cạnh → nhìn ra thẳng / góc / ngã ba / ngã tư.
            float m = r.width * 0.2f;
            Rect core = new Rect(r.x + m, r.y + m, r.width - 2 * m, r.height - 2 * m);
            EditorGUI.DrawRect(core, ColRoad);
            if (map.IsRoad(x, y - 1)) EditorGUI.DrawRect(new Rect(core.x, r.y, core.width, m), ColRoad);
            if (map.IsRoad(x, y + 1)) EditorGUI.DrawRect(new Rect(core.x, core.yMax, core.width, m), ColRoad);
            if (map.IsRoad(x - 1, y)) EditorGUI.DrawRect(new Rect(r.x, core.y, m, core.height), ColRoad);
            if (map.IsRoad(x + 1, y)) EditorGUI.DrawRect(new Rect(core.xMax, core.y, m, core.height), ColRoad);
            if (cell == RoadCell.Exit)
            {
                float t = r.width * 0.12f;
                EditorGUI.DrawRect(new Rect(r.center.x - t, r.center.y - t, 2 * t, 2 * t), ColExit);
            }
        }
        else if (cell == RoadCell.Column)
        {
            EditorGUI.DrawRect(new Rect(r.x + 3, r.y + 1, r.width - 6, r.height - 2), ColColumn);
            if (slotIndex.TryGetValue(new Vector2Int(x, y), out int s))
                GUI.Label(r, s.ToString(), CenterLabel);
        }

        // Kẻ lưới.
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), ColLine);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), ColLine);
    }

    private static string Side(ColumnSide s) => s == ColumnSide.Top ? "trên" : s == ColumnSide.Bottom ? "dưới" : "không";

    private static readonly Color ColIn = new(0.3f, 0.8f, 1f);
    private static readonly Color ColOut = new(1f, 0.6f, 0.15f);

    /// <summary>Mũi tên nhỏ ở đầu cột trên lưới: vào = xanh (chỉ vào trong cột), ra = cam (chỉ ra ngoài). Sai = vạch đỏ ở mép.</summary>
    private static void DrawFlowMark(Rect cell, bool top, bool entry, bool ok)
    {
        bool pointDown = top == entry;
        Color c = entry ? ColIn : ColOut;
        float h = cell.height * 0.28f, w = cell.width * 0.5f;
        float cy = top ? cell.y + h * 0.5f + 2f : cell.yMax - h * 0.5f - 2f;
        // Tam giác vẽ bằng các dải ngang thu dần.
        int rows = Mathf.Max(3, Mathf.RoundToInt(h));
        for (int i = 0; i < rows; i++)
        {
            float t = i / (float)rows;
            float rw = w * (pointDown ? 1f - t : t);
            float y = cy - h * 0.5f + h * t;
            EditorGUI.DrawRect(new Rect(cell.center.x - rw * 0.5f, y, rw, h / rows + 0.5f), c);
        }
        if (!ok) EditorGUI.DrawRect(new Rect(cell.x, top ? cell.y : cell.yMax - 3, cell.width, 3), new Color(1f, 0.25f, 0.25f, 0.95f));
    }

    private static GUIStyle centerLabel;
    private static GUIStyle CenterLabel => centerLabel ??= new GUIStyle(EditorStyles.miniLabel)
    {
        alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.8f) },
    };

    private void HandleInput(Rect area, int w, int h)
    {
        Event e = Event.current;
        if (!area.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseMove) hover = "";
            return;
        }
        int x = Mathf.FloorToInt((e.mousePosition.x - area.x) / cellPx);
        int y = Mathf.FloorToInt((e.mousePosition.y - area.y) / cellPx);
        if (!map.InBounds(x, y)) return;

        if (e.type == EventType.MouseMove)
        {
            hover = $"({x},{y})  {Describe(x, y)}";
            Repaint();
        }

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && (e.button == 0 || e.button == 1))
        {
            if (e.type == EventType.MouseDown)
            {
                Undo.IncrementCurrentGroup();
                strokeStartY.Clear();
            }
            RoadCell want = e.button == 1 ? RoadCell.Empty : brush switch
            {
                Brush.Đường => RoadCell.Road,
                Brush.Cột => RoadCell.Column,
                Brush.Lối_ra => RoadCell.Exit,
                _ => RoadCell.Empty,
            };
            bool changed = false;
            if (map.Get(x, y) != want)
            {
                Record("Vẽ road map");
                map.Set(x, y, want);
                changed = true;
            }
            // Hướng cột theo chiều kéo: ô bắt đầu nét = đầu RA, ô đang tới = đầu VÀO (kéo lại trên cột có sẵn để đổi hướng).
            if (want == RoadCell.Column)
            {
                if (!strokeStartY.TryGetValue(x, out int y0)) strokeStartY[x] = y;
                else if (y0 != y)
                {
                    bool exitBottom = y0 > y; // bắt đầu ở dưới, kéo lên → ra dưới, vào trên
                    var exit = exitBottom ? ColumnSide.Bottom : ColumnSide.Top;
                    var entry = exitBottom ? ColumnSide.Top : ColumnSide.Bottom;
                    if (map.GetFlow(x, y) != (entry, exit))
                    {
                        Record("Hướng cột");
                        map.SetColumnFlow(x, y, entry, exit);
                        changed = true;
                    }
                }
            }
            if (changed) Changed();
            hover = $"({x},{y})  {Describe(x, y)}";
            e.Use();
        }
    }

    private string Describe(int x, int y)
    {
        RoadCell c = map.Get(x, y);
        if (c == RoadCell.Column)
        {
            (ColumnSide en, ColumnSide ex) = map.GetFlow(x, y);
            return $"Cột (ô xe) · vào {Side(en)}, ra {Side(ex)}";
        }
        if (c == RoadCell.Empty) return "Trống";
        bool u = map.IsRoad(x, y - 1), d = map.IsRoad(x, y + 1), l = map.IsRoad(x - 1, y), r = map.IsRoad(x + 1, y);
        int n = (u ? 1 : 0) + (d ? 1 : 0) + (l ? 1 : 0) + (r ? 1 : 0);
        string piece = n switch
        {
            4 => "Ngã tư",
            3 => "Ngã ba",
            2 => (u && d) || (l && r) ? "Thẳng" : "Góc",
            1 => "Cụt",
            _ => "Ô lẻ",
        };
        return c == RoadCell.Exit ? $"Lối ra · {piece}" : $"Đường · {piece}";
    }

    // ================= BAKE =================

    private void Record(string name) => Undo.RecordObject(map, name);

    private void Changed()
    {
        EditorUtility.SetDirty(map);
        if (liveBake) Bake(false);
        Repaint();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    protected override void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        base.OnDisable();
    }

    private void OnUndoRedo()
    {
        if (map == null) return;
        map.Touch();
        if (liveBake) Bake(false);
        Repaint();
    }

    private void OnMapChanged()
    {
        if (map == null) return;
        newWidth = map.Width;
        newHeight = map.Height;
    }

    private void Bake(bool select)
    {
        if (map == null) return;
        RoadMapBuilder builder = FindBuilder(select);
        if (builder == null) return;
        builder.Build();
        SceneView.RepaintAll();
        if (select) Selection.activeGameObject = builder.gameObject;
    }

    /// <summary>Tam giác chỉ xuống dùng cho mũi tên cột (giống RoadLoopBuilder).</summary>
    private static Sprite LoadArrowSprite()
    {
        const string path = "Assets/01_PackingLoop/RipImport/Texture2D/sactx-2-2048x2048-ASTC 6x6-gameplay-scene-atlas-2b70c67b.png";
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Sprite s && s.name.EndsWith("_0")) return s;
        return null;
    }

    /// <summary>Builder trong scene đang dùng map này; chưa có thì (khi bấm Bake) tạo mới cạnh road cũ.</summary>
    private RoadMapBuilder FindBuilder(bool create)
    {
        foreach (RoadMapBuilder b in FindObjectsByType<RoadMapBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.Map == map) return b;
        if (!create) return null;

        var go = new GameObject("RoadMap-" + map.name);
        Undo.RegisterCreatedObjectUndo(go, "Tạo RoadMap");
        GameObject playfield = GameObject.Find("Playfield");
        if (playfield != null) go.transform.SetParent(playfield.transform, false);
        // Cùng transform với road hiện có (RoadLoop / RoadNetwork): nằm trên mặt đất, cùng tỉ lệ px.
        go.transform.SetPositionAndRotation(new Vector3(0f, 0f, 4.74f), Quaternion.Euler(90f, 0f, 0f));
        go.transform.localScale = Vector3.one * 0.3767f;

        var builder = go.AddComponent<RoadMapBuilder>();
        builder.Map = map;
        builder.SetArt(
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/01_PackingLoop/RipImport/Sprite/Road_Vertical.asset"),
            AssetDatabase.LoadAssetAtPath<Material>("Assets/01_PackingLoop/RoadMesh.mat"),
            AssetDatabase.LoadAssetAtPath<Material>("Assets/00_Game/Arts/Road/RoadCurbSide.mat"),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/01_PackingLoop/RipImport/Sprite/asphalt-texture_0.asset"),
            AssetDatabase.LoadAssetAtPath<Material>("Assets/01_PackingLoop/BoardMesh.mat"));
        var spriteMat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");
        builder.SetArrowArt(LoadArrowSprite(), spriteMat);
        builder.SetDashArt(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/00_Game/Arts/Road/RoadDash_Round.png"), spriteMat);
        EditorUtility.SetDirty(builder);
        return builder;
    }
}
