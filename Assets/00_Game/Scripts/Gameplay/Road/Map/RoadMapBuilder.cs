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

    [BoxGroup("Vạch đứt"), LabelText("Sprite"), Tooltip("RoadDash_Round như RoadLoop/RoadNetwork. Trống = không vẽ vạch.")]
    [SerializeField] private Sprite dash;
    [BoxGroup("Vạch đứt"), LabelText("Material"), Tooltip("Sprite-Unlit-Default.")]
    [SerializeField] private Material dashMaterial;
    [BoxGroup("Vạch đứt"), LabelText("Scale")]
    [SerializeField, Min(0.01f)] private float dashScale = 0.45f;
    [BoxGroup("Vạch đứt"), LabelText("Khoảng cách (px)")]
    [SerializeField, Min(10f)] private float dashPeriod = 600f;
    [BoxGroup("Vạch đứt"), LabelText("Vạch đường trong"),
     Tooltip("Như RoadNetwork: đường giữa (nối 2 bên vòng ngoài) có vạch theo tâm, 2 đầu cua vào tâm làn vòng ngoài.")]
    [SerializeField] private bool innerDashes = true;
    [BoxGroup("Vạch đứt"), LabelText("Điểm tách khỏi làn (px)"), ShowIf(nameof(innerDashes)),
     Tooltip("Như turnDashBranch của RoadNetwork: bỏ đoạn đầu/cuối vạch đường trong còn trùng làn vòng ngoài (lệch < khoảng này).")]
    [SerializeField, Min(0f)] private float turnDashBranch = 40f;
    [BoxGroup("Vạch đứt"), LabelText("Chừa quanh mũi tên (px)")]
    [SerializeField, Min(0f)] private float dashClearance = 60f;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: sprite"), Tooltip("Vạch li ti dẫn vào barrier. Trống = sprite vạch thường.")]
    [SerializeField] private Sprite middleDash;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: scale")]
    [SerializeField, Min(0.01f)] private float middleDashScale = 0.22f;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: khoảng cách (px)")]
    [SerializeField, Min(10f)] private float middleDashPeriod = 130f;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: đầu trên cao hơn mép trên đường giữa (px)")]
    [SerializeField] private float barrierGuideTop = 70f;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: đầu dưới cao hơn tay chắn (px)")]
    [SerializeField] private float barrierGuideBottom = 200f;

    [BoxGroup("Mũi tên nhập làn"), LabelText("Sprite"), Tooltip("arrow_corner. Chỉ hiện khi map bật 'Barrier + mũi tên nhập làn'.")]
    [SerializeField] private Sprite mergeArrow;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Scale")]
    [SerializeField, Min(0.01f)] private float mergeArrowScale = 1.09f;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Cách mép ngoài phải (px)")]
    [SerializeField] private float mergeArrowInset = 238f;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Cao hơn tâm đường giữa (px)")]
    [SerializeField] private float mergeArrowOffsetY = 289f;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Màu")]
    [SerializeField] private Color mergeArrowColor = new(0.565f, 0.573f, 0.694f, 1f);

    [BoxGroup("Barrier"), LabelText("Barrier"), Tooltip("Object barrier trong scene (gốc = tâm cột, tay chắn sang phải). Builder chỉ dời vị trí + bật/tắt.")]
    [SerializeField] private Transform barrier;
    [BoxGroup("Barrier"), LabelText("Cách mép dưới đường giữa (px)")]
    [SerializeField] private float barrierGap = 120f;
    [BoxGroup("Barrier"), LabelText("Cột cách mép ngoài (px)")]
    [SerializeField] private float barrierInset = 50f;
    [BoxGroup("Barrier"), LabelText("Chừa quanh barrier (px, dọc làn)")]
    [SerializeField, Min(0f)] private float barrierDashClearance = 150f;

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

    private void OnValidate() => builtVersion = -1; // đổi field của builder (art, vạch…) → build lại

    public void SetDashArt(Sprite sprite, Material mat)
    {
        dash = sprite;
        dashMaterial = mat;
        builtVersion = -1;
    }

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
        int mouth = 0;
        for (int y = 0; y < h; y++)
        {
            if (y > 0 && MouthLine(y))
            {
                float mm = map.mouthMargins != null && mouth < map.mouthMargins.Count && map.mouthMargins[mouth] >= 0f ? map.mouthMargins[mouth] : m;
                mouth++;
                if (mm > 0.5f) { rows.Add(-y - 1); heights.Add(mm); }
            }
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
        for (int x = 0; x <= w; x++) grid.Xs[x] = x * map.cellWidth;
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
    private float Cw => map != null ? map.cellWidth : 520f;
    // Mép ngoài nở ra thêm = bề rộng đường − cỡ ô theo phương pháp tuyến (ngang: bề ngang ô, dọc: cỡ ô).
    private float DeltaX => map != null ? map.roadWidth - Cw : 0f;
    private float DeltaY => map != null ? map.roadWidth - Cs : 0f;
    private float DeltaAlong(Vector2 normal) => Mathf.Abs(normal.x) > 0.5f ? DeltaX : DeltaY;

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

    /// <summary>Nhóm đầu viền đảo — mỗi nhóm 1 tham số viền thò.</summary>
    public enum CurbGroup { MidBottom, MidTop, RingTop, RingBottom }

    private float CurbExt(CurbGroup g) => g switch
    {
        CurbGroup.MidTop => map.curbExtendMidTop,
        CurbGroup.RingTop => map.curbExtendRing,
        CurbGroup.RingBottom => map.curbExtendRingBottom,
        _ => map.curbExtend,
    };

    public static string CurbGroupName(CurbGroup g) => g switch
    {
        CurbGroup.MidTop => "giữa, đầu trên",
        CurbGroup.RingTop => "ngoài, đầu trên",
        CurbGroup.RingBottom => "ngoài, đầu dưới",
        _ => "giữa, đầu dưới",
    };

    public float GetCurbExt(CurbGroup g) => CurbExt(g);

    public struct CurbEnd
    {
        public int Loop;
        public bool Start;
        public float BaseS; // vị trí đầu/cuối viền khi viền thò = 0
        public CurbGroup Group;
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
        switch (c.Group)
        {
            case CurbGroup.MidTop: map.curbExtendMidTop = e; break;
            case CurbGroup.RingTop: map.curbExtendRing = e; break;
            case CurbGroup.RingBottom: map.curbExtendRingBottom = e; break;
            default: map.curbExtend = e; break;
        }
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
        for (int i = 0; i < n; i++)
            off[i] = loop.Outer ? DeltaAlong(NormalOf(loop.Runs[i].Dir)) + pad : loop.Runs[i].Ring ? -pad : 0f;
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
                    Center = CellCenterPx(new Vector2Int(x, y)) + nn * ((Mathf.Abs(nn.x) > 0.5f ? Cw : Cs) * 0.5f + DeltaAlong(nn) + map.islandPadding),
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
            else if (map.junctionHorns) LiftHorns(l);
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
                    // Mỗi đầu viền thò theo nhóm: (vòng ngoài / đường giữa) × (đầu trên / đầu dưới đảo).
                    (bool topRing, bool botRing) = IslandRingSides(li);
                    float midY = LoopMidY(l);
                    CurbGroup GroupAt(float s) => PointAt(l, s).P.y < midY
                        ? (topRing ? CurbGroup.RingTop : CurbGroup.MidTop)
                        : (botRing ? CurbGroup.RingBottom : CurbGroup.MidBottom);
                    float ExtAt(float s) => CurbExt(GroupAt(s));
                    iv = Extend(l, iv, ExtAt);
                    foreach (Vector2 r in iv)
                    {
                        if (r.y - r.x >= l.Total - 0.5f) continue;
                        CurbGroup ga = GroupAt(r.x), gb = GroupAt(r.y);
                        curbEnds.Add(new CurbEnd { Loop = li, Start = true, Group = ga, BaseS = r.x + CurbExt(ga), PosPx = CurbPos(l, r.x), DirPx = CurbDir(l, r.x) });
                        curbEnds.Add(new CurbEnd { Loop = li, Start = false, Group = gb, BaseS = r.y - CurbExt(gb), PosPx = CurbPos(l, r.y), DirPx = CurbDir(l, r.y) });
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
        BuildDashes(root);

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

    // ================= VẠCH ĐỨT =================

    // Làn = phần mặt đường lộ ra giữa viền ngoài và viền đảo (giống RoadNetworkBuilder), tính từ mép ngoài vào.
    private float LaneCenter => ((OuterColEdge - OuterColIn) + (map.roadWidth - (InnerColIn - InnerColEdge))) * 0.5f;

    /// <summary>
    /// Vạch đứt như RoadNetwork: (1) tâm làn vòng ngoài, rải đều cả vòng; (2) mỗi đường trong nối 2 bên vòng ngoài
    /// (dải ô đường không thuộc vòng ngoài, 2 đầu giáp vòng ngoài) → vạch theo tâm, 2 đầu cua Bezier vào tâm làn vòng ngoài.
    /// Đường ngang cua lên trên (như RoadNetwork: làn trái ↓ → đường giữa → làn phải ↑), đường dọc cua sang phải.
    /// </summary>
    private void BuildDashes(Transform root)
    {
        dashZones.Clear();
        List<InnerRun> runs = InnerRuns();
        int barrierRun = -1;
        if (map.middleBarrier)
            for (int i = 0; i < runs.Count; i++) if (runs[i].Horiz) { barrierRun = i; break; }
        bool barrierOn = barrierRun >= 0;

        // Đồ đặt trên mặt đường trước → đăng ký vùng cấm → vạch né (như RoadNetwork).
        if (barrierOn) PlaceMergeArrow(root, runs[barrierRun]);
        PlaceBarrier(barrierOn ? runs[barrierRun] : (InnerRun?)null);

        if (dash == null) return;
        Loop outer = null;
        foreach (Loop l in loops) if (l.Outer && l.Pts.Count >= 3) { outer = l; break; }
        if (outer == null) return;

        var parent = new GameObject("Dashes").transform;
        parent.SetParent(root, false);

        var lane = new List<Vector2>(outer.Pts.Count);
        foreach (Pt p in outer.Pts) lane.Add(p.P - p.N * LaneCenter);

        var paths = new List<List<Vector2>>();
        if (innerDashes) foreach (InnerRun r in runs) paths.Add(InnerPath(lane, r));

        if (barrierOn && paths[barrierRun] != null)
        {
            // Làn trái từ chỗ bắt đầu cua vào đường giữa tới barrier: vạch vòng ngoài nhường cho vạch đường giữa.
            InnerRun br = runs[barrierRun];
            float yStart = paths[barrierRun][0].y, yEnd = G.Ys[br.Line + 1] + barrierGap, w = map.roadWidth;
            AddDashZonePx(new Vector2(OuterLeftX + w * 0.5f, (yStart + yEnd) * 0.5f), new Vector2(w * 0.5f, Mathf.Abs(yEnd - yStart) * 0.5f), true);
        }

        DashAlong(parent, lane, true, dash, dashScale, dashPeriod, true);
        for (int i = 0; i < paths.Count; i++)
            if (paths[i] != null) DashAlong(parent, paths[i], false, dash, dashScale, dashPeriod, false, barrierOn && i == barrierRun);

        // Vạch li ti: cong từ mép ngoài làn trái xuống đầu tay chắn barrier.
        if (barrierOn && innerDashes)
            DashAlong(parent, BarrierGuidePath(runs[barrierRun]), false, middleDash != null ? middleDash : dash, middleDashScale, middleDashPeriod, false);
    }

    // ---------- Barrier / mũi tên nhập làn (như RoadNetwork) ----------

    /// <summary>Mũi tên nhập làn ở làn phải, cao hơn tâm đường giữa mergeArrowOffsetY, chỉ lên.</summary>
    private void PlaceMergeArrow(Transform root, InnerRun run)
    {
        if (mergeArrow == null) return;
        float yc = (G.Ys[run.Line] + G.Ys[run.Line + 1]) * 0.5f;
        var go = new GameObject("MergeArrow");
        go.transform.SetParent(root, false);
        go.transform.localPosition = ToLocal(new Vector2(OuterRightX - mergeArrowInset, yc - mergeArrowOffsetY));
        go.transform.localScale = Vector3.one * mergeArrowScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = mergeArrow;
        sr.color = mergeArrowColor;
        sr.sortingOrder = 7;
        if (dashMaterial != null) sr.sharedMaterial = dashMaterial;
        AddDashZone(go.transform, mergeArrow);
    }

    /// <summary>Barrier chặn làn trái ngay dưới đường giữa. Không có đường giữa / tắt → ẩn barrier.</summary>
    private void PlaceBarrier(InnerRun? run)
    {
        if (barrier == null) return;
        bool on = run.HasValue;
        if (barrier.gameObject.activeSelf != on) barrier.gameObject.SetActive(on);
        if (!on) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RecordObject(barrier, "Place Barrier");
#endif
        float y = G.Ys[run.Value.Line + 1] + barrierGap;
        Vector3 w = PxToWorld(new Vector2(OuterLeftX + barrierInset, y));
        barrier.position = new Vector3(w.x, barrier.position.y, w.z);
        // Tay chắn ngang hết làn trái → không vẽ vạch vòng ngoài ngay trên/dưới nó (vạch li ti vẫn được chạy tới sát).
        AddDashZonePx(new Vector2(OuterLeftX + map.roadWidth * 0.5f, y), new Vector2(map.roadWidth * 0.5f, barrierDashClearance), true);
    }

    /// <summary>Đường vạch li ti: từ sát mép ngoài làn trái (trên mép trên đường giữa) cong xuống đầu tay chắn (như RoadNetwork).</summary>
    private List<Vector2> BarrierGuidePath(InnerRun run)
    {
        float midTop = G.Ys[run.Line], midBot = G.Ys[run.Line + 1];
        Vector2 p0 = new(OuterLeftX + (OuterColEdge - OuterColIn) + 15f, midTop - barrierGuideTop);
        Vector2 p2 = new(G.Xs[run.A] - map.islandPadding - 10f, midBot + barrierGap - barrierGuideBottom);
        Vector2 c = new(Mathf.Lerp(p0.x, p2.x, 0.2f), Mathf.Lerp(p0.y, p2.y, 0.7f));
        var pts = new List<Vector2>();
        for (int k = 0; k <= 24; k++) { float t = k / 24f, u = 1f - t; pts.Add(u * u * p0 + 2f * u * t * c + t * t * p2); }
        return pts;
    }

    // ---------- Vùng cấm vạch đứt ----------

    private struct DashZone
    {
        public Vector2 C, Half;
        public float Cos, Sin;
        public bool LoopOnly; // chỉ chặn vạch vòng ngoài
        public bool Contains(Vector2 p)
        {
            Vector2 d = p - C;
            float x = d.x * Cos + d.y * Sin, y = -d.x * Sin + d.y * Cos;
            return Mathf.Abs(x) <= Half.x && Mathf.Abs(y) <= Half.y;
        }
    }

    private readonly List<DashZone> dashZones = new();

    private void AddDashZone(Transform t, Sprite s)
    {
        Bounds b = s.bounds;
        Vector3 sc = t.localScale;
        Vector3 c = t.localPosition + t.localRotation * Vector3.Scale(b.center, sc);
        float a = t.localEulerAngles.z * Mathf.Deg2Rad, pad = dashClearance * PxToUnit;
        dashZones.Add(new DashZone
        {
            C = c, Cos = Mathf.Cos(a), Sin = Mathf.Sin(a),
            Half = new Vector2(Mathf.Abs(b.extents.x * sc.x) + pad, Mathf.Abs(b.extents.y * sc.y) + pad),
        });
    }

    private void AddDashZonePx(Vector2 centerPx, Vector2 halfPx, bool loopOnly = false) =>
        dashZones.Add(new DashZone { C = ToLocal(centerPx), Cos = 1f, Sin = 0f, Half = halfPx * PxToUnit, LoopOnly = loopOnly });

    private bool InDashZone(Vector2 px, bool loop)
    {
        Vector2 p = ToLocal(px);
        foreach (DashZone z in dashZones) if ((loop || !z.LoopOnly) && z.Contains(p)) return true;
        return false;
    }

    /// <summary>
    /// Rải vạch dọc path, né vùng cấm: cắt thành các đoạn liền ngoài vùng cấm, mỗi đoạn chia đều (như DashAlong của RoadNetwork).
    /// </summary>
    private void DashAlong(Transform parent, List<Vector2> pts, bool closed, Sprite sprite, float scale, float period, bool loopPath,
        bool stopAtFirstBlock = false)
    {
        List<(Vector2 p, Vector2 d)> sp = Resample(pts, closed);
        int count = sp.Count;
        if (count < 2) return;
        var inside = new bool[count];
        int firstIn = -1;
        for (int i = 0; i < count; i++) { inside[i] = InDashZone(sp[i].p, loopPath); if (inside[i] && firstIn < 0) firstIn = i; }

        if (closed && firstIn < 0) { DashRun(parent, sp, 0, count, true, sprite, scale, period); return; }

        int start = closed ? firstIn : 0, runStart = -1;
        for (int j = 0; j <= count; j++)
        {
            bool blocked = j == count || inside[(start + j) % count];
            if (!blocked && runStart < 0) runStart = j;
            if (blocked && runStart >= 0)
            {
                DashRun(parent, sp, start + runStart, j - runStart, false, sprite, scale, period);
                runStart = -1;
                if (stopAtFirstBlock) return; // vạch đường giữa dừng ở mũi tên nhập làn
            }
        }
    }

    /// <summary>Đường trong: dải ô đường thẳng (ngang/dọc), không thuộc vòng ngoài, 2 đầu giáp ô đường vòng ngoài.</summary>
    /// <summary>Đường trong (lưới mở rộng): line = hàng (ngang) / cột (dọc), ô a..b.</summary>
    private struct InnerRun { public bool Horiz; public int Line, A, B; }

    /// <summary>Dải ô đường thẳng không thuộc vòng ngoài, 2 đầu giáp ô đường vòng ngoài. Ngang trước (trên → dưới), rồi dọc.</summary>
    private List<InnerRun> InnerRuns()
    {
        var res = new List<InnerRun>();
        bool Inner(int x, int y) => G.IsRoad(x, y) && !IsRingRoad(x, y);
        for (int axis = 0; axis < 2; axis++)
        {
            bool horiz = axis == 0;
            int lines = horiz ? G.Height : G.Width, len = horiz ? G.Width : G.Height;
            for (int line = 0; line < lines; line++)
            {
                int k = 0;
                while (k < len)
                {
                    Vector2Int C(int t) => horiz ? new Vector2Int(t, line) : new Vector2Int(line, t);
                    if (!Inner(C(k).x, C(k).y)) { k++; continue; }
                    int a = k;
                    while (k < len && Inner(C(k).x, C(k).y)) k++;
                    int b = k - 1;
                    Vector2Int before = C(a - 1), after = C(b + 1);
                    if (!IsRingRoad(before.x, before.y) || !IsRingRoad(after.x, after.y)) continue;
                    res.Add(new InnerRun { Horiz = horiz, Line = line, A = a, B = b });
                }
            }
        }
        return res;
    }

    private List<Vector2> InnerPath(List<Vector2> lane, InnerRun r) =>
        r.Horiz ? HorizontalInnerPath(lane, r.Line, r.A, r.B) : VerticalInnerPath(lane, r.Line, r.A, r.B);

    // Mép ngoài vòng (px) trái / phải.
    private float OuterLeftX => G.Xs[0] - DeltaX - map.islandPadding;
    private float OuterRightX => G.Xs[G.Width] + DeltaX + map.islandPadding;

    /// <summary>
    /// Sừng đảo dưới đường giữa (như RoadNetwork): ở 2 đầu mỗi đường ngang trong, mép trên đảo bên dưới hất lên theo cung
    /// đồng tâm với góc bo đảo bên trên (tâm (mép đảo ± r, mép trên đường − r), bán kính r + bề rộng đường) → đầu sừng nhọn.
    /// </summary>
    private void LiftHorns(Loop l)
    {
        bool changed = false;
        foreach (InnerRun run in InnerRuns())
        {
            if (!run.Horiz) continue;
            float r = map.junctionRadius, pad = map.islandPadding;
            float yT = G.Ys[run.Line], yB = G.Ys[run.Line + 1], R = r + (yB - yT), cy = yT - r;
            for (int side = 0; side < 2; side++)
            {
                float xE = side == 0 ? G.Xs[run.A] - pad : G.Xs[run.B + 1] + pad;
                float cx = side == 0 ? xE + r : xE - r;
                for (int i = 0; i < l.Pts.Count; i++)
                {
                    Vector2 p = l.Pts[i].P;
                    if (p.y < yB - 0.5f || p.y > yB + r + 0.5f) continue;
                    bool inX = side == 0 ? p.x >= xE - 0.5f && p.x <= cx : p.x <= xE + 0.5f && p.x >= cx;
                    if (!inX) continue;
                    float dx = Mathf.Clamp(p.x - cx, -R, R);
                    float y = cy + Mathf.Sqrt(R * R - dx * dx);
                    if (y >= p.y - 0.01f) continue;
                    l.Pts[i] = new Pt(new Vector2(p.x, y), l.Pts[i].N);
                    changed = true;
                }
            }
        }
        if (!changed) return;

        // Bỏ điểm trùng (nhiều điểm dồn về đầu sừng), tính lại pháp tuyến theo hướng cũ.
        var pts = new List<Pt>();
        var curb = new List<bool>();
        for (int i = 0; i < l.Pts.Count; i++)
        {
            if (pts.Count > 0 && Vector2.Distance(pts[^1].P, l.Pts[i].P) < 0.5f) { curb[^1] = curb[^1] || l.Curb[i]; continue; }
            pts.Add(l.Pts[i]);
            curb.Add(l.Curb[i]);
        }
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 t = pts[(i + 1) % n].P - pts[(i - 1 + n) % n].P;
            if (t.sqrMagnitude < 1e-6f) continue;
            Vector2 nn = new Vector2(t.y, -t.x).normalized;
            if (Vector2.Dot(nn, pts[i].N) < 0f) nn = -nn;
            pts[i] = new Pt(pts[i].P, nn);
        }
        l.Pts = pts;
        l.Curb = curb;
    }

    private List<Vector2> HorizontalInnerPath(List<Vector2> lane, int row, int a, int b, bool trim = true)
    {
        float yc = (G.Ys[row] + G.Ys[row + 1]) * 0.5f;
        float xa = G.Xs[a], xb = G.Xs[b + 1];
        if (!LaneCross(lane, true, yc, xa, -1, out float xl) || !LaneCross(lane, true, yc, xb, 1, out float xr)) return null;
        // Như RoadNetwork.MiddleLanePath: ngang = (tâm làn → mép đảo) + bo ngã ba; dọc thêm nửa bề rộng đường trong.
        float r = map.junctionRadius, pad = map.islandPadding, half = (G.Ys[row + 1] - G.Ys[row]) * 0.5f;
        float kl = Mathf.Min(xa - pad - xl + r, (xb - xa) * 0.5f + (xa - xl));
        float kr = Mathf.Min(xr - (xb + pad) + r, (xb - xa) * 0.5f + (xr - xb));
        var pts = new List<Vector2>();
        Quad(pts, new Vector2(xl, yc - half - kl), new Vector2(xl, yc), new Vector2(xl + kl, yc));
        Quad(pts, new Vector2(xr - kr, yc), new Vector2(xr, yc), new Vector2(xr, yc - half - kr));
        return trim ? TrimBranch(pts) : pts;
    }

    private List<Vector2> VerticalInnerPath(List<Vector2> lane, int col, int a, int b, bool trim = true)
    {
        float xc = (G.Xs[col] + G.Xs[col + 1]) * 0.5f;
        float ya = G.Ys[a], yb = G.Ys[b + 1];
        if (!LaneCross(lane, false, xc, ya, -1, out float yt) || !LaneCross(lane, false, xc, yb, 1, out float ybo)) return null;
        float r = map.junctionRadius, pad = map.islandPadding, half = (G.Xs[col + 1] - G.Xs[col]) * 0.5f;
        float kt = Mathf.Min(ya - pad - yt + r, (yb - ya) * 0.5f + (ya - yt));
        float kb = Mathf.Min(ybo - (yb + pad) + r, (yb - ya) * 0.5f + (ybo - yb));
        var pts = new List<Vector2>();
        Quad(pts, new Vector2(xc + half + kt, yt), new Vector2(xc, yt), new Vector2(xc, yt + kt));
        Quad(pts, new Vector2(xc, ybo - kb), new Vector2(xc, ybo), new Vector2(xc + half + kb, ybo));
        return trim ? TrimBranch(pts) : pts;
    }

    // ================= GAMEPLAY API =================

    /// <summary>Làn xe (world). Points theo chiều đường vẽ (vòng ngoài: chiều biên; đường trong: trái→phải / trên→dưới).</summary>
    public struct LaneInfo
    {
        public List<Vector3> Points;
        public bool Closed; // vòng ngoài
        public bool Forced; // đường giữa có barrier chặn làn trái → xe vòng ngoài bắt buộc rẽ vào
    }

    /// <summary>Dựng lại nếu dữ liệu hình học chưa có (vd vào Play mode).</summary>
    public void EnsureBuilt()
    {
        if (map == null) return;
        if (IsDirty || loops == null || loops.Count == 0) Build();
    }

    /// <summary>Vòng ngoài (phần tử 0) + các đường trong. stepPx = khoảng cách điểm (px).</summary>
    public List<LaneInfo> GetLanesWorld(float stepPx = 50f)
    {
        EnsureBuilt();
        var res = new List<LaneInfo>();
        Loop outer = null;
        foreach (Loop l in loops) if (l.Outer && l.Pts.Count >= 3) { outer = l; break; }
        if (outer == null) return res;

        var lane = new List<Vector2>(outer.Pts.Count);
        foreach (Pt p in outer.Pts) lane.Add(p.P - p.N * LaneCenter);
        res.Add(new LaneInfo { Points = ToWorldPath(lane, true, stepPx), Closed = true });

        List<InnerRun> runs = InnerRuns();
        int barrierRun = -1;
        if (map.middleBarrier)
            for (int i = 0; i < runs.Count; i++) if (runs[i].Horiz) { barrierRun = i; break; }
        for (int i = 0; i < runs.Count; i++)
        {
            InnerRun r = runs[i];
            List<Vector2> p = r.Horiz ? HorizontalInnerPath(lane, r.Line, r.A, r.B, false) : VerticalInnerPath(lane, r.Line, r.A, r.B, false);
            if (p == null || p.Count < 2) continue;
            res.Add(new LaneInfo { Points = ToWorldPath(p, false, stepPx), Forced = i == barrierRun });
        }
        return res;
    }

    private List<Vector3> ToWorldPath(List<Vector2> px, bool closed, float stepPx)
    {
        var res = new List<Vector3>();
        int segs = closed ? px.Count : px.Count - 1;
        for (int i = 0; i < segs; i++)
        {
            Vector2 a = px[i], b = px[(i + 1) % px.Count];
            float len = Vector2.Distance(a, b);
            if (len < 0.001f) continue;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / stepPx));
            for (int k = 0; k < n; k++) Add(Vector2.Lerp(a, b, k / (float)n));
        }
        if (!closed) Add(px[^1]);
        return res;

        void Add(Vector2 p)
        {
            Vector3 w = PxToWorld(p);
            if (res.Count > 0 && (res[^1] - w).sqrMagnitude < 1e-6f) return;
            res.Add(w);
        }
    }

    public ColumnSide ColumnEntry(int col) => Columns[col].entry;
    public ColumnSide ColumnExit(int col) => Columns[col].exit;

    /// <summary>Cột chứa ô map (x, y), −1 nếu không có.</summary>
    public int FindColumn(int x, int y)
    {
        for (int i = 0; i < Columns.Count; i++)
        {
            RoadMapSO.ColumnRun c = Columns[i];
            if (c.x == x && y >= c.TopY && y <= c.bottomY) return i;
        }
        return -1;
    }

    /// <summary>Lối ra đầu tiên (world): tâm trên mép ngoài + nửa bề rộng (world).</summary>
    public bool GetExitOpeningWorld(out Vector3 center, out float halfWidth)
    {
        EnsureBuilt();
        center = default;
        halfWidth = 0f;
        if (exitEnds.Count == 0) return false;
        center = PxToWorld(exitEnds[0].CenterPx);
        halfWidth = map.exitWidth * 0.5f * PxToUnit * transform.lossyScale.x;
        return true;
    }

    private static void Quad(List<Vector2> pts, Vector2 p0, Vector2 c, Vector2 p1)
    {
        for (int k = 0; k <= 16; k++) { float t = k / 16f, u = 1f - t; pts.Add(u * u * p0 + 2f * u * t * c + t * t * p1); }
    }

    /// <summary>
    /// Giao của làn vòng ngoài với đường thẳng (ngang y = v nếu horiz, dọc x = v), lấy giao gần from nhất về phía dir (−1/+1).
    /// </summary>
    private static bool LaneCross(List<Vector2> lane, bool horiz, float v, float from, int dir, out float at)
    {
        at = 0f;
        float best = float.MaxValue;
        int n = lane.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = lane[i], q = lane[(i + 1) % n];
            float pv = horiz ? p.y : p.x, qv = horiz ? q.y : q.x;
            if ((pv - v) * (qv - v) > 0f || Mathf.Approximately(pv, qv)) continue;
            float t = (v - pv) / (qv - pv);
            float c = horiz ? Mathf.Lerp(p.x, q.x, t) : Mathf.Lerp(p.y, q.y, t);
            float d = (c - from) * dir;
            if (d >= 0f && d < best) { best = d; at = c; }
        }
        return best < float.MaxValue;
    }

    /// <summary>Bỏ đoạn đầu/cuối còn chạy thẳng trùng làn vòng ngoài (lệch &lt; turnDashBranch) — như TurnDashPath của RoadNetwork.</summary>
    private List<Vector2> TrimBranch(List<Vector2> p)
    {
        if (p.Count < 3) return p;
        Vector2 s = p[0], e = p[^1];
        // 2 đầu luôn bám làn thẳng (dọc/ngang) → đo lệch vuông góc với trục đó (như RoadNetwork đo |x − x làn|).
        static Vector2 Axis(Vector2 v) => Mathf.Abs(v.x) > Mathf.Abs(v.y) ? Vector2.right : Vector2.up;
        Vector2 ds = Axis(p[1] - p[0]), de = Axis(p[^1] - p[^2]);
        float Off(Vector2 q, Vector2 o, Vector2 d) { Vector2 v = q - o; return Mathf.Abs(v.x * d.y - v.y * d.x); }
        int i0 = 0, i1 = p.Count - 1;
        while (i0 + 1 < i1 && Off(p[i0 + 1], s, ds) < turnDashBranch) i0++;
        while (i1 - 1 > i0 && Off(p[i1 - 1], e, de) < turnDashBranch) i1--;
        return p.GetRange(i0, i1 - i0 + 1);
    }

    /// <summary>Lấy mẫu đều 10px theo quãng đường (kèm hướng).</summary>
    private static List<(Vector2 p, Vector2 d)> Resample(List<Vector2> pts, bool closed)
    {
        const float step = 10f;
        var res = new List<(Vector2, Vector2)>();
        int segs = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < segs; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
            float len = Vector2.Distance(a, b);
            if (len < 0.001f) continue;
            Vector2 dir = (b - a) / len;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (int k = 0; k < n; k++) res.Add((a + dir * (len * k / n), dir));
        }
        if (!closed && pts.Count > 0) res.Add((pts[^1], res.Count > 0 ? res[^1].Item2 : Vector2.right));
        return res;
    }

    /// <summary>Rải vạch đều: n = round(dài / dashPeriod), vạch thứ d ở (d + 0.5)·khoảng (như DashRun của RoadNetwork).</summary>
    private void DashRun(Transform parent, List<(Vector2 p, Vector2 d)> sp, int first, int n, bool loop,
        Sprite sprite, float scale, float period)
    {
        if (n < 2) return;
        int count = sp.Count;
        (Vector2 p, Vector2 d) S(int k) => sp[(first + k) % count];
        var cum = new float[n];
        for (int k = 1; k < n; k++) cum[k] = cum[k - 1] + Vector2.Distance(S(k - 1).p, S(k).p);
        float total = loop ? cum[n - 1] + Vector2.Distance(S(n - 1).p, S(0).p) : cum[n - 1];
        int dashes = Mathf.RoundToInt(total / period);
        if (dashes <= 0) return;
        float spacing = total / dashes;
        float spriteAngle = sprite.rect.height > sprite.rect.width ? -90f : 0f;
        int k0 = 0;
        for (int d = 0; d < dashes; d++)
        {
            float target = (d + 0.5f) * spacing;
            while (k0 + 1 < n && cum[k0 + 1] < target) k0++;
            Vector2 a = S(k0).p;
            Vector2 b = k0 + 1 < n ? S(k0 + 1).p : (loop ? S(0).p : a);
            float segLen = (k0 + 1 < n ? cum[k0 + 1] : total) - cum[k0];
            Vector2 p = segLen > 1e-4f ? Vector2.Lerp(a, b, (target - cum[k0]) / segLen) : a;
            Vector2 dir = S(k0).d;

            var go = new GameObject("Dash");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ToLocal(p);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg + spriteAngle);
            go.transform.localScale = Vector3.one * (sprite.pixelsPerUnit * PxToUnit * scale);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 5;
            if (dashMaterial != null) sr.sharedMaterial = dashMaterial;
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
                roadWidthHandlePx = new Vector2(bestX + DeltaX + map.islandPadding, (G.Ys[r.Start.y] + G.Ys[r.Start.y + r.Len]) * 0.5f);
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
