using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Dựng road từ RoadMapSO (lưới ô). Cách làm:
/// 1. Vùng đường = hợp các ô Road/Exit. Dò biên vùng này thành các vòng khép kín (cạnh ô giáp ô không phải đường).
///    Vòng có diện tích dương = mép ngoài vùng đường (viền ngoài), âm = mép đảo (viền đảo).
/// 2. Mỗi chỗ gấp 90° = 1 góc bo tròn, tham số CHUNG như RoadLoop/RoadNetwork:
///    - góc ngoài (mép ngoài khúc cua) = convexRadius;
///    - góc đảo ở khúc cua = đồng tâm với góc ngoài (convexRadius − roadWidth) → đường rộng đều quanh cua;
///    - góc đảo còn lại (giáp đường giữa / ngã ba) = junctionRadius.
/// 3. roadWidth > cỡ ô → mép ngoài nở ra ngoài, đảo/cột giữ nguyên (giống chấm vàng RoadLoop).
/// 4. Viền hở: đầu cột (cạnh ngang giáp ô Column; viền đảo thò thêm curbExtend), lối ra (exitWidth, giữa cạnh ô Exit quay ra ngoài lưới).
/// Art lấy y như RoadNetworkBuilder. Đơn vị "px": 1 px = 0.01 unit local; dựng trong mặt phẳng XY local (object xoay X=90),
/// tâm lưới = gốc local. Chấm kéo: RoadMapBuilderEditor.
/// </summary>
public class RoadMapBuilder : MonoBehaviour
{
    private const float PxToUnit = 0.01f;
    private const string GeneratedName = "Generated";

    // Mặt cắt Road_Vertical (cột pixel) — giống RoadNetworkBuilder.
    private const float OuterColEdge = 455f, OuterColIn = 403f, OuterColOut = 467f, OuterColCenter = 435f;
    private const float OuterUvOut = 457f, OuterUvIn = 409f;
    private const float InnerColEdge = 34f, InnerColOut = 23f, InnerColIn = 90f, InnerColCenter = 56.5f;
    private const float InnerUvOut = 32f, InnerUvIn = 80f;
    private const float AsphaltColA = 130f, AsphaltColB = 370f;
    private const float RowStart = 10f, RowTile = 440f, CapRow = 230f;
    private const float MinRadius = 2f;
    private const float CurbMinLength = 40f;
    private const int ArcSteps = 16;

    // Viền: độ lệch dọc pháp tuyến. Viền ngoài: pháp tuyến ra ngoài vùng đường. Viền đảo: pháp tuyến từ đảo ra đường.
    private const float OuterO0 = OuterColOut - OuterColEdge, OuterO1 = -(OuterColEdge - OuterColIn);
    private const float IslandO0 = -(InnerColEdge - InnerColOut), IslandO1 = InnerColIn - InnerColEdge;
    public const float OuterCurbMid = (OuterO0 + OuterO1) * 0.5f;
    public const float IslandCurbMid = (IslandO0 + IslandO1) * 0.5f;

    [Required, InlineEditor(Expanded = true)]
    [SerializeField] private RoadMapSO map;

    [BoxGroup("Ô (slot)"), LabelText("Cỡ ô xe (px)"), Tooltip("Chỉ để vẽ gizmo / đặt đồ. x = ngang, y = dọc cột.")]
    [SerializeField] private Vector2 slotSize = new(478f, 521f);
    [BoxGroup("Ô (slot)"), LabelText("Gizmo ô")]
    [SerializeField] private bool drawSlotGizmos = true;
    [BoxGroup("Ô (slot)"), LabelText("Gizmo lưới")]
    [SerializeField] private bool drawGridGizmos = true;

    [BoxGroup("Dải phân cách"), LabelText("Bật"), Tooltip("Dải viền trắng chạy giữa các ô trống trong đảo. Mặc định tắt: ô trống trong đảo chỉ là nền đảo.")]
    [SerializeField] private bool drawDividers = false;
    [BoxGroup("Dải phân cách"), LabelText("Bề rộng (px)"), ShowIf(nameof(drawDividers))]
    [SerializeField, Min(10f)] private float dividerWidth = 70f;

    [BoxGroup("Mũi tên cột"), LabelText("Sprite"), Tooltip("Tam giác chỉ xuống (sactx-2 ..._0). Đặt ở miệng cột, chỉ theo chiều xe chạy.")]
    [SerializeField] private Sprite columnArrow;
    [BoxGroup("Mũi tên cột"), LabelText("Material"), Tooltip("Để trống = Sprite-Unlit-Default.")]
    [SerializeField] private Material columnArrowMaterial;
    [BoxGroup("Mũi tên cột"), LabelText("Scale")]
    [SerializeField, Min(0.01f)] private float columnArrowScale = 3f;
    [BoxGroup("Mũi tên cột"), LabelText("Vạch chia giữa cột (px)"), Tooltip("Như RoadLoop: dải mảnh màu thân tam giác giữa 2 cột liền nhau. 0 = tắt.")]
    [SerializeField, Min(0f)] private float columnDividerWidth = 22f;
    [BoxGroup("Mũi tên cột"), LabelText("Không có viền: chừa mép đảo (px)"), Tooltip("Đảo không có đầu viền (viền kín / không viền) → đầu cột cách mép đảo khoảng này.")]
    [SerializeField, Min(0f)] private float columnDividerGap = 20f;

    [FoldoutGroup("Art"), Required][SerializeField] private Sprite profile;          // Road_Vertical
    [FoldoutGroup("Art"), Required][SerializeField] private Material roadMaterial;   // RoadMesh
    [FoldoutGroup("Art")][SerializeField] private Material curbSideMaterial;         // RoadCurbSide
    [FoldoutGroup("Art"), LabelText("Dài thành + bóng (px)")]
    [SerializeField, Min(0f)] private float curbSideLength = 72f;
    [FoldoutGroup("Art")][SerializeField] private Sprite boardSprite;                // asphalt-texture_0
    [FoldoutGroup("Art")][SerializeField] private Material boardMaterial;            // BoardMesh

    [System.NonSerialized] private int builtVersion = -1;

    public RoadMapSO Map
    {
        get => map;
        set => map = value;
    }

    public bool IsDirty => map != null && map.Version != builtVersion;

#if UNITY_EDITOR
    /// <summary>Gán art mặc định (dùng khi window tạo builder mới).</summary>
    public void SetArt(Sprite prof, Material road, Material side, Sprite board, Material boardMat, Sprite arrow = null)
    {
        profile = prof;
        roadMaterial = road;
        curbSideMaterial = side;
        boardSprite = board;
        boardMaterial = boardMat;
        if (arrow != null) columnArrow = arrow;
    }

    public void SetArrowArt(Sprite arrow, Material mat)
    {
        columnArrow = arrow;
        columnArrowMaterial = mat;
    }

    public Sprite ColumnArrowSprite
    {
        get => columnArrow;
        set => columnArrow = value;
    }
#endif

    // ---------- Chấm mũi tên ----------
    private bool hasArrowHandle;
    private Vector2 arrowHandlePx, arrowInwardPx;
    public bool HasArrowHandle => hasArrowHandle;
    public Vector2 ArrowHandlePx => arrowHandlePx;
    public Vector2 ArrowInwardPx => arrowInwardPx;

    public void SetArrowHandle(Vector3 world)
    {
        map.arrowInset += Vector2.Dot(WorldToPx(world) - arrowHandlePx, arrowInwardPx);
        map.Touch();
    }

    // ================= LƯỚI MỞ RỘNG =================

    /// <summary>
    /// Lưới dùng để dựng hình = lưới map + 1 hàng "khoảng đầu cột" (cao columnEndMargin) chèn vào mỗi đường biên có miệng cột
    /// (ô Column giáp ô đường theo chiều dọc). Ô của hàng chèn: giữa 2 ô đường → đường; giáp cột → phần nối dài của cột
    /// (Margin, không phải ô xe); còn lại → trống. Nhờ vậy đảo dài thêm ở miệng cột còn đường phía sau dịch ra, giữ bề rộng.
    /// </summary>
    private class Grid
    {
        public int Width, Height;
        public RoadCell[,] C;
        public bool[,] Margin;
        public float[] Xs, Ys; // vị trí px các đường lưới (Width+1, Height+1)
        public int[] RowOf;    // hàng map → hàng lưới mở rộng

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public RoadCell Get(int x, int y) => InBounds(x, y) ? C[x, y] : RoadCell.Empty;
        public bool IsRoad(int x, int y) { RoadCell c = Get(x, y); return c == RoadCell.Road || c == RoadCell.Exit; }
        public Vector2 P(Vector2Int v) => new(Xs[v.x], Ys[v.y]);
        public float TotalW => Xs[Width];
        public float TotalH => Ys[Height];
    }

    private Grid g;
    private Grid G => g ??= BuildGrid();

    private Grid BuildGrid()
    {
        var grid = new Grid();
        if (map == null)
        {
            grid.Width = grid.Height = 0;
            grid.C = new RoadCell[0, 0];
            grid.Margin = new bool[0, 0];
            grid.Xs = new[] { 0f };
            grid.Ys = new[] { 0f };
            grid.RowOf = new int[0];
            return grid;
        }

        int w = map.Width, h = map.Height;
        float cs = map.cellSize, m = map.columnEndMargin;
        bool Col(int x, int y) => map.Get(x, y) == RoadCell.Column;
        bool MouthLine(int k)
        {
            for (int x = 0; x < w; x++)
                if ((map.IsRoad(x, k - 1) && Col(x, k)) || (Col(x, k - 1) && map.IsRoad(x, k))) return true;
            return false;
        }

        // Mỗi hàng mở rộng: (hàng map, hoặc -k = hàng chèn trước hàng map k).
        var rows = new List<int>();
        var heights = new List<float>();
        grid.RowOf = new int[h];
        for (int y = 0; y < h; y++)
        {
            if (y > 0 && m > 0.5f && MouthLine(y)) { rows.Add(-y - 1); heights.Add(m); }
            grid.RowOf[y] = rows.Count;
            rows.Add(y);
            heights.Add(cs);
        }

        grid.Width = w;
        grid.Height = rows.Count;
        grid.C = new RoadCell[w, rows.Count];
        grid.Margin = new bool[w, rows.Count];
        for (int r = 0; r < rows.Count; r++)
        for (int x = 0; x < w; x++)
        {
            if (rows[r] >= 0) { grid.C[x, r] = map.Get(x, rows[r]); continue; }
            int k = -rows[r] - 1; // chèn giữa hàng k−1 và k
            bool ra = map.IsRoad(x, k - 1), rb = map.IsRoad(x, k);
            if (ra && rb) grid.C[x, r] = RoadCell.Road;
            else if (Col(x, k - 1) || Col(x, k)) { grid.C[x, r] = RoadCell.Column; grid.Margin[x, r] = true; }
            else grid.C[x, r] = RoadCell.Empty;
        }

        grid.Xs = new float[w + 1];
        for (int x = 0; x <= w; x++) grid.Xs[x] = x * cs;
        grid.Ys = new float[rows.Count + 1];
        for (int r = 0; r < rows.Count; r++) grid.Ys[r + 1] = grid.Ys[r] + heights[r];
        return grid;
    }

    private bool[,] _outside;

    /// <summary>Ô (lưới mở rộng) là đường của vòng ngoài → cạnh đảo giáp nó bị nở thêm islandPadding.</summary>
    private bool IsRingRoad(int x, int y)
    {
        if (!G.IsRoad(x, y)) return false;
        _outside ??= OutsideMask();
        bool Out(int a, int b) => !G.InBounds(a, b) || _outside[a, b];
        return Out(x - 1, y) || Out(x + 1, y) || Out(x, y - 1) || Out(x, y + 1);
    }

    // ================= TOẠ ĐỘ =================

    private float Cs => map != null ? map.cellSize : 520f;
    private float GridW => G.TotalW;
    private float GridH => G.TotalH;
    private float Delta => map != null ? map.roadWidth - Cs : 0f; // mép ngoài nở ra thêm

    private Vector3 ToLocal(Vector2 px) => new((px.x - GridW * 0.5f) * PxToUnit, (GridH * 0.5f - px.y) * PxToUnit, 0f);
    private Vector2 FromLocal(Vector3 l) => new(l.x / PxToUnit + GridW * 0.5f, GridH * 0.5f - l.y / PxToUnit);
    public Vector3 PxToWorld(Vector2 px) => transform.TransformPoint(ToLocal(px));
    public Vector2 WorldToPx(Vector3 w) => FromLocal(transform.InverseTransformPoint(w));
    public Vector3 PxDirToWorld(Vector2 d) => transform.TransformDirection(new Vector3(d.x, -d.y, 0f)).normalized;
    /// <summary>Tâm ô theo toạ độ MAP (x, y gốc).</summary>
    public Vector2 CellCenterPx(Vector2Int c)
    {
        int r = G.RowOf[c.y];
        return new Vector2((G.Xs[c.x] + G.Xs[c.x + 1]) * 0.5f, (G.Ys[r] + G.Ys[r + 1]) * 0.5f);
    }

    // ================= CỘT / Ô =================

    private List<RoadMapSO.ColumnRun> _columns;
    private List<RoadMapSO.ColumnRun> Columns => _columns ??= map != null ? map.GetColumns() : new List<RoadMapSO.ColumnRun>();

    public int ColumnCount => Columns.Count;
    public int SlotsInColumn(int col) => Columns[col].count;
    public Vector2Int ColumnCell(int col, int slot) => Columns[col].Cell(slot);
    /// <summary>Tâm ô xe (world). slot 0 = dưới cùng.</summary>
    public Vector3 GetSlotCenterWorld(int col, int slot) => PxToWorld(CellCenterPx(Columns[col].Cell(slot)));
    public Vector2 SlotSizeWorld => Vector2.Scale(slotSize * PxToUnit, new Vector2(transform.lossyScale.x, transform.lossyScale.y));

    // ================= DỮ LIỆU BIÊN =================

    private struct Pt
    {
        public Vector2 P, N; // px (y xuống), pháp tuyến (xem từng chỗ dùng)
        public Pt(Vector2 p, Vector2 n) { P = p; N = n; }
    }

    private struct UnitEdge
    {
        public Vector2Int Start, Dir;
        public bool Open;
        public bool Ring; // cạnh thuộc ô đường của vòng ngoài (ô đường giáp phía ngoài map)
    }

    private class Run
    {
        public Vector2Int Start, Dir;
        public bool Ring;
        public readonly List<bool> Open = new(); // từng cạnh ô dọc theo run
        public int Len => Open.Count;
    }

    private class Loop
    {
        public readonly List<Run> Runs = new();
        public bool Outer;
        public List<Pt> Pts = new();
        public List<bool> Curb = new(); // đoạn i (Pts[i] → Pts[i+1]) có viền không
        public float[] S;               // quãng đường tích luỹ, S[n] = Total
        public float Total;
    }

    // ---------- Chấm kéo ----------

    /// <summary>Góc có chấm: Junction = false → bo góc ngoài (cam), true → bo ngã ba (hồng).</summary>
    public struct CornerHandle
    {
        public bool Junction;
        public float Radius;
        public Vector2 VertexPx, BisectorPx; // đỉnh góc (sau khi nở đường) + hướng từ đỉnh vào tâm cung
        public Vector2 MidPx => VertexPx + BisectorPx * (Radius * (Mathf.Sqrt(2f) - 1f));
    }

    public struct ExitEnd
    {
        public Vector2 CenterPx, DirPx, PosPx; // tâm lối ra (trên mép ngoài), hướng dọc mép, vị trí chấm (giữa viền)
    }

    public struct CurbEnd
    {
        public int Loop;
        public bool Start;
        public float BaseS; // vị trí đầu/cuối viền khi viền thò = 0
        public bool Ring;   // đầu viền quay ra vòng ngoài → chỉnh curbExtendRing, ngược lại curbExtend
        public Vector2 PosPx;
        public Vector2 DirPx; // hướng dọc viền tại đầu viền → chấm chỉ kéo được 2 chiều theo mép đảo
    }

    private readonly List<CornerHandle> cornerHandles = new();
    private readonly List<ExitEnd> exitEnds = new();
    private readonly List<CurbEnd> curbEnds = new();
    private List<Loop> loops = new();
    private Vector2 roadWidthHandlePx;
    private bool hasRoadWidthHandle;
    private Vector2 paddingHandlePx, paddingDirPx;
    private bool hasPaddingHandle;

    public bool HasPaddingHandle => hasPaddingHandle;
    public Vector2 PaddingHandlePx => paddingHandlePx;
    public Vector2 PaddingDirPx => paddingDirPx;

    public void SetPaddingHandle(Vector3 world)
    {
        map.islandPadding = Mathf.Clamp(map.islandPadding + Vector2.Dot(WorldToPx(world) - paddingHandlePx, paddingDirPx), 0f, 2f * Cs);
        map.Touch();
    }

    public IReadOnlyList<CornerHandle> CornerHandles => cornerHandles;
    public IReadOnlyList<ExitEnd> ExitEnds => exitEnds;
    public IReadOnlyList<CurbEnd> CurbEnds => curbEnds;
    public bool HasRoadWidthHandle => hasRoadWidthHandle;
    public Vector2 RoadWidthHandlePx => roadWidthHandlePx;

    public void SetCornerHandle(CornerHandle c, Vector3 world)
    {
        float along = Vector2.Dot(WorldToPx(world) - c.VertexPx, c.BisectorPx);
        float r = Mathf.Clamp(along / (Mathf.Sqrt(2f) - 1f), MinRadius, 20f * Cs);
        if (c.Junction) map.junctionRadius = r;
        else map.convexRadius = r;
        map.Touch();
    }

    public void SetRoadWidthHandle(Vector3 world)
    {
        float x = WorldToPx(world).x;
        map.roadWidth = Mathf.Max(Cs * 0.5f, map.roadWidth + (x - roadWidthHandlePx.x));
        map.Touch();
    }

    public void SetExitHandle(ExitEnd e, Vector3 world)
    {
        map.exitWidth = Mathf.Max(0f, 2f * Mathf.Abs(Vector2.Dot(WorldToPx(world) - e.CenterPx, e.DirPx)));
        map.Touch();
    }

    public void SetCurbEndHandle(CurbEnd c, Vector3 world)
    {
        Loop l = loops[c.Loop];
        float s = NearestS(l, WorldToPx(world));
        float d = Mathf.Repeat(s - c.BaseS + l.Total * 0.5f, l.Total) - l.Total * 0.5f;
        float e = Mathf.Clamp(c.Start ? -d : d, -2f * Cs, 3f * Cs);
        if (c.Ring) map.curbExtendRing = e;
        else map.curbExtend = e;
        map.Touch();
    }

    // ================= DÒ BIÊN =================

    private static Vector2Int RightOf(Vector2Int d) => new(-d.y, d.x); // rẽ phải trên màn hình (y xuống)
    private static Vector2Int LeftOf(Vector2Int d) => new(d.y, -d.x);
    private static Vector2 NormalOf(Vector2 d) => new(d.y, -d.x);       // pháp tuyến ra khỏi vùng đường

    /// <summary>Ô không phải đường thông ra mép lưới (= phía ngoài map).</summary>
    private bool[,] OutsideMask()
    {
        Grid map = G;
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
        return reach;

        void Seed(int x, int y)
        {
            if (!map.InBounds(x, y) || reach[x, y] || map.IsRoad(x, y)) return;
            reach[x, y] = true;
            q.Enqueue(new Vector2Int(x, y));
        }
    }

    private List<UnitEdge> CollectEdges()
    {
        Grid map = G;
        var l = new List<UnitEdge>();
        bool[,] outside = OutsideMask();
        bool Outside(int x, int y) => !map.InBounds(x, y) || outside[x, y];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (!map.IsRoad(x, y)) continue;
            bool ring = Outside(x - 1, y) || Outside(x + 1, y) || Outside(x, y - 1) || Outside(x, y + 1);
            // Đi theo chiều kim đồng hồ quanh ô (đường nằm bên PHẢI hướng đi).
            Try(x, y - 1, new Vector2Int(x, y), new Vector2Int(1, 0), true);
            Try(x + 1, y, new Vector2Int(x + 1, y), new Vector2Int(0, 1), false);
            Try(x, y + 1, new Vector2Int(x + 1, y + 1), new Vector2Int(-1, 0), true);
            Try(x - 1, y, new Vector2Int(x, y + 1), new Vector2Int(0, -1), false);

            void Try(int nx, int ny, Vector2Int start, Vector2Int dir, bool horizontal)
            {
                if (map.IsRoad(nx, ny)) return;
                bool open = horizontal && map.Get(nx, ny) == RoadCell.Column; // đầu cột: xe ra/vào
                l.Add(new UnitEdge { Start = start, Dir = dir, Open = open, Ring = ring });
            }
        }
        return l;
    }

    private List<Loop> TraceLoops()
    {
        List<UnitEdge> edges = CollectEdges();
        var byStart = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < edges.Count; i++)
        {
            if (!byStart.TryGetValue(edges[i].Start, out var list)) byStart[edges[i].Start] = list = new List<int>();
            list.Add(i);
        }

        var used = new bool[edges.Count];
        var res = new List<Loop>();
        for (int first = 0; first < edges.Count; first++)
        {
            if (used[first]) continue;
            var seq = new List<UnitEdge>();
            int cur = first;
            int guard = edges.Count + 4;
            while (guard-- > 0)
            {
                used[cur] = true;
                seq.Add(edges[cur]);
                Vector2Int end = edges[cur].Start + edges[cur].Dir;
                if (!byStart.TryGetValue(end, out var cand)) break;
                // Ưu tiên rẽ phải (về phía đường) → 2 ô chéo nhau không bị nối thành 1 vòng.
                Vector2Int d = edges[cur].Dir;
                int next = -1;
                foreach (Vector2Int want in new[] { RightOf(d), d, LeftOf(d) })
                {
                    foreach (int k in cand)
                        if (edges[k].Dir == want && (!used[k] || k == first)) { next = k; break; }
                    if (next >= 0) break;
                }
                if (next < 0 || next == first) break;
                cur = next;
            }
            if (seq.Count >= 4) res.Add(ToRuns(seq));
        }
        return res;
    }

    /// <summary>Gộp các cạnh ô thẳng hàng thành run; run đầu tiên bắt đầu ở 1 góc.</summary>
    private static Loop ToRuns(List<UnitEdge> seq)
    {
        int n = seq.Count, s0 = 0;
        for (int i = 0; i < n; i++)
            if (seq[i].Dir != seq[(i - 1 + n) % n].Dir) { s0 = i; break; }

        var loop = new Loop();
        Run run = null;
        for (int k = 0; k < n; k++)
        {
            UnitEdge e = seq[(s0 + k) % n];
            if (run == null || e.Dir != run.Dir)
            {
                run = new Run { Start = e.Start, Dir = e.Dir };
                loop.Runs.Add(run);
            }
            run.Open.Add(e.Open);
            run.Ring |= e.Ring;
        }

        // Diện tích có dấu (y xuống): dương = đi quanh vùng đường theo chiều kim đồng hồ = mép ngoài.
        float area = 0f;
        for (int i = 0; i < loop.Runs.Count; i++)
        {
            Vector2Int a = loop.Runs[i].Start, b = loop.Runs[(i + 1) % loop.Runs.Count].Start;
            area += a.x * b.y - b.x * a.y;
        }
        loop.Outer = area > 0f;
        return loop;
    }

    /// <summary>Khoá góc ngoài: (đỉnh, hướng vào tâm cung). Góc đảo đồng tâm nằm ở đỉnh + hướng đó (lệch 1 ô chéo).</summary>
    private static (Vector2Int, Vector2Int) CornerKey(Run pin, Run pout) => (pout.Start, pout.Dir - pin.Dir);

    /// <summary>
    /// Sinh điểm cho vòng: mỗi run (cạnh thẳng) dời song song 1 khoảng dọc pháp tuyến (dương = ra khỏi đường):
    /// mép ngoài = (bề rộng đường − cỡ ô) + padding; cạnh đảo quay ra vòng ngoài = −padding (đảo nở ra đường); còn lại 0.
    /// Đỉnh góc = giao 2 cạnh đã dời, bo cung tại đó. Đoạn thẳng chia ≤ 60px, tách ở mép ô (để biết đoạn nào hở viền).
    /// </summary>
    private void Sample(Loop loop, HashSet<(Vector2Int, Vector2Int)> outerCorners)
    {
        float pad = map.islandPadding;
        int n = loop.Runs.Count;
        var off = new float[n];
        for (int i = 0; i < n; i++) off[i] = loop.Outer ? Delta + pad : loop.Runs[i].Ring ? -pad : 0f;
        var vf = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int p = (i - 1 + n) % n;
            vf[i] = G.P(loop.Runs[i].Start) + NormalOf(loop.Runs[p].Dir) * off[p] + NormalOf(loop.Runs[i].Dir) * off[i];
        }

        var radius = new float[n];
        for (int i = 0; i < n; i++)
        {
            Run pin = loop.Runs[(i - 1 + n) % n], pout = loop.Runs[i];
            bool convex = pout.Dir == RightOf(pin.Dir);
            float lenIn = Vector2.Distance(vf[(i - 1 + n) % n], vf[i]), lenOut = Vector2.Distance(vf[i], vf[(i + 1) % n]);
            float max = 0.5f * Mathf.Min(lenIn, lenOut);
            (Vector2Int v, Vector2Int dir) = CornerKey(pin, pout);

            float r;
            bool handle = true, junction;
            if (loop.Outer && convex) { r = map.convexRadius; junction = false; }
            else if (loop.Outer) { r = map.junctionRadius; junction = true; }
            // Góc đảo ở khúc cua vòng ngoài: đồng tâm với góc ngoài → đường rộng đều.
            else if (!convex && outerCorners.Contains((v - dir, dir))) { r = map.convexRadius - map.roadWidth; handle = false; junction = false; }
            else { r = map.junctionRadius; junction = true; }
            r = Mathf.Clamp(r, MinRadius, Mathf.Max(MinRadius, max));
            radius[i] = r;

            if (!handle) continue;
            cornerHandles.Add(new CornerHandle
            {
                Junction = junction, Radius = r, VertexPx = vf[i], BisectorPx = ((Vector2)dir).normalized,
            });
        }

        for (int i = 0; i < n; i++)
        {
            Run pin = loop.Runs[(i - 1 + n) % n], run = loop.Runs[i];
            Vector2 din = pin.Dir, d = run.Dir;
            Vector2 v = vf[i];
            float r = radius[i];
            Vector2 p0 = G.P(run.Start);
            float shift = Vector2.Dot(v - p0, d); // đỉnh dời dọc run bao nhiêu so với lưới
            float runLen = Vector2.Distance(v, vf[(i + 1) % n]);
            // Vị trí các mép ô dọc run (lưới mở rộng: hàng cao không đều).
            var lineS = new float[run.Len + 1];
            for (int k = 0; k <= run.Len; k++) lineS[k] = Vector2.Dot(G.P(run.Start + run.Dir * k) - p0, d);

            if (!loop.Outer && run.Ring && run.Dir == new Vector2Int(0, 1) && !hasPaddingHandle)
            {
                // Chấm padding: giữa cạnh trái đảo (cạnh đi xuống), kéo ngang về phía đường.
                hasPaddingHandle = true;
                paddingHandlePx = v + d * (runLen * 0.5f);
                paddingDirPx = -NormalOf(d);
            }

            // Cung bo góc i: từ A = v − din·r tới B = v + d·r, tâm C = v − din·r + d·r.
            bool arcCurb = !pin.Open[^1] || !run.Open[0];
            Vector2 c = v - din * r + d * r;
            Vector2 u0 = -d, u1 = din;
            float sign = Vector2.Dot(u0, NormalOf(din)) > 0f ? 1f : -1f;
            for (int k = 0; k < ArcSteps; k++)
            {
                float a = 0.5f * Mathf.PI * k / ArcSteps;
                Vector2 dir = u0 * Mathf.Cos(a) + u1 * Mathf.Sin(a);
                Add(c + dir * r, dir * sign, arcCurb);
            }

            // Đoạn thẳng từ B tới đầu cung góc i+1.
            Vector2 nrm = NormalOf(d);
            float s0 = r, s1 = runLen - radius[(i + 1) % n];
            var cuts = new List<float> { s0 };
            for (int k = 1; k < run.Len; k++)
            {
                float sk = lineS[k] - shift;
                if (sk > s0 && sk < s1) cuts.Add(sk);
            }
            cuts.Add(s1);
            for (int k = 0; k + 1 < cuts.Count; k++)
            {
                float a = cuts[k], b = cuts[k + 1];
                if (b - a < 0.01f) continue;
                float mid = (a + b) * 0.5f + shift;
                int e = 0;
                while (e < run.Len - 1 && mid > lineS[e + 1]) e++;
                bool curb = !run.Open[e];
                int steps = Mathf.Max(1, Mathf.CeilToInt((b - a) / 60f));
                for (int j = 0; j < steps; j++) Add(v + d * Mathf.Lerp(a, b, j / (float)steps), nrm, curb);
            }
        }

        // Đảo: đảo chiều + lật pháp tuyến → đi theo chiều kim đồng hồ quanh đảo, pháp tuyến từ đảo ra đường (như RoadNetwork).
        if (!loop.Outer)
        {
            int m = loop.Pts.Count;
            var pts = new List<Pt>(m);
            var curb = new List<bool>(m);
            for (int i = m - 1; i >= 0; i--)
            {
                pts.Add(new Pt(loop.Pts[i].P, -loop.Pts[i].N));
                curb.Add(loop.Curb[(i - 1 + m) % m]);
            }
            loop.Pts = pts;
            loop.Curb = curb;
        }

        void Add(Vector2 p, Vector2 nn, bool curb)
        {
            if (loop.Pts.Count > 0 && (p - loop.Pts[^1].P).sqrMagnitude < 0.25f)
            {
                loop.Curb[^1] = curb;
                return;
            }
            loop.Pts.Add(new Pt(p, nn));
            loop.Curb.Add(curb);
        }
    }

    private static void ComputeS(Loop l)
    {
        int n = l.Pts.Count;
        l.S = new float[n + 1];
        for (int i = 0; i < n; i++) l.S[i + 1] = l.S[i] + Vector2.Distance(l.Pts[i].P, l.Pts[(i + 1) % n].P);
        l.Total = l.S[n];
    }

    private static Pt PointAt(Loop l, float s)
    {
        int n = l.Pts.Count;
        s = Mathf.Repeat(s, l.Total);
        for (int k = 0; k < n; k++)
            if (s <= l.S[k + 1])
                return Lerp(l.Pts[k], l.Pts[(k + 1) % n], Mathf.InverseLerp(l.S[k], l.S[k + 1], s));
        return l.Pts[0];
    }

    private static float NearestS(Loop l, Vector2 p)
    {
        int n = l.Pts.Count;
        float best = float.MaxValue, bestS = 0f;
        for (int k = 0; k < n; k++)
        {
            Vector2 a = l.Pts[k].P, b = l.Pts[(k + 1) % n].P, ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float d = (a + ab * t - p).sqrMagnitude;
            if (d < best) { best = d; bestS = Mathf.Lerp(l.S[k], l.S[k + 1], t); }
        }
        return bestS;
    }

    /// <summary>Đoạn path từ quãng a tới b (b có thể vượt Total = quấn vòng).</summary>
    private static List<Pt> SubPath(Loop l, float a, float b)
    {
        if (a < 0f) { a += l.Total; b += l.Total; }
        int n = l.Pts.Count;
        var res = new List<Pt> { PointAt(l, a) };
        for (int k = 0; k < 2 * n; k++)
        {
            float sk = l.S[k % n] + (k >= n ? l.Total : 0f);
            if (sk > a + 0.01f && sk < b - 0.01f) res.Add(l.Pts[k % n]);
        }
        res.Add(PointAt(l, b));
        return res;
    }

    // ---------- Lối ra ----------

    private struct Opening { public Vector2 Center, Dir, Normal; }

    /// <summary>Lối ra: giữa mỗi cạnh ô Exit quay ra ngoài lưới (đã nở theo bề rộng đường).</summary>
    private List<Opening> ExitOpenings()
    {
        var l = new List<Opening>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            if (map.Get(x, y) != RoadCell.Exit) continue;
            foreach (Vector2Int n in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                if (map.InBounds(x + n.x, y + n.y)) continue;
                Vector2 nn = n;
                l.Add(new Opening
                {
                    Center = CellCenterPx(new Vector2Int(x, y)) + nn * (Cs * 0.5f + Delta + map.islandPadding),
                    Normal = nn,
                    Dir = new Vector2(-nn.y, nn.x),
                });
            }
        }
        return l;
    }

    /// <summary>Chèn điểm đúng tại 2 mép lối ra rồi đánh dấu các đoạn nằm trong lối ra là hở.</summary>
    private void ApplyOpenings(Loop l, List<Opening> openings)
    {
        float half = map.exitWidth * 0.5f;
        foreach (Opening o in openings)
        {
            if (half <= 0f) continue;
            foreach (float t in new[] { -half, half })
            {
                for (int i = 0; i < l.Pts.Count; i++)
                {
                    int j = (i + 1) % l.Pts.Count;
                    Vector2 a = l.Pts[i].P, b = l.Pts[j].P;
                    if (Mathf.Abs(Vector2.Dot(a - o.Center, o.Normal)) > 2f || Mathf.Abs(Vector2.Dot(b - o.Center, o.Normal)) > 2f) continue;
                    float da = Vector2.Dot(a - o.Center, o.Dir) - t, db = Vector2.Dot(b - o.Center, o.Dir) - t;
                    if (da * db >= 0f) continue;
                    Pt p = Lerp(l.Pts[i], l.Pts[j], da / (da - db));
                    l.Pts.Insert(i + 1, p);
                    l.Curb.Insert(i + 1, l.Curb[i]);
                    break;
                }
            }
            for (int i = 0; i < l.Pts.Count; i++)
            {
                Vector2 m = (l.Pts[i].P + l.Pts[(i + 1) % l.Pts.Count].P) * 0.5f;
                if (Mathf.Abs(Vector2.Dot(m - o.Center, o.Normal)) <= 2f && Mathf.Abs(Vector2.Dot(m - o.Center, o.Dir)) < half)
                    l.Curb[i] = false;
            }
        }
    }

    // ---------- Khoảng có viền ----------

    /// <summary>Các khoảng [a, b] (theo quãng đường) có viền. null = cả vòng có viền.</summary>
    private static List<Vector2> CurbIntervals(Loop l)
    {
        int n = l.Pts.Count;
        int start = l.Curb.IndexOf(false);
        if (start < 0) return null;
        var res = new List<Vector2>();
        float a = -1f;
        for (int k = 1; k <= n; k++)
        {
            int i = (start + k) % n;
            float si = l.S[i] + (start + k >= n ? l.Total : 0f);
            if (l.Curb[i] && a < 0f) a = si;
            if (!l.Curb[i] && a >= 0f) { res.Add(new Vector2(a, si)); a = -1f; }
        }
        if (a >= 0f) res.Add(new Vector2(a, l.S[start] + l.Total));
        return res;
    }

    // ================= BUILD =================

    [Button("Build", ButtonSizes.Large), GUIColor(0.55f, 0.85f, 0.55f), PropertyOrder(-1)]
    public void Build()
    {
        cornerHandles.Clear();
        exitEnds.Clear();
        curbEnds.Clear();
        hasRoadWidthHandle = false;
        hasPaddingHandle = false;
        _columns = null;
        g = null; // lưới mở rộng dựng lại theo map hiện tại
        _outside = null;
        if (map == null || profile == null || roadMaterial == null) return;
        builtVersion = map.Version;

        Transform root = ResetGenerated();
        loops = TraceLoops();

        var outerCorners = new HashSet<(Vector2Int, Vector2Int)>();
        foreach (Loop l in loops)
        {
            if (!l.Outer) continue;
            int n = l.Runs.Count;
            for (int i = 0; i < n; i++)
            {
                Run pin = l.Runs[(i - 1 + n) % n], pout = l.Runs[i];
                if (pout.Dir == RightOf(pin.Dir)) outerCorners.Add(CornerKey(pin, pout));
            }
        }

        List<Opening> openings = ExitOpenings();
        foreach (Loop l in loops)
        {
            Sample(l, outerCorners);
            if (l.Outer) ApplyOpenings(l, openings);
            ComputeS(l);
        }
        BuildHandles(openings);

        var islandFill = new MeshData();
        var asphalt = new MeshData();
        var outerCurb = new MeshData();
        var islandCurb = new MeshData();

        for (int li = 0; li < loops.Count; li++)
        {
            Loop l = loops[li];
            if (l.Pts.Count < 3) continue;
            if (l.Outer)
            {
                FillAsphalt(asphalt, l.Pts);
                Curbs(outerCurb, l, CurbIntervals(l), OuterO0, OuterO1, OuterUvOut, OuterUvIn, OuterColCenter);
            }
            else
            {
                if (boardSprite != null && boardMaterial != null) FillPolygon(islandFill, l.Pts, 8f, null);

                List<Vector2> iv = CurbIntervals(l);
                if (iv != null)
                {
                    // Mỗi đầu viền thò theo phía của nó: miệng cột quay ra vòng ngoài hay vào đường giữa.
                    (bool topRing, bool botRing) = IslandRingSides(li);
                    float midY = LoopMidY(l);
                    bool RingAt(float s) => PointAt(l, s).P.y < midY ? topRing : botRing;
                    float ExtAt(float s) => RingAt(s) ? map.curbExtendRing : map.curbExtend;
                    iv = Extend(l, iv, ExtAt);
                    foreach (Vector2 r in iv)
                    {
                        if (r.y - r.x >= l.Total - 0.5f) continue;
                        bool ra = RingAt(r.x), rb = RingAt(r.y);
                        float ea = ra ? map.curbExtendRing : map.curbExtend, eb = rb ? map.curbExtendRing : map.curbExtend;
                        curbEnds.Add(new CurbEnd { Loop = li, Start = true, Ring = ra, BaseS = r.x + ea, PosPx = CurbPos(l, r.x), DirPx = CurbDir(l, r.x) });
                        curbEnds.Add(new CurbEnd { Loop = li, Start = false, Ring = rb, BaseS = r.y - eb, PosPx = CurbPos(l, r.y), DirPx = CurbDir(l, r.y) });
                    }
                }
                Curbs(islandCurb, l, iv, IslandO0, IslandO1, InnerUvOut, InnerUvIn, InnerColCenter);
            }
        }

        // Mặt đường (order -2) phủ cả vùng trong mép ngoài; nền đảo (order -1) đè lên phần đảo.
        CreateMesh(root, "Asphalt", asphalt, -2);
        if (islandFill.V.Count > 0) CreateMesh(root, "Island-Fill", islandFill, -1, boardMaterial);
        CreateRaised(root, "Curb-Outer", outerCurb);
        CreateRaised(root, "Curb-Island", islandCurb);

        BuildColumnArrows(root);
        BuildColumnDividers(root);

        if (drawDividers)
        {
            var divider = new MeshData();
            BuildDividers(divider);
            CreateRaised(root, "Divider", divider);
        }
    }

    /// <summary>
    /// Mũi tên tam giác theo hướng từng cột (sprite gốc chỉ xuống, giống RoadLoop: scale 3, cách đầu cột arrowInset):
    /// đầu VÀO → mũi tên chỉ vào trong cột; đầu RA → mũi tên chỉ ra ngoài cột.
    /// Vào/ra chung 1 miệng → chỉ vẽ mũi tên ra. Mặc định vào trên, ra dưới → 2 mũi tên cùng chỉ xuống như RoadLoop.
    /// </summary>
    private void BuildColumnArrows(Transform root)
    {
        hasArrowHandle = false;
        if (columnArrow == null) return;
        var parent = new GameObject("Arrows").transform;
        parent.SetParent(root, false);
        float inset = map.arrowInset;

        foreach (RoadMapSO.ColumnRun c in Columns)
        {
            // Mép miệng cột = mép đảo thật giáp đường (tính cả khoảng đầu cột + padding đảo phía vòng ngoài).
            int rt = G.RowOf[c.TopY], rb = G.RowOf[c.bottomY];
            if (rt > 0 && G.Margin[c.x, rt - 1]) rt--;
            if (rb + 1 < G.Height && G.Margin[c.x, rb + 1]) rb++;
            float x = (G.Xs[c.x] + G.Xs[c.x + 1]) * 0.5f;
            float edgeTop = G.Ys[rt] - (IsRingRoad(c.x, rt - 1) ? map.islandPadding : 0f);
            float edgeBot = G.Ys[rb + 1] + (IsRingRoad(c.x, rb + 1) ? map.islandPadding : 0f);
            Vector2 inside = CellCenterPx(new Vector2Int(c.x, c.TopY));
            // Đầu/cuối cột kiểu RoadLoop (bám đầu viền đảo); mũi tên thụt arrowInset từ đó.
            float yTop = ColumnEndY(inside, true, edgeTop);
            float yBot = ColumnEndY(inside, false, edgeBot);
            if (c.entry != ColumnSide.None && c.entry != c.exit) Mark(c.entry, true, "In");
            if (c.exit != ColumnSide.None) Mark(c.exit, false, "Out");

            void Mark(ColumnSide side, bool inward, string tag)
            {
                bool top = side == ColumnSide.Top;
                var px = new Vector2(x, top ? yTop + inset : yBot - inset);
                bool pointDown = top == inward; // đầu trên: vào = chỉ xuống; đầu dưới: ra = chỉ xuống
                Arrow($"Arrow-{c.x}-{c.TopY}-{tag}", px, !pointDown, top ? new Vector2(0f, 1f) : new Vector2(0f, -1f));
            }
        }

        // inwardPx (y xuống) = hướng từ mép đầu cột vào trong cột — cho chấm kéo khoảng cách.
        void Arrow(string name, Vector2 px, bool pointUp, Vector2 inwardPx)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ToLocal(px) + new Vector3(0f, 0f, -0.01f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, pointUp ? 180f : 0f);
            go.transform.localScale = Vector3.one * (columnArrow.pixelsPerUnit * PxToUnit * columnArrowScale);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = columnArrow;
            sr.sortingOrder = 5;
            if (columnArrowMaterial != null) sr.sharedMaterial = columnArrowMaterial;

            if (hasArrowHandle) return;
            hasArrowHandle = true;
            arrowHandlePx = px;
            arrowInwardPx = inwardPx;
        }
    }

    /// <summary>
    /// Vạch chia giữa 2 cột liền nhau (như RoadLoop): dải mảnh ở mép chung, dài theo đoạn 2 cột cùng có ô,
    /// màu + độ mờ = texel thân tam giác (cùng atlas với roadMaterial).
    /// </summary>
    private void BuildColumnDividers(Transform root)
    {
        if (columnDividerWidth <= 0f) return;
        Vector2 uv;
        if (columnArrow != null && columnArrow.texture == roadMaterial.mainTexture)
        {
            Rect ar = columnArrow.textureRect;
            Texture at = columnArrow.texture;
            uv = new Vector2((ar.x + ar.width * 0.5f) / at.width, (ar.y + ar.height * 0.7f) / at.height);
        }
        else uv = Uv(OuterColCenter, CapRow);

        var m = new MeshData();
        float half = columnDividerWidth * 0.5f;
        Grid gr = G;
        for (int x = 0; x + 1 < gr.Width; x++)
        {
            int y = 0;
            while (y < gr.Height)
            {
                if (!Both(x, y)) { y++; continue; }
                int y0 = y;
                while (y < gr.Height && Both(x, y)) y++;
                float ex = gr.Xs[x + 1], ya = gr.Ys[y0], yb = gr.Ys[y];
                // Đầu vạch giáp đường = đầu cột kiểu RoadLoop (đầu viền đảo + columnLinePadding), như mũi tên.
                var inside = new Vector2(ex, (ya + yb) * 0.5f);
                if (gr.IsRoad(x, y0 - 1) && gr.IsRoad(x + 1, y0 - 1))
                    ya = ColumnEndY(inside, true, ya - (IsRingRoad(x, y0 - 1) ? map.islandPadding : 0f));
                if (gr.IsRoad(x, y) && gr.IsRoad(x + 1, y))
                    yb = ColumnEndY(inside, false, yb + (IsRingRoad(x, y) ? map.islandPadding : 0f));
                if (yb - ya < 1f) continue;
                m.Quad(ToLocal(new Vector2(ex - half, ya)), ToLocal(new Vector2(ex + half, ya)),
                    ToLocal(new Vector2(ex + half, yb)), ToLocal(new Vector2(ex - half, yb)), uv, uv, uv, uv);
            }
        }
        CreateMesh(root, "Column-Dividers", m, 1);

        // Ô cột hoặc khoảng đầu cột (đều là RoadCell.Column trong lưới mở rộng).
        bool Both(int x, int y) => gr.Get(x, y) == RoadCell.Column && gr.Get(x + 1, y) == RoadCell.Column;
    }

    /// <summary>
    /// Đầu (top) / cuối cột kiểu RoadLoop: tìm đảo chứa điểm inside, lấy đầu viền đảo cao nhất ở nửa trên (hoặc thấp nhất
    /// ở nửa dưới) rồi lùi vào columnLinePadding. Đảo không có đầu viền → mép đảo (edge) lùi columnDividerGap.
    /// </summary>
    private float ColumnEndY(Vector2 inside, bool top, float edge)
    {
        int li = IslandAt(inside);
        if (li >= 0)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Pt p in loops[li].Pts) { minY = Mathf.Min(minY, p.P.y); maxY = Mathf.Max(maxY, p.P.y); }
            float mid = (minY + maxY) * 0.5f, best = top ? float.MaxValue : float.MinValue;
            bool found = false;
            foreach (CurbEnd c in curbEnds)
            {
                if (c.Loop != li) continue;
                float y = c.PosPx.y;
                if (top && y < mid && y < best) { best = y; found = true; }
                if (!top && y > mid && y > best) { best = y; found = true; }
            }
            if (found) return top ? best + map.columnLinePadding : best - map.columnLinePadding;
        }
        return top ? edge + columnDividerGap : edge - columnDividerGap;
    }

    /// <summary>Chỉ số vòng đảo chứa điểm p (px), −1 nếu không có.</summary>
    private int IslandAt(Vector2 p)
    {
        for (int li = 0; li < loops.Count; li++)
        {
            List<Pt> pts = loops[li].Pts;
            if (loops[li].Outer || pts.Count < 3) continue;
            bool inside = false;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                Vector2 a = pts[i].P, b = pts[j].P;
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            if (inside) return li;
        }
        return -1;
    }

    private static Vector2 CurbPos(Loop l, float s)
    {
        Pt p = PointAt(l, s);
        return p.P + p.N * IslandCurbMid;
    }

    /// <summary>Tiếp tuyến của mép đảo tại s (hướng kéo chấm đầu viền).</summary>
    private static Vector2 CurbDir(Loop l, float s)
    {
        Vector2 d = PointAt(l, s + 2f).P - PointAt(l, s - 2f).P;
        if (d.sqrMagnitude < 1e-6f) { Vector2 n = PointAt(l, s).N; d = new Vector2(-n.y, n.x); }
        return d.normalized;
    }

    private static float LoopMidY(Loop l)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (Pt p in l.Pts) { minY = Mathf.Min(minY, p.P.y); maxY = Mathf.Max(maxY, p.P.y); }
        return (minY + maxY) * 0.5f;
    }

    /// <summary>Miệng cột phía trên / dưới của đảo li có quay ra vòng đường ngoài không.</summary>
    private (bool top, bool bottom) IslandRingSides(int li)
    {
        bool top = false, bottom = false;
        foreach (RoadMapSO.ColumnRun c in map.GetColumns())
        {
            if (IslandAt(CellCenterPx(new Vector2Int(c.x, c.TopY))) != li) continue;
            int rt = G.RowOf[c.TopY], rb = G.RowOf[c.bottomY];
            if (rt > 0 && G.Margin[c.x, rt - 1]) rt--;
            if (rb + 1 < G.Height && G.Margin[c.x, rb + 1]) rb++;
            top |= IsRingRoad(c.x, rt - 1);
            bottom |= IsRingRoad(c.x, rb + 1);
        }
        return (top, bottom);
    }

    /// <summary>Nới mỗi khoảng ra e(s) ở từng đầu (e âm = thu lại), gộp chỗ chồng nhau, bỏ khoảng quá ngắn.</summary>
    private static List<Vector2> Extend(Loop l, List<Vector2> iv, Func<float, float> e)
    {
        var l2 = new List<Vector2>();
        foreach (Vector2 r in iv)
        {
            var x = new Vector2(r.x - e(r.x), r.y + e(r.y));
            if (x.y - x.x >= CurbMinLength) l2.Add(x);
        }
        l2.Sort((p, q) => p.x.CompareTo(q.x));
        var res = new List<Vector2>();
        foreach (Vector2 r in l2)
        {
            if (res.Count > 0 && r.x <= res[^1].y) res[^1] = new Vector2(res[^1].x, Mathf.Max(res[^1].y, r.y));
            else res.Add(r);
        }
        // Quấn vòng: khoảng cuối chồng sang khoảng đầu.
        if (res.Count > 1 && res[^1].y - l.Total >= res[0].x)
        {
            res[0] = new Vector2(res[^1].x, Mathf.Max(res[^1].y, res[0].y + l.Total));
            res.RemoveAt(res.Count - 1);
        }
        for (int i = 0; i < res.Count; i++)
            if (res[i].y - res[i].x >= l.Total) res[i] = new Vector2(0f, l.Total);
        return res;
    }

    private void BuildHandles(List<Opening> openings)
    {
        // Lối ra: 2 chấm ở 2 mép, nằm giữa dải viền ngoài.
        float half = map.exitWidth * 0.5f;
        foreach (Opening o in openings)
            for (int s = -1; s <= 1; s += 2)
                exitEnds.Add(new ExitEnd
                {
                    CenterPx = o.Center, DirPx = o.Dir,
                    PosPx = o.Center + o.Dir * (s * half) + o.Normal * OuterCurbMid,
                });

        // Bề rộng đường: mép ngoài cạnh phải xa nhất, giữa chiều cao run.
        float bestX = float.MinValue;
        foreach (Loop l in loops)
        {
            if (!l.Outer) continue;
            foreach (Run r in l.Runs)
            {
                if (r.Dir != new Vector2Int(0, 1) || G.Xs[r.Start.x] <= bestX) continue; // (0,1) = đi xuống = cạnh phải
                bestX = G.Xs[r.Start.x];
                roadWidthHandlePx = new Vector2(bestX + Delta + map.islandPadding, (G.Ys[r.Start.y] + G.Ys[r.Start.y + r.Len]) * 0.5f);
                hasRoadWidthHandle = true;
            }
        }
    }

    /// <summary>Quét viền theo từng khoảng có viền; khoảng hở thì bo tròn 2 đầu. iv = null → cả vòng.</summary>
    private void Curbs(MeshData m, Loop l, List<Vector2> iv, float o0, float o1, float uvOut, float uvIn, float colCenter)
    {
        if (iv == null)
        {
            Strip(m, l.Pts, true, o0, o1, uvOut, uvIn);
            return;
        }
        foreach (Vector2 r in iv)
        {
            if (r.y - r.x >= l.Total - 0.5f)
            {
                Strip(m, l.Pts, true, o0, o1, uvOut, uvIn);
                continue;
            }
            List<Pt> path = SubPath(l, r.x, r.y);
            if (path.Count < 2) continue;
            Strip(m, path, false, o0, o1, uvOut, uvIn);
            AddCaps(m, path, o0, o1, colCenter, uvOut);
        }
    }

    /// <summary>
    /// Dải viền trắng ở giữa các ô trống trong đảo: mỗi dải ngang/dọc ≥ 2 ô liền nhau = 1 đoạn thẳng qua tâm ô.
    /// Đầu giáp đường → kéo ra tới mép đảo (nối vào viền đảo); đầu tự do → bo tròn.
    /// </summary>
    private void BuildDividers(MeshData m)
    {
        Grid gr = G;
        bool[,] outside = OutsideMask();
        int w = gr.Width, h = gr.Height;
        bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && !outside[x, y] && gr.Get(x, y) == RoadCell.Empty;
        float Along(bool hor, int i) => hor ? gr.Xs[i] : gr.Ys[i];
        float Across(bool hor, int a) => hor ? (gr.Ys[a] + gr.Ys[a + 1]) * 0.5f : (gr.Xs[a] + gr.Xs[a + 1]) * 0.5f;
        float half = dividerWidth * 0.5f;
        // Mặt cắt đối xứng: tâm dải = giữa vùng mặt viền, 2 mép = mép vát xuống đường.
        float colMid = (InnerUvOut + InnerUvIn) * 0.5f, colEdge = InnerUvIn;

        // Dải dọc: ngăn 2 bên cột. Dải ngang: chặn đầu kín của cột (cột trên/dưới không thông nhau).
        foreach (bool horizontal in new[] { true, false })
        {
            int outerN = horizontal ? h : w, innerN = horizontal ? w : h;
            for (int a = 0; a < outerN; a++)
            {
                int b = 0;
                while (b < innerN)
                {
                    if (!InCell(a, b)) { b++; continue; }
                    int b0 = b;
                    while (b < innerN && InCell(a, b)) b++;
                    int b1 = b - 1;
                    // Ô lẻ theo chiều này: thuộc dải chiều kia thì để chiều kia vẽ.
                    if (b1 == b0 && (horizontal ? In(b0, a - 1) || In(b0, a + 1) : In(a - 1, b0) || In(a + 1, b0))) continue;
                    // Đầu dải dừng ở mép đảo (bo tròn, không thò ra đường); đầu tự do dừng giữa ô.
                    bool roadA = IsRoadAt(a, b0 - 1), roadB = IsRoadAt(a, b1 + 1);
                    float s0 = roadA ? Along(horizontal, b0) + half + 10f : (Along(horizontal, b0) + Along(horizontal, b0 + 1)) * 0.5f;
                    float s1 = roadB ? Along(horizontal, b1 + 1) - half - 10f : (Along(horizontal, b1) + Along(horizontal, b1 + 1)) * 0.5f;
                    if (s1 - s0 < 1f) continue;
                    float c = Across(horizontal, a);
                    Vector2 p0 = horizontal ? new Vector2(s0, c) : new Vector2(c, s0);
                    Vector2 p1 = horizontal ? new Vector2(s1, c) : new Vector2(c, s1);
                    Vector2 d = (p1 - p0).normalized, n = new Vector2(d.y, -d.x);
                    var path = new List<Pt> { new(p0, n), new(p1, n) };
                    Strip(m, path, false, 0f, half, colMid, colEdge);
                    Strip(m, path, false, 0f, -half, colMid, colEdge);
                    AddCap(m, path[0], -d, -half, half, colMid, colEdge);
                    AddCap(m, path[1], d, -half, half, colMid, colEdge);
                }
            }

            bool InCell(int a, int b) => horizontal ? In(b, a) : In(a, b);
            bool IsRoadAt(int a, int b) => horizontal ? gr.IsRoad(b, a) : gr.IsRoad(a, b);
        }
    }

    [Button("Clear"), PropertyOrder(-1)]
    public void Clear()
    {
        Transform old = transform.Find(GeneratedName);
        if (old == null) return;
        foreach (MeshFilter mf in old.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) SafeDestroy(mf.sharedMesh);
        SafeDestroy(old.gameObject);
    }

    private Transform ResetGenerated()
    {
        Clear();
        var go = new GameObject(GeneratedName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private static void SafeDestroy(UnityEngine.Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ================= MESH =================

    private class MeshData
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector2> UV = new();
        public readonly List<int> Tri = new();
        public readonly List<Vector3> EdgeA = new(), EdgeB = new(), EdgeIn = new();

        public void StripQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Quad(a, b, c, d, ua, ub, uc, ud);
            EdgeA.Add(a); EdgeB.Add(d); EdgeIn.Add((b + c) * 0.5f);
            EdgeA.Add(b); EdgeB.Add(c); EdgeIn.Add((a + d) * 0.5f);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
            Tri.Add(i); Tri.Add(i + 1); Tri.Add(i + 2);
            Tri.Add(i); Tri.Add(i + 2); Tri.Add(i + 3);
        }

        public void Tri3(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            UV.Add(ua); UV.Add(ub); UV.Add(uc);
            Tri.Add(i); Tri.Add(i + 1); Tri.Add(i + 2);
        }
    }

    private Vector2 Uv(float col, float row)
    {
        Rect r = profile.textureRect;
        Texture t = profile.texture;
        return new Vector2((r.x + col) / t.width, (r.y + r.height - row) / t.height);
    }

    // Viên lát asphalt (px trên map): vùng asphalt của profile (240 cột × 440 hàng) phủ ô này.
    // Cùng mật độ vân với RoadNetwork (ngang ~0.5 texel/px, dọc 1 texel/px).
    private const float AsphaltTileW = 480f, AsphaltTileH = RowTile;

    /// <summary>
    /// Mặt đường = đa giác mép ngoài, cắt theo lưới viên lát; mỗi viên map nguyên vùng asphalt của profile,
    /// viên lẻ lật gương → mép các viên khớp nhau, không lộ đường nối, không bị kéo giãn ở ngã ba/ngã tư.
    /// </summary>
    private void FillAsphalt(MeshData m, List<Pt> path)
    {
        var poly = new List<Vector2>(path.Count);
        foreach (Pt p in path) poly.Add(p.P);
        List<int> tris = Triangulate(poly);
        var clip = new List<Vector2>(8);
        var tmp = new List<Vector2>(8);
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            Vector2 a = poly[tris[t]], b = poly[tris[t + 1]], c = poly[tris[t + 2]];
            Vector2 min = Vector2.Min(a, Vector2.Min(b, c)), max = Vector2.Max(a, Vector2.Max(b, c));
            int ix0 = Mathf.FloorToInt(min.x / AsphaltTileW), ix1 = Mathf.FloorToInt(max.x / AsphaltTileW);
            int iy0 = Mathf.FloorToInt(min.y / AsphaltTileH), iy1 = Mathf.FloorToInt(max.y / AsphaltTileH);
            for (int iy = iy0; iy <= iy1; iy++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float x0 = ix * AsphaltTileW, y0 = iy * AsphaltTileH;
                clip.Clear();
                clip.Add(a); clip.Add(b); clip.Add(c);
                ClipAxis(clip, tmp, 0, x0, true);
                ClipAxis(clip, tmp, 0, x0 + AsphaltTileW, false);
                ClipAxis(clip, tmp, 1, y0, true);
                ClipAxis(clip, tmp, 1, y0 + AsphaltTileH, false);
                if (clip.Count < 3) continue;

                Vector2 TileUv(Vector2 p)
                {
                    float u = (p.x - x0) / AsphaltTileW, v = (p.y - y0) / AsphaltTileH;
                    if ((ix & 1) != 0) u = 1f - u;
                    if ((iy & 1) != 0) v = 1f - v;
                    return Uv(Mathf.Lerp(AsphaltColA + 2f, AsphaltColB - 2f, u), Mathf.Lerp(RowStart + 1f, RowStart + RowTile - 1f, v));
                }
                for (int k = 1; k + 1 < clip.Count; k++)
                    m.Tri3(ToLocal(clip[0]), ToLocal(clip[k]), ToLocal(clip[k + 1]), TileUv(clip[0]), TileUv(clip[k]), TileUv(clip[k + 1]));
            }
        }
    }

    /// <summary>Cắt đa giác lồi theo 1 nửa mặt phẳng trục (axis 0 = x, 1 = y): keepGreater → giữ phần ≥ value.</summary>
    private static void ClipAxis(List<Vector2> poly, List<Vector2> tmp, int axis, float value, bool keepGreater)
    {
        tmp.Clear();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            float da = (keepGreater ? 1f : -1f) * (a[axis] - value), db = (keepGreater ? 1f : -1f) * (b[axis] - value);
            if (da >= 0f) tmp.Add(a);
            if ((da >= 0f) != (db >= 0f)) tmp.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        poly.Clear();
        poly.AddRange(tmp);
    }

    private Vector2 BoardUv(Vector2 p, Vector2 min, Vector2 max)
    {
        Rect r = boardSprite.textureRect;
        Texture t = boardSprite.texture;
        // Thu vào 3% mỗi phía: hàng/cột pixel viền của sprite sáng hơn.
        float u = Mathf.Lerp(0.03f, 0.97f, Mathf.InverseLerp(min.x, max.x, p.x));
        float v = Mathf.Lerp(0.03f, 0.97f, Mathf.InverseLerp(max.y, min.y, p.y));
        return new Vector2((r.x + u * r.width) / t.width, (r.y + v * r.height) / t.height);
    }

    /// <summary>Lấp đa giác (ear clipping). expand: đẩy mép ra theo pháp tuyến (luồn dưới mặt đường cho khỏi hở khe).</summary>
    private void FillPolygon(MeshData m, List<Pt> path, float expand, System.Func<Vector2, Vector2, Vector2, Vector2> uv)
    {
        var poly = new List<Vector2>(path.Count);
        foreach (Pt p in path) poly.Add(p.P + p.N * expand);
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (Vector2 p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        uv ??= BoardUv;

        List<int> tris = Triangulate(poly);
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector2 a = poly[tris[i]], b = poly[tris[i + 1]], c = poly[tris[i + 2]];
            m.Tri3(ToLocal(a), ToLocal(b), ToLocal(c), uv(a, min, max), uv(b, min, max), uv(c, min, max));
        }
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    private static List<int> Triangulate(List<Vector2> pts)
    {
        var res = new List<int>();
        var idx = new List<int>();
        // Bỏ điểm thẳng hàng (đoạn thẳng đã chia nhỏ) → ít đỉnh, ear clipping không kẹt.
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[(i - 1 + pts.Count) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
            if (Mathf.Abs(Cross(a, b, c)) > 1e-3f * (b - a).magnitude * (c - b).magnitude) idx.Add(i);
        }
        if (idx.Count < 3) return res;

        float area = 0f;
        for (int i = 0; i < idx.Count; i++)
        {
            Vector2 a = pts[idx[i]], b = pts[idx[(i + 1) % idx.Count]];
            area += a.x * b.y - b.x * a.y;
        }
        float sgn = area > 0f ? 1f : -1f;

        int guard = idx.Count * idx.Count + 10;
        while (idx.Count > 3 && guard-- > 0)
        {
            int c = idx.Count;
            bool clipped = false;
            for (int i = 0; i < c; i++)
            {
                int ia = idx[(i - 1 + c) % c], ib = idx[i], ic = idx[(i + 1) % c];
                Vector2 A = pts[ia], B = pts[ib], C = pts[ic];
                if (Cross(A, B, C) * sgn <= 1e-6f) continue; // đỉnh lõm
                bool inside = false;
                for (int j = 0; j < c; j++)
                {
                    int ip = idx[j];
                    if (ip == ia || ip == ib || ip == ic) continue;
                    Vector2 P = pts[ip];
                    if (Cross(A, B, P) * sgn >= 0f && Cross(B, C, P) * sgn >= 0f && Cross(C, A, P) * sgn >= 0f) { inside = true; break; }
                }
                if (inside) continue;
                res.Add(ia); res.Add(ib); res.Add(ic);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) idx.RemoveAt(0); // đa giác lỗi (tự cắt) — bỏ 1 đỉnh để khỏi kẹt
        }
        if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
        return res;
    }

    /// <summary>
    /// Quét 1 dải dọc theo path giữa 2 độ lệch o0, o1 (dọc pháp tuyến) với cột UV col0, col1.
    /// Texture tile theo chiều dài, tách quad ở mép tile để UV không tràn khỏi sprite trong atlas.
    /// </summary>
    private void Strip(MeshData m, List<Pt> path, bool closed, float o0, float o1, float col0, float col1)
    {
        int n = path.Count;
        int segs = closed ? n : n - 1;
        float s = 0f, oMid = (o0 + o1) * 0.5f;
        for (int i = 0; i < segs; i++)
        {
            Pt a = path[i], b = path[(i + 1) % n];
            Vector2 ma = a.P + a.N * oMid, mb = b.P + b.N * oMid;
            float len = Vector2.Distance(ma, mb);
            if (len < 0.001f) continue;

            float t0 = 0f, pos = Mathf.Repeat(s, RowTile);
            if (RowTile - pos < 0.01f) pos = 0f;
            while (t0 < 1f)
            {
                float t1 = Mathf.Min(1f, t0 + (RowTile - pos) / len);
                if (t1 <= t0) break;
                float r0 = RowStart + pos, r1 = r0 + (t1 - t0) * len;
                Pt p0 = Lerp(a, b, t0), p1 = Lerp(a, b, t1);
                m.StripQuad(ToLocal(p0.P + p0.N * o0), ToLocal(p0.P + p0.N * o1),
                    ToLocal(p1.P + p1.N * o1), ToLocal(p1.P + p1.N * o0),
                    Uv(col0, r0), Uv(col1, r0), Uv(col1, r1), Uv(col0, r1));
                pos = t1 >= 1f ? pos + (t1 - t0) * len : 0f;
                t0 = t1;
            }
            s += len;
        }
    }

    private static Pt Lerp(Pt a, Pt b, float t)
    {
        Vector2 nn = Vector2.Lerp(a.N, b.N, t);
        return new Pt(Vector2.Lerp(a.P, b.P, t), nn.sqrMagnitude > 1e-8f ? nn.normalized : a.N);
    }

    private void AddCaps(MeshData m, List<Pt> path, float o0, float o1, float colCenter, float colRing)
    {
        if (path.Count < 2) return;
        AddCap(m, path[0], (path[0].P - path[1].P).normalized, o0, o1, colCenter, colRing);
        AddCap(m, path[^1], (path[^1].P - path[^2].P).normalized, o0, o1, colCenter, colRing);
    }

    private void AddCap(MeshData m, Pt end, Vector2 dir, float o0, float o1, float colCenter, float colRing)
    {
        if (dir.sqrMagnitude < 1e-8f) return;
        Vector2 c = end.P + end.N * ((o0 + o1) * 0.5f);
        float radius = Mathf.Abs(o1 - o0) * 0.5f;
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector2 uc = Uv(colCenter, CapRow), ur = Uv(colRing, CapRow);
        const int steps = 12;
        for (int i = 0; i < steps; i++)
        {
            float a0 = Mathf.Lerp(-90f, 90f, i / (float)steps) * Mathf.Deg2Rad;
            float a1 = Mathf.Lerp(-90f, 90f, (i + 1) / (float)steps) * Mathf.Deg2Rad;
            Vector2 p0 = c + (dir * Mathf.Cos(a0) + side * Mathf.Sin(a0)) * radius;
            Vector2 p1 = c + (dir * Mathf.Cos(a1) + side * Mathf.Sin(a1)) * radius;
            m.Tri3(ToLocal(c), ToLocal(p0), ToLocal(p1), uc, ur, ur);
            m.EdgeA.Add(ToLocal(p0)); m.EdgeB.Add(ToLocal(p1)); m.EdgeIn.Add(ToLocal(c));
        }
    }

    /// <summary>Mặt viền (order 3) + thành/bóng quét xuống dưới màn hình từ các mép quay xuống (order 2).</summary>
    private void CreateRaised(Transform root, string name, MeshData top)
    {
        if (top.V.Count == 0) return;
        if (curbSideMaterial != null && curbSideLength > 0f)
        {
            var side = new MeshData();
            Vector3 off = new Vector3(0f, -curbSideLength * PxToUnit, 0f);
            for (int i = 0; i < top.EdgeA.Count; i++)
            {
                Vector3 a = top.EdgeA[i], b = top.EdgeB[i], e = b - a;
                if (e.sqrMagnitude < 1e-10f) continue;
                Vector3 n = new Vector3(e.y, -e.x, 0f).normalized;
                if (Vector3.Dot(n, (a + b) * 0.5f - top.EdgeIn[i]) < 0f) n = -n;
                if (n.y > -1e-4f) continue;
                side.Quad(a, b, b + off, a + off,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            }
            CreateMesh(root, name + "-Side", side, 2, curbSideMaterial);
        }
        CreateMesh(root, name, top, 3);
    }

    private void CreateMesh(Transform parent, string name, MeshData data, int order, Material material = null)
    {
        if (data.V.Count == 0) return;
        var mesh = new Mesh { name = name };
        mesh.indexFormat = data.V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(data.V);
        mesh.SetUVs(0, data.UV);
        var colors = new Color32[data.V.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
        mesh.colors32 = colors;
        mesh.SetTriangles(data.Tri, 0);
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material != null ? material : roadMaterial;
        mr.sortingOrder = order;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // ================= GIZMO =================

    private void OnDrawGizmos()
    {
        if (map == null) return;
        if (IsDirty || g == null || g.RowOf.Length != map.Height || g.Width != map.Width) g = null;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (drawGridGizmos)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.12f);
            for (int x = 0; x <= G.Width; x++)
                Gizmos.DrawLine(ToLocal(new Vector2(G.Xs[x], 0f)), ToLocal(new Vector2(G.Xs[x], GridH)));
            for (int y = 0; y <= G.Height; y++)
                Gizmos.DrawLine(ToLocal(new Vector2(0f, G.Ys[y])), ToLocal(new Vector2(GridW, G.Ys[y])));
        }

        if (!drawSlotGizmos) return;
        _columns = map.GetColumns();
        Vector3 size = new Vector3(slotSize.x * PxToUnit, slotSize.y * PxToUnit, 0f);
        foreach (RoadMapSO.ColumnRun col in _columns)
        for (int s = 0; s < col.count; s++)
        {
            Vector3 c = ToLocal(CellCenterPx(col.Cell(s)));
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.18f);
            Gizmos.DrawCube(c, size);
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 1f);
            Gizmos.DrawWireCube(c, size);
        }
    }
}
