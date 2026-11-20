using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Loại 1 ô lưới map.</summary>
public enum RoadCell : byte
{
    Empty = 0,
    Road = 1,
    Column = 2, // 1 ô xe trong cột. Các ô Column liền nhau theo chiều dọc = 1 cột.
    Exit = 3,   // ô đường; cạnh quay ra NGOÀI lưới có lối ra (hở viền, bề rộng = exitWidth).
}

/// <summary>Phía đầu cột (trên/dưới màn hình). None = không có (vd cột không nhận xe vào).</summary>
public enum ColumnSide : byte
{
    Top = 0,
    Bottom = 1,
    None = 2,
}

/// <summary>
/// Layout 1 map dựng bằng lưới (RoadMapWindow). 1 ô lưới = 1 ô xe.
/// Hệ toạ độ lưới: x sang phải, y xuống dưới, (0,0) = góc trên trái.
/// Tham số hình dáng (bo góc, bề rộng đường, lối ra, viền đảo) giống RoadLoop/RoadNetwork, chỉnh bằng chấm kéo trong Scene.
/// </summary>
[CreateAssetMenu(menuName = "Game/Road Map", fileName = "RoadMap")]
public class RoadMapSO : ScriptableObject
{
    [SerializeField, ReadOnly, Min(1)] private int width = 10;
    [SerializeField, ReadOnly, Min(1)] private int height = 10;
    [SerializeField, HideInInspector] private RoadCell[] cells;
    // Hướng cột theo từng ô Column: 0 = mặc định (vào trên, ra dưới); khác 0: 0x80 | entry | exit << 4.
    [SerializeField, HideInInspector] private byte[] flow;

    [BoxGroup("Hình dáng (px)"), LabelText("Cỡ 1 ô"), Tooltip("= 1 ô xe = bề rộng đường tối thiểu. Gốc: ô xe 521.")]
    [Min(100f)] public float cellSize = 520f;

    [BoxGroup("Hình dáng (px)"), LabelText("Bề rộng đường"), Tooltip("Chấm vàng. Lớn hơn cỡ ô → đường nở ra phía ngoài, đảo/cột giữ nguyên. Gốc: 510.")]
    [Min(100f)] public float roadWidth = 520f;

    [BoxGroup("Hình dáng (px)"), LabelText("Padding đảo phía ngoài"),
     Tooltip("Chấm tím. Cạnh đảo quay ra vòng đường ngoài nở thêm khoảng này (cột lùi xa góc cong); vòng ngoài dịch ra theo → bề rộng đường giữ nguyên. Cạnh giáp đường giữa không đổi.")]
    [Min(0f)] public float islandPadding = 0f;

    [BoxGroup("Hình dáng (px)"), LabelText("Bo góc ngoài"), Tooltip("Chấm cam. Góc đảo ở khúc cua tự đồng tâm (= bo ngoài − bề rộng đường). Gốc: 960.")]
    [Min(1f)] public float convexRadius = 960f;

    [FormerlySerializedAs("concaveRadius")]
    [BoxGroup("Hình dáng (px)"), LabelText("Bo góc ngã ba"), Tooltip("Chấm hồng. Góc đảo giáp đường giữa / ngã ba. Gốc: 450.")]
    [Min(1f)] public float junctionRadius = 450f;

    [BoxGroup("Hình dáng (px)"), LabelText("Lối ra"), Tooltip("Chấm xanh dương. Bề rộng khoảng hở viền ngoài ở ô Lối ra. Gốc: 600.")]
    [Min(0f)] public float exitWidth = 600f;

    [BoxGroup("Hình dáng (px)"), LabelText("Viền thò (phía đường giữa)"),
     Tooltip("Chấm xanh lá. Đầu viền đảo ở miệng cột quay vào đường giữa / ngã ba: kéo dài (+) / thu lại (−).")]
    public float curbExtend = 0f;

    [BoxGroup("Hình dáng (px)"), LabelText("Viền thò (phía vòng ngoài)"),
     Tooltip("Chấm xanh lá. Đầu viền đảo ở miệng cột quay ra vòng đường ngoài (chỗ góc cong): kéo dài (+) / thu lại (−). Độc lập với phía đường giữa.")]
    public float curbExtendRing = 0f;

    [BoxGroup("Hình dáng (px)"), LabelText("Khoảng đầu cột"),
     Tooltip("Như RoadLoop: ở mỗi miệng cột, đảo dài thêm khoảng này (giữa ô xe cuối và mép đường) — chỗ đặt mũi tên, xe đỗ không che. " +
             "Đường phía sau dịch ra theo, bề rộng đường không đổi. RoadLoop ≈ 200.")]
    [Min(0f)] public float columnEndMargin = 200f;

    [BoxGroup("Mũi tên cột (px)"), LabelText("Đầu cột cách đầu viền"),
     Tooltip("Như columnPaddingY của RoadLoop: đầu cột (đầu vạch chia) = điểm đầu/cuối viền đảo + khoảng này. Kéo viền (chấm xanh lá) → cột, vạch chia, mũi tên chạy theo.")]
    public float columnLinePadding = 40f;

    [BoxGroup("Mũi tên cột (px)"), LabelText("Mũi tên cách đầu cột"), Tooltip("Chấm trắng. Như RoadLoop: tâm mũi tên = đầu cột + khoảng này (thụt vào trong).")]
    public float arrowInset = 50f;

    [NonSerialized] private int version;
    /// <summary>Tăng mỗi khi dữ liệu đổi → builder biết để build lại.</summary>
    public int Version => version;
    public void Touch() => version++;

    public int Width => width;
    public int Height => height;

    private void OnValidate() => Touch();

    private void EnsureCells()
    {
        if (cells == null || cells.Length != width * height) cells = new RoadCell[width * height];
        if (flow == null || flow.Length != width * height) flow = new byte[width * height];
    }

    // ---------- Hướng cột ----------

    public (ColumnSide entry, ColumnSide exit) GetFlow(int x, int y)
    {
        if (!InBounds(x, y)) return (ColumnSide.Top, ColumnSide.Bottom);
        EnsureCells();
        byte f = flow[y * width + x];
        if ((f & 0x80) == 0) return (ColumnSide.Top, ColumnSide.Bottom);
        return ((ColumnSide)(f & 0x0F), (ColumnSide)((f >> 4) & 0x07));
    }

    private void SetFlowCell(int x, int y, ColumnSide entry, ColumnSide exit)
    {
        EnsureCells();
        flow[y * width + x] = (byte)(0x80 | (byte)entry | ((byte)exit << 4));
    }

    /// <summary>Đặt hướng cho cả cột chứa ô (x, y): xe vào từ phía entry, ra phía exit.</summary>
    public void SetColumnFlow(int x, int y, ColumnSide entry, ColumnSide exit)
    {
        if (Get(x, y) != RoadCell.Column) return;
        int y0 = y, y1 = y;
        while (Get(x, y0 - 1) == RoadCell.Column) y0--;
        while (Get(x, y1 + 1) == RoadCell.Column) y1++;
        for (int k = y0; k <= y1; k++) SetFlowCell(x, k, entry, exit);
        Touch();
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

    public RoadCell Get(int x, int y)
    {
        if (!InBounds(x, y)) return RoadCell.Empty;
        EnsureCells();
        return cells[y * width + x];
    }

    public void Set(int x, int y, RoadCell c)
    {
        if (!InBounds(x, y)) return;
        EnsureCells();
        cells[y * width + x] = c;
        flow[y * width + x] = 0;
        // Ô cột mới nối vào cột có sẵn → lấy hướng của cột đó.
        if (c == RoadCell.Column)
        {
            if (Get(x, y - 1) == RoadCell.Column) flow[y * width + x] = flow[(y - 1) * width + x];
            else if (Get(x, y + 1) == RoadCell.Column) flow[y * width + x] = flow[(y + 1) * width + x];
        }
        Touch();
    }

    public bool IsRoad(int x, int y)
    {
        RoadCell c = Get(x, y);
        return c == RoadCell.Road || c == RoadCell.Exit;
    }

    /// <summary>Đổi kích thước lưới, giữ nội dung neo góc trên trái.</summary>
    public void Resize(int w, int h)
    {
        w = Mathf.Max(1, w);
        h = Mathf.Max(1, h);
        EnsureCells();
        var n = new RoadCell[w * h];
        var nf = new byte[w * h];
        for (int y = 0; y < Mathf.Min(h, height); y++)
        for (int x = 0; x < Mathf.Min(w, width); x++)
        {
            n[y * w + x] = cells[y * width + x];
            nf[y * w + x] = flow[y * width + x];
        }
        width = w;
        height = h;
        cells = n;
        flow = nf;
        Touch();
    }

    public void Clear()
    {
        cells = new RoadCell[width * height];
        flow = new byte[width * height];
        Touch();
    }

    /// <summary>Ô trống nằm trong đảo (bị đường bao kín, không thông ra mép lưới) → vẽ dải viền trắng ở giữa.</summary>
    public bool[,] InteriorEmpty()
    {
        var reach = new bool[width, height];
        var q = new Queue<Vector2Int>();
        for (int x = 0; x < width; x++) { Seed(x, 0); Seed(x, height - 1); }
        for (int y = 0; y < height; y++) { Seed(0, y); Seed(width - 1, y); }
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            Seed(c.x + 1, c.y); Seed(c.x - 1, c.y); Seed(c.x, c.y + 1); Seed(c.x, c.y - 1);
        }
        var res = new bool[width, height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            res[x, y] = !reach[x, y] && Get(x, y) == RoadCell.Empty;
        return res;

        void Seed(int x, int y)
        {
            if (!InBounds(x, y) || reach[x, y] || IsRoad(x, y)) return;
            reach[x, y] = true;
            q.Enqueue(new Vector2Int(x, y));
        }
    }

    /// <summary>Các cột: dải ô Column liền nhau theo chiều dọc. Sắp theo x rồi y. Ô 0 = dưới cùng.</summary>
    public List<ColumnRun> GetColumns()
    {
        var l = new List<ColumnRun>();
        for (int x = 0; x < width; x++)
        {
            int y = height - 1;
            while (y >= 0)
            {
                if (Get(x, y) != RoadCell.Column) { y--; continue; }
                int bottom = y;
                while (y >= 0 && Get(x, y) == RoadCell.Column) y--;
                (ColumnSide entry, ColumnSide exit) = GetFlow(x, y + 1); // hướng lấy theo ô trên cùng của cột
                l.Add(new ColumnRun { x = x, bottomY = bottom, count = bottom - y, entry = entry, exit = exit });
            }
        }
        return l;
    }

    public struct ColumnRun
    {
        public int x, bottomY, count;
        public ColumnSide entry, exit; // xe vào từ phía entry (None = không nhận xe), ra phía exit
        public int TopY => bottomY - count + 1;
        /// <summary>Ô lưới của slot s (0 = dưới cùng).</summary>
        public Vector2Int Cell(int s) => new(x, bottomY - s);
    }
}
