using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gameplay chạy trên RoadMapBuilder + level JSON (LevelData). Luật giống ColumnTapController:
/// - Tap cột → xe đầu (sát miệng RA) chạy ra, chờ ở vạch sát làn, nhập làn khi trống; các xe sau dồn lên.
/// - Xe trên làn chạy ngược chiều kim đồng hồ (phải ↑, trên ←, trái ↓, dưới →) trên vòng ngoài.
/// - Đường trong (nối 2 bên vòng ngoài): đường ngang chạy trái → phải, đường dọc trên → dưới. Xe vòng ngoài rẽ vào khi
///   đường đó có cột nhận được màu của nó; có barrier (map bật "Barrier + mũi tên nhập làn") thì bắt buộc rẽ.
/// - Xe chạy qua miệng VÀO của 1 cột: cột còn chỗ và xe cuối cùng màu (hoặc cột trống) → rẽ vào đỗ.
/// - Cột đủ chỗ cùng 1 màu → biến mất (FX), cột trống lại.
/// - Khu dưới (lot): tap → xe đầu chạy lên qua lối ra của road, nhập làn dưới.
/// Đổi level lúc chạy: phím 1..9 hoặc nút góc trên trái.
/// </summary>
public class MapGameController : MonoBehaviour
{
    [BoxGroup("Level"), LabelText("Level JSON"), SerializeField] private List<TextAsset> levels = new();
    [BoxGroup("Level"), LabelText("Level bắt đầu"), SerializeField, Min(0)] private int startLevel;
    [BoxGroup("Level"), LabelText("Vị trí road khi chơi"), Tooltip("Road của level được dời tới đây (chỉ trong Play mode), các road khác tắt.")]
    [SerializeField] private Vector3 roadPosition = new(0f, 0f, 4.74f);
    [BoxGroup("Level"), LabelText("Tắt khi chơi"), Tooltip("Object cũ (BoardCars, LotCars, RoadLoop, RoadNetwork…) tắt trong Awake.")]
    [SerializeField] private List<GameObject> disableOnPlay = new();

    [BoxGroup("Khu dưới"), SerializeField] private ColumnAreaBuilder lot;
    [BoxGroup("Khu dưới"), LabelText("Sức chứa 1 cột"), SerializeField, Min(1)] private int lotCapacity = 3;
    [BoxGroup("Khu dưới"), LabelText("Khe giữa 2 xe (unit)"), SerializeField] private float lotSpacingY = 0.85f;
    [BoxGroup("Khu dưới"), LabelText("Lề lối ra (unit)"), SerializeField] private float lotOpeningMargin = 0.15f;

    [BoxGroup("Xe"), SerializeField, Required] private Mesh carMesh;                 // yedek.001
    [BoxGroup("Xe"), LabelText("Material xe"), Tooltip("CarColor-X, CarColorWindow-X, CarColorHeadlight-X, CarColorTaillight-X, CarWheel, CarBumper.")]
    [SerializeField] private List<Material> carMaterials = new();
    [BoxGroup("Xe"), LabelText("Scale mesh"), SerializeField] private float carScale = 172.73f;
    [BoxGroup("Xe"), LabelText("Độ cao tâm xe (y)"), SerializeField] private float carY = 0.6f;

    [BoxGroup("Obstacle"), LabelText("Barrier chắn miệng cột"), SerializeField] private GameObject blockerPrefab; // ColumnRoadBlocker
    [BoxGroup("Obstacle"), LabelText("Thanh chắn trên: lệch thêm (world)"),
     Tooltip("Cộng thêm vào vị trí thanh chắn ở miệng TRÊN (sau khi đặt mép trong cột). Chỉnh tay cho hợp mắt: (0, 0.19, 0.40).")]
    [SerializeField] private Vector3 topBlockerOffset = new(0f, 0.19f, 0.40f);
    [BoxGroup("Obstacle"), LabelText("Thanh chắn dưới: lệch thêm (world)"), SerializeField] private Vector3 bottomBlockerOffset = Vector3.zero;
    [BoxGroup("Obstacle"), LabelText("Nón"), SerializeField] private GameObject conePrefab;                     // ObstacleCone
    [BoxGroup("Obstacle"), LabelText("Dấu X"), SerializeField] private GameObject crossPrefab;                  // ObstacleCross

    [BoxGroup("Chạy"), LabelText("Tốc độ tối đa (unit/s)"), SerializeField] private float speed = 12f;
    [BoxGroup("Chạy"), LabelText("Phanh (unit/s²)"), SerializeField] private float brake = 60f;
    [BoxGroup("Chạy"), LabelText("Tốc độ xoay (độ/s)"), SerializeField] private float turnSpeed = 720f;
    [BoxGroup("Chạy"), LabelText("Bán kính cua"), SerializeField] private float turnRadius = 1.2f;
    [BoxGroup("Nhập làn"), LabelText("Vạch chờ cách mép làn (× rộng xe)"), SerializeField] private float holdGap = 0.15f;
    [BoxGroup("Nhập làn"), LabelText("Xe phía sau phải cách (× quãng nhập)"), SerializeField] private float mergeLookBehind = 1f;

    [BoxGroup("Dồn xe"), LabelText("Tốc độ tối đa"), SerializeField] private float shiftSpeed = 9f;
    [BoxGroup("Dồn xe"), LabelText("Tăng tốc"), SerializeField] private float shiftAccel = 45f;
    [BoxGroup("Dồn xe"), LabelText("Phanh"), SerializeField] private float shiftBrake = 35f;
    [BoxGroup("Dồn xe"), LabelText("Xe sau khởi động trễ (s)"), SerializeField] private float shiftStagger = 0.05f;
    [BoxGroup("Dồn xe"), LabelText("Khe với xe vừa ra (× dài xe)"), SerializeField] private float shiftFollowGap = 0.08f;
    [BoxGroup("Dồn xe"), LabelText("Chúi/ngửa thân (độ)"), SerializeField] private float shiftTilt = 4f;

    [BoxGroup("Cột đủ màu"), LabelText("FX biến mất"), SerializeField] private GameObject clearFx;
    [BoxGroup("Cột đủ màu"), SerializeField] private float clearDelay = 0.05f;
    [BoxGroup("Cột đủ màu"), SerializeField] private float clearStagger = 0.04f;
    [BoxGroup("Cột đủ màu"), SerializeField] private float clearShrinkTime = 0.16f;
    [BoxGroup("Cột đủ màu"), SerializeField] private float clearPop = 1.2f;
    [BoxGroup("Cột đủ màu"), SerializeField] private float clearHop = 0.35f;

    // ================= DỮ LIỆU CHẠY =================

    private class Lane
    {
        public List<Vector3> P;
        public float[] S;
        public float Len;
        public bool Closed, Forced;
        public int JoinIdx;                                    // đường trong: chạy hết thì nối vào vòng ngoài tại đây
        public bool[] OnRing;                                  // đường trong: điểm nằm sát làn vòng ngoài
        public readonly Dictionary<int, List<int>> EntryAt = new(); // điểm làn → các cột rẽ vào
        public readonly List<int> EntryCols = new();
        public Func<RoadCar, int, bool> OnPoint;
    }

    private class Column
    {
        public readonly List<Transform> Cars = new();   // 0 = xe đầu (sát miệng ra)
        public readonly List<string> Colors = new();
        public readonly List<RoadCar> Incoming = new();
        public readonly List<RoadCar> Leaving = new();
        public readonly List<Vector3> Slots = new();    // 0 = sát miệng ra
        public readonly Dictionary<Transform, float> ShiftV = new();
        public readonly Dictionary<Transform, float> ShiftPitch = new();
        public Coroutine Shift;
        public bool Clearing, IsLot;
        public float ExitSign = -1f;                     // hướng z xe rời cột
        public ColumnSide Entry = ColumnSide.Top, Exit = ColumnSide.Bottom;
        public float X;
        public int Index = -1;                           // chỉ số cột trong RoadMapBuilder
        public Lane ExitLane;
        public int ExitNear, ExitJoin;

        public int Capacity => Slots.Count;
        public int Reserved => Cars.Count + Incoming.Count;
        public bool FullOneColor
        {
            get
            {
                if (Capacity == 0 || Reserved < Capacity) return false;
                string c0 = Colors.Count > 0 ? Colors[0] : Incoming[0].ColorKey;
                foreach (string c in Colors) if (c != c0) return false;
                foreach (RoadCar rc in Incoming) if (rc.ColorKey != c0) return false;
                return true;
            }
        }
        public bool Locked => Clearing || FullOneColor;
        public string TailColor => Incoming.Count > 0 ? Incoming[^1].ColorKey : Colors.Count > 0 ? Colors[^1] : null;
    }

    private RoadMapBuilder _road;
    private readonly List<Lane> _lanes = new();        // 0 = vòng ngoài
    private readonly Dictionary<int, Lane> _branchAt = new(); // điểm vòng ngoài → đường trong bắt đầu ở đó
    private readonly List<Column> _cols = new();
    private readonly List<Column> _lotCols = new();
    private Transform _carRoot;
    private float _carLen = 2f, _carWidth = 1.8f;
    private int _level = -1;
    private readonly Dictionary<string, Material> _mats = new();

    private Lane Ring => _lanes.Count > 0 ? _lanes[0] : null;

    // ================= KHỞI TẠO =================

    private void Awake()
    {
        foreach (GameObject g in disableOnPlay) if (g != null) g.SetActive(false);
        foreach (Material m in carMaterials) if (m != null) _mats[m.name] = m;
    }

    private void Start() => LoadLevel(startLevel);

    private void ClearLevel()
    {
        StopAllCoroutines();
        if (_carRoot != null) Destroy(_carRoot.gameObject);
        RoadCar.Moving.Clear();
        _lanes.Clear();
        _branchAt.Clear();
        _cols.Clear();
        _lotCols.Clear();
    }

    public void LoadLevel(int index)
    {
        if (levels.Count == 0) return;
        index = Mathf.Clamp(index, 0, levels.Count - 1);
        ClearLevel();
        _level = index;

        LevelData data = JsonUtility.FromJson<LevelData>(levels[index].text);
        _road = null;
        foreach (RoadMapBuilder b in FindObjectsByType<RoadMapBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.Map != null && b.Map.name == data.road) _road = b;
        if (_road == null) { Debug.LogError($"[MapGame] Không thấy RoadMapBuilder có map '{data.road}' trong scene."); return; }
        foreach (RoadMapBuilder b in FindObjectsByType<RoadMapBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            b.gameObject.SetActive(b == _road);
        _road.transform.position = roadPosition;
        _road.EnsureBuilt();

        _carRoot = new GameObject("LevelCars").transform;
        _carRoot.SetParent(transform, false);
        MeasureCar();
        BuildLanes();
        BuildColumns(data);
        BuildLot(data);
        BuildEntriesAndExits();
    }

    private void MeasureCar()
    {
        Transform t = MakeCarObject("Measure", Vector3.zero, Vector3.back, _carRoot);
        Renderer r = t.GetComponentInChildren<Renderer>();
        _carLen = r.bounds.size.z;
        _carWidth = r.bounds.size.x;
        DestroyImmediate(t.gameObject);
    }

    /// <summary>Pivot ở tâm xe, forward = mũi xe; con "Mesh" là mesh xe (gốc xoay (270,180,0) = mũi xuống màn hình).</summary>
    private Transform MakeCarObject(string color, Vector3 pos, Vector3 nose, Transform parent)
    {
        var pivot = new GameObject($"Car-{color}").transform;
        pivot.SetParent(parent, false);
        pivot.SetPositionAndRotation(pos, Quaternion.LookRotation(nose, Vector3.up));
        var mesh = new GameObject("Mesh");
        mesh.transform.SetParent(pivot, false);
        mesh.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(Vector3.back)) * Quaternion.Euler(270f, 180f, 0f);
        mesh.transform.localScale = Vector3.one * carScale;
        mesh.AddComponent<MeshFilter>().sharedMesh = carMesh;
        var mr = mesh.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[]
        {
            Mat("CarColor-" + color), Mat("CarColorWindow-" + color), Mat("CarColorHeadlight-" + color),
            Mat("CarColorTaillight-" + color), Mat("CarWheel"), Mat("CarBumper"),
        };
        mesh.transform.position -= mr.bounds.center - pos; // tâm xe trùng pivot
        return pivot;
    }

    private Material Mat(string n) => _mats.TryGetValue(n, out Material m) ? m : null;

    private Transform SpawnCar(string color, Vector3 pos, Vector3 nose)
    {
        Transform t = MakeCarObject(color, pos, nose, _carRoot);
        t.gameObject.AddComponent<RoadCar>().Setup(color, speed, turnSpeed, brake, _carLen, _carWidth);
        return t;
    }

    private void BuildLanes()
    {
        foreach (RoadMapBuilder.LaneInfo info in _road.GetLanesWorld())
        {
            var lane = new Lane { P = info.Points, Closed = info.Closed, Forced = info.Forced };
            for (int i = 0; i < lane.P.Count; i++) lane.P[i] = new Vector3(lane.P[i].x, carY, lane.P[i].z);
            if (lane.Closed && SignedArea(lane.P) < 0f) lane.P.Reverse(); // ngược chiều kim đồng hồ trên màn hình
            lane.S = new float[lane.P.Count];
            for (int i = 0; i < lane.P.Count; i++)
            {
                lane.S[i] = lane.Len;
                if (i + 1 < lane.P.Count || lane.Closed) lane.Len += Vector3.Distance(lane.P[i], lane.P[(i + 1) % lane.P.Count]);
            }
            Lane l = lane;
            lane.OnPoint = (rc, i) => OnLanePoint(l, rc, i);
            _lanes.Add(lane);
        }

        // Đường trong: điểm rẽ trên vòng ngoài (gần điểm đầu) + điểm nhập lại (sau điểm cuối).
        for (int k = 1; k < _lanes.Count; k++)
        {
            Lane l = _lanes[k];
            l.OnRing = new bool[l.P.Count];
            for (int i = 0; i < l.P.Count; i++) l.OnRing[i] = (Ring.P[Nearest(Ring, l.P[i])] - l.P[i]).sqrMagnitude < Sq(_carWidth * 0.3f);
            int b = Nearest(Ring, l.P[0]);
            _branchAt[b] = l;
            l.JoinIdx = (Nearest(Ring, l.P[^1]) + 1) % Ring.P.Count;
        }
    }

    private static float SignedArea(List<Vector3> p)
    {
        float a = 0f;
        for (int i = 0; i < p.Count; i++) { Vector3 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.z - v.x * u.z; }
        return a * 0.5f;
    }

    private static int Nearest(Lane l, Vector3 p)
    {
        int best = 0;
        float d = float.MaxValue;
        for (int i = 0; i < l.P.Count; i++)
        {
            float e = (l.P[i] - p).sqrMagnitude;
            if (e < d) { d = e; best = i; }
        }
        return best;
    }

    private void BuildColumns(LevelData data)
    {
        for (int i = 0; i < _road.ColumnCount; i++)
        {
            var col = new Column { Entry = _road.ColumnEntry(i), Exit = _road.ColumnExit(i), Index = i };
            bool exitTop = col.Exit == ColumnSide.Top;
            col.ExitSign = exitTop ? 1f : -1f;
            int n = _road.SlotsInColumn(i);
            for (int s = 0; s < n; s++)
            {
                Vector3 p = _road.GetSlotCenterWorld(i, exitTop ? n - 1 - s : s);
                col.Slots.Add(new Vector3(p.x, carY, p.z));
            }
            col.X = col.Slots.Count > 0 ? col.Slots[0].x : 0f;
            _cols.Add(col);
            SetColumnArrows(col, true); // level trước có thể đã ẩn
        }

        foreach (LevelData.ColumnData cd in data.columns)
        {
            int ci = _road.FindColumn(cd.x, cd.y);
            if (ci < 0) { Debug.LogWarning($"[MapGame] Ô ({cd.x},{cd.y}) không thuộc cột nào của {data.road}."); continue; }
            Column col = _cols[ci];
            if (cd.blocked)
            {
                BlockColumn(col);
                continue;
            }
            foreach (string color in cd.cars)
            {
                if (string.IsNullOrEmpty(color)) continue;
                if (col.Cars.Count >= col.Capacity) { Debug.LogWarning($"[MapGame] Cột ({cd.x},{cd.y}) quá {col.Capacity} xe."); break; }
                Transform t = SpawnCar(color, col.Slots[col.Cars.Count], Vector3.forward * col.ExitSign);
                col.Cars.Add(t);
                col.Colors.Add(color);
            }
        }
    }

    /// <summary>
    /// Cột bị chặn: không vào / không ra (bỏ miệng → không có điểm rẽ, tap không có tác dụng).
    /// Hình: barrier sọc vàng đen chắn 2 miệng, ô xen kẽ dấu X / nón (sprite gốc, quay về camera như game gốc).
    /// </summary>
    private void BlockColumn(Column col)
    {
        col.Entry = ColumnSide.None;
        col.Exit = ColumnSide.None;
        if (col.Slots.Count == 0) return;
        var root = new GameObject("BlockedColumn").transform;
        root.SetParent(_carRoot, false);
        // Thanh chắn: nằm trong cột, mép ngoài trùng mép đảo ở miệng (như game gốc).
        Place(blockerPrefab, _road.GetColumnMouthWorld(col.Index, true), 1, topBlockerOffset);
        Place(blockerPrefab, _road.GetColumnMouthWorld(col.Index, false), -1, bottomBlockerOffset);
        // Nón / X: giữa từng ô.
        for (int i = 0; i < col.Slots.Count; i++)
            Place(i % 2 == 0 ? crossPrefab : conePrefab, col.Slots[i], 0, Vector3.zero);
        SetColumnArrows(col, false);

        // align: 0 = tâm hình vào at; +1 = mép trên hình chạm at (nằm dưới at); −1 = mép dưới hình chạm at.
        void Place(GameObject prefab, Vector3 at, int align, Vector3 extra)
        {
            if (prefab == null) return;
            GameObject go = Instantiate(prefab, root);
            float y0 = _road.transform.position.y;
            go.transform.position = new Vector3(go.transform.position.x, y0 + 0.05f, go.transform.position.z);
            // Sprite nghiêng về camera → vị trí nhìn trên màn hình = điểm chiếu dọc hướng nhìn xuống mặt đường.
            Camera cam = Camera.main;
            Vector3 f = cam != null && cam.transform.forward.y < -0.01f ? cam.transform.forward : Vector3.down;
            float minZ = float.MaxValue, maxZ = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (SpriteRenderer r in go.GetComponentsInChildren<SpriteRenderer>())
            {
                Bounds b = r.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var p = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 g = p + f * ((p.y - y0) / -f.y);
                    minZ = Mathf.Min(minZ, g.z); maxZ = Mathf.Max(maxZ, g.z);
                    minX = Mathf.Min(minX, g.x); maxX = Mathf.Max(maxX, g.x);
                }
            }
            if (minZ > maxZ) return;
            float refZ = align > 0 ? maxZ : align < 0 ? minZ : (minZ + maxZ) * 0.5f;
            go.transform.position += new Vector3(at.x - (minX + maxX) * 0.5f, 0f, at.z - refZ) + extra;
        }
    }

    /// <summary>Bật/tắt tam giác (mũi tên vào/ra) của cột — sprite "Arrow-{x}-{yTrên}-In/Out" do RoadMapBuilder dựng.</summary>
    private void SetColumnArrows(Column col, bool on)
    {
        if (col.Index < 0) return;
        int n = _road.SlotsInColumn(col.Index);
        Vector2Int top = _road.ColumnCell(col.Index, n - 1);
        string prefix = $"Arrow-{top.x}-{top.y}-";
        foreach (SpriteRenderer sr in _road.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.name.StartsWith(prefix)) sr.gameObject.SetActive(on);
    }

    private void BuildLot(LevelData data)
    {
        if (lot == null) return;
        float zTop = lot.transform.position.z;
        for (int c = 0; c < lot.ColumnCount; c++)
        {
            var col = new Column { IsLot = true, ExitSign = 1f, Exit = ColumnSide.Top, Entry = ColumnSide.None };
            float x = lot.GetColumnCenterWorld(c).x;
            col.X = x;
            for (int i = 0; i < lotCapacity; i++)
                col.Slots.Add(new Vector3(x, carY, zTop - i * (_carLen + lotSpacingY) - _carLen * 0.5f));
            if (data.lot != null && c < data.lot.Count)
                foreach (string color in data.lot[c].cars)
                {
                    if (string.IsNullOrEmpty(color) || col.Cars.Count >= col.Capacity) continue;
                    col.Cars.Add(SpawnCar(color, col.Slots[col.Cars.Count], Vector3.forward));
                    col.Colors.Add(color);
                }
            _lotCols.Add(col);
        }
    }

    /// <summary>
    /// Miệng VÀO: điểm làn gần miệng nhất (phía ngoài cột) → lùi R ngược chiều chạy = điểm bắt đầu cua.
    /// Miệng RA: điểm làn gần miệng nhất → tiến R theo chiều chạy = điểm nhập làn.
    /// </summary>
    private void BuildEntriesAndExits()
    {
        for (int c = 0; c < _cols.Count; c++)
        {
            Column col = _cols[c];
            if (col.Slots.Count == 0) continue;

            if (col.Entry != ColumnSide.None)
            {
                float outward = col.Entry == ColumnSide.Top ? 1f : -1f;
                if (FindMouthLane(col, outward, out Lane l, out int j))
                {
                    // Đoạn nhận xe: từ điểm cua (lùi R trước miệng) tới sát miệng (còn ≥ 0.4R) — xe vừa nhập làn
                    // giữa đoạn này vẫn rẽ vào được ngay, không phải chạy hết vòng.
                    var idxs = new List<int>();
                    int i = j;
                    float acc = 0f;
                    while (acc < turnRadius)
                    {
                        int prev = l.Closed ? (i - 1 + l.P.Count) % l.P.Count : i - 1;
                        if (prev < 0) break;
                        acc += Vector3.Distance(l.P[prev], l.P[i]);
                        i = prev;
                        if (acc >= turnRadius * 0.4f) idxs.Add(i);
                    }
                    if (idxs.Count == 0) idxs.Add(i);
                    foreach (int k in idxs)
                    {
                        if (!l.EntryAt.TryGetValue(k, out List<int> list)) l.EntryAt[k] = list = new List<int>();
                        list.Add(c);
                    }
                    l.EntryCols.Add(c);
                }
            }

            if (col.Exit != ColumnSide.None)
            {
                float outward = col.ExitSign;
                if (FindMouthLane(col, outward, out Lane l, out int j))
                {
                    int k = j;
                    for (float acc = 0f; acc < turnRadius;)
                    {
                        int next = l.Closed ? (k + 1) % l.P.Count : k + 1;
                        if (next >= l.P.Count) break;
                        acc += Vector3.Distance(l.P[k], l.P[next]);
                        k = next;
                    }
                    col.ExitLane = l;
                    col.ExitNear = j;
                    col.ExitJoin = k;
                }
            }
        }
    }

    /// <summary>Điểm làn gần miệng cột nhất, nằm phía ngoài cột theo hướng outward (±z).</summary>
    private bool FindMouthLane(Column col, float outward, out Lane lane, out int idx)
    {
        float zMouth = outward > 0f ? Max(col.Slots) + _carLen * 0.5f : Min(col.Slots) - _carLen * 0.5f;
        var mouth = new Vector3(col.X, carY, zMouth);
        lane = null;
        idx = -1;
        float best = float.MaxValue;
        foreach (Lane l in _lanes)
            for (int i = 0; i < l.P.Count; i++)
            {
                Vector3 p = l.P[i];
                if ((p.z - zMouth) * outward <= 0f) continue;
                if (!l.Closed && l.OnRing[i]) continue; // đoạn đầu/cuối đường trong trùng vòng ngoài → thuộc vòng ngoài
                // Ưu tiên điểm thẳng trước miệng (lệch ngang phạt nặng) — cột sát góc không bị bắt nhầm làn bên.
                float dx = p.x - mouth.x, dz = p.z - mouth.z;
                float d = dx * dx * 16f + dz * dz;
                if (d < best) { best = d; lane = l; idx = i; }
            }
        return lane != null;
    }

    private static float Max(List<Vector3> s) { float m = float.MinValue; foreach (Vector3 p in s) m = Mathf.Max(m, p.z); return m; }
    private static float Min(List<Vector3> s) { float m = float.MaxValue; foreach (Vector3 p in s) m = Mathf.Min(m, p.z); return m; }

    // ================= INPUT =================

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null)
            for (int i = 0; i < Mathf.Min(9, levels.Count); i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) { LoadLevel(i); return; }

        Pointer p = Pointer.current;
        if (p == null || !p.press.wasPressedThisFrame || _road == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(p.position.ReadValue());
        if (!new Plane(Vector3.up, new Vector3(0f, _road.transform.position.y, 0f)).Raycast(ray, out float enter)) return;
        Vector3 hit = ray.GetPoint(enter);

        int c = ColumnAt(_cols, hit, _road.SlotSizeWorld.x * 0.55f);
        if (c >= 0) { Launch(_cols[c]); return; }
        if (lot != null)
        {
            c = ColumnAt(_lotCols, hit, (lot.ColumnSize.x + lot.ColumnSpacingX) * 0.5f);
            if (c >= 0) LaunchFromLot(_lotCols[c]);
        }
    }

    private void OnGUI()
    {
        if (levels.Count <= 1) return;
        GUILayout.BeginArea(new Rect(10, 10, 260, 40 * levels.Count));
        for (int i = 0; i < levels.Count; i++)
        {
            string label = $"{i + 1}. {(levels[i] != null ? levels[i].name : "?")}{(i == _level ? "  ◄" : "")}";
            if (GUILayout.Button(label, GUILayout.Height(34))) LoadLevel(i);
        }
        GUILayout.EndArea();
    }

    private int ColumnAt(List<Column> cols, Vector3 w, float halfW)
    {
        for (int c = 0; c < cols.Count; c++)
        {
            Column col = cols[c];
            if (col.Slots.Count == 0) continue;
            float z0 = Min(col.Slots) - _carLen * 0.75f, z1 = Max(col.Slots) + _carLen * 0.75f;
            if (Mathf.Abs(w.x - col.X) <= halfW && w.z >= z0 && w.z <= z1) return c;
        }
        return -1;
    }

    // ================= RA KHỎI CỘT =================

    private void Launch(Column col)
    {
        if (col.Cars.Count == 0 || col.Locked || col.ExitLane == null) return;
        Transform pivot = col.Cars[0];
        col.Cars.RemoveAt(0);
        col.Colors.RemoveAt(0);

        var car = pivot.GetComponent<RoadCar>();
        Lane l = col.ExitLane;
        if (IsExitOnly(l))
        {
            // Đường trong chỉ để ra → rẽ về đầu GẦN hơn (trái hoặc phải), chờ ở vạch sát vòng ngoài rồi nhập theo chiều chạy.
            bool toStart = LaneDist(l, 0, col.ExitNear) < LaneDist(l, col.ExitNear, l.P.Count - 1);
            List<Vector3> back = BuildBackExitPath(col, pivot.position, toStart, out int bHold, out float bLen, out int ringJoin);
            Lane ring = Ring;
            car.Follow(back, RoadCar.State.Exiting, rc => StartOnLane(rc, ring, ringJoin), bHold, rc => CanMerge(rc, ring, ringJoin, bLen));
        }
        else
        {
            List<Vector3> path = BuildExitPath(col, pivot.position, out int holdIdx, out float mergeLen);
            int join = col.ExitJoin;
            car.Follow(path, RoadCar.State.Exiting, rc => StartOnLane(rc, l, join), holdIdx, rc => CanMerge(rc, l, join, mergeLen));
        }
        col.Leaving.Add(car);
        StartShift(col);
    }

    /// <summary>Đường trong không có cột nào lấy xe từ nó và không bị barrier ép → chỉ xe ra cột chạy → cho đi 2 chiều.</summary>
    private static bool IsExitOnly(Lane l) => !l.Closed && !l.Forced && l.EntryCols.Count == 0;

    /// <summary>
    /// Ra cột rồi rẽ về phía ĐẦU đường trong (ngược chiều vẽ), chạy tới sát vòng ngoài, chờ ở vạch rồi cua nhập vòng ngoài
    /// theo chiều chạy (vd cột bên trái: rẽ trái → nhập làn trái đang chạy xuống).
    /// </summary>
    private List<Vector3> BuildBackExitPath(Column col, Vector3 start, bool toStart, out int holdIdx, out float mergeLen, out int ringJoin)
    {
        float d = col.ExitSign, R = turnRadius;
        Lane l = col.ExitLane;
        Lane ring = Ring;
        float laneZ = l.P[col.ExitNear].z;
        float xr = (toStart ? l.P[0] : l.P[^1]).x;          // đầu đường trong = trên làn vòng ngoài
        float dirX = Mathf.Sign(xr - col.X);
        var path = new List<Vector3> { start };
        var a = new Vector3(col.X, carY, laneZ - d * R);
        if ((a.z - start.z) * d > 0f) path.Add(a);
        AddQuadBezier(path, a, new Vector3(col.X, carY, laneZ), new Vector3(col.X + dirX * R, carY, laneZ), 10);
        var c = new Vector3(xr - dirX * R, carY, laneZ);
        if ((c.x - path[^1].x) * dirX > 0f) path.Add(c);
        else c = path[^1];
        holdIdx = path.Count - 1;

        int n = ring.P.Count, j = Nearest(ring, new Vector3(xr, carY, laneZ));
        for (float acc = 0f; acc < R; j = (j + 1) % n) acc += Vector3.Distance(ring.P[j], ring.P[(j + 1) % n]);
        AddQuadBezier(path, c, new Vector3(xr, carY, laneZ), ring.P[j], 10);
        ringJoin = j;
        mergeLen = 0f;
        for (int i = holdIdx + 1; i < path.Count; i++) mergeLen += Vector3.Distance(path[i - 1], path[i]);
        return path;
    }

    /// <summary>Thẳng ra miệng tới vạch chờ (mũi xe sát mép làn) → cua vào làn theo chiều chạy.</summary>
    private List<Vector3> BuildExitPath(Column col, Vector3 start, out int holdIdx, out float mergeLen)
    {
        float d = col.ExitSign, R = turnRadius;
        Lane l = col.ExitLane;
        float laneZ = l.P[col.ExitNear].z;
        var path = new List<Vector3> { start };
        float holdZ = laneZ - d * (_carWidth * (0.5f + holdGap) + _carLen * 0.5f);
        if ((holdZ - start.z) * d < 0f) holdZ = start.z;
        holdIdx = path.Count;
        path.Add(new Vector3(col.X, carY, holdZ));
        var a = new Vector3(col.X, carY, laneZ - d * R);
        if ((a.z - holdZ) * d > 0f) path.Add(a);
        AddQuadBezier(path, a, new Vector3(col.X, carY, laneZ), l.P[col.ExitJoin], 10);
        mergeLen = 0f;
        for (int i = holdIdx + 1; i < path.Count; i++) mergeLen += Vector3.Distance(path[i - 1], path[i]);
        return path;
    }

    private void StartOnLane(RoadCar car, Lane l, int idx)
    {
        int next = l.Closed ? (idx + 1) % l.P.Count : Mathf.Min(idx + 1, l.P.Count - 1);
        car.Loop(l.P, next, l.OnPoint);
    }

    private void LaunchFromLot(Column col)
    {
        if (col.Cars.Count == 0 || Ring == null) return;
        if (!_road.GetExitOpeningWorld(out Vector3 openC, out float openHalf)) return;
        Transform pivot = col.Cars[0];
        col.Cars.RemoveAt(0);
        col.Colors.RemoveAt(0);

        var car = pivot.GetComponent<RoadCar>();
        List<Vector3> path = BuildLotExitPath(col, pivot.position, openC, openHalf, out int join, out int holdIdx, out float mergeLen);
        Lane ring = Ring;
        car.Follow(path, RoadCar.State.Exiting, rc => StartOnLane(rc, ring, join), holdIdx, rc => CanMerge(rc, ring, join, mergeLen));
        col.Leaving.Add(car);
        StartShift(col);
    }

    /// <summary>Lên thẳng → lượn S vào trong lối ra → vạch chờ dưới làn dưới → cua phải vào làn.</summary>
    private List<Vector3> BuildLotExitPath(Column col, Vector3 start, Vector3 openC, float openHalf,
        out int joinIdx, out int holdIdx, out float mergeLen)
    {
        float y = carY, R = turnRadius;
        Lane ring = Ring;
        int n = ring.P.Count;
        var path = new List<Vector3> { start };
        float lim = Mathf.Max(0f, openHalf - _carWidth * 0.5f - lotOpeningMargin);
        float xt = Mathf.Clamp(start.x, openC.x - lim, openC.x + lim);
        if (!Mathf.Approximately(xt, start.x))
        {
            float bestD = float.MaxValue;
            foreach (Column lc in _lotCols)
            {
                if (Mathf.Abs(lc.X - openC.x) > lim) continue;
                float dd = Mathf.Abs(lc.X - start.x);
                if (dd < bestD) { bestD = dd; xt = lc.X; }
            }
        }

        int k0 = Nearest(ring, new Vector3(xt, y, openC.z));
        float zBottom = ring.P[k0].z;
        float holdZ = zBottom - _carWidth * (0.5f + holdGap) - _carLen * 0.5f;
        float zOut = Mathf.Min(lot.transform.position.z + _carLen * 0.5f + 0.1f, holdZ);
        if (zOut > start.z) path.Add(new Vector3(start.x, y, zOut));
        else zOut = start.z;
        if (Mathf.Abs(xt - start.x) > 0.01f && holdZ > zOut)
        {
            float zm = (zOut + holdZ) * 0.5f;
            AddCubicBezier(path, new Vector3(start.x, y, zOut), new Vector3(start.x, y, zm), new Vector3(xt, y, zm), new Vector3(xt, y, holdZ), 12);
        }
        else path.Add(new Vector3(xt, y, Mathf.Max(holdZ, zOut)));
        holdIdx = path.Count - 1;

        int j = k0;
        for (float acc = 0f; acc < R; j = (j + 1) % n) acc += Vector3.Distance(ring.P[j], ring.P[(j + 1) % n]);
        var a = new Vector3(xt, y, zBottom - R);
        if (a.z > path[^1].z) path.Add(a);
        AddQuadBezier(path, a, new Vector3(xt, y, zBottom), ring.P[j], 10);
        joinIdx = j;
        mergeLen = 0f;
        for (int i = holdIdx + 1; i < path.Count; i++) mergeLen += Vector3.Distance(path[i - 1], path[i]);
        return path;
    }

    /// <summary>Quãng theo chiều chạy từ a tới b trên làn (∞ nếu làn hở và b ở phía sau a).</summary>
    private static float LaneDist(Lane l, int a, int b)
    {
        float d = l.S[b] - l.S[a];
        if (d >= 0f) return d;
        return l.Closed ? d + l.Len : float.PositiveInfinity;
    }

    private bool CanMerge(RoadCar me, Lane l, int joinIdx, float mergeLen)
    {
        Vector3 joinPos = l.P[joinIdx];
        float need = mergeLen * mergeLookBehind;
        foreach (RoadCar o in RoadCar.Moving)
        {
            if (o == me) continue;
            if (o.Mode == RoadCar.State.Looping && o.CurrentLane == l.P)
            {
                float behind = LaneDist(l, o.LaneIndex, joinIdx);
                float ahead = LaneDist(l, joinIdx, o.LaneIndex);
                if (ahead < o.SafeDist) return false;
                if (behind < need + o.SafeDist) return false;
            }
            else if (o.Mode == RoadCar.State.Looping)
            {
                // Xe ở làn khác (vd đường trong sắp nhập vào vòng ngoài): gần điểm nhập thì chờ.
                if ((o.transform.position - joinPos).sqrMagnitude < Sq(need + 2f * o.SafeDist)) return false;
            }
            else if ((o.transform.position - joinPos).sqrMagnitude < Sq(o.SafeDist)) return false;
        }
        return true;
    }

    private static float Sq(float v) => v * v;

    // ================= TRÊN LÀN =================

    private bool CanPark(Column col, string color)
    {
        if (col.Clearing || col.Reserved >= col.Capacity) return false;
        string tail = col.TailColor;
        if (tail == color) return true;
        // Cột trống = dự phòng: chỉ vào khi không còn cột nào đang chờ đúng màu này
        // (tránh xe vừa ra lại chui về cột trống cũ / cột trống gần nhất, mãi không tới được cột cùng màu).
        return tail == null && !HasMatchingColumn(color);
    }

    private bool HasMatchingColumn(string color)
    {
        foreach (Column c in _cols)
            if (c.Entry != ColumnSide.None && !c.Clearing && c.Reserved < c.Capacity && c.TailColor == color) return true;
        return false;
    }

    private bool OnLanePoint(Lane lane, RoadCar car, int idx)
    {
        // 1) Rẽ vào cột.
        if (lane.EntryAt.TryGetValue(idx, out List<int> cols))
            foreach (int c in cols)
                if (CanPark(_cols[c], car.ColorKey)) { Enter(lane, idx, c, car); return true; }

        // 2) Vòng ngoài → đường trong.
        if (lane == Ring && _branchAt.TryGetValue(idx, out Lane inner))
        {
            bool useful = false;
            foreach (int c in inner.EntryCols) if (CanPark(_cols[c], car.ColorKey)) { useful = true; break; }
            if (inner.Forced || useful) { car.Loop(inner.P, Mathf.Min(1, inner.P.Count - 1), inner.OnPoint); return true; }
        }

        // 3) Hết đường trong → nhập lại vòng ngoài.
        if (!lane.Closed && idx >= lane.P.Count - 1)
        {
            car.Loop(Ring.P, lane.JoinIdx, Ring.OnPoint);
            return true;
        }
        return false;
    }

    private void Enter(Lane lane, int idx, int c, RoadCar car)
    {
        Column col = _cols[c];
        int slot = col.Reserved;
        col.Incoming.Add(car);

        float inward = col.Entry == ColumnSide.Top ? -1f : 1f, R = turnRadius;
        float laneZ = FindMouthLane(col, -inward, out Lane l, out int j) ? l.P[j].z : lane.P[idx].z;
        Vector3 p0 = lane.P[idx];
        var path = new List<Vector3> { p0 };
        AddQuadBezier(path, p0, new Vector3(col.X, carY, laneZ), new Vector3(col.X, carY, laneZ + inward * R), 10);
        Vector3 target = col.Slots[Mathf.Min(slot, col.Slots.Count - 1)];
        path.Add(new Vector3(col.X, carY, target.z));
        car.Follow(path, RoadCar.State.Entering, rc => Park(col, rc));
    }

    private void Park(Column col, RoadCar car)
    {
        col.Incoming.Remove(car);
        col.Cars.Add(car.transform);
        col.Colors.Add(car.ColorKey);
        car.Park();
        car.transform.rotation = Quaternion.LookRotation(Vector3.forward * col.ExitSign, Vector3.up);
        StartShift(col);
    }

    // ================= DỒN XE (như ColumnTapController) =================

    private void StartShift(Column col)
    {
        if (col.Shift != null) StopCoroutine(col.Shift);
        col.Shift = StartCoroutine(ShiftTo(col));
    }

    private IEnumerator ShiftTo(Column col)
    {
        float sign = col.ExitSign;
        float S(Vector3 p) => p.z * sign;
        Quaternion baseRot = Quaternion.LookRotation(Vector3.forward * sign, Vector3.up);
        float followDist = _carLen * (1f + shiftFollowGap);
        float exitedAt = col.Slots.Count > 0 ? S(col.Slots[0]) + _carLen * 1.05f : 0f;
        float elapsed = 0f;
        while (true)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            col.Leaving.RemoveAll(rc => rc == null || rc.Mode != RoadCar.State.Exiting || S(rc.transform.position) > exitedAt);
            float frontLimit = float.PositiveInfinity;
            foreach (RoadCar rc in col.Leaving) frontLimit = Mathf.Min(frontLimit, S(rc.transform.position) - followDist);

            bool busy = col.Leaving.Count > 0;
            int n = Mathf.Min(col.Cars.Count, col.Slots.Count);
            for (int i = 0; i < n; i++)
            {
                Transform car = col.Cars[i];
                Vector3 p = car.position;
                float s = S(p), target = S(col.Slots[i]);
                float limit = i == 0 ? frontLimit : S(col.Cars[i - 1].position) - (S(col.Slots[i - 1]) - target);
                float goal = Mathf.Min(target, limit);
                float dist = goal - s;

                col.ShiftV.TryGetValue(car, out float v);
                float v0 = v;
                bool started = v > 0f || elapsed >= i * shiftStagger;
                if (dist <= 1e-4f || !started) v = 0f;
                else v = Mathf.Min(v + shiftAccel * dt, shiftSpeed, Mathf.Sqrt(2f * shiftBrake * dist));
                float step = Mathf.Min(v * dt, Mathf.Max(0f, dist));
                if (step > 0f) car.position = p + Vector3.forward * (sign * step);
                if (dist - step <= 1e-4f && goal >= target) { car.position = col.Slots[i]; v = 0f; }
                col.ShiftV[car] = v;

                float a = dt > 0f ? (v - v0) / dt : 0f;
                float norm = a >= 0f ? a / Mathf.Max(0.01f, shiftAccel) : a / Mathf.Max(0.01f, shiftBrake);
                float pitchGoal = -Mathf.Clamp(norm, -1f, 1f) * shiftTilt;
                col.ShiftPitch.TryGetValue(car, out float pitch);
                pitch = Mathf.Lerp(pitch, pitchGoal, 1f - Mathf.Exp(-18f * dt));
                if (Mathf.Abs(pitch) < 0.02f && Mathf.Abs(pitchGoal) < 0.02f) pitch = 0f;
                col.ShiftPitch[car] = pitch;
                car.rotation = baseRot * Quaternion.AngleAxis(pitch, Vector3.right);
                if (v > 0f || pitch != 0f || (car.position - col.Slots[i]).sqrMagnitude > 1e-6f) busy = true;
            }
            if (!busy) break;
            yield return null;
        }
        col.ShiftV.Clear();
        col.ShiftPitch.Clear();
        col.Shift = null;
        if (!col.IsLot && !col.Clearing && col.Incoming.Count == 0 && col.FullOneColor) StartCoroutine(ClearColumn(col));
    }

    // ================= CỘT ĐỦ 1 MÀU =================

    private IEnumerator ClearColumn(Column col)
    {
        col.Clearing = true;
        yield return new WaitForSeconds(clearDelay);
        var cars = new List<Transform>(col.Cars);
        foreach (Transform t in cars)
        {
            StartCoroutine(PopAndVanish(t));
            yield return new WaitForSeconds(clearStagger);
        }
        yield return new WaitForSeconds(clearShrinkTime);
        foreach (Transform t in cars) if (t != null) Destroy(t.gameObject);
        col.Cars.Clear();
        col.Colors.Clear();
        col.Clearing = false;
    }

    private IEnumerator PopAndVanish(Transform car)
    {
        Vector3 s0 = car.localScale, p0 = car.position;
        float tPop = clearShrinkTime * 0.35f;
        bool fxDone = false;
        for (float t = 0f; t < clearShrinkTime; t += Time.deltaTime)
        {
            float k, hop;
            if (t < tPop)
            {
                float u = t / tPop, e = 1f - (1f - u) * (1f - u);
                k = Mathf.Lerp(1f, clearPop, e);
                hop = clearHop * e;
            }
            else
            {
                if (!fxDone) { fxDone = true; SpawnClearFx(car); }
                float u = (t - tPop) / (clearShrinkTime - tPop);
                k = Mathf.Lerp(clearPop, 0f, u * u);
                hop = clearHop * (1f - u * 0.5f);
            }
            car.localScale = s0 * k;
            car.position = p0 + Vector3.up * hop;
            yield return null;
        }
        if (!fxDone) SpawnClearFx(car);
        car.localScale = Vector3.zero;
    }

    private void SpawnClearFx(Transform car)
    {
        if (clearFx == null) return;
        GameObject fx = Instantiate(clearFx, car.position + Vector3.up * 0.5f, Quaternion.identity);
        Destroy(fx, 2f);
        Color tint = Color.white;
        Renderer r = car.GetComponentInChildren<Renderer>();
        if (r != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null && r.sharedMaterials[0].HasProperty("_BaseColor"))
            tint = Color.Lerp(r.sharedMaterials[0].GetColor("_BaseColor"), Color.white, 0.2f);
        foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            main.startColor = new Color(tint.r, tint.g, tint.b, main.startColor.color.a);
            ps.Clear(true);
            ps.Play(true);
        }
    }

    private static void AddCubicBezier(List<Vector3> path, Vector3 p0, Vector3 c0, Vector3 c1, Vector3 p1, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1f - t;
            path.Add(u * u * u * p0 + 3f * u * u * t * c0 + 3f * u * t * t * c1 + t * t * t * p1);
        }
    }

    private static void AddQuadBezier(List<Vector3> path, Vector3 p0, Vector3 c, Vector3 p1, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1f - t;
            path.Add(u * u * p0 + 2f * u * t * c + t * t * p1);
        }
    }
}
