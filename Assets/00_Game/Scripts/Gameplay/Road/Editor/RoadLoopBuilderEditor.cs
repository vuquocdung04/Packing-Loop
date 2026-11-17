using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Handle trong Scene cho RoadLoopBuilder:
/// - 4 góc: kéo theo đường chéo để đổi bán kính (vào trong = cong hơn, ra góc = vuông hơn).
/// - 2 đầu bo cạnh dưới: kéo trái/phải để đổi bề rộng lối vào.
/// - 2 chấm vàng giữa cạnh phải = 2 mép mặt đường. Chấm ngoài kéo ra: đường rộng ra phía ngoài (bàn giữ nguyên);
///   chấm trong kéo vào: đường rộng vào trong (bàn thu nhỏ). Xe chạy ở tâm mặt đường.
/// </summary>
[CustomEditor(typeof(RoadLoopBuilder))]
public class RoadLoopBuilderEditor : OdinEditor
{
    private static readonly Color CornerColor = new(1f, 0.6f, 0.15f);
    private static readonly Color OpeningColor = new(0.2f, 0.8f, 1f);
    private static readonly Color InnerCurbColor = new(0.4f, 1f, 0.4f);
    private static readonly Color LaneColor = new(1f, 0.9f, 0.1f);

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
        if (target is RoadLoopBuilder b) b.Build();
    }

    private void OnSceneGUI()
    {
        var b = (RoadLoopBuilder)target;

        Handles.color = CornerColor;
        for (int i = 0; i < 4; i++)
        {
            Vector3 pos = b.GetCornerHandle(i);
            float size = HandleUtility.GetHandleSize(pos) * 0.18f;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(pos, b.GetCornerHandleDirection(i), size, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(b, "Road Corner Radius");
                b.SetCornerHandle(i, moved);
                Apply(b);
            }

            if (i == 0) Handles.Label(pos + Vector3.right * size * 1.5f, $"R = {b.CornerRadius:0}");
        }

        Handles.color = OpeningColor;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 pos = b.GetOpeningHandle(side);
            float size = HandleUtility.GetHandleSize(pos) * 0.15f;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(pos, b.GetOpeningHandleDirection(), size, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(b, "Road Bottom Opening");
                b.SetOpeningHandle(moved);
                Apply(b);
            }

            if (side > 0) Handles.Label(pos + Vector3.down * size * 2f, $"Lối vào = {b.BottomOpeningWidth:0}");
        }

        // Làn xe: dải vàng (mép ngoài + mép trong) + đường tâm mảnh; 2 chấm kéo ngang ở giữa cạnh phải.
        Handles.color = LaneColor;
        DrawLoop(b.GetLaneEdge(true), 4f);
        DrawLoop(b.GetLaneEdge(false), 4f);
        var lane = b.Lane;
        if (lane.Count > 1)
        {
            var center = new System.Collections.Generic.List<Vector3>(lane.Count);
            foreach (Vector2 p in lane) center.Add(b.transform.TransformPoint(p));
            Handles.color = new Color(LaneColor.r, LaneColor.g, LaneColor.b, 0.5f);
            DrawLoop(center, 1.5f);
            Handles.color = LaneColor;
        }

        for (int k = 0; k < 2; k++)
        {
            bool outer = k == 0;
            Vector3 pos = b.GetLaneHandle(outer);
            float size = HandleUtility.GetHandleSize(pos) * 0.16f;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(pos, b.GetLaneHandleDirection(), size, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(b, "Road Lane");
                b.SetLaneHandle(outer, moved);
                Apply(b);
            }
            if (outer) Handles.Label(pos + Vector3.right * size * 1.5f, $"Đường rộng = {b.RoadWidthPx:0}  (làn {b.LaneInnerPx - b.LaneOuterPx:0})");
        }

        Handles.color = InnerCurbColor;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int end = 0; end < 2; end++)
            {
                bool first = end == 0;
                Vector3 pos = b.GetInnerCurbHandle(side, first);
                float size = HandleUtility.GetHandleSize(pos) * 0.15f;

                // Kéo tự do, builder chiếu điểm về đường bao → chấm luôn bám theo hình.
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(pos, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(b, "Road Inner Curb");
                    b.SetInnerCurbHandle(side, first, moved);
                    Apply(b);
                }

                if (side > 0)
                    Handles.Label(pos + Vector3.right * size * 1.5f, first ? $"Đầu = {b.InnerCurbStart:0}" : $"Cuối = {b.InnerCurbEnd:0}");
            }
        }
    }

    private static void DrawLoop(System.Collections.Generic.List<Vector3> pts, float width)
    {
        if (pts.Count < 2) return;
        pts.Add(pts[0]);
        Handles.DrawAAPolyLine(width, pts.ToArray());
    }

    private static void Apply(RoadLoopBuilder b)
    {
        b.Build();
        EditorUtility.SetDirty(b);
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
    }
}
