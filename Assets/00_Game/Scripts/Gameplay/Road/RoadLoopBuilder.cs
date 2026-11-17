using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Sinh vòng đường chữ nhật bo góc bằng mesh. Mặt cắt (bóng / viền / asphalt) lấy từ sprite profile
/// (Road_Vertical) nên giữ nguyên art, còn bán kính góc + kích thước chỉnh tự do (có handle trong Scene).
/// Đơn vị "px" = pixel của profile (1 px = 0.01 unit local). Dựng trong mặt phẳng XY local.
/// </summary>
public class RoadLoopBuilder : MonoBehaviour
{
    private const float PxToUnit = 0.01f;
    private const string GeneratedName = "Generated";

    // Mặt cắt Road_Vertical (cột pixel, tính từ trái): viền trong 34–79, asphalt 91–402, viền ngoài 411–455.
    private const float DefaultRoadWidth = 421f;   // bề rộng gốc của profile: mép ngoài viền ngoài → mép trong viền trong
    private const float MinRoadWidth = 250f;
    private const float OuterColEdge = 455f;       // cột mép ngoài viền ngoài (d = 0)
    private const float OuterColIn = 403f;         // bóng phía trong viền ngoài
    private const float OuterColOut = 467f;        // bóng phía ngoài viền ngoài
    private const float OuterColCenter = 435f;
    private const float InnerColEdge = 34f;        // mép trong viền trong (d = RoadWidth)
    private const float InnerColOut = 23f;         // bóng phía bàn chơi
    private const float InnerColIn = 90f;
    private const float InnerColCenter = 56.5f;
    // Cột UV cho MẶT TRÊN viền: bỏ phần rìa tối/bóng ở mép profile (bóng giờ vẽ riêng, lệch xuống dưới).
    private const float OuterUvOut = 457f;
    private const float InnerUvOut = 32f;
    // Phía mặt đường: dừng trước vệt tối (AO) của asphalt trong profile, giữ phần vát của viền.
    private const float OuterUvIn = 409f;
    private const float InnerUvIn = 80f;
    private const float AsphaltColA = 130f;
    private const float AsphaltColB = 370f;
    private const float RowStart = 10f;            // bỏ vài hàng đầu/cuối (border của sprite)
    private const float RowTile = 440f;
    private const float CapRow = 230f;
    private const float MinRadius = 60f;

    [BoxGroup("Kích thước (px)"), LabelText("Nửa ngang (mép ngoài)")]
    [SerializeField, Min(500f), OnValueChanged(nameof(Build))] private float outerHalfWidth = 1700f;
    [BoxGroup("Kích thước (px)"), LabelText("Cao (mép ngoài)")]
    [SerializeField, Min(1000f), OnValueChanged(nameof(Build))] private float outerHeight = 3500f;
    [BoxGroup("Kích thước (px)"), LabelText("Bán kính góc")]
    [Tooltip("Nhỏ = vuông, lớn = cong. Kéo handle ở 4 góc trong Scene.")]
    [SerializeField, OnValueChanged(nameof(Build))] private float cornerRadius = 700f;
    [BoxGroup("Kích thước (px)"), LabelText("Lối vào bottom")]
    [Tooltip("Khoảng hở viền ngoài cạnh dưới. Kéo 2 handle ở đầu bo trong Scene.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float bottomOpeningWidth = 920f;

    [BoxGroup("Kích thước (px)"), LabelText("Viền trong: điểm đầu")]
    [Tooltip("Quãng đường (px) dọc theo vòng, tính từ giữa cạnh trên theo chiều kim đồng hồ, tới đầu bo thứ nhất của viền trắng bên trong. Kéo handle trong Scene.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float innerCurbStart = 1508f;
    [BoxGroup("Kích thước (px)"), LabelText("Viền trong: điểm cuối")]
    [Tooltip("Quãng đường (px) dọc theo vòng tới đầu bo thứ hai. Bên trái đối xứng.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float innerCurbEnd = 3540f;

    [BoxGroup("Kích thước (px)"), LabelText("Bề rộng đường")]
    [Tooltip("Mép ngoài viền ngoài → mép trong viền trong (gốc 421). Kéo 2 chấm vàng ở cạnh phải trong Scene: chấm ngoài nới ra ngoài (bàn giữ nguyên), chấm trong nới vào trong.")]
    [SerializeField, OnValueChanged(nameof(Build))] private float roadWidth = DefaultRoadWidth;

    [FoldoutGroup("Art")][SerializeField, Required] private Sprite profile;          // Road_Vertical
    [FoldoutGroup("Art")][SerializeField, Required] private Material roadMaterial;   // sprite shader, _MainTex = atlas của profile
    [FoldoutGroup("Art")][SerializeField] private Sprite dash;                       // dashed_line_0
    [FoldoutGroup("Art")][SerializeField] private Material dashMaterial;

    [BoxGroup("Vạch đứt"), LabelText("Scale vạch")]
    [SerializeField, OnValueChanged(nameof(Build))] private float dashScale = 3f;
    [BoxGroup("Vạch đứt"), LabelText("Khoảng cách vạch"), Tooltip("Càng lớn càng ít vạch.")]
    [SerializeField, Min(10f), OnValueChanged(nameof(Build))] private float dashPeriod = 300f;


    // Viền nổi: thành bên + bóng lấy nguyên pixel từ sprite gốc (Road_Under_Corner), quét xuống dưới màn hình
    // theo mọi mép viền quay xuống. Texture: hàng trên cùng = sát mép viền, hàng dưới = cuối bóng.
    [FoldoutGroup("Art"), LabelText("Material thành + bóng viền")]
    [SerializeField, OnValueChanged(nameof(Build))] private Material curbSideMaterial;   // RoadCurbSide.mat
    [FoldoutGroup("Art"), LabelText("Dài thành + bóng (px)"), Tooltip("= số hàng pixel của texture (72) để giữ đúng tỉ lệ art gốc.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float curbSideLength = 72f;

    // ---------- Board (phần bên trong) ----------
    [FoldoutGroup("Board"), LabelText("Số cột")]
    [SerializeField, Min(1), OnValueChanged(nameof(BuildBoard))] private int columnCount = 3;
    [FoldoutGroup("Board"), LabelText("Rộng cột (px)")]
    [SerializeField, Min(10f), OnValueChanged(nameof(BuildBoard))] private float columnWidth = 380f;
    [FoldoutGroup("Board"), LabelText("Space X (px)"), Tooltip("Khoảng cách giữa 2 cột liền nhau. Vạch chia nằm giữa khoảng này.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(BuildBoard))] private float columnSpacingX = 120f;
    [FoldoutGroup("Board"), LabelText("Cột ngắn hơn chấm xanh (px)")]
    [Tooltip("Đầu/cuối cột bám theo độ cao 2 điểm đầu/cuối viền trong (chấm xanh lá), rồi ngắn vào thêm khoảng này.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(BuildBoard))] private float columnPaddingY = 40f;
    [FoldoutGroup("Board"), LabelText("Dày vạch chia (px)")]
    [SerializeField, Min(0f), OnValueChanged(nameof(BuildBoard))] private float dividerWidth = 22f;
    [FoldoutGroup("Board")][SerializeField] private Sprite boardSprite;      // asphalt-texture_0
    [FoldoutGroup("Board")][SerializeField] private Material boardMaterial;  // sprite shader, _MainTex = atlas của boardSprite

    [FoldoutGroup("Board"), LabelText("Mũi tên cột"), Tooltip("Sprite tam giác chỉ xuống. Mỗi cột có 1 ở đầu + 1 ở cuối.")]
    [SerializeField, OnValueChanged(nameof(BuildBoard))] private Sprite columnArrow;
    [FoldoutGroup("Board"), LabelText("Material mũi tên"), Tooltip("Để trống = dùng dashMaterial.")]
    [SerializeField, OnValueChanged(nameof(BuildBoard))] private Material columnArrowMaterial;
    [FoldoutGroup("Board"), LabelText("Scale mũi tên")]
    [SerializeField, Min(0.01f), OnValueChanged(nameof(BuildBoard))] private float columnArrowScale = 3f;
    [FoldoutGroup("Board"), LabelText("Mũi tên cách đầu/cuối cột (px)"), Tooltip("Âm = đẩy ra ngoài cột (vào phần lề).")]
    [SerializeField, OnValueChanged(nameof(BuildBoard))] private float columnArrowInset = 50f;

    private const string BoardName = "Board";
    private const float DividerCol = 56.5f; // tâm viền trong của profile → màu viền phẳng

    [SerializeField, HideInInspector] private List<Vector2> _lane = new(); // lưu vào scene để dùng lúc Play

    /// <summary>Tâm làn (local, unit) theo chiều kim đồng hồ — dùng làm đường chạy cho xe.</summary>
    public IReadOnlyList<Vector2> Lane => _lane;

    /// <summary>
    /// Đường chạy xe (world), theo chiều KIM ĐỒNG HỒ trên màn hình (trên → phải ↓ → dưới ← → trái ↑).
    /// Nếu chưa có (chưa Build) thì tự tính lại, không dựng mesh.
    /// </summary>
    public List<Vector3> GetLaneWorld()
    {
        if (_lane.Count < 2) BuildLane(BuildStations());
        var list = new List<Vector3>(_lane.Count);
        foreach (Vector2 p in _lane) list.Add(transform.TransformPoint(p));
        return list;
    }

    public float CornerRadius => Radius;
    public float BottomOpeningWidth => 2f * OpeningHalf;

    private float A => outerHalfWidth;
    private float H => outerHeight;
    private float Radius => Mathf.Clamp(cornerRadius, MinRadius, Mathf.Min(A, H * 0.5f));
    private float CapRadius => (OuterColOut - OuterColIn) * 0.5f;
    private float OpeningHalf =>
        Mathf.Clamp(bottomOpeningWidth * 0.5f, CapRadius, A - Mathf.Max(Radius, RoadWidth) - CapRadius);

    // ================= BUILD =================

    [Button("Build", ButtonSizes.Large), GUIColor(0.55f, 0.85f, 0.55f), PropertyOrder(-1)]
    [ContextMenu("Build")]
    public void Build()
    {
        if (profile == null || roadMaterial == null)
        {
            Debug.LogError("[RoadLoopBuilder] Thiếu profile sprite hoặc roadMaterial.", this);
            return;
        }

        Transform root = ResetGenerated();
        List<Station> stations = BuildStations();

        var asphalt = new MeshData();
        AddStrip(asphalt, stations, 0f, RoadWidth, AsphaltColB, AsphaltColA, _ => true);
        CreateMesh(root, "Asphalt", asphalt, 0);

        var outer = new MeshData();
        float dIn = OuterColEdge - OuterColIn, dOut = OuterColEdge - OuterColOut;
        AddStrip(outer, stations, dOut, dIn, OuterUvOut, OuterUvIn, seg => seg != SegBottomMid);
        AddOpeningCaps(outer);

        var inner = new MeshData();
        AddInnerCurbs(inner);

        // Thứ tự: asphalt 0 → bóng 1 → thành 2 → mặt viền 3.
        CreateRaised(root, "Curb-Outer", outer);
        CreateRaised(root, "Curb-Inner", inner);

        BuildLane(stations);
        PlaceDashes(root);

        if (transform.Find(BoardName) != null) BuildBoard(); // bàn đã có → cập nhật theo kích thước đường mới
    }

    [Button("Clear"), PropertyOrder(-1)]
    private void Clear()
    {
        DestroyGenerated();
        _lane.Clear();
    }

    private Transform ResetGenerated()
    {
        DestroyGenerated();
        var go = new GameObject(GeneratedName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private void DestroyGenerated()
    {
        Transform old = transform.Find(GeneratedName);
        if (old == null) return;

        foreach (MeshFilter mf in old.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) SafeDestroy(mf.sharedMesh);

        SafeDestroy(old.gameObject);
    }

    private static void SafeDestroy(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ================= ĐƯỜNG BAO (rounded rect tại độ lùn d) =================

    private const int SegTopRight = 0, SegArcTR = 1, SegRight = 2, SegArcBR = 3,
        SegBottomRight = 4, SegBottomMid = 5, SegBottomLeft = 6, SegArcBL = 7,
        SegLeft = 8, SegArcTL = 9, SegTopLeft = 10, SegCount = 11;

    private struct Station
    {
        public int Seg;
        public float T;
        public Station(int seg, float t) { Seg = seg; T = t; }
    }

    private static bool IsArc(int seg) => seg == SegArcTR || seg == SegArcBR || seg == SegArcBL || seg == SegArcTL;

    /// <summary>Điểm (px, y xuống) + pháp tuyến hướng ra ngoài, tại độ lùn d vào trong tính từ mép ngoài.</summary>
    private void Sample(float d, int seg, float t, out Vector2 p, out Vector2 n)
    {
        float hw = A - d, top = d, bot = H - d;
        float r = Mathf.Max(Radius - d, 0f);
        float cx = hw - r, cyT = top + r, cyB = bot - r;
        float xo = OpeningHalf;

        switch (seg)
        {
            case SegTopRight: p = new Vector2(Mathf.Lerp(0f, cx, t), top); n = Vector2.down; return;
            case SegRight: p = new Vector2(hw, Mathf.Lerp(cyT, cyB, t)); n = Vector2.right; return;
            case SegBottomRight: p = new Vector2(Mathf.Lerp(cx, xo, t), bot); n = Vector2.up; return;
            case SegBottomMid: p = new Vector2(Mathf.Lerp(xo, -xo, t), bot); n = Vector2.up; return;
            case SegBottomLeft: p = new Vector2(Mathf.Lerp(-xo, -cx, t), bot); n = Vector2.up; return;
            case SegLeft: p = new Vector2(-hw, Mathf.Lerp(cyB, cyT, t)); n = Vector2.left; return;
            case SegTopLeft: p = new Vector2(Mathf.Lerp(-cx, 0f, t), top); n = Vector2.down; return;
        }

        Vector2 c; float a0;
        switch (seg)
        {
            case SegArcTR: c = new Vector2(cx, cyT); a0 = -90f; break;
            case SegArcBR: c = new Vector2(cx, cyB); a0 = 0f; break;
            case SegArcBL: c = new Vector2(-cx, cyB); a0 = 90f; break;
            default: c = new Vector2(-cx, cyT); a0 = 180f; break;
        }

        float a = (a0 + 90f * t) * Mathf.Deg2Rad;
        n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        p = c + n * r;
    }

    private List<Station> BuildStations()
    {
        var list = new List<Station>();
        for (int seg = 0; seg < SegCount; seg++)
        {
            int steps;
            if (IsArc(seg)) steps = 32;
            else
            {
                Sample(0f, seg, 0f, out Vector2 a, out _);
                Sample(0f, seg, 1f, out Vector2 b, out _);
                steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 60f));
            }

            for (int i = 0; i < steps; i++) list.Add(new Station(seg, i / (float)steps));
        }

        return list;
    }

    // ================= MESH =================

    private class MeshData
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector2> UV = new();
        public readonly List<int> Tri = new();

        // Mép biên (local) + 1 điểm nằm phía trong hình — để dựng dải thành bên/bóng của viền.
        public readonly List<Vector3> EdgeA = new(), EdgeB = new(), EdgeIn = new();

        public void Edge(Vector3 a, Vector3 b, Vector3 inside)
        {
            EdgeA.Add(a); EdgeB.Add(b); EdgeIn.Add(inside);
        }

        /// <summary>Quad dải: cạnh a→d và b→c là 2 mép biên dọc theo dải.</summary>
        public void StripQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Quad(a, b, c, d, ua, ub, uc, ud);
            Edge(a, d, (b + c) * 0.5f);
            Edge(b, c, (a + d) * 0.5f);
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

    private Vector3 ToLocal(Vector2 px) => new(px.x * PxToUnit, (H * 0.5f - px.y) * PxToUnit, 0f);

    private Vector2 FromLocal(Vector3 local) => new(local.x / PxToUnit, H * 0.5f - local.y / PxToUnit);

    private Vector2 Uv(float col, float row)
    {
        Rect r = profile.textureRect;
        Texture t = profile.texture;
        return new Vector2((r.x + col) / t.width, (r.y + r.height - row) / t.height);
    }

    /// <summary>Quét 1 dải giữa độ lùn d0 (cột col0) và d1 (cột col1) dọc theo đường bao, tile texture theo chiều dài.</summary>
    private void AddStrip(MeshData m, List<Station> st, float d0, float d1, float col0, float col1,
        System.Func<int, bool> filter)
    {
        float s = 0f;
        float dMid = (d0 + d1) * 0.5f;

        for (int i = 0; i < st.Count; i++)
        {
            Station a = st[i];
            Station b = i + 1 < st.Count ? st[i + 1] : st[0];
            float tb = b.Seg == a.Seg ? b.T : 1f;
            int seg = a.Seg;

            Sample(dMid, seg, a.T, out Vector2 ma, out _);
            Sample(dMid, seg, tb, out Vector2 mb, out _);
            float len = Vector2.Distance(ma, mb);

            if (filter(seg) && len > 0.001f)
            {
                // Tách quad khi vượt qua mép tile để UV không tràn ra ngoài vùng sprite trong atlas.
                float t0 = 0f;
                float pos = Mathf.Repeat(s, RowTile);
                while (t0 < 1f)
                {
                    float remain = (RowTile - pos) / len;
                    float t1 = Mathf.Min(1f, t0 + remain);
                    float row0 = RowStart + pos;
                    float row1 = RowStart + pos + (t1 - t0) * len;

                    float ta = Mathf.Lerp(a.T, tb, t0), tc = Mathf.Lerp(a.T, tb, t1);
                    Sample(d0, seg, ta, out Vector2 p00, out _);
                    Sample(d1, seg, ta, out Vector2 p10, out _);
                    Sample(d1, seg, tc, out Vector2 p11, out _);
                    Sample(d0, seg, tc, out Vector2 p01, out _);

                    m.StripQuad(ToLocal(p00), ToLocal(p10), ToLocal(p11), ToLocal(p01),
                        Uv(col0, row0), Uv(col1, row0), Uv(col1, row1), Uv(col0, row1));

                    pos = t1 >= 1f ? pos + (t1 - t0) * len : 0f;
                    t0 = t1;
                }
            }

            s += len;
        }
    }

    /// <summary>Nửa đĩa bo đầu dải viền: tâm c, hướng đi ra ngoài dir. Ring dùng nửa mặt cắt có bóng.</summary>
    private void AddCap(MeshData m, Vector2 c, Vector2 dir, float radius, float colCenter, float colRing)
    {
        const int steps = 12;
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector2 uc = Uv(colCenter, CapRow), ur = Uv(colRing, CapRow);

        for (int i = 0; i < steps; i++)
        {
            float a0 = Mathf.Lerp(-90f, 90f, i / (float)steps) * Mathf.Deg2Rad;
            float a1 = Mathf.Lerp(-90f, 90f, (i + 1) / (float)steps) * Mathf.Deg2Rad;
            Vector2 p0 = c + (dir * Mathf.Cos(a0) + side * Mathf.Sin(a0)) * radius;
            Vector2 p1 = c + (dir * Mathf.Cos(a1) + side * Mathf.Sin(a1)) * radius;
            m.Tri3(ToLocal(c), ToLocal(p0), ToLocal(p1), uc, ur, ur);
            m.Edge(ToLocal(p0), ToLocal(p1), ToLocal(c));
        }
    }

    private void AddOpeningCaps(MeshData m)
    {
        float dc = OuterColEdge - OuterColCenter;
        float y = H - dc;
        float xo = OpeningHalf;
        AddCap(m, new Vector2(xo, y), Vector2.left, CapRadius, OuterColCenter, OuterUvOut);
        AddCap(m, new Vector2(-xo, y), Vector2.right, CapRadius, OuterColCenter, OuterUvOut);
    }

    // ---- Viền trong: chạy dọc theo đường bao (nửa phải, bên trái đối xứng) ----

    private float RoadWidth => Mathf.Clamp(roadWidth, MinRoadWidth, Mathf.Max(MinRoadWidth, Mathf.Min(A, H * 0.5f) - 60f));
    private float InnerDOut => RoadWidth + (InnerColEdge - InnerColOut);       // phía bàn chơi
    private float InnerDIn => RoadWidth - (InnerColIn - InnerColEdge);         // phía mặt đường
    private float InnerDCenter => RoadWidth - (InnerColCenter - InnerColEdge);
    private const float InnerMinLength = 60f;

    private struct PathSpan
    {
        public int Seg;
        public float T0, T1, S0, Len;
    }

    /// <summary>Nửa phải đường bao tại tâm viền trong: từ giữa cạnh trên → giữa cạnh dưới, kèm quãng đường tích luỹ.</summary>
    private List<PathSpan> InnerPath(out float total)
    {
        var spans = new List<PathSpan>();
        total = 0f;
        for (int seg = SegTopRight; seg <= SegBottomMid; seg++)
        {
            float tEnd = seg == SegBottomMid ? 0.5f : 1f;
            int steps = IsArc(seg) ? 32 : 16;
            for (int i = 0; i < steps; i++)
            {
                float t0 = tEnd * i / steps, t1 = tEnd * (i + 1) / steps;
                Sample(InnerDCenter, seg, t0, out Vector2 a, out _);
                Sample(InnerDCenter, seg, t1, out Vector2 b, out _);
                float len = Vector2.Distance(a, b);
                if (len < 0.001f) continue;
                spans.Add(new PathSpan { Seg = seg, T0 = t0, T1 = t1, S0 = total, Len = len });
                total += len;
            }
        }

        return spans;
    }

    private static bool Locate(List<PathSpan> spans, float s, out int seg, out float t)
    {
        if (spans.Count == 0) { seg = SegRight; t = 0.5f; return false; }

        foreach (PathSpan sp in spans)
        {
            if (s <= sp.S0 + sp.Len)
            {
                seg = sp.Seg;
                t = Mathf.Lerp(sp.T0, sp.T1, Mathf.Clamp01((s - sp.S0) / sp.Len));
                return true;
            }
        }

        PathSpan last = spans[^1];
        seg = last.Seg; t = last.T1;
        return true;
    }

    private void InnerRange(float total, out float s0, out float s1)
    {
        s0 = Mathf.Clamp(innerCurbStart, 0f, total - InnerMinLength);
        s1 = Mathf.Clamp(innerCurbEnd, s0 + InnerMinLength, total);
    }

    private void AddInnerCurbs(MeshData m)
    {
        List<PathSpan> spans = InnerPath(out float total);
        if (spans.Count == 0) return;
        InnerRange(total, out float sA, out float sB);

        var right = new MeshData();
        foreach (PathSpan sp in spans)
        {
            float a = Mathf.Max(sA, sp.S0), b = Mathf.Min(sB, sp.S0 + sp.Len);
            if (b <= a) continue;

            // Tách theo mép tile của texture.
            float s = a;
            while (s < b)
            {
                float pos = Mathf.Repeat(s - sA, RowTile);
                // Sai số float có thể cho pos ≈ RowTile → bước = 0 → kẹt vòng lặp. Coi như đầu tile mới.
                if (RowTile - pos < 0.01f) pos = 0f;
                float e = Mathf.Min(b, s + (RowTile - pos));
                if (e <= s) break;
                float ta = Mathf.Lerp(sp.T0, sp.T1, (s - sp.S0) / sp.Len);
                float tb = Mathf.Lerp(sp.T0, sp.T1, (e - sp.S0) / sp.Len);

                Sample(InnerDOut, sp.Seg, ta, out Vector2 p00, out _);
                Sample(InnerDIn, sp.Seg, ta, out Vector2 p10, out _);
                Sample(InnerDIn, sp.Seg, tb, out Vector2 p11, out _);
                Sample(InnerDOut, sp.Seg, tb, out Vector2 p01, out _);
                float r0 = RowStart + pos, r1 = r0 + (e - s);

                right.StripQuad(ToLocal(p00), ToLocal(p10), ToLocal(p11), ToLocal(p01),
                    Uv(InnerUvOut, r0), Uv(InnerUvIn, r0), Uv(InnerUvIn, r1), Uv(InnerUvOut, r1));
                s = e;
            }
        }

        AddInnerCap(right, spans, sA, -1f);
        AddInnerCap(right, spans, sB, 1f);

        // Ghép nửa phải + bản đối xứng sang trái.
        AppendMirrored(m, right, false);
        AppendMirrored(m, right, true);
    }

    private void AddInnerCap(MeshData m, List<PathSpan> spans, float s, float forward)
    {
        Locate(spans, s, out int seg, out float t);
        Sample(InnerDCenter, seg, t, out Vector2 p, out Vector2 n);
        Vector2 tangent = new Vector2(-n.y, n.x); // chiều kim đồng hồ trong hệ y xuống
        AddCap(m, p, tangent * forward, InnerCapRadius, InnerColCenter, InnerUvOut);
    }

    private static void AppendMirrored(MeshData dst, MeshData src, bool mirror)
    {
        int offset = dst.V.Count;
        foreach (Vector3 v in src.V) dst.V.Add(mirror ? new Vector3(-v.x, v.y, v.z) : v);
        dst.UV.AddRange(src.UV);
        foreach (int i in src.Tri) dst.Tri.Add(i + offset);
        for (int i = 0; i < src.EdgeA.Count; i++)
            dst.Edge(Mirror(src.EdgeA[i], mirror), Mirror(src.EdgeB[i], mirror), Mirror(src.EdgeIn[i], mirror));
    }

    private static Vector3 Mirror(Vector3 v, bool mirror) => mirror ? new Vector3(-v.x, v.y, v.z) : v;

    /// <summary>
    /// Viền nổi: mặt viền (order 3) + dải thành bên/bóng (order 2). Dải được quét thẳng xuống dưới màn hình
    /// từ từng mép biên quay xuống của viền, UV dọc theo texture RoadCurbSide → đúng pixel art gốc.
    /// </summary>
    private void CreateRaised(Transform root, string name, MeshData top)
    {
        if (curbSideMaterial != null && curbSideLength > 0f)
        {
            var side = new MeshData();
            Vector3 off = new Vector3(0f, -curbSideLength * PxToUnit, 0f);
            for (int i = 0; i < top.EdgeA.Count; i++)
            {
                Vector3 a = top.EdgeA[i], b = top.EdgeB[i];
                Vector3 e = b - a;
                if (e.sqrMagnitude < 1e-10f) continue;
                Vector3 n = new Vector3(e.y, -e.x, 0f).normalized;
                if (Vector3.Dot(n, (a + b) * 0.5f - top.EdgeIn[i]) < 0f) n = -n; // pháp tuyến hướng ra ngoài viền
                if (n.y > -1e-4f) continue;                                       // chỉ mép quay xuống mới thấy thành
                side.Quad(a, b, b + off, a + off,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            }
            CreateMesh(root, name + "-Side", side, 2, curbSideMaterial);
        }
        CreateMesh(root, name, top, 3);
    }

    private void CreateMesh(Transform parent, string name, MeshData data, int order, Material material = null)
    {
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

    // ================= LÀN + VẠCH ĐỨT =================

    private readonly List<Vector2> _lanePx = new();
    private readonly List<Vector2> _dashPx = new();

    private void BuildLane(List<Station> st)
    {
        _lane.Clear();
        SamplePath(st, LaneCenter, _lanePx);
        SamplePath(st, LaneCenter, _dashPx);

        foreach (Vector2 p in _lanePx)
        {
            Vector3 l = ToLocal(p);
            _lane.Add(new Vector2(l.x, l.y));
        }
    }

    private void SamplePath(List<Station> st, float d, List<Vector2> dst)
    {
        dst.Clear();
        foreach (Station s in st)
        {
            Sample(d, s.Seg, s.T, out Vector2 p, out _);
            if (dst.Count == 0 || Vector2.Distance(dst[^1], p) > 0.5f) dst.Add(p);
        }
    }

    private void PlaceDashes(Transform root)
    {
        if (dash == null || _dashPx.Count < 2) return;

        var parent = new GameObject("Dashes").transform;
        parent.SetParent(root, false);

        // Sprite dọc (cao > rộng) → trục dài là Y, xoay thêm -90° để nằm dọc theo đường.
        float spriteAngle = dash.rect.height > dash.rect.width ? -90f : 0f;

        float carried = dashPeriod * 0.5f;
        for (int i = 0; i < _dashPx.Count; i++)
        {
            Vector2 a = _dashPx[i];
            Vector2 b = _dashPx[(i + 1) % _dashPx.Count];
            float len = Vector2.Distance(a, b);
            Vector2 dir = (b - a) / Mathf.Max(len, 0.0001f);

            while (carried <= len)
            {
                Vector2 p = a + dir * carried;
                var go = new GameObject("Dash");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = ToLocal(p);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg + spriteAngle);
                go.transform.localScale = Vector3.one * (dash.pixelsPerUnit * PxToUnit * dashScale);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = dash;
                sr.sortingOrder = 5;
                if (dashMaterial != null) sr.sharedMaterial = dashMaterial;
                carried += dashPeriod;
            }

            carried -= len;
        }
    }

    // ================= BOARD =================

    // Vùng trong = mép trong của đường (d = RoadWidth).
    private float BoardTop => RoadWidth;
    private float BoardBottom => H - RoadWidth;
    private float ColumnsTotalWidth => columnCount * columnWidth + (columnCount - 1) * columnSpacingX;

    // Đầu/cuối cột bám theo độ cao 2 điểm đầu/cuối viền trong (chấm xanh lá), ngắn vào thêm columnPaddingY.
    private float InnerEndY(bool first)
    {
        List<PathSpan> spans = InnerPath(out float total);
        InnerRange(total, out float a, out float b);
        Locate(spans, first ? a : b, out int seg, out float t);
        Sample(InnerDCenter, seg, t, out Vector2 p, out _);
        return p.y;
    }

    private float ColumnTop
    {
        get
        {
            float top = Mathf.Max(BoardTop, InnerEndY(true)) + columnPaddingY;
            float bot = Mathf.Min(BoardBottom, InnerEndY(false)) - columnPaddingY;
            return top <= bot ? top : (top + bot) * 0.5f;
        }
    }

    private float ColumnBottom => Mathf.Max(ColumnTop, Mathf.Min(BoardBottom, InnerEndY(false)) - columnPaddingY);
    private float ColumnHeight => ColumnBottom - ColumnTop;

    /// <summary>Tâm cột i (local, unit).</summary>
    public Vector3 GetColumnCenter(int i)
    {
        float x = -ColumnsTotalWidth * 0.5f + columnWidth * 0.5f + i * (columnWidth + columnSpacingX);
        return ToLocal(new Vector2(x, (ColumnTop + ColumnBottom) * 0.5f));
    }

    public int ColumnCount => columnCount;
    public Vector2 ColumnSize => new Vector2(columnWidth, ColumnHeight) * PxToUnit;

    [FoldoutGroup("Board"), Button("Build Board", ButtonSizes.Large), GUIColor(0.55f, 0.75f, 1f)]
    public void BuildBoard()
    {
        if (boardSprite == null || boardMaterial == null || profile == null || roadMaterial == null)
        {
            Debug.LogError("[RoadLoopBuilder] Board thiếu boardSprite/boardMaterial (hoặc profile/roadMaterial).", this);
            return;
        }

        Transform old = transform.Find(BoardName);
        if (old != null)
        {
            foreach (MeshFilter mf in old.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) SafeDestroy(mf.sharedMesh);
            SafeDestroy(old.gameObject);
        }

        var root = new GameObject(BoardName).transform;
        root.SetParent(transform, false);

        BuildBoardFill(root);
        BuildDividers(root);
        BuildColumnArrows(root);
    }

    /// <summary>Mỗi cột 2 mũi tên chỉ xuống (hướng xe chạy): 1 ở đầu cột, 1 ở cuối cột.</summary>
    private void BuildColumnArrows(Transform root)
    {
        if (columnArrow == null) return;

        var parent = new GameObject("Arrows").transform;
        parent.SetParent(root, false);

        float y0 = ColumnTop + columnArrowInset;    // px, y xuống
        float y1 = ColumnBottom - columnArrowInset;
        Material mat = columnArrowMaterial != null ? columnArrowMaterial : dashMaterial;

        for (int i = 0; i < columnCount; i++)
        {
            float x = GetColumnCenter(i).x / PxToUnit;
            CreateArrow(parent, $"Arrow-{i}-Top", new Vector2(x, y0), mat);
            CreateArrow(parent, $"Arrow-{i}-Bottom", new Vector2(x, y1), mat);
        }
    }

    private void CreateArrow(Transform parent, string name, Vector2 px, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = ToLocal(px);
        go.transform.localScale = Vector3.one * (columnArrow.pixelsPerUnit * PxToUnit * columnArrowScale);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = columnArrow;
        sr.sortingOrder = 5;
        if (mat != null) sr.sharedMaterial = mat;
    }

    [FoldoutGroup("Board"), Button("Clear Board")]
    private void ClearBoard()
    {
        Transform old = transform.Find(BoardName);
        if (old != null) SafeDestroy(old.gameObject);
    }

    /// <summary>Lấp vùng trong bằng mặt bàn: fan từ tâm theo đường bao tại mép trong (bo góc khớp đường).</summary>
    private void BuildBoardFill(Transform root)
    {
        var m = new MeshData();
        List<Station> st = BuildStations();
        float hw = A - RoadWidth;
        var center = new Vector2(0f, H * 0.5f);

        Rect r = boardSprite.textureRect;
        Texture t = boardSprite.texture;
        Vector2 BoardUv(Vector2 p)
        {
            float u = Mathf.InverseLerp(-hw, hw, p.x);
            float v = Mathf.InverseLerp(BoardBottom, BoardTop, p.y);
            return new Vector2((r.x + u * r.width) / t.width, (r.y + v * r.height) / t.height);
        }

        for (int i = 0; i < st.Count; i++)
        {
            Station a = st[i];
            Station b = i + 1 < st.Count ? st[i + 1] : st[0];
            float d = RoadWidth - 8f; // luồn dưới mép đường vài px, tránh khe hở
            Sample(d, a.Seg, a.T, out Vector2 pa, out _);
            Sample(d, a.Seg, b.Seg == a.Seg ? b.T : 1f, out Vector2 pb, out _);
            if ((pa - pb).sqrMagnitude < 0.01f) continue;
            m.Tri3(ToLocal(center), ToLocal(pa), ToLocal(pb), BoardUv(center), BoardUv(pa), BoardUv(pb));
        }

        CreateMesh(root, "Board-Fill", m, -1, boardMaterial);
    }

    /// <summary>Vạch chia ở giữa khoảng Space X giữa các cột, cao bằng cột.</summary>
    private void BuildDividers(Transform root)
    {
        if (columnCount < 2 || dividerWidth <= 0f) return;

        var m = new MeshData();
        float y0 = ColumnTop, y1 = ColumnBottom;
        float half = dividerWidth * 0.5f;

        // Màu + độ mờ giống mũi tên: lấy texel ở thân tam giác (cùng atlas với roadMaterial).
        Vector2 uv;
        if (columnArrow != null && columnArrow.texture == roadMaterial.mainTexture)
        {
            Rect ar = columnArrow.textureRect;
            Texture at = columnArrow.texture;
            uv = new Vector2((ar.x + ar.width * 0.5f) / at.width, (ar.y + ar.height * 0.7f) / at.height);
        }
        else uv = Uv(DividerCol, CapRow);

        for (int i = 0; i < columnCount - 1; i++)
        {
            float x = -ColumnsTotalWidth * 0.5f + (i + 1) * columnWidth + (i + 0.5f) * columnSpacingX;
            m.Quad(ToLocal(new Vector2(x - half, y0)), ToLocal(new Vector2(x + half, y0)),
                ToLocal(new Vector2(x + half, y1)), ToLocal(new Vector2(x - half, y1)), uv, uv, uv, uv);
        }

        CreateMesh(root, "Dividers", m, 1, roadMaterial);
    }

    private void OnDrawGizmos()
    {
        if (columnCount < 1) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 size = new Vector3(columnWidth * PxToUnit, ColumnHeight * PxToUnit, 0f);
        for (int i = 0; i < columnCount; i++)
        {
            Vector3 c = GetColumnCenter(i);
            Gizmos.color = new Color(1f, 0f, 0f, 0.12f);
            Gizmos.DrawCube(c, size);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(c, size);
        }
    }

    // ================= HANDLE API (dùng bởi editor) =================

    // TR, BR, BL, TL — dấu theo hệ px (y xuống): y = -1 là cạnh trên.
    private static readonly Vector2[] CornerSign = { new(1, -1), new(1, 1), new(-1, 1), new(-1, -1) };

    /// <summary>Hướng kéo (world) của handle góc i: hướng vào tâm vòng theo đường chéo.</summary>
    public Vector3 GetCornerHandleDirection(int i)
    {
        Vector2 s = CornerSign[i];
        return transform.TransformDirection(new Vector3(-s.x, s.y, 0f).normalized);
    }

    public Vector3 GetCornerHandle(int i)
    {
        Vector2 k = CornerPx(i);
        Vector2 g = CornerInwardPx(i);
        return transform.TransformPoint(ToLocal(k + g * Radius * (Mathf.Sqrt(2f) - 1f)));
    }

    public void SetCornerHandle(int i, Vector3 world)
    {
        Vector2 p = FromLocal(transform.InverseTransformPoint(world));
        float delta = Vector2.Dot(p - CornerPx(i), CornerInwardPx(i));
        cornerRadius = Mathf.Clamp(delta / (Mathf.Sqrt(2f) - 1f), MinRadius, Mathf.Min(A, H * 0.5f));
    }

    public Vector3 GetOpeningHandle(int side)
    {
        float dc = OuterColEdge - OuterColCenter;
        return transform.TransformPoint(ToLocal(new Vector2(side * OpeningHalf, H - dc)));
    }

    public Vector3 GetOpeningHandleDirection() => transform.TransformDirection(Vector3.right);

    public void SetOpeningHandle(Vector3 world)
    {
        Vector2 p = FromLocal(transform.InverseTransformPoint(world));
        bottomOpeningWidth = 2f * Mathf.Abs(p.x);
        bottomOpeningWidth = 2f * OpeningHalf; // kẹp trong khoảng hợp lệ
    }

    // ---- Handle viền trong: chạy theo đường bao ----
    private float InnerCapRadius => (InnerColIn - InnerColOut) * 0.5f;

    public float InnerCurbStart { get { InnerRange(InnerPathLength, out float a, out _); return a; } }
    public float InnerCurbEnd { get { InnerRange(InnerPathLength, out _, out float b); return b; } }
    private float InnerPathLength { get { InnerPath(out float total); return total; } }

    /// <summary>Handle đầu bo viền trong. side = -1 trái / 1 phải; first = điểm đầu (phía trên).</summary>
    public Vector3 GetInnerCurbHandle(int side, bool first)
    {
        List<PathSpan> spans = InnerPath(out float total);
        InnerRange(total, out float a, out float b);
        Locate(spans, first ? a : b, out int seg, out float t);
        Sample(InnerDCenter, seg, t, out Vector2 p, out _);
        return transform.TransformPoint(ToLocal(new Vector2(side * p.x, p.y)));
    }

    /// <summary>Chiếu vị trí kéo lên đường bao (điểm gần nhất) rồi đổi ra quãng đường.</summary>
    public void SetInnerCurbHandle(int side, bool first, Vector3 world)
    {
        Vector2 q = FromLocal(transform.InverseTransformPoint(world));
        q.x *= side; // đưa về nửa phải

        List<PathSpan> spans = InnerPath(out float total);
        float best = float.MaxValue, bestS = 0f;
        foreach (PathSpan sp in spans)
        {
            Sample(InnerDCenter, sp.Seg, sp.T0, out Vector2 a, out _);
            Sample(InnerDCenter, sp.Seg, sp.T1, out Vector2 b, out _);
            Vector2 ab = b - a;
            float f = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
            float dist = (a + ab * f - q).sqrMagnitude;
            if (dist < best) { best = dist; bestS = sp.S0 + f * sp.Len; }
        }

        InnerRange(total, out float s0, out float s1);
        if (first) innerCurbStart = Mathf.Clamp(bestS, 0f, s1 - InnerMinLength);
        else innerCurbEnd = Mathf.Clamp(bestS, s0 + InnerMinLength, total);
    }

    // ---- Làn xe = phần mặt đường lộ ra giữa 2 viền; 2 chấm ở giữa cạnh phải, kéo ngang ----
    // Chấm ngoài: nới mép ngoài ra (tăng kích thước vòng + bề rộng đường, bàn giữ nguyên).
    // Chấm trong: nới mép trong vào (tăng bề rộng đường, bàn thu nhỏ).
    private float LaneOuter => OuterColEdge - OuterColIn;                 // mép trong viền ngoài
    private float LaneInner => RoadWidth - (InnerColIn - InnerColEdge);   // mép phía đường của viền trong
    private float LaneCenter => (LaneOuter + LaneInner) * 0.5f;
    public float RoadWidthPx => RoadWidth;

    /// <summary>Bề rộng làn (unit local).</summary>
    public float LaneWidth => (LaneInner - LaneOuter) * PxToUnit;
    public float LaneOuterPx => LaneOuter;
    public float LaneInnerPx => LaneInner;

    /// <summary>Mép làn (local) để vẽ trong editor. outer = true: mép ngoài.</summary>
    public List<Vector3> GetLaneEdge(bool outer)
    {
        var list = new List<Vector3>();
        float d = outer ? LaneOuter : LaneInner;
        foreach (Station s in BuildStations())
        {
            Sample(d, s.Seg, s.T, out Vector2 p, out _);
            list.Add(transform.TransformPoint(ToLocal(p)));
        }
        return list;
    }

    public Vector3 GetLaneHandle(bool outer)
    {
        Sample(outer ? LaneOuter : LaneInner, SegRight, 0.5f, out Vector2 p, out _);
        return transform.TransformPoint(ToLocal(p));
    }

    public Vector3 GetLaneHandleDirection() => transform.TransformDirection(Vector3.right);

    public void SetLaneHandle(bool outer, Vector3 world)
    {
        Vector2 p = FromLocal(transform.InverseTransformPoint(world));
        float d = A - p.x; // vị trí mới, tính từ mép ngoài hiện tại

        if (outer)
        {
            // Mép ngoài dời ra δ: vòng to thêm δ mỗi phía, đường rộng thêm δ → mép trong (bàn) đứng yên.
            float delta = Mathf.Max(LaneOuter - d, MinRoadWidth - RoadWidth);
            outerHalfWidth += delta;
            outerHeight += 2f * delta;
            cornerRadius += delta;
            roadWidth = RoadWidth + delta;
        }
        else
        {
            roadWidth = d + (InnerColIn - InnerColEdge);
            roadWidth = RoadWidth; // kẹp
        }
    }

    // px, y xuống
    private Vector2 CornerPx(int i)
    {
        Vector2 s = CornerSign[i];
        return new Vector2(s.x * A, s.y < 0 ? 0f : H);
    }

    private Vector2 CornerInwardPx(int i)
    {
        Vector2 s = CornerSign[i];
        return new Vector2(-s.x, s.y < 0 ? 1f : -1f).normalized;
    }

    private void OnDrawGizmosSelected()
    {
        if (_lane.Count < 2) return;
        Gizmos.color = Color.yellow;
        Gizmos.matrix = transform.localToWorldMatrix;
        for (int i = 0; i < _lane.Count; i++)
            Gizmos.DrawLine(_lane[i], _lane[(i + 1) % _lane.Count]);
    }
}
