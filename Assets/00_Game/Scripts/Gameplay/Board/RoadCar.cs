using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Xe chạy trên road. Chế độ:
/// - Exiting / Entering: chạy hết 1 polyline rồi gọi callback (ra khỏi cột, rẽ vào cột).
/// - Looping: chạy vòng liên tục trên lane; mỗi khi tới 1 điểm lane thì hỏi controller (có thể rẽ vào cột).
///
/// Cảm giác lái: chạy max tốc ngay (không gia tốc); phanh theo khoảng trống phía trước (v ≤ √(2·phanh·khoảng trống)) nên
/// tới sát xe trước thì chậm dần rồi dừng êm, xe trước đi thì bám đuôi luôn.
/// Vạch chờ (hold): path có thể có 1 điểm chờ; trước điểm đó, nếu controller báo chưa nhập làn được
/// thì xe phanh dừng tại vạch, được phép là đi tiếp ngay.
/// Object này là pivot ở tâm xe, forward (+Z) = mũi xe.
/// </summary>
public class RoadCar : MonoBehaviour
{
    public enum State { Parked, Exiting, Looping, Entering }

    /// <summary>Các xe đang chạy (không tính xe đỗ).</summary>
    public static readonly List<RoadCar> Moving = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Moving.Clear();

    public string ColorKey { get; private set; }
    public State Mode { get; private set; } = State.Parked;
    public int LaneIndex => _laneIdx;
    /// <summary>Làn đang chạy vòng (null nếu đang theo path / đỗ).</summary>
    public List<Vector3> CurrentLane => _lane;
    public float Speed => _v;

    private float _maxSpeed, _turnSpeed, _brake, _length, _width;
    private float _v;

    private List<Vector3> _path;
    private int _next;
    private Action<RoadCar> _onPathDone;
    private int _holdIdx = -1;
    private Func<RoadCar, bool> _canPassHold;

    private List<Vector3> _lane;
    private int _laneIdx;
    private Func<RoadCar, int, bool> _onLanePoint;

    public float SafeDist => _length * 1.12f;    // tâm-tâm tối thiểu theo hướng đi (khe ~12% thân xe)
    private float SideDist => _width * 0.9f;     // lệch ngang dưới mức này = cùng làn → chắn

    public void Setup(string colorKey, float maxSpeed, float turnSpeed, float brake, float length, float width)
    {
        ColorKey = colorKey;
        _maxSpeed = maxSpeed;
        _turnSpeed = turnSpeed;
        _brake = brake;
        _length = length;
        _width = width;
    }

    /// <summary>holdIdx: chỉ số điểm chờ trong path (-1 = không có); canPass: được qua vạch chưa.</summary>
    public void Follow(List<Vector3> path, State mode, Action<RoadCar> onDone, int holdIdx = -1, Func<RoadCar, bool> canPass = null)
    {
        _lane = null;
        _path = path;
        _next = 0;
        _onPathDone = onDone;
        _holdIdx = canPass != null ? holdIdx : -1;
        _canPassHold = canPass;
        SetMode(mode);
    }

    public void Loop(List<Vector3> lane, int startIdx, Func<RoadCar, int, bool> onLanePoint)
    {
        _path = null;
        _lane = lane;
        _laneIdx = startIdx;
        _onLanePoint = onLanePoint;
        SetMode(State.Looping);
    }

    public void Park()
    {
        _path = null;
        _lane = null;
        _v = 0f;
        SetMode(State.Parked);
    }

    private void SetMode(State m)
    {
        Mode = m;
        if (m == State.Parked) Moving.Remove(this);
        else if (!Moving.Contains(this)) Moving.Add(this);
    }

    private void OnDisable() => Moving.Remove(this);

    private int Priority => Mode == State.Entering ? 2 : Mode == State.Looping ? 1 : 0;

    private Vector3? CurrentTarget()
    {
        if (_path != null) return _next < _path.Count ? _path[_next] : (Vector3?)null;
        if (_lane != null) return _lane[_laneIdx];
        return null;
    }

    private Vector3? LookAheadTarget()
    {
        if (_path != null) return _path[Mathf.Min(_next + 1, _path.Count - 1)];
        if (_lane != null) return _lane[(_laneIdx + 1) % _lane.Count];
        return null;
    }

    /// <summary>Hướng đang đi (theo điểm đích, fallback forward).</summary>
    private Vector3 Heading()
    {
        Vector3? t = CurrentTarget();
        Vector3 d = t.HasValue ? t.Value - transform.position : Vector3.zero;
        if (d.sqrMagnitude < 0.01f)
        {
            Vector3? a = LookAheadTarget();
            if (a.HasValue) d = a.Value - transform.position;
        }
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
    }

    /// <summary>Khoảng còn được tiến tới other (∞ nếu other không nằm trước mặt trong làn).</summary>
    private float FreeDistanceTo(RoadCar other, Vector3 heading)
    {
        Vector3 d = other.transform.position - transform.position;
        d.y = 0f;
        float f = Vector3.Dot(d, heading);
        if (f <= 0f) return float.PositiveInfinity;
        float side = (d - heading * f).magnitude;
        if (side >= SideDist) return float.PositiveInfinity;
        return f - SafeDist;
    }

    /// <summary>Khoảng trống phía trước: xe khác + vạch chờ.</summary>
    private float FreeDistance()
    {
        Vector3 h = Heading();
        float free = float.PositiveInfinity;
        foreach (RoadCar o in Moving)
        {
            if (o == this) continue;
            float f = FreeDistanceTo(o, h);
            if (f >= free) continue;

            // Chắn lẫn nhau → xe ưu tiên cao hơn (hoặc id nhỏ hơn khi bằng) được đi.
            if (o.FreeDistanceTo(this, o.Heading()) < float.PositiveInfinity)
            {
                bool iWin = Priority > o.Priority || (Priority == o.Priority && GetInstanceID() < o.GetInstanceID());
                if (iWin) continue;
            }
            free = f;
        }

        // Vạch chờ: chưa được nhập làn → coi vạch như đuôi xe chắn.
        // Xét lại liên tục tới khi sát vạch / không còn kịp phanh thì mới chốt đi.
        if (_path != null && _holdIdx >= 0 && _next <= _holdIdx)
        {
            float dHold = PathDistanceTo(_holdIdx);
            if (_canPassHold(this))
            {
                if (dHold < 0.3f || _v * _v / (2f * _brake) >= dHold) _holdIdx = -1; // chốt: đi luôn
            }
            else free = Mathf.Min(free, dHold);
        }
        return Mathf.Max(0f, free);
    }

    private float PathDistanceTo(int idx)
    {
        float d = Vector3.Distance(transform.position, _path[_next]);
        for (int i = _next; i < idx; i++) d += Vector3.Distance(_path[i], _path[i + 1]);
        return d;
    }

    private void Update()
    {
        if (Mode == State.Parked) return;
        float dt = Time.deltaTime;

        // Tốc độ: chạy max ngay (không gia tốc), chỉ giảm khi cần phanh kịp trong khoảng trống.
        float free = FreeDistance();
        _v = Mathf.Min(_maxSpeed, Mathf.Sqrt(2f * _brake * free));
        float move = Mathf.Min(_v * dt, free);

        for (int guard = 0; move > 0f && guard < 64; guard++)
        {
            if (_path != null && _next >= _path.Count) break;

            Vector3? t = CurrentTarget();
            if (t == null) break;

            Vector3 to = t.Value - transform.position;
            float dist = to.magnitude;
            if (dist > move)
            {
                transform.position += to / dist * move;
                break;
            }

            transform.position = t.Value;
            move -= dist;

            if (_path != null) _next++;
            else if (_lane != null)
            {
                int reached = _laneIdx;
                _laneIdx = (_laneIdx + 1) % _lane.Count;
                _onLanePoint?.Invoke(this, reached); // có thể gọi Follow() để rẽ vào cột
            }
        }

        if (_path != null && _next >= _path.Count)
        {
            Action<RoadCar> done = _onPathDone;
            _path = null;
            done?.Invoke(this); // có thể chuyển sang Loop / Park
        }

        // Quay đầu theo hướng đi.
        if (Mode != State.Parked)
        {
            Vector3 dir = Heading();
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(dir, Vector3.up), _turnSpeed * dt);
        }
    }
}
