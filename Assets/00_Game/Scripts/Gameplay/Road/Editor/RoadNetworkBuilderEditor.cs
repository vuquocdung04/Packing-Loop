using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Chấm kéo trong Scene cho RoadNetworkBuilder:
/// - Cam: bán kính góc ngoài (kéo theo đường chéo góc trên-phải).
/// - Vàng: bề rộng đường vòng (mép đảo ở cạnh phải, kéo ngang).
/// - Xanh dương: 2 đầu lối ra cạnh dưới (kéo ngang).
/// - Hồng: tâm đường giữa (kéo dọc) + mép dưới đường giữa (kéo dọc = bề rộng)
///   + chấm ở góc đảo trên-phải giáp đường giữa (kéo chéo = độ cong 4 góc ngã ba).
/// - Xanh lá: đầu/cuối viền mỗi đảo (kéo tự do, chấm bám theo mép đảo; trái/phải đối xứng).
/// </summary>
[CustomEditor(typeof(RoadNetworkBuilder))]
public class RoadNetworkBuilderEditor : OdinEditor
{
    private static readonly Color CornerColor = new(1f, 0.6f, 0.15f);
    private static readonly Color WidthColor = new(1f, 0.9f, 0.1f);
    private static readonly Color OpeningColor = new(0.2f, 0.8f, 1f);
    private static readonly Color MiddleColor = new(1f, 0.4f, 0.8f);
    private static readonly Color CurbColor = new(0.4f, 1f, 0.4f);

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
        if (target is RoadNetworkBuilder b) b.Build();
    }

    private void OnSceneGUI()
    {
        var b = (RoadNetworkBuilder)target;

        Handles.color = CornerColor;
        Slider(b, b.GetCornerHandle(), b.CornerHandleDir, 0.18f, "Road Corner", b.SetCornerHandle, $"R = {b.CornerRadiusPx:0}");

        Handles.color = WidthColor;
        Slider(b, b.GetRoadWidthHandle(), b.Right, 0.16f, "Road Width", b.SetRoadWidthHandle, $"Đường rộng = {b.RoadWidthPx:0}");

        Handles.color = OpeningColor;
        for (int side = -1; side <= 1; side += 2)
            Slider(b, b.GetOpeningHandle(side), b.Right, 0.15f, "Road Opening", b.SetOpeningHandle,
                side > 0 ? $"Lối ra = {b.BottomOpeningPx:0}" : null);

        if (b.HasMiddleRoad)
        {
            Handles.color = MiddleColor;
            Slider(b, b.GetMiddleHandle(), b.Down, 0.15f, "Middle Road Y", b.SetMiddleHandle, $"Đường giữa y = {b.MiddleYPx:0}");
            Slider(b, b.GetMiddleWidthHandle(), b.Down, 0.13f, "Middle Road Width", b.SetMiddleWidthHandle, $"rộng = {b.MiddleWidthPx:0}");
            Slider(b, b.GetJunctionHandle(), b.JunctionHandleDir, 0.15f, "Junction Radius", b.SetJunctionHandle, $"Cong khúc rẽ = {b.JunctionRadiusPx:0}");
            Slider(b, b.GetLowerJunctionHandle(), b.LowerJunctionHandleDir, 0.13f, "Lower Junction Radius", b.SetLowerJunctionHandle, $"Cong góc đảo dưới = {b.LowerJunctionRadiusPx:0}");
        }

        Handles.color = CurbColor;
        for (int island = 0; island < b.Islands; island++)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int e = 0; e < 2; e++)
                {
                    bool first = e == 0;
                    Vector3 pos = b.GetCurbHandle(island, side, first);
                    float size = HandleUtility.GetHandleSize(pos) * 0.15f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(pos, size, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(b, "Road Island Curb");
                        int i = island, sd = side;
                        b.SetCurbHandle(i, sd, first, moved);
                        Apply(b);
                    }
                    if (side > 0)
                        Handles.Label(pos + Vector3.right * size * 1.5f,
                            (first ? "Đầu = " : "Cuối = ") + b.GetCurbValue(island, first).ToString("0"));
                }
            }
        }
    }

    private static void Slider(RoadNetworkBuilder b, Vector3 pos, Vector3 dir, float scale, string undo,
        System.Action<Vector3> set, string label)
    {
        float size = HandleUtility.GetHandleSize(pos) * scale;
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.Slider(pos, dir, size, Handles.SphereHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(b, undo);
            set(moved);
            Apply(b);
        }
        if (label != null) Handles.Label(pos + Vector3.right * size * 1.5f, label);
    }

    private static void Apply(RoadNetworkBuilder b)
    {
        b.Build();
        EditorUtility.SetDirty(b);
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
    }
}
