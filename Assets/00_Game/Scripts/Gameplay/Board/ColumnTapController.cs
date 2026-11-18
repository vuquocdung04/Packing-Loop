using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gameplay kiểu sort:
/// - Tap 1 cột → xe dưới cùng chạy ra ngay (xuống, rẽ phải vào làn). Tới vạch chờ sát mép làn, nếu có xe
///   đang chạy qua thì khựng lại; xe kia qua là nối đuôi luôn. Xe ra khỏi ô thì các xe còn lại dồn xuống.
/// - Xe trên road chạy vòng liên tục ngược chiều kim đồng hồ (phải ↑, trên ←, trái ↓, dưới →).
/// - Mỗi lần đi qua đầu 1 cột ở cạnh trên: nếu cột chưa đủ chỗ và xe cuối cột (xe nó sẽ đỗ sau) cùng màu
///   → rẽ vào từ phía trên, đỗ sát sau. Cột trống thì nhận mọi màu. Không có thì chạy vòng tiếp.
/// - Không xuyên nhau: xem RoadCar (giữ khoảng cách + phanh/tăng tốc mượt).
/// Xe lấy từ con của object này, tên "Car-{cột}-{thứ tự}" (0 = dưới cùng); màu = tên material thân xe.
/// </summary>
public class ColumnTapController : MonoBehaviour
{
    [SerializeField, Required] private RoadLoopBuilder road;
    [Tooltip("Khu cột dưới (giữa 2 bãi cỏ). Tap cột dưới → xe đầu hàng chạy lên, qua lối mở đáy road rồi nhập làn.")]
    [SerializeField] private ColumnAreaBuilder lot;
    [BoxGroup("Nhập làn"), LabelText("Lề lối mở đáy road (unit)"), Tooltip("Xe từ khu dưới đi vào lối mở, cách mép lối mở ít nhất khoảng này.")]
    [SerializeField] private float lotOpeningMargin = 0.15f;

    [BoxGroup("Chạy"), LabelText("Tốc độ tối đa (unit/s)"), SerializeField] private float speed = 12f;
    [BoxGroup("Chạy"), LabelText("Phanh (unit/s²)"), SerializeField] private float brake = 60f;
    [BoxGroup("Chạy"), LabelText("Tốc độ xoay (độ/s)"), SerializeField] private float turnSpeed = 720f;
    [BoxGroup("Chạy"), LabelText("Bán kính cua"), SerializeField] private float turnRadius = 1.2f;
    [BoxGroup("Dồn xe"), LabelText("Tốc độ tối đa (unit/s)"), SerializeField] private float shiftSpeed = 9f;
    [BoxGroup("Dồn xe"), LabelText("Tăng tốc (unit/s²)"), SerializeField] private float shiftAccel = 45f;
    [BoxGroup("Dồn xe"), LabelText("Phanh (unit/s²)"), SerializeField] private float shiftBrake = 35f;
    [BoxGroup("Dồn xe"), LabelText("Xe sau khởi động trễ (s)"), Tooltip("Hiệu ứng domino: xe phía sau bắt đầu chạy trễ hơn xe trước khoảng này.")]
    [SerializeField] private float shiftStagger = 0.05f;
    [BoxGroup("Dồn xe"), LabelText("Khe với xe vừa ra (× dài xe)"), Tooltip("Xe đầu hàng bám theo xe vừa ra, giữ khe tối thiểu này (tâm-tâm = dài xe × (1 + khe)).")]
    [SerializeField] private float shiftFollowGap = 0.08f;
    [BoxGroup("Dồn xe"), LabelText("Chúi/ngửa thân (độ)"), Tooltip("Tăng tốc → ngửa, phanh → chúi mũi. 0 = tắt.")]
    [SerializeField] private float shiftTilt = 4f;

    [BoxGroup("Nhập làn"), LabelText("Vạch chờ cách mép làn (× rộng xe)")]
    [Tooltip("Mũi xe dừng cách mép làn khoảng này khi đang có xe chạy qua.")]
    [SerializeField] private float holdGap = 0.15f;
    [BoxGroup("Nhập làn"), LabelText("Xe phía sau phải cách (× quãng nhập làn)")]
    [Tooltip("Xe chạy vòng phía sau phải còn cách điểm nhập ≥ quãng từ vạch chờ tới làn × hệ số này + 1 khoảng an toàn.")]
    [SerializeField] private float mergeLookBehind = 1f;

    [BoxGroup("Cột đủ màu"), LabelText("FX biến mất"), SerializeField] private GameObject clearFx; // CarClearFX
    [BoxGroup("Cột đủ màu"), LabelText("Chờ trước khi biến (s)"), SerializeField] private float clearDelay = 0.05f;
    [BoxGroup("Cột đủ màu"), LabelText("Cách nhau mỗi xe (s)"), SerializeField] private float clearStagger = 0.04f;
    [BoxGroup("Cột đủ màu"), LabelText("Thời gian thu nhỏ (s)"), SerializeField] private float clearShrinkTime = 0.16f;
    [BoxGroup("Cột đủ màu"), LabelText("Phình lên trước khi thu"), SerializeField] private float clearPop = 1.2f;
    [BoxGroup("Cột đủ màu"), LabelText("Nảy lên (unit)"), SerializeField] private float clearHop = 0.35f;

    private class Column
    {
        public readonly List<Transform> Cars = new();      // 0 = dưới cùng
        public readonly List<string> Colors = new();
        public readonly List<RoadCar> Incoming = new();    // xe đang rẽ vào (đã giữ chỗ)
        public readonly List<RoadCar> Leaving = new();     // xe vừa ra, chưa rời hẳn ô dưới cùng
        public readonly List<Vector3> Slots = new();       // vị trí pivot từng ô, 0 = dưới cùng
        public Coroutine Shift;
        public readonly Dictionary<Transform, float> ShiftV = new(); // vận tốc dồn hiện tại (giữ khi dồn bị khởi động lại)
        public readonly Dictionary<Transform, float> ShiftPitch = new(); // góc chúi/ngửa hiện tại
        public bool Clearing;                               // đang chạy animation biến mất
        public bool IsLot;                                  // cột khu dưới: chỉ cho xe ra, xe đi lên (+Z)
        public float ExitSign = -1f;                        // hướng Z xe rời cột (board: xuống, lot: lên)

        public int Capacity => Slots.Count;
        public int Reserved => Cars.Count + Incoming.Count;

        /// <summary>Đủ chỗ (tính cả xe đang vào) và tất cả cùng 1 màu.</summary>
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

        /// <summary>Không cho tap: đang biến mất, hoặc đã/đang đủ 1 màu (chờ xe cuối vào).</summary>
        public bool Locked => Clearing || FullOneColor;

        /// <summary>Màu xe cuối hàng (tính cả xe đang vào). null = cột trống.</summary>
        public string TailColor => Incoming.Count > 0 ? Incoming[^1].ColorKey
            : Colors.Count > 0 ? Colors[^1] : null;
    }

    private readonly List<Column> _cols = new();
    private readonly List<Column> _lotCols = new();
    private List<Vector3> _lane;                                  // world, ngược chiều kim đồng hồ
    private float[] _laneS;                                        // quãng đường tích luỹ tới từng điểm lane
    private float _laneLen;
    private readonly Dictionary<int, int> _entryAt = new();        // chỉ số lane → cột rẽ vào
    private float _zBottom, _zTop, _carY, _carLen = 2f, _carWidth = 1.8f;

    private void Start()
    {
        CollectCars();
        BuildLane();
    }

    // ================= KHỞI TẠO =================

    private void CollectCars()
    {
        _cols.Clear();
        _lotCols.Clear();
        for (int c = 0; c < road.ColumnCount; c++) _cols.Add(new Column());
        // Board: mũi xuống (-Z), xe 0 = dưới cùng.
        CollectInto(transform, _cols, Vector3.back, true);

        if (lot != null && lot.CarsRoot != null)
        {
            for (int c = 0; c < lot.ColumnCount; c++) _lotCols.Add(new Column { IsLot = true, ExitSign = 1f });
            // Khu dưới: mũi lên (+Z), xe 0 = trên cùng (đầu hàng).
            CollectInto(lot.CarsRoot, _lotCols, Vector3.forward, false);
        }
    }

    /// <summary>Lấy con tên "Car-{cột}-{thứ tự}" của root, bọc mỗi xe vào pivot ở tâm xe (forward = mũi xe).</summary>
    private void CollectInto(Transform root, List<Column> cols, Vector3 forward, bool measure)
    {
        int n = cols.Count;
        var tmp = new List<(int c, int i, Transform t)>();
        foreach (Transform t in root)
        {
            string[] parts = t.name.Split('-');
            if (parts.Length < 3 || parts[0] != "Car") continue;
            if (int.TryParse(parts[1], out int c) && int.TryParse(parts[2], out int i) && c < n) tmp.Add((c, i, t));
        }
        tmp.Sort((a, b) => a.c != b.c ? a.c.CompareTo(b.c) : a.i.CompareTo(b.i));

        // Kích thước xe (dài theo Z, rộng theo X).
        if (measure && tmp.Count > 0 && tmp[0].t.TryGetComponent(out Renderer r0))
        {
            _carLen = r0.bounds.size.z;
            _carWidth = r0.bounds.size.x;
        }

        foreach (var e in tmp)
        {
            Renderer r = e.t.GetComponent<Renderer>();
            Vector3 center = r != null ? r.bounds.center : e.t.position;
            var pivot = new GameObject(e.t.name).transform;
            pivot.SetParent(root, false);
            pivot.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));
            e.t.SetParent(pivot, true);
            e.t.name = "Mesh";

            string color = ColorOf(r);
            var car = pivot.gameObject.AddComponent<RoadCar>();
            car.Setup(color, speed, turnSpeed, brake, _carLen, _carWidth);

            Column col = cols[e.c];
            col.Cars.Add(pivot);
            col.Colors.Add(color);
            col.Slots.Add(center);
            if (measure) _carY = center.y;
        }
    }

    private static string ColorOf(Renderer r)
    {
        if (r == null || r.sharedMaterials.Length == 0 || r.sharedMaterials[0] == null) return "";
        string n = r.sharedMaterials[0].name;              // "CarColor-Red"
        int k = n.IndexOf('-');
        return k >= 0 ? n.Substring(k + 1) : n;
    }

    private void BuildLane()
    {
        _lane = road.GetLaneWorld();
        _lane.Reverse(); // ngược chiều kim đồng hồ trên màn hình
        _zBottom = float.MaxValue;
        _zTop = float.MinValue;
        for (int i = 0; i < _lane.Count; i++)
        {
            _lane[i] = new Vector3(_lane[i].x, _carY, _lane[i].z);
            _zBottom = Mathf.Min(_zBottom, _lane[i].z);
            _zTop = Mathf.Max(_zTop, _lane[i].z);
        }

        _laneS = new float[_lane.Count];
        _laneLen = 0f;
        for (int i = 0; i < _lane.Count; i++)
        {
            _laneS[i] = _laneLen;
            _laneLen += Vector3.Distance(_lane[i], _lane[(i + 1) % _lane.Count]);
        }

        // Điểm rẽ vào cột c: điểm lane gần (tâm cột + R, cạnh trên) nhất, chỉ xét nửa trên đường
        // (cột sát góc thì điểm rẽ rơi vào khúc cua trên-phải, xe cua liền một mạch vào cột).
        _entryAt.Clear();
        float zMid = (_zTop + _zBottom) * 0.5f;
        for (int c = 0; c < road.ColumnCount; c++)
        {
            var want = new Vector3(ColumnX(c) + turnRadius, _carY, _zTop);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _lane.Count; i++)
            {
                Vector3 p = _lane[i];
                if (p.z < zMid || p.x < ColumnX(c)) continue; // phải ở bên phải tâm cột (đi sang trái tới cột)
                float d = (p - want).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) _entryAt[best] = c;
        }
    }

    private float ColumnX(int c) => road.transform.TransformPoint(road.GetColumnCenter(c)).x;

    /// <summary>Quãng đường theo chiều chạy từ điểm lane a tới điểm lane b.</summary>
    private float LaneDist(int a, int b)
    {
        float d = _laneS[b] - _laneS[a];
        return d < 0f ? d + _laneLen : d;
    }

    // ================= INPUT =================

    private void Update()
    {
        Pointer p = Pointer.current;
        if (p == null || !p.press.wasPressedThisFrame) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(p.position.ReadValue());
        var plane = new Plane(Vector3.up, new Vector3(0f, road.transform.position.y, 0f));
        if (!plane.Raycast(ray, out float enter)) return;

        Vector3 hit = ray.GetPoint(enter);
        int col = ColumnAt(hit);
        if (col >= 0) { Launch(col); return; }
        col = LotColumnAt(hit);
        if (col >= 0) LaunchFromLot(col);
    }

    private int LotColumnAt(Vector3 world)
    {
        if (lot == null || _lotCols.Count == 0) return -1;
        Vector2 size = lot.ColumnSize;
        float halfW = (size.x + lot.ColumnSpacingX) * 0.5f;
        float halfH = size.y * 0.5f;
        for (int c = 0; c < _lotCols.Count; c++)
        {
            Vector3 center = lot.GetColumnCenterWorld(c);
            if (Mathf.Abs(world.x - center.x) <= halfW && Mathf.Abs(world.z - center.z) <= halfH) return c;
        }
        return -1;
    }

    private int ColumnAt(Vector3 world)
    {
        Transform rt = road.transform;
        Vector2 size = road.ColumnSize;
        float halfW = size.x * rt.lossyScale.x * 0.65f;     // nới chút sang khe giữa 2 cột
        float halfH = size.y * rt.lossyScale.y * 0.5f + 1f; // tính cả vùng mũi tên
        for (int c = 0; c < road.ColumnCount; c++)
        {
            Vector3 center = rt.TransformPoint(road.GetColumnCenter(c));
            if (Mathf.Abs(world.x - center.x) <= halfW && Mathf.Abs(world.z - center.z) <= halfH) return c;
        }
        return -1;
    }

    // ================= RA KHỎI CỘT =================

    private void Launch(int c)
    {
        Column col = _cols[c];
        if (col.Cars.Count == 0 || col.Locked) return;

        Transform pivot = col.Cars[0];
        col.Cars.RemoveAt(0);
        col.Colors.RemoveAt(0);

        var car = pivot.GetComponent<RoadCar>();
        List<Vector3> path = BuildExitPath(pivot.position, out int joinIdx, out int holdIdx, out float mergeLen);
        car.Follow(path, RoadCar.State.Exiting,
            rc => rc.Loop(_lane, joinIdx, OnLanePoint),
            holdIdx, rc => CanMerge(rc, joinIdx, mergeLen));

        col.Leaving.Add(car);
        Compact(c); // chờ xe vừa ra rời ô dưới cùng rồi mới dồn
    }

    // ================= KHU DƯỚI → ROAD =================

    /// <summary>Xe đầu hàng cột dưới chạy lên, qua lối mở đáy road, chờ ở vạch rồi rẽ phải nhập làn.</summary>
    private void LaunchFromLot(int c)
    {
        Column col = _lotCols[c];
        if (col.Cars.Count == 0) return;

        Transform pivot = col.Cars[0];
        col.Cars.RemoveAt(0);
        col.Colors.RemoveAt(0);

        var car = pivot.GetComponent<RoadCar>();
        List<Vector3> path = BuildLotExitPath(pivot.position, out int joinIdx, out int holdIdx, out float mergeLen);
        car.Follow(path, RoadCar.State.Exiting,
            rc => rc.Loop(_lane, joinIdx, OnLanePoint),
            holdIdx, rc => CanMerge(rc, joinIdx, mergeLen));

        col.Leaving.Add(car);
        StartShift(col);
    }

    /// <summary>
    /// Đường lên: thẳng lên ra khỏi cột → lượn chữ S vào trong lối mở đáy road → vạch chờ (mũi xe sát mép làn)
    /// → cua phải vào làn dưới (làn dưới chạy sang phải).
    /// </summary>
    private List<Vector3> BuildLotExitPath(Vector3 start, out int joinIdx, out int holdIdx, out float mergeLen)
    {
        float y = _carY, R = turnRadius;
        int n = _lane.Count;
        var path = new List<Vector3> { start };

        // Lối mở đáy road (world), tâm x = tâm road.
        float openHalf = road.BottomOpeningHalfWorld;
        float cx = road.transform.position.x;
        float lim = Mathf.Max(0f, openHalf - _carWidth * 0.5f - lotOpeningMargin);
        // Cột nằm trong lối mở thì đi thẳng; cột ngoài nhập chung tuyến với cột gần nhất trong lối mở
        // (xe nối đuôi nhau, không chen ngang nhau).
        float xt = Mathf.Clamp(start.x, cx - lim, cx + lim);
        if (!Mathf.Approximately(xt, start.x))
        {
            float bestD = float.MaxValue;
            for (int c = 0; c < lot.ColumnCount; c++)
            {
                float x = lot.GetColumnCenterWorld(c).x;
                if (Mathf.Abs(x - cx) > lim) continue;
                float d = Mathf.Abs(x - start.x);
                if (d < bestD) { bestD = d; xt = x; }
            }
        }

        float holdZ = _zBottom - _carWidth * (0.5f + holdGap) - _carLen * 0.5f;
        float zOut = Mathf.Min(lot.transform.position.z + _carLen * 0.5f + 0.1f, holdZ); // đuôi xe vừa ra khỏi cột
        if (zOut > start.z) path.Add(new Vector3(start.x, y, zOut));
        else zOut = start.z;

        if (Mathf.Abs(xt - start.x) > 0.01f && holdZ > zOut)
        {
            // Chữ S từ (x cột, zOut) tới (xt, holdZ), 2 đầu đều thẳng đứng.
            float zm = (zOut + holdZ) * 0.5f;
            AddCubicBezier(path, new Vector3(start.x, y, zOut), new Vector3(start.x, y, zm),
                new Vector3(xt, y, zm), new Vector3(xt, y, holdZ), 12);
        }
        else path.Add(new Vector3(xt, y, Mathf.Max(holdZ, zOut)));
        holdIdx = path.Count - 1;

        // Cua phải vào làn.
        Vector3 corner = new Vector3(xt, y, _zBottom);
        int k0 = 0;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = (_lane[i] - corner).sqrMagnitude;
            if (d < best) { best = d; k0 = i; }
        }
        int j = k0;
        for (float acc = 0f; acc < R; j = (j + 1) % n)
            acc += Vector3.Distance(_lane[j], _lane[(j + 1) % n]);

        Vector3 a = new Vector3(xt, y, _zBottom - R);
        if (a.z > path[^1].z) path.Add(a);
        AddQuadBezier(path, a, corner, _lane[j], 10);
        joinIdx = (j + 1) % n;

        mergeLen = 0f;
        for (int i = holdIdx + 1; i < path.Count; i++) mergeLen += Vector3.Distance(path[i - 1], path[i]);
        return path;
    }

    /// <summary>
    /// Đường ra: thẳng xuống tới vạch chờ (mũi xe sát mép làn), tiếp tục xuống rồi cua phải vào làn.
    /// joinIdx = điểm lane kế tiếp để chạy vòng; mergeLen = quãng từ vạch chờ tới điểm nhập làn.
    /// </summary>
    private List<Vector3> BuildExitPath(Vector3 start, out int joinIdx, out int holdIdx, out float mergeLen)
    {
        float y = _carY, R = turnRadius;
        int n = _lane.Count;
        var path = new List<Vector3> { start };

        Vector3 corner = new Vector3(start.x, y, _zBottom);
        int k0 = 0;
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = (_lane[i] - corner).sqrMagnitude;
            if (d < best) { best = d; k0 = i; }
        }
        int j = k0;
        for (float acc = 0f; acc < R; j = (j + 1) % n)
            acc += Vector3.Distance(_lane[j], _lane[(j + 1) % n]);

        // Vạch chờ: tâm xe cách tâm làn = nửa rộng xe (xe trên làn) + nửa dài xe + khe.
        float holdZ = _zBottom + _carWidth * (0.5f + holdGap) + _carLen * 0.5f;
        Vector3 hold = new Vector3(start.x, y, Mathf.Min(holdZ, start.z));
        Vector3 a = new Vector3(start.x, y, _zBottom + R);

        holdIdx = path.Count;
        path.Add(hold);
        if (a.z < hold.z) path.Add(a);
        AddQuadBezier(path, a, corner, _lane[j], 10);
        joinIdx = (j + 1) % n;

        mergeLen = 0f;
        for (int i = holdIdx + 1; i < path.Count; i++) mergeLen += Vector3.Distance(path[i - 1], path[i]);
        return path;
    }

    /// <summary>
    /// Được qua vạch chờ chưa: xe vừa chạy qua đã đi khỏi điểm nhập ≥ 1 khoảng an toàn (để nối đuôi),
    /// và xe phía sau còn đủ xa để mình kịp nhập làn.
    /// </summary>
    private bool CanMerge(RoadCar me, int joinIdx, float mergeLen)
    {
        Vector3 joinPos = _lane[joinIdx];
        foreach (RoadCar o in RoadCar.Moving)
        {
            if (o == me) continue;
            if (o.Mode == RoadCar.State.Looping)
            {
                float behind = LaneDist(o.LaneIndex, joinIdx);     // o còn cách điểm nhập bao xa (phía sau)
                float ahead = _laneLen - behind;                    // o đã đi qua điểm nhập bao xa
                if (ahead < o.SafeDist) return false;               // còn đang ở ngay điểm nhập
                if (behind < mergeLen * mergeLookBehind + o.SafeDist) return false; // sắp tới
            }
            else if ((o.transform.position - joinPos).sqrMagnitude < o.SafeDist * o.SafeDist)
            {
                return false; // xe khác (đang ra/đang rẽ) đứng chắn chỗ nhập
            }
        }
        return true;
    }

    // ================= VÀO CỘT =================

    private bool CanPark(Column col, string color)
    {
        if (col.Clearing || col.Reserved >= col.Capacity) return false;
        string tail = col.TailColor;
        return tail == null || tail == color;
    }

    /// <summary>Gọi mỗi khi xe đang chạy vòng tới 1 điểm lane.</summary>
    private bool OnLanePoint(RoadCar car, int laneIdx)
    {
        if (!_entryAt.TryGetValue(laneIdx, out int c)) return false;
        Column col = _cols[c];
        if (!CanPark(col, car.ColorKey)) return false;

        int slot = col.Reserved;
        col.Incoming.Add(car);

        float y = _carY, R = turnRadius, x = ColumnX(c);
        Vector3 p0 = _lane[laneIdx];
        Vector3 ctrl = new Vector3(x, y, p0.z);
        Vector3 p1 = new Vector3(x, y, p0.z - R);
        var path = new List<Vector3> { p0 };
        AddQuadBezier(path, p0, ctrl, p1, 10);
        Vector3 target = col.Slots[Mathf.Min(slot, col.Slots.Count - 1)];
        path.Add(new Vector3(x, y, target.z));

        car.Follow(path, RoadCar.State.Entering, rc => Park(c, rc));
        return true;
    }

    private void Park(int c, RoadCar car)
    {
        Column col = _cols[c];
        col.Incoming.Remove(car);
        col.Cars.Add(car.transform);
        col.Colors.Add(car.ColorKey);
        car.Park();
        car.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        Compact(c);
    }

    // ================= DỒN XE =================

    private void Compact(int c) => StartShift(_cols[c]);

    private void StartShift(Column col)
    {
        if (col.Shift != null) StopCoroutine(col.Shift);
        col.Shift = StartCoroutine(ShiftTo(col));
    }

    /// <summary>
    /// Dồn xe như xe thật chạy: mỗi xe tăng tốc → chạy → phanh êm vào đúng ô của nó.
    /// Xe đầu hàng bám theo xe vừa ra (giữ khe), xe sau bám xe trước (giữ khoảng cách ô) → không phải chờ
    /// xe ra rời hẳn ô mới dồn, cũng không đè nhau. Xe sau khởi động trễ chút (domino). Thân xe chúi/ngửa theo gia tốc.
    /// Tính theo trục dọc cột: s = z × ExitSign (s lớn = phía trước, hướng xe rời cột).
    /// </summary>
    private IEnumerator ShiftTo(Column col)
    {
        float sign = col.ExitSign;
        float S(Vector3 p) => p.z * sign;
        Quaternion baseRot = Quaternion.LookRotation(Vector3.forward * sign, Vector3.up);
        float followDist = _carLen * (1f + shiftFollowGap);
        float exitedAt = col.Slots.Count > 0 ? S(col.Slots[0]) + _carLen * 1.05f : 0f; // xe ra qua mốc này = rời cột

        float elapsed = 0f;
        while (true)
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            // Xe vừa ra: còn trong vùng cột thì xe đầu hàng phải bám sau nó.
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

                // Giới hạn phía trước: xe ra (với xe đầu) hoặc xe ngay trước (giữ đúng khoảng cách 2 ô).
                float limit = i == 0 ? frontLimit
                    : S(col.Cars[i - 1].position) - (S(col.Slots[i - 1]) - target);
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

                // Chúi/ngửa: tăng tốc → ngửa (mũi lên), phanh → chúi; mượt dần, dừng thì nhún về 0.
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

        // Xe cuối đã vào chỗ và cả cột 1 màu → biến mất.
        if (!col.IsLot && !col.Clearing && col.Incoming.Count == 0 && col.FullOneColor)
            StartCoroutine(ClearColumn(col));
    }

    // ================= CỘT ĐỦ 1 MÀU → BIẾN MẤT =================

    /// <summary>Từ xe đầu (dưới cùng) tới xe cuối: phình nhẹ → thu về 0 → bung particle. Xong thì cột trống.</summary>
    private IEnumerator ClearColumn(Column col)
    {
        col.Clearing = true;
        yield return new WaitForSeconds(clearDelay);

        var cars = new List<Transform>(col.Cars);
        for (int i = 0; i < cars.Count; i++)
        {
            StartCoroutine(PopAndVanish(cars[i]));
            yield return new WaitForSeconds(clearStagger);
        }
        yield return new WaitForSeconds(clearShrinkTime);

        foreach (Transform t in cars) if (t != null) Destroy(t.gameObject);
        col.Cars.Clear();
        col.Colors.Clear();
        col.Clearing = false; // cột trống, nhận màu khác
    }

    /// <summary>Phình + nảy lên nhanh (35%) → bung FX → co về 0 kiểu ease-in (tăng tốc dần, dứt khoát).</summary>
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
                float u = t / tPop;
                float e = 1f - (1f - u) * (1f - u);           // ease-out
                k = Mathf.Lerp(1f, clearPop, e);
                hop = clearHop * e;
            }
            else
            {
                if (!fxDone) { fxDone = true; SpawnClearFx(car); }
                float u = (t - tPop) / (clearShrinkTime - tPop);
                k = Mathf.Lerp(clearPop, 0f, u * u);            // ease-in
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

        // Tô màu particle theo màu thân xe (sáng lên chút cho rõ trên nền tối).
        Color tint = Color.white;
        Renderer r = car.GetComponentInChildren<Renderer>();
        if (r != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null && r.sharedMaterials[0].HasProperty("_BaseColor"))
            tint = Color.Lerp(r.sharedMaterials[0].GetColor("_BaseColor"), Color.white, 0.2f);

        foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            Color c = main.startColor.color;
            main.startColor = new Color(tint.r, tint.g, tint.b, c.a);
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
