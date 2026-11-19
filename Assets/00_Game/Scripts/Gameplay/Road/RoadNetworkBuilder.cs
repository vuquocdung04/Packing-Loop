using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Road kiểu game gốc: vòng ngoài chữ nhật bo góc (có lối ra ở cạnh dưới) + 1 đoạn đường ngang ở giữa chia trong
/// thành 2 đảo (đảo trên / đảo dưới). Mỗi đảo có nền mặt bàn và viền ở 2 cạnh bên (đối xứng trái/phải),
/// điểm đầu/cuối viền chạy dọc theo mép đảo (ôm 1 phần góc) — cạnh trên/dưới để hở cho xe vào/ra cột.
/// Đường giữa không có viền.
/// Art lấy nguyên từ profile Road_Vertical (asphalt / viền ngoài / viền trong) như RoadLoopBuilder,
/// thành + bóng viền lấy từ RoadCurbSide. Đơn vị "px" = pixel profile (1 px = 0.01 unit local),
/// dựng trong mặt phẳng XY local (object xoay X=90 để nằm trên mặt đất). Hệ px: x sang phải, y xuống dưới,
/// gốc = giữa cạnh trên của mép ngoài. Có chấm kéo trong Scene (RoadNetworkBuilderEditor).
/// </summary>
public class RoadNetworkBuilder : MonoBehaviour
{
    private const float PxToUnit = 0.01f;
    private const string GeneratedName = "Generated";

    // Mặt cắt Road_Vertical (cột pixel) — giống RoadLoopBuilder.
    private const float OuterColEdge = 455f, OuterColIn = 403f, OuterColOut = 467f, OuterColCenter = 435f;
    private const float OuterUvOut = 457f, OuterUvIn = 409f;
    private const float InnerColEdge = 34f, InnerColOut = 23f, InnerColIn = 90f, InnerColCenter = 56.5f;
    private const float InnerUvOut = 32f, InnerUvIn = 80f;
    private const float AsphaltColA = 130f, AsphaltColB = 370f;
    private const float RowStart = 10f, RowTile = 440f, CapRow = 230f;
    private const float MinRadius = 20f;
    private const float CurbMinLength = 40f;

    // ---------- Vòng ngoài ----------
    [BoxGroup("Vòng ngoài (px)"), LabelText("Nửa ngang (mép ngoài)")]
    [SerializeField, Min(300f), OnValueChanged(nameof(Build))] private float outerHalfWidth = 1793f;
    [BoxGroup("Vòng ngoài (px)"), LabelText("Cao (mép ngoài)")]
    [SerializeField, Min(600f), OnValueChanged(nameof(Build))] private float outerHeight = 4608f;
    [BoxGroup("Vòng ngoài (px)"), LabelText("Bán kính góc ngoài")]
    [SerializeField, Min(MinRadius), OnValueChanged(nameof(Build))] private float cornerRadius = 960f;
    [BoxGroup("Vòng ngoài (px)"), LabelText("Bề rộng đường vòng"), Tooltip("Mép ngoài viền ngoài → mép đảo (gốc 421).")]
    [SerializeField, Min(200f), OnValueChanged(nameof(Build))] private float roadWidth = 510f;
    [BoxGroup("Vòng ngoài (px)"), LabelText("Lối ra cạnh dưới"), Tooltip("Khoảng hở viền ngoài ở giữa cạnh dưới. 0 = kín.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float bottomOpeningWidth = 600f;

    // ---------- Đường giữa ----------
    [BoxGroup("Đường giữa (px)"), LabelText("Bật đường giữa")]
    [SerializeField, OnValueChanged(nameof(Build))] private bool middleRoad = true;
    [BoxGroup("Đường giữa (px)"), LabelText("Vị trí (tâm, tính từ mép trên)"), ShowIf(nameof(middleRoad))]
    [SerializeField, OnValueChanged(nameof(Build))] private float middleY = 2215f;
    [BoxGroup("Đường giữa (px)"), LabelText("Bề rộng"), ShowIf(nameof(middleRoad))]
    [SerializeField, Min(150f), OnValueChanged(nameof(Build))] private float middleWidth = 401f;
    [BoxGroup("Đường giữa (px)"), LabelText("Độ cong hất lên (góc dưới đảo trên)"), ShowIf(nameof(middleRoad))]
    [Tooltip("Đường giữa hất cong lên 2 làn bên = 2 góc dưới đảo trên bo tròn. Kéo chấm ở giữa cung trong Scene.")]
    [SerializeField, Min(1f), OnValueChanged(nameof(Build))] private float junctionRadius = 450f;
    [BoxGroup("Đường giữa (px)"), LabelText("Bo đầu sừng đảo dưới"), ShowIf(nameof(middleRoad))]
    [Tooltip("Mép dưới đường giữa hất lên đồng tâm → 2 góc trên đảo dưới nhô thành sừng nhọn; đây là bán kính bo đầu sừng.")]
    [SerializeField, Min(1f), OnValueChanged(nameof(Build))] private float lowerJunctionRadius = 60f;

    // ---------- Viền đảo (quãng đường dọc mép đảo nửa phải, từ giữa cạnh trên theo chiều kim đồng hồ) ----------
    [BoxGroup("Viền đảo (px)"), LabelText("Đảo trên: điểm đầu"), Tooltip("Kéo chấm xanh lá trong Scene. Bên trái đối xứng.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float topCurbStart = 1300f;
    [BoxGroup("Viền đảo (px)"), LabelText("Đảo trên: điểm cuối")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float topCurbEnd = 2000f;
    [BoxGroup("Viền đảo (px)"), LabelText("Đảo dưới: điểm đầu")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float bottomCurbStart = 1300f;
    [BoxGroup("Viền đảo (px)"), LabelText("Đảo dưới: điểm cuối")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float bottomCurbEnd = 2000f;

    // ---------- Nền đảo ----------
    [BoxGroup("Nền đảo"), LabelText("Bật nền đảo")]
    [SerializeField, OnValueChanged(nameof(Build))] private bool islandFill = true;
    [BoxGroup("Nền đảo"), ShowIf(nameof(islandFill))][SerializeField, OnValueChanged(nameof(Build))] private Sprite boardSprite;     // asphalt-texture_0
    [BoxGroup("Nền đảo"), ShowIf(nameof(islandFill))][SerializeField, OnValueChanged(nameof(Build))] private Material boardMaterial; // BoardMesh

    // ---------- Vạch đứt ----------
    [BoxGroup("Vạch đứt"), LabelText("Scale vạch")]
    [SerializeField, OnValueChanged(nameof(Build))] private float dashScale = 0.45f;
    [BoxGroup("Vạch đứt"), LabelText("Khoảng cách vạch (px)")]
    [SerializeField, Min(10f), OnValueChanged(nameof(Build))] private float dashPeriod = 600f;
    [BoxGroup("Vạch đứt"), LabelText("Vạch ở đường giữa (loại 2)")]
    [Tooltip("Vạch thường theo đường xe rẽ (làn trái → cua → tâm đường giữa → cua lên làn phải, dừng trước mũi tên) " +
             "+ vạch li ti (loại 2) cong từ mép ngoài làn trái xuống đầu tay chắn barrier.")]
    [SerializeField, OnValueChanged(nameof(Build))] private bool middleDashes = true;
    [BoxGroup("Vạch đứt"), LabelText("Loại 2: sprite"), ShowIf(nameof(middleDashes)), Tooltip("Để trống = dùng sprite vạch vòng ngoài.")]
    [SerializeField, OnValueChanged(nameof(Build))] private Sprite middleDash;
    [BoxGroup("Vạch đứt"), LabelText("Loại 2: scale"), ShowIf(nameof(middleDashes))]
    [SerializeField, Min(0.01f), OnValueChanged(nameof(Build))] private float middleDashScale = 0.22f;
    [BoxGroup("Vạch đứt"), LabelText("Loại 2: khoảng cách (px)"), ShowIf(nameof(middleDashes))]
    [SerializeField, Min(10f), OnValueChanged(nameof(Build))] private float middleDashPeriod = 130f;
    [BoxGroup("Vạch đứt"), LabelText("Loại 2: điểm tách khỏi làn trái (px)"), ShowIf(nameof(middleDashes))]
    [Tooltip("Vạch ngắn bắt đầu khi đường rẽ đã lệch khỏi tâm làn trái chừng này. To = bắt đầu cua muộn hơn (gần đường giữa hơn).")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float turnDashBranch = 40f;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: đầu trên cao hơn mép trên đường giữa (px)"), ShowIf(nameof(middleDashes))]
    [SerializeField, OnValueChanged(nameof(Build))] private float barrierGuideTop = 70f;
    [BoxGroup("Vạch đứt"), LabelText("Li ti: đầu dưới cao hơn tay chắn (px)"), ShowIf(nameof(middleDashes))]
    [SerializeField, OnValueChanged(nameof(Build))] private float barrierGuideBottom = 200f;
    [BoxGroup("Vạch đứt"), LabelText("Chừa quanh mũi tên/decal (px)")]
    [Tooltip("Vạch đứt không vẽ đè lên mũi tên, decal barrier, barrier — chừa thêm khoảng này quanh chúng. " +
             "Mỗi đoạn vạch còn lại được chia đều để 2 đầu không bị cụt lệch.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float dashClearance = 60f;
    [BoxGroup("Vạch đứt"), LabelText("Chừa quanh barrier (px, dọc làn)")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float barrierDashClearance = 150f;

    // ---------- Mũi tên nhập làn ----------
    [BoxGroup("Mũi tên nhập làn"), LabelText("Sprite"), Tooltip("arrow_corner — đặt ở làn phải, ngang đường giữa, chỉ lên.")]
    [SerializeField, OnValueChanged(nameof(Build))] private Sprite mergeArrow;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Scale")]
    [SerializeField, Min(0.01f), OnValueChanged(nameof(Build))] private float mergeArrowScale = 1.09f;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Cách mép ngoài phải (px)"), Tooltip("Gốc game: 238.")]
    [SerializeField, OnValueChanged(nameof(Build))] private float mergeArrowInset = 238f;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Cao hơn tâm đường giữa (px)"), Tooltip("Gốc game: 289.")]
    [SerializeField, OnValueChanged(nameof(Build))] private float mergeArrowOffsetY = 289f;
    [BoxGroup("Mũi tên nhập làn"), LabelText("Màu")]
    [SerializeField, OnValueChanged(nameof(Build))] private Color mergeArrowColor = new(0.565f, 0.573f, 0.694f, 1f);

    // ---------- Barrier ----------
    [BoxGroup("Barrier"), LabelText("Barrier"), Tooltip("Gốc = tâm cột barrier, tay chắn nằm ngang sang phải. Builder chỉ dời vị trí.")]
    [SerializeField, OnValueChanged(nameof(Build))] private Transform barrier;
    [BoxGroup("Barrier"), LabelText("Cách mép dưới đường giữa (px)")]
    [SerializeField, OnValueChanged(nameof(Build))] private float barrierGap = 120f;
    [BoxGroup("Barrier"), LabelText("Cột cách mép ngoài (px)"), Tooltip("Dương = vào trong đường.")]
    [SerializeField, OnValueChanged(nameof(Build))] private float barrierInset = 50f;

    /// <summary>Sprite trang trí đặt theo mốc (mép ngoài trái, tâm đường giữa). Offset px: x sang phải, y xuống.</summary>
    [System.Serializable]
    public class Decal
    {
        public Sprite sprite;
        public Vector2 offset;
        [Min(0.01f)] public float scale = 1f;
        public float rotation;
        public Color color = Color.white;
    }

    [BoxGroup("Barrier"), LabelText("Hiện decal gốc"), Tooltip("Decal rời từ road gốc. Tắt khi dùng vạch đường giữa loại 2 (thay thế, liền mạch hơn).")]
    [SerializeField, OnValueChanged(nameof(Build))] private bool showBarrierDecals = false;
    [BoxGroup("Barrier"), LabelText("Vạch rẽ khi có barrier"), ShowIf(nameof(showBarrierDecals))]
    [Tooltip("Hiện khi barrier chặn làn trái: RoadMiddleDashes (vạch cong làn trái → đường giữa) + 2 vạch lẻ RoadMiddleRotatedLine. " +
             "Mốc = (mép ngoài trái, tâm đường giữa); số mặc định lấy từ road gốc.")]
    [SerializeField, OnValueChanged(nameof(Build))] private List<Decal> barrierDecals = new();

    // ---------- Art ----------
    [FoldoutGroup("Art"), Required][SerializeField] private Sprite profile;          // Road_Vertical
    [FoldoutGroup("Art"), Required][SerializeField] private Material roadMaterial;   // _MainTex = atlas của profile
    [FoldoutGroup("Art")][SerializeField] private Material curbSideMaterial;         // RoadCurbSide
    [FoldoutGroup("Art"), LabelText("Dài thành + bóng (px)")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float curbSideLength = 72f;
    [FoldoutGroup("Art")][SerializeField] private Sprite dash;                       // RoadDash_Round
    [FoldoutGroup("Art")][SerializeField] private Material spriteMaterial;           // Sprite-Unlit-Default

    // ================= HÌNH HỌC =================

    private float A => outerHalfWidth;
    private float H => outerHeight;
    private float W => Mathf.Clamp(roadWidth, 200f, Mathf.Min(A, H * 0.5f) - 100f);
    private float R => Mathf.Clamp(cornerRadius, MinRadius, Mathf.Min(A, H * 0.5f));
    private float IslandX => A - W;                       // nửa ngang đảo
    private float IslandR => Mathf.Max(MinRadius, R - W); // bo góc đảo phía vòng ngoài
    private float OpeningHalf => Mathf.Clamp(bottomOpeningWidth * 0.5f, 0f, Mathf.Max(0f, A - R - 40f));
    private bool HasMiddle => middleRoad && MidTop - W > 2f * MinRadius && H - W - MidBot > 2f * MinRadius;
    private float MW => Mathf.Max(150f, middleWidth);
    private float MidTop => middleY - MW * 0.5f;
    private float MidBot => middleY + MW * 0.5f;
    private float RJ(float r, float islandH) => Mathf.Clamp(r, 1f, Mathf.Max(1f, Mathf.Min(IslandX, islandH - IslandR - 10f)));
    private float RJTop => RJ(junctionRadius, MidTop - W);              // 2 góc dưới đảo trên (khúc hất lên)
    private float RJBot => Mathf.Clamp(lowerJunctionRadius, 1f, Mathf.Max(1f, Mathf.Min(RJTop * 0.9f, H - W - MidBot - IslandR - 10f))); // bo đầu sừng đảo dưới

    // Viền ngoài / viền đảo: độ lệch theo pháp tuyến hướng ra ngoài hình (âm = vào trong).
    private const float OuterO0 = OuterColOut - OuterColEdge, OuterO1 = -(OuterColEdge - OuterColIn);
    private const float IslandO0 = -(InnerColEdge - InnerColOut), IslandO1 = InnerColIn - InnerColEdge;

    // Làn xe = phần mặt đường lộ ra giữa 2 viền (độ lùn tính từ mép ngoài vào).
    private float LaneOuter => OuterColEdge - OuterColIn;
    private float LaneInner => W - (InnerColIn - InnerColEdge);
    private float LaneCenter => (LaneOuter + LaneInner) * 0.5f;

    private struct Pt
    {
        public Vector2 P, N; // px (y xuống), pháp tuyến hướng ra ngoài hình
        public Pt(Vector2 p, Vector2 n) { P = p; N = n; }
    }

    /// <summary>
    /// Chữ nhật bo góc (theo chiều kim đồng hồ trên màn hình), mỗi góc 1 bán kính.
    /// skipSide (0 trên, 1 phải, 2 dưới, 3 trái) ≥ 0 → bỏ cạnh đó, trả về path HỞ bắt đầu ngay sau cạnh bị bỏ,
    /// kết thúc ngay trước nó; mỗi đầu thò thêm extend dọc theo cạnh bị bỏ.
    /// </summary>
    private static List<Pt> RoundRect(float x0, float x1, float y0, float y1, float rTL, float rTR, float rBR, float rBL,
        int skipSide = -1, float extend = 0f)
    {
        // 8 đoạn: cạnh trên, cung TR, cạnh phải, cung BR, cạnh dưới, cung BL, cạnh trái, cung TL (không gồm điểm cuối).
        var segs = new List<Pt>[8];
        for (int i = 0; i < 8; i++) segs[i] = new List<Pt>();
        void Line(int k, Vector2 a, Vector2 b, Vector2 n)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 60f));
            for (int i = 0; i < steps; i++) segs[k].Add(new Pt(Vector2.Lerp(a, b, i / (float)steps), n));
        }
        void Arc(int k, Vector2 c, float r, float a0)
        {
            const int steps = 24;
            for (int i = 0; i < steps; i++)
            {
                float a = (a0 + 90f * i / steps) * Mathf.Deg2Rad;
                var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                segs[k].Add(new Pt(c + n * r, n));
            }
        }
        var sideA = new[] { new Vector2(x0 + rTL, y0), new Vector2(x1, y0 + rTR), new Vector2(x1 - rBR, y1), new Vector2(x0, y1 - rBL) };
        var sideB = new[] { new Vector2(x1 - rTR, y0), new Vector2(x1, y1 - rBR), new Vector2(x0 + rBL, y1), new Vector2(x0, y0 + rTL) };
        var sideN = new[] { Vector2.down, Vector2.right, Vector2.up, Vector2.left };
        for (int s = 0; s < 4; s++) Line(2 * s, sideA[s], sideB[s], sideN[s]);
        Arc(1, new Vector2(x1 - rTR, y0 + rTR), rTR, -90f);
        Arc(3, new Vector2(x1 - rBR, y1 - rBR), rBR, 0f);
        Arc(5, new Vector2(x0 + rBL, y1 - rBL), rBL, 90f);
        Arc(7, new Vector2(x0 + rTL, y0 + rTL), rTL, 180f);

        var l = new List<Pt>();
        if (skipSide < 0)
        {
            foreach (List<Pt> sg in segs) l.AddRange(sg);
            return l;
        }

        int k0 = 2 * skipSide;
        Vector2 a0p = sideA[skipSide], b0p = sideB[skipSide], n0 = sideN[skipSide];
        Vector2 dir = (b0p - a0p).normalized;
        if (extend > 0f) l.Add(new Pt(b0p - dir * extend, n0));
        for (int i = 1; i < 8; i++) l.AddRange(segs[(k0 + i) % 8]);
        l.Add(new Pt(a0p, n0));
        if (extend > 0f) l.Add(new Pt(a0p + dir * extend, n0));
        return l;
    }

    private List<Pt> OuterPath() => RoundRect(-A, A, 0f, H, R, R, R, R);

    /// <summary>Viền ngoài: kín, hoặc hở ở giữa cạnh dưới (lối ra).</summary>
    private List<Pt> OuterCurbPath(out bool closed)
    {
        closed = OpeningHalf <= 0.5f;
        if (closed) return OuterPath();
        return RoundRect(-A, A, 0f, H, R, R, R, R, 2, (A - R) - OpeningHalf);
    }

    private int IslandCount => HasMiddle ? 2 : 1;

    // ---- Đường giữa: dải ngang thẳng đè lên hình chữ nhật bo góc bên trong vòng.
    // Mép trên hất cong lên ở 2 đầu (= góc dưới đảo trên bo RJTop), mép dưới thẳng (góc trên đảo dưới gần vuông). ----
    private float CY(float x) => middleY;
    private float TopEdgeY(float x) => CY(x) - MW * 0.5f;   // mép trên đường giữa (phần thẳng) = mép dưới đảo trên

    /// <summary>
    /// Mép dưới đường giữa = mép trên đảo dưới. Ở giữa thẳng; 2 đầu hất lên theo cung ĐỒNG TÂM với góc bo đảo trên
    /// (tâm C = (±(X − r), MidTop − r), bán kính r + bề rộng đường giữa) → cả dải đường giữa cong đều, bề rộng không đổi,
    /// ngã ba không bị phình.
    /// </summary>
    private float BotEdgeY(float x)
    {
        float X = IslandX, r = RJTop, ax = Mathf.Min(Mathf.Abs(x), X);
        float dx = ax - (X - r);
        if (dx <= 0f) return MidBot;
        float R2 = r + MW;
        return (MidTop - r) + Mathf.Sqrt(Mathf.Max(0f, R2 * R2 - dx * dx));
    }

    /// <summary>Mép đảo i (khép kín, pháp tuyến hướng ra đường) = nửa phải + nửa trái đối xứng. 0 = trên, 1 = dưới.</summary>
    private List<Pt> IslandPath(int i)
    {
        List<Pt> half = IslandHalf(i, out _);
        var full = new List<Pt>(half);
        for (int k = half.Count - 2; k >= 1; k--)
            full.Add(new Pt(new Vector2(-half[k].P.x, half[k].P.y), new Vector2(-half[k].N.x, half[k].N.y)));
        return full;
    }

    /// <summary>
    /// Nửa phải mép đảo (theo chiều kim đồng hồ): giữa cạnh trên → góc → cạnh phải → góc → giữa cạnh dưới.
    /// Cạnh giáp đường giữa bám theo parabol, góc ngã ba bo bằng Bezier bậc 2. Kèm quãng đường tích luỹ.
    /// </summary>
    private List<Pt> IslandHalf(int i, out List<float> s)
    {
        var l = new List<Vector2>();
        float X = IslandX, top = W, bot = H - W, rt = IslandR;
        void Line(Vector2 a, Vector2 b)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 60f));
            for (int k = 0; k < n; k++) l.Add(Vector2.Lerp(a, b, k / (float)n));
        }
        void Arc(Vector2 c, float r, float a0)
        {
            for (int k = 0; k < 24; k++)
            {
                float a = (a0 + 90f * k / 24f) * Mathf.Deg2Rad;
                l.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }
        void Quad(Vector2 p0, Vector2 c, Vector2 p1)
        {
            for (int k = 0; k < 16; k++)
            {
                float t = k / 16f, u = 1f - t;
                l.Add(u * u * p0 + 2f * u * t * c + t * t * p1);
            }
        }
        void Along(float xa, float xb, System.Func<float, float> fy)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(xb - xa) / 40f));
            for (int k = 0; k < n; k++) { float x = Mathf.Lerp(xa, xb, k / (float)n); l.Add(new Vector2(x, fy(x))); }
        }

        if (!HasMiddle)
        {
            Line(new Vector2(0f, top), new Vector2(X - rt, top));
            Arc(new Vector2(X - rt, top + rt), rt, -90f);
            Line(new Vector2(X, top + rt), new Vector2(X, bot - rt));
            Arc(new Vector2(X - rt, bot - rt), rt, 0f);
            Line(new Vector2(X - rt, bot), new Vector2(0f, bot));
            l.Add(new Vector2(0f, bot));
        }
        else if (i == 0)
        {
            float rj = RJTop;
            Line(new Vector2(0f, top), new Vector2(X - rt, top));
            Arc(new Vector2(X - rt, top + rt), rt, -90f);
            Line(new Vector2(X, top + rt), new Vector2(X, TopEdgeY(X) - rj));
            Quad(new Vector2(X, TopEdgeY(X) - rj), new Vector2(X, TopEdgeY(X)), new Vector2(X - rj, TopEdgeY(X - rj)));
            Along(X - rj, 0f, TopEdgeY);
            l.Add(new Vector2(0f, TopEdgeY(0f)));
        }
        else
        {
            float rj = RJBot;
            Along(0f, X - rj, BotEdgeY);
            Quad(new Vector2(X - rj, BotEdgeY(X - rj)), new Vector2(X, BotEdgeY(X)), new Vector2(X, BotEdgeY(X) + rj));
            Line(new Vector2(X, BotEdgeY(X) + rj), new Vector2(X, bot - rt));
            Arc(new Vector2(X - rt, bot - rt), rt, 0f);
            Line(new Vector2(X - rt, bot), new Vector2(0f, bot));
            l.Add(new Vector2(0f, bot));
        }

        // Bỏ điểm trùng, pháp tuyến ra ngoài = vuông góc tiếp tuyến (đi theo chiều kim đồng hồ, y xuống).
        var pts = new List<Vector2>();
        foreach (Vector2 p in l) if (pts.Count == 0 || (p - pts[^1]).sqrMagnitude > 0.25f) pts.Add(p);
        var half = new List<Pt>(pts.Count);
        for (int k = 0; k < pts.Count; k++)
        {
            Vector2 t = pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)];
            t = t.sqrMagnitude > 1e-8f ? t.normalized : Vector2.right;
            half.Add(new Pt(pts[k], new Vector2(t.y, -t.x)));
        }

        s = new List<float> { 0f };
        for (int k = 1; k < half.Count; k++) s.Add(s[k - 1] + Vector2.Distance(half[k - 1].P, half[k].P));
        return half;
    }

    private void CurbRange(int island, float total, out float a, out float b)
    {
        float s0 = island == 0 ? topCurbStart : bottomCurbStart;
        float s1 = island == 0 ? topCurbEnd : bottomCurbEnd;
        a = Mathf.Clamp(s0, 0f, Mathf.Max(0f, total - CurbMinLength));
        b = Mathf.Clamp(s1, a + CurbMinLength, total);
    }

    private static Pt AtDistance(List<Pt> path, List<float> s, float d)
    {
        for (int k = 1; k < path.Count; k++)
            if (d <= s[k]) return Lerp(path[k - 1], path[k], Mathf.InverseLerp(s[k - 1], s[k], d));
        return path[^1];
    }

    /// <summary>Đoạn viền nửa phải của đảo (từ điểm đầu tới điểm cuối).</summary>
    private List<Pt> IslandCurbRight(int island)
    {
        List<Pt> half = IslandHalf(island, out List<float> s);
        CurbRange(island, s[^1], out float a, out float b);
        var sub = new List<Pt> { AtDistance(half, s, a) };
        for (int k = 0; k < half.Count; k++) if (s[k] > a && s[k] < b) sub.Add(half[k]);
        sub.Add(AtDistance(half, s, b));
        return sub;
    }

    private static List<Pt> Mirror(List<Pt> src)
    {
        var l = new List<Pt>(src.Count);
        foreach (Pt p in src) l.Add(new Pt(new Vector2(-p.P.x, p.P.y), new Vector2(-p.N.x, p.N.y)));
        return l;
    }

    // ================= BUILD =================

    [Button("Build", ButtonSizes.Large), GUIColor(0.55f, 0.85f, 0.55f), PropertyOrder(-1)]
    [ContextMenu("Build")]
    public void Build()
    {
        if (profile == null || roadMaterial == null) return;

        Transform root = ResetGenerated();
        List<Pt> outer = OuterPath();

        if (islandFill && boardSprite != null && boardMaterial != null) BuildIslandFill(root);

        // Mặt đường: dải vòng ngoài + dải đường giữa + 4 góc lượn ở ngã ba.
        var asphalt = new MeshData();
        Strip(asphalt, outer, true, 0f, -W - 8f, AsphaltColB, AsphaltColA);
        if (HasMiddle) AddMiddleAsphalt(asphalt);
        CreateMesh(root, "Asphalt", asphalt, 0);

        // Viền ngoài (hở lối ra ở cạnh dưới nếu có).
        var outerCurb = new MeshData();
        List<Pt> oc = OuterCurbPath(out bool ocClosed);
        Strip(outerCurb, oc, ocClosed, OuterO0, OuterO1, OuterUvOut, OuterUvIn);
        if (!ocClosed) AddCaps(outerCurb, oc, OuterO0, OuterO1, OuterColCenter, OuterUvOut);
        CreateRaised(root, "Curb-Outer", outerCurb);

        // Viền đảo: 2 cạnh bên mỗi đảo (đối xứng), đầu bo tròn.
        var islandCurb = new MeshData();
        for (int i = 0; i < IslandCount; i++)
        {
            List<Pt> right = IslandCurbRight(i);
            foreach (List<Pt> c in new[] { right, Mirror(right) })
            {
                Strip(islandCurb, c, false, IslandO0, IslandO1, InnerUvOut, InnerUvIn);
                AddCaps(islandCurb, c, IslandO0, IslandO1, InnerColCenter, InnerUvOut);
            }
        }
        CreateRaised(root, "Curb-Island", islandCurb);

        // Đồ trang trí đặt trước, đăng ký vùng cấm → vạch đứt né chúng.
        dashZones.Clear();
        PlaceArrow(root);
        PlaceBarrier();
        PlaceBarrierDecals(root);
        PlaceDashes(root);
    }

    // ================= VÙNG CẤM VẠCH ĐỨT =================

    /// <summary>Hình chữ nhật xoay (local builder, đơn vị unit) — vạch đứt có tâm nằm trong sẽ bị bỏ.</summary>
    private struct DashZone
    {
        public Vector2 C, Half;
        public float Cos, Sin;
        public bool LoopOnly; // chỉ chặn vạch vòng ngoài (vd làn trái đoạn xe đã rẽ vào đường giữa)
        public bool Contains(Vector2 p)
        {
            Vector2 d = p - C;
            float x = d.x * Cos + d.y * Sin, y = -d.x * Sin + d.y * Cos;
            return Mathf.Abs(x) <= Half.x && Mathf.Abs(y) <= Half.y;
        }
    }

    private readonly List<DashZone> dashZones = new();

    /// <summary>Đăng ký vùng cấm theo khung sprite (đã scale/xoay) + dashClearance. Dùng cho mọi sprite đặt trên mặt đường.</summary>
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

    /// <summary>Đăng ký vùng cấm chữ nhật thẳng theo toạ độ px.</summary>
    private void AddDashZonePx(Vector2 centerPx, Vector2 halfPx, bool loopOnly = false)
    {
        dashZones.Add(new DashZone { C = ToLocal(centerPx), Cos = 1f, Sin = 0f, Half = halfPx * PxToUnit, LoopOnly = loopOnly });
    }

    private bool InDashZone(Vector2 px, bool loop)
    {
        Vector2 p = ToLocal(px);
        foreach (DashZone z in dashZones) if ((loop || !z.LoopOnly) && z.Contains(p)) return true;
        return false;
    }

    private bool BarrierOn => barrier != null && HasMiddle;

    private void PlaceBarrierDecals(Transform root)
    {
        if (!BarrierOn || !showBarrierDecals || barrierDecals == null) return;
        var parent = new GameObject("BarrierDecals").transform;
        parent.SetParent(root, false);
        Vector2 anchor = new Vector2(-A, middleY);
        foreach (Decal d in barrierDecals)
        {
            if (d == null || d.sprite == null) continue;
            var go = new GameObject(d.sprite.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ToLocal(anchor + d.offset);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, d.rotation);
            go.transform.localScale = Vector3.one * d.scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = d.sprite;
            sr.color = d.color;
            sr.sortingOrder = 5;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            AddDashZone(go.transform, d.sprite);
        }
    }

    [Button("Clear"), PropertyOrder(-1)]
    private void Clear()
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

    private static void SafeDestroy(Object o)
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

    private Vector3 ToLocal(Vector2 px) => new(px.x * PxToUnit, (H * 0.5f - px.y) * PxToUnit, 0f);
    private Vector2 FromLocal(Vector3 local) => new(local.x / PxToUnit, H * 0.5f - local.y / PxToUnit);
    private Vector3 ToWorld(Vector2 px) => transform.TransformPoint(ToLocal(px));
    private Vector2 FromWorld(Vector3 world) => FromLocal(transform.InverseTransformPoint(world));

    private Vector2 Uv(float col, float row)
    {
        Rect r = profile.textureRect;
        Texture t = profile.texture;
        return new Vector2((r.x + col) / t.width, (r.y + r.height - row) / t.height);
    }

    /// <summary>
    /// Quét 1 dải dọc theo path giữa 2 độ lệch o0, o1 (dọc pháp tuyến ra ngoài; âm = vào trong) với cột UV col0, col1.
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

    /// <summary>Bo tròn 2 đầu của 1 dải viền hở.</summary>
    private void AddCaps(MeshData m, List<Pt> path, float o0, float o1, float colCenter, float colRing)
    {
        if (path.Count < 2) return;
        AddCap(m, path[0], (path[0].P - path[1].P).normalized, o0, o1, colCenter, colRing);
        AddCap(m, path[^1], (path[^1].P - path[^2].P).normalized, o0, o1, colCenter, colRing);
    }

    private void AddCap(MeshData m, Pt end, Vector2 dir, float o0, float o1, float colCenter, float colRing)
    {
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

    /// <summary>Dải ngang đường giữa (luồn vào 2 đường dọc) + 4 góc lượn giữa mép đảo bo tròn và góc vuông ngã ba.</summary>
    private void AddMiddleAsphalt(MeshData m)
    {
        float x0 = -IslandX - 8f, x1 = IslandX + 8f;
        var line = new List<Pt>();
        int steps = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / 60f));
        // Dải từ mép trên (thẳng) xuống mép dưới (hất lên 2 đầu). Tách quad ở mép tile texture theo chiều ngang.
        var xs = new List<float>();
        for (int i = 0; i <= steps; i++) xs.Add(Mathf.Lerp(x0, x1, i / (float)steps));
        for (float t = x0 + RowTile; t < x1; t += RowTile) xs.Add(t);
        xs.Sort();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            float xa = xs[i], xb = xs[i + 1];
            if (xb - xa < 0.01f) continue;
            // Cột UV lùi vào trong 40px: cạnh dải đường giữa lộ ra ngoài (không có viền che) → tránh mipmap ăn sang vùng viền sáng.
            float ra = RowStart + Mathf.Repeat(xa - x0, RowTile), rb = ra + (xb - xa);
            float cTop = AsphaltColB - 40f, cBot = AsphaltColA + 40f;
            float yTop = MidTop - 3f; // trùm 3px lên phần lấp góc cua → không hở khe sáng ở chỗ nối
            m.Quad(ToLocal(new Vector2(xa, yTop)), ToLocal(new Vector2(xa, BotEdgeY(xa))),
                ToLocal(new Vector2(xb, BotEdgeY(xb))), ToLocal(new Vector2(xb, yTop)),
                Uv(cTop, ra), Uv(cBot, ra), Uv(cBot, rb), Uv(cTop, rb));
        }

        // Góc ngã ba: lấp phần giữa góc nhọn (cạnh đảo ∩ mép đường giữa) và đường bo Bezier của đảo. Trái = đối xứng.
        float X = IslandX, rjT = RJTop, rjB = RJBot;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            AddFillet(m, sx, new Vector2(X, TopEdgeY(X) - rjT), new Vector2(X, TopEdgeY(X)), new Vector2(X - rjT, TopEdgeY(X - rjT)));
            AddFillet(m, sx, new Vector2(X - rjB, BotEdgeY(X - rjB)), new Vector2(X, BotEdgeY(X)), new Vector2(X, BotEdgeY(X) + rjB));
        }
    }

    /// <summary>
    /// Fan từ góc nhọn c (= điểm điều khiển Bezier) qua đường bo p0→p1 của đảo (nửa phải, sx = -1 lật sang trái).
    /// UV trải theo vị trí trên vùng asphalt của profile để có hạt nhiễu như phần đường còn lại.
    /// </summary>
    private void AddFillet(MeshData m, int sx, Vector2 p0, Vector2 c, Vector2 p1)
    {
        Vector2 F(Vector2 p) => new(p.x * sx, p.y);
        float span = Mathf.Max(1f, Mathf.Max(Vector2.Distance(c, p0), Vector2.Distance(c, p1)) + 4f);
        Vector2 FUv(Vector2 p) => Uv(
            Mathf.Lerp(AsphaltColA, AsphaltColB, Mathf.Clamp01(Mathf.Abs(p.x - c.x) / span)),
            RowStart + Mathf.Clamp01(Mathf.Abs(p.y - c.y) / span) * (RowTile - 2f));
        const int steps = 16;
        Vector2 Bz(float t) { float u = 1f - t; return u * u * p0 + 2f * u * t * c + t * t * p1; }
        // Đẩy đường bo vào trong đảo 2px để không hở khe với nền đảo.
        Vector2 mid = (p0 + p1) * 0.5f, inward = (mid - c).sqrMagnitude > 1e-6f ? (mid - c).normalized : Vector2.zero;
        for (int i = 0; i < steps; i++)
        {
            Vector2 a = Bz(i / (float)steps) + inward * 2f, b = Bz((i + 1) / (float)steps) + inward * 2f;
            m.Tri3(ToLocal(F(c)), ToLocal(F(a)), ToLocal(F(b)), FUv(c), FUv(a), FUv(b));
        }
    }

    /// <summary>Nền mặt bàn trong mỗi đảo: fan từ tâm theo mép đảo (luồn dưới mặt đường vài px để không hở khe).</summary>
    private void BuildIslandFill(Transform root)
    {
        var m = new MeshData();
        Rect r = boardSprite.textureRect;
        Texture t = boardSprite.texture;
        for (int i = 0; i < IslandCount; i++)
        {
            List<Pt> path = IslandPath(i);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (Pt p in path) { min = Vector2.Min(min, p.P); max = Vector2.Max(max, p.P); }
            Vector2 center = (min + max) * 0.5f;
            Vector2 BoardUv(Vector2 p)
            {
                // Thu vào 3% mỗi phía: hàng/cột pixel viền của sprite sáng hơn → lộ thành vạch ở cạnh đảo không có viền.
                float u = Mathf.Lerp(0.03f, 0.97f, Mathf.InverseLerp(min.x, max.x, p.x));
                float v = Mathf.Lerp(0.03f, 0.97f, Mathf.InverseLerp(max.y, min.y, p.y));
                return new Vector2((r.x + u * r.width) / t.width, (r.y + v * r.height) / t.height);
            }
            for (int k = 0; k < path.Count; k++)
            {
                Vector2 a = path[k].P + path[k].N * 8f, b = path[(k + 1) % path.Count].P + path[(k + 1) % path.Count].N * 8f;
                m.Tri3(ToLocal(center), ToLocal(a), ToLocal(b), BoardUv(center), BoardUv(a), BoardUv(b));
            }
        }
        CreateMesh(root, "Island-Fill", m, -1, boardMaterial);
    }

    /// <summary>Mặt viền (order 3) + thành/bóng quét xuống dưới màn hình từ các mép quay xuống (order 2).</summary>
    private void CreateRaised(Transform root, string name, MeshData top)
    {
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

    // ================= VẠCH / MŨI TÊN / BARRIER =================

    private void PlaceDashes(Transform root)
    {
        if (dash == null) return;
        var parent = new GameObject("Dashes").transform;
        parent.SetParent(root, false);

        bool turn = HasMiddle && middleDashes;
        List<Vector2> turnPath = turn ? TurnDashPath() : null;
        if (turn && BarrierOn)
        {
            // Làn trái từ chỗ bắt đầu cua vào đường giữa tới barrier: vạch vòng ngoài nhường cho vạch loại 2.
            float yStart = turnPath[0].y;
            float yEnd = MidBot + barrierGap;
            AddDashZonePx(new Vector2(-A + W * 0.5f, (yStart + yEnd) * 0.5f), new Vector2(W * 0.5f, (yEnd - yStart) * 0.5f), true);
        }

        var loop = new List<Vector2>();
        foreach (Pt p in OuterPath()) loop.Add(p.P - p.N * LaneCenter);
        DashAlong(parent, loop, true, dash, dashScale, dashPeriod, true);

        if (turn)
        {
            // Đường xe rẽ (cua trái → tâm đường giữa → cua lên làn phải) = vạch thường, dừng trước mũi tên.
            DashAlong(parent, turnPath, false, dash, dashScale, dashPeriod, false, true);
            // Vạch li ti: cong từ mép ngoài làn trái xuống đầu tay chắn barrier.
            if (BarrierOn)
                DashAlong(parent, BarrierGuidePath(), false, middleDash != null ? middleDash : dash, middleDashScale, middleDashPeriod, false);
        }
    }

    /// <summary>
    /// Đường cong vạch li ti ở chỗ barrier: từ sát mép ngoài làn trái (cao hơn mép trên đường giữa barrierGuideTop px)
    /// cong chéo xuống tới đầu tay chắn (mép đảo, cao hơn tay chắn barrierGuideBottom px). Dốc đứng lúc đầu, thoải dần.
    /// </summary>
    private List<Vector2> BarrierGuidePath()
    {
        Vector2 p0 = new(-A + LaneOuter + 15f, MidTop - barrierGuideTop);
        Vector2 p2 = new(-IslandX - 10f, MidBot + barrierGap - barrierGuideBottom);
        Vector2 c = new(Mathf.Lerp(p0.x, p2.x, 0.2f), Mathf.Lerp(p0.y, p2.y, 0.7f));
        var pts = new List<Vector2>();
        for (int k = 0; k <= 24; k++) { float t = k / 24f, u = 1f - t; pts.Add(u * u * p0 + 2f * u * t * c + t * t * p2); }
        return pts;
    }

    /// <summary>
    /// Đường vạch loại 2 = tâm làn rẽ, cắt bỏ đoạn đầu còn trùng thẳng với tâm làn trái (lệch &lt; turnDashBranch px)
    /// → vạch dài làn trái chạy tới đúng chỗ tách nhánh rồi vạch ngắn mới bắt đầu cua.
    /// </summary>
    private List<Vector2> TurnDashPath()
    {
        List<Vector2> p = MiddleLanePath();
        float xl = p[0].x;
        int i0 = 0;
        while (i0 + 1 < p.Count && Mathf.Abs(p[i0 + 1].x - xl) < turnDashBranch) i0++;
        return p.GetRange(i0, p.Count - i0);
    }

    /// <summary>
    /// Tâm làn đường giữa: làn trái ↓ → cua (Bezier quanh góc dưới-trái đảo trên) → bám parabol tâm đường giữa
    /// → cua lên làn phải ↑.
    /// </summary>
    public List<Vector2> MiddleLanePathPx() => MiddleLanePath();

    private List<Vector2> MiddleLanePath()
    {
        float r = RJTop, X = IslandX, xl = A - LaneCenter;
        float extra = xl - X;                                // tâm làn dọc cách mép đảo
        var pts = new List<Vector2>();
        void Quad(Vector2 p0, Vector2 c, Vector2 p1)
        {
            for (int k = 0; k <= 16; k++) { float t = k / 16f, u = 1f - t; pts.Add(u * u * p0 + 2f * u * t * c + t * t * p1); }
        }
        float xa = -X + r, xb = X - r;
        Quad(new Vector2(-xl, TopEdgeY(-X) - r - extra), new Vector2(-xl, CY(-X)), new Vector2(xa, CY(xa)));
        int n = Mathf.Max(2, Mathf.CeilToInt((xb - xa) / 40f));
        for (int k = 1; k < n; k++) { float x = Mathf.Lerp(xa, xb, k / (float)n); pts.Add(new Vector2(x, CY(x))); }
        Quad(new Vector2(xb, CY(xb)), new Vector2(xl, CY(X)), new Vector2(xl, TopEdgeY(X) - r - extra));
        return pts;
    }

    /// <summary>
    /// Rải vạch đứt dọc path, né các vùng cấm (dashZones). Path được lấy mẫu dày, cắt thành các đoạn liền nằm ngoài
    /// vùng cấm; mỗi đoạn chia đều n = round(dài / dashPeriod) vạch, cách 2 đầu nửa khoảng → không có vạch cụt sát mũi tên/decal.
    /// </summary>
    private void DashAlong(Transform parent, List<Vector2> pts, bool closed, Sprite sprite, float scale, float period, bool loopPath,
        bool stopAtFirstBlock = false)
    {
        const float step = 10f;
        // Lấy mẫu đều theo quãng đường.
        var sp = new List<Vector2>();
        var sd = new List<Vector2>(); // hướng
        int segs = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < segs; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
            float len = Vector2.Distance(a, b);
            if (len < 0.001f) continue;
            Vector2 dir = (b - a) / len;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            for (int k = 0; k < n; k++) { sp.Add(a + dir * (len * k / n)); sd.Add(dir); }
        }
        if (!closed && pts.Count > 0) { sp.Add(pts[^1]); sd.Add(sd.Count > 0 ? sd[^1] : Vector2.right); }
        int count = sp.Count;
        if (count < 2) return;

        var inside = new bool[count];
        int firstIn = -1;
        for (int i = 0; i < count; i++) { inside[i] = InDashZone(sp[i], loopPath); if (inside[i] && firstIn < 0) firstIn = i; }

        // Vòng kín không có vùng cấm: rải đều cả vòng như cũ.
        if (closed && firstIn < 0)
        {
            DashRun(parent, sp, sd, 0, count, true, sprite, scale, period);
            return;
        }

        // Duyệt bắt đầu từ 1 mẫu bị cấm (vòng kín) để đoạn liền không bị cắt đôi ở chỗ nối đầu/cuối.
        int start = closed ? firstIn : 0;
        int runStart = -1;
        for (int j = 0; j <= count; j++)
        {
            bool atEnd = j == count;
            int idx = (start + j) % count;
            bool blocked = atEnd || inside[idx];
            if (!blocked && runStart < 0) runStart = j;
            if (blocked && runStart >= 0)
            {
                DashRun(parent, sp, sd, start + runStart, j - runStart, false, sprite, scale, period);
                runStart = -1;
                if (stopAtFirstBlock) return; // vd vạch rẽ dừng ở mũi tên nhập làn, không chạy tiếp lên làn phải
            }
        }
    }

    /// <summary>Rải vạch đều trên đoạn mẫu [first, first + n) (chỉ số lấy mod số mẫu).</summary>
    private void DashRun(Transform parent, List<Vector2> sp, List<Vector2> sd, int first, int n, bool loop,
        Sprite sprite, float scale, float period)
    {
        if (n < 2) return;
        int count = sp.Count;
        var cum = new float[n];
        for (int k = 1; k < n; k++) cum[k] = cum[k - 1] + Vector2.Distance(sp[(first + k - 1) % count], sp[(first + k) % count]);
        float total = loop ? cum[n - 1] + Vector2.Distance(sp[(first + n - 1) % count], sp[first % count]) : cum[n - 1];
        int dashes = Mathf.RoundToInt(total / period);
        if (dashes <= 0) return;
        float spacing = total / dashes;

        float spriteAngle = sprite.rect.height > sprite.rect.width ? -90f : 0f;
        int k0 = 0;
        for (int d = 0; d < dashes; d++)
        {
            float target = (d + 0.5f) * spacing;
            while (k0 + 1 < n && cum[k0 + 1] < target) k0++;
            Vector2 a = sp[(first + k0) % count];
            Vector2 b = k0 + 1 < n ? sp[(first + k0 + 1) % count] : (loop ? sp[first % count] : a);
            float segLen = (k0 + 1 < n ? cum[k0 + 1] : total) - cum[k0];
            Vector2 p = segLen > 1e-4f ? Vector2.Lerp(a, b, (target - cum[k0]) / segLen) : a;
            Vector2 dir = sd[(first + k0) % count];

            var go = new GameObject("Dash");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ToLocal(p);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg + spriteAngle);
            go.transform.localScale = Vector3.one * (sprite.pixelsPerUnit * PxToUnit * scale);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 5;
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
        }
    }

    /// <summary>Mũi tên nhập làn ở làn phải, ngang đường giữa, chỉ lên.</summary>
    private void PlaceArrow(Transform root)
    {
        if (mergeArrow == null || !HasMiddle) return;
        var go = new GameObject("MergeArrow");
        go.transform.SetParent(root, false);
        go.transform.localPosition = ToLocal(new Vector2(A - mergeArrowInset, middleY - mergeArrowOffsetY));
        go.transform.localScale = Vector3.one * mergeArrowScale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = mergeArrow;
        sr.color = mergeArrowColor;
        sr.sortingOrder = 7;
        if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
        AddDashZone(go.transform, mergeArrow);
    }

    /// <summary>Barrier chặn làn trái ngay dưới đường giữa.</summary>
    private void PlaceBarrier()
    {
        if (barrier == null) return;
        bool on = HasMiddle;
        if (barrier.gameObject.activeSelf != on) barrier.gameObject.SetActive(on);
        if (!on) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.Undo.RecordObject(barrier, "Place Barrier");
#endif
        Vector3 w = ToWorld(new Vector2(-A + barrierInset, MidBot + barrierGap));
        barrier.position = new Vector3(w.x, barrier.position.y, w.z);
        // Tay chắn ngang hết làn trái → không vẽ vạch đứt ngay dưới/trên nó.
        // LoopOnly: vạch li ti chỗ barrier được phép chạy tới sát tay chắn.
        AddDashZonePx(new Vector2(-A + W * 0.5f, MidBot + barrierGap), new Vector2(W * 0.5f, barrierDashClearance), true);
    }

    // ================= LÀN (cho gameplay sau này) =================

    /// <summary>Tâm làn vòng ngoài (world), theo chiều kim đồng hồ trên màn hình.</summary>
    public List<Vector3> GetLoopLaneWorld()
    {
        var list = new List<Vector3>();
        foreach (Pt p in OuterPath()) list.Add(ToWorld(p.P - p.N * LaneCenter));
        return list;
    }

    // ================= HANDLE API (RoadNetworkBuilderEditor) =================

    public float CornerRadiusPx => R;
    public float RoadWidthPx => W;
    public float BottomOpeningPx => OpeningHalf * 2f;
    public bool HasMiddleRoad => HasMiddle;
    public float MiddleYPx => middleY;
    public float MiddleWidthPx => MW;
    public int Islands => IslandCount;

    public Vector3 Right => transform.TransformDirection(Vector3.right);
    public Vector3 Down => transform.TransformDirection(Vector3.down); // y px tăng = xuống màn hình

    /// <summary>Chấm bán kính góc: trên đường chéo góc trên-phải.</summary>
    public Vector3 GetCornerHandle()
    {
        Vector2 k = new Vector2(A, 0f), g = new Vector2(-1f, 1f).normalized;
        return ToWorld(k + g * R * (Mathf.Sqrt(2f) - 1f));
    }
    public Vector3 CornerHandleDir => transform.TransformDirection(new Vector3(-1f, -1f, 0f).normalized);
    public void SetCornerHandle(Vector3 world)
    {
        Vector2 p = FromWorld(world), g = new Vector2(-1f, 1f).normalized;
        float d = Vector2.Dot(p - new Vector2(A, 0f), g);
        cornerRadius = Mathf.Clamp(d / (Mathf.Sqrt(2f) - 1f), MinRadius, Mathf.Min(A, H * 0.5f));
    }

    /// <summary>Chấm bề rộng đường: mép đảo ở cạnh phải, ngang 1/4 chiều cao.</summary>
    public Vector3 GetRoadWidthHandle() => ToWorld(new Vector2(IslandX, H * 0.25f));
    public void SetRoadWidthHandle(Vector3 world) { roadWidth = A - FromWorld(world).x; roadWidth = W; }

    public Vector3 GetOpeningHandle(int side) => ToWorld(new Vector2(side * OpeningHalf, H - (OuterColEdge - OuterColCenter)));
    public void SetOpeningHandle(Vector3 world) { bottomOpeningWidth = 2f * Mathf.Abs(FromWorld(world).x); bottomOpeningWidth = 2f * OpeningHalf; }

    /// <summary>Chấm độ cong góc đảo ở ngã ba: trên đường chéo góc dưới-phải của đảo trên (vào trong đảo).</summary>
    public float JunctionRadiusPx => RJTop;
    public float LowerJunctionRadiusPx => RJBot;

    // Góc bo là Bezier bậc 2 (p0, góc nhọn c, p1) → điểm GIỮA cung nằm trên đường chéo, cách c = r·√2/4.
    private static readonly float MidArc = Mathf.Sqrt(2f) * 0.25f;

    /// <summary>Chấm ở GIỮA cung hất lên (góc dưới-phải đảo trên). Kéo chéo vào đảo = cong hơn.</summary>
    public Vector3 GetJunctionHandle() => ToWorld(new Vector2(IslandX, MidTop) + new Vector2(-1f, -1f).normalized * RJTop * MidArc);
    public Vector3 JunctionHandleDir => transform.TransformDirection(new Vector3(-1f, 1f, 0f).normalized);
    public void SetJunctionHandle(Vector3 world)
    {
        float d = Vector2.Dot(FromWorld(world) - new Vector2(IslandX, MidTop), new Vector2(-1f, -1f).normalized);
        junctionRadius = Mathf.Max(1f, d / MidArc);
        junctionRadius = RJTop;
    }

    /// <summary>Chấm bo đầu sừng đảo dưới (góc trên-phải): đặt ở giữa đường bo, kéo xuống = bo to hơn.</summary>
    public Vector3 GetLowerJunctionHandle()
    {
        float X = IslandX, rb = RJBot;
        Vector2 p0 = new(X - rb, BotEdgeY(X - rb)), c = new(X, BotEdgeY(X)), p1 = new(X, BotEdgeY(X) + rb);
        return ToWorld(0.25f * p0 + 0.5f * c + 0.25f * p1);
    }
    public Vector3 LowerJunctionHandleDir => transform.TransformDirection(Vector3.down);
    public void SetLowerJunctionHandle(Vector3 world)
    {
        // Giữa đường bo cách đầu sừng theo chiều dọc ≈ rb/4 (+ phần cong của mép) → lấy 4× khoảng dọc.
        float dy = FromWorld(world).y - BotEdgeY(IslandX);
        lowerJunctionRadius = Mathf.Max(1f, dy * 4f);
        lowerJunctionRadius = RJBot;
    }

    /// <summary>Chấm vị trí đường giữa: tâm đường, kéo dọc dời cả đường.</summary>
    public Vector3 GetMiddleHandle() => ToWorld(new Vector2(0f, middleY));
    public void SetMiddleHandle(Vector3 world) => middleY = FromWorld(world).y;

    /// <summary>Chấm bề rộng: mép dưới đường giữa, lệch phải.</summary>
    public Vector3 GetMiddleWidthHandle() => ToWorld(new Vector2(IslandX * 0.5f, MidBot));
    public void SetMiddleWidthHandle(Vector3 world) => middleWidth = Mathf.Max(150f, 2f * (FromWorld(world).y - middleY));

    /// <summary>Chấm đầu/cuối viền đảo. side -1 trái / 1 phải; first = điểm đầu.</summary>
    public Vector3 GetCurbHandle(int island, int side, bool first)
    {
        List<Pt> half = IslandHalf(island, out List<float> s);
        CurbRange(island, s[^1], out float a, out float b);
        Pt p = AtDistance(half, s, first ? a : b);
        Vector2 c = p.P + p.N * ((IslandO0 + IslandO1) * 0.5f);
        return ToWorld(new Vector2(side * c.x, c.y));
    }

    public float GetCurbValue(int island, bool first)
    {
        IslandHalf(island, out List<float> s);
        CurbRange(island, s[^1], out float a, out float b);
        return first ? a : b;
    }

    /// <summary>Chiếu vị trí kéo lên mép đảo (nửa phải) → quãng đường.</summary>
    public void SetCurbHandle(int island, int side, bool first, Vector3 world)
    {
        Vector2 q = FromWorld(world);
        q.x *= side;
        List<Pt> half = IslandHalf(island, out List<float> s);
        float best = float.MaxValue, bestS = 0f;
        for (int k = 1; k < half.Count; k++)
        {
            Vector2 a = half[k - 1].P, ab = half[k].P - a;
            float f = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
            float d = (a + ab * f - q).sqrMagnitude;
            if (d < best) { best = d; bestS = Mathf.Lerp(s[k - 1], s[k], f); }
        }
        CurbRange(island, s[^1], out float s0, out float s1);
        if (first) bestS = Mathf.Clamp(bestS, 0f, s1 - CurbMinLength);
        else bestS = Mathf.Clamp(bestS, s0 + CurbMinLength, s[^1]);

        if (island == 0) { if (first) topCurbStart = bestS; else topCurbEnd = bestS; }
        else { if (first) bottomCurbStart = bestS; else bottomCurbEnd = bestS; }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        List<Vector3> lane = GetLoopLaneWorld();
        for (int i = 0; i < lane.Count; i++) Gizmos.DrawLine(lane[i], lane[(i + 1) % lane.Count]);
    }
}
