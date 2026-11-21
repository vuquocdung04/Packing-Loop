using System;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Chấm kéo trong Scene cho RoadMapBuilder — cùng kiểu RoadLoopBuilderEditor / RoadNetworkBuilderEditor:
/// mỗi loại chấm = 1 tham số CHUNG của map (kéo 1 chấm, mọi chấm cùng loại đổi theo).
/// - Cam: bo góc ngoài (kéo theo đường chéo góc). Góc đảo ở khúc cua tự đồng tâm → đường rộng đều.
/// - Hồng: bo góc đảo ở ngã ba / đường giữa (kéo theo đường chéo).
/// - Vàng: bề rộng đường (mép ngoài cạnh phải, kéo ngang) — đường nở ra ngoài, đảo/cột giữ nguyên.
/// - Xanh dương: 2 mép lối ra (kéo dọc mép, đối xứng).
/// - Xanh lá: đầu/cuối viền đảo ở chỗ hở đầu cột (kéo tự do, bám theo mép đảo).
/// - Trắng: khoảng cách mũi tên tam giác tới đầu cột (kéo dọc cột).
/// - Tím: padding đảo phía ngoài (cạnh trái đảo, kéo ngang) — đảo nở về phía vòng ngoài, vòng ngoài dịch theo.
/// </summary>
[CustomEditor(typeof(RoadMapBuilder))]
public class RoadMapBuilderEditor : OdinEditor
{
    private static readonly Color CornerColor = new(1f, 0.6f, 0.15f);
    private static readonly Color JunctionColor = new(1f, 0.4f, 0.8f);
    private static readonly Color WidthColor = new(1f, 0.9f, 0.1f);
    private static readonly Color OpeningColor = new(0.2f, 0.8f, 1f);
    private static readonly Color CurbColor = new(0.4f, 1f, 0.4f);
    private static readonly Color PaddingColor = new(0.7f, 0.45f, 1f);

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
        if (target is not RoadMapBuilder b || b.Map == null) return;
        b.Map.Touch();
        b.Build();
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        var b = (RoadMapBuilder)target;
        if (b.IsDirty) b.Build();
    }

    private void OnSceneGUI()
    {
        var b = (RoadMapBuilder)target;
        RoadMapSO map = b.Map;
        if (map == null) return;
        if (b.IsDirty) b.Build();

        // Copy ra mảng trước: kéo 1 chấm → Build lại → danh sách chấm đổi.
        var corners = new RoadMapBuilder.CornerHandle[b.CornerHandles.Count];
        for (int i = 0; i < corners.Length; i++) corners[i] = b.CornerHandles[i];
        var exits = new RoadMapBuilder.ExitEnd[b.ExitEnds.Count];
        for (int i = 0; i < exits.Length; i++) exits[i] = b.ExitEnds[i];
        var curbs = new RoadMapBuilder.CurbEnd[b.CurbEnds.Count];
        for (int i = 0; i < curbs.Length; i++) curbs[i] = b.CurbEnds[i];

        bool labelCorner = true, labelJunction = true;
        foreach (RoadMapBuilder.CornerHandle c in corners)
        {
            Handles.color = c.Junction ? JunctionColor : CornerColor;
            string label = null;
            if (c.Junction && labelJunction) { label = $"Bo ngã ba = {map.junctionRadius:0}"; labelJunction = false; }
            if (!c.Junction && labelCorner) { label = $"R = {map.convexRadius:0}"; labelCorner = false; }
            RoadMapBuilder.CornerHandle cc = c;
            Slider(b, b.PxToWorld(c.MidPx), b.PxDirToWorld(c.BisectorPx), 0.16f, "Road Map Corner",
                w => b.SetCornerHandle(cc, w), label);
        }

        if (b.HasRoadWidthHandle)
        {
            Handles.color = WidthColor;
            Slider(b, b.PxToWorld(b.RoadWidthHandlePx), b.PxDirToWorld(Vector2.right), 0.16f, "Road Map Width",
                b.SetRoadWidthHandle, $"Đường rộng = {map.roadWidth:0}");
        }

        if (b.HasPaddingHandle)
        {
            Handles.color = PaddingColor;
            Slider(b, b.PxToWorld(b.PaddingHandlePx), b.PxDirToWorld(b.PaddingDirPx), 0.16f, "Road Map Island Padding",
                b.SetPaddingHandle, $"Padding đảo = {map.islandPadding:0}");
        }

        if (b.HasArrowHandle)
        {
            Handles.color = Color.white;
            Slider(b, b.PxToWorld(b.ArrowHandlePx), b.PxDirToWorld(b.ArrowInwardPx), 0.12f, "Road Map Arrow",
                b.SetArrowHandle, $"Mũi tên cách đầu cột = {map.arrowInset:0}");
        }

        Handles.color = OpeningColor;
        for (int i = 0; i < exits.Length; i++)
        {
            RoadMapBuilder.ExitEnd e = exits[i];
            Slider(b, b.PxToWorld(e.PosPx), b.PxDirToWorld(e.DirPx), 0.15f, "Road Map Exit",
                w => b.SetExitHandle(e, w), i == 0 ? $"Lối ra = {map.exitWidth:0}" : null);
        }

        Handles.color = CurbColor;
        var labeled = new bool[4];
        for (int i = 0; i < curbs.Length; i++)
        {
            RoadMapBuilder.CurbEnd c = curbs[i];
            string label = null;
            if (!labeled[(int)c.Group])
            {
                labeled[(int)c.Group] = true;
                label = $"Viền thò {RoadMapBuilder.CurbGroupName(c.Group)} = {b.GetCurbExt(c.Group):0}";
            }
            // Chỉ kéo dọc mép đảo (2 chiều): dài ra / thu lại. Phía vòng ngoài và phía đường giữa chỉnh riêng.
            Slider(b, b.PxToWorld(c.PosPx), b.PxDirToWorld(c.DirPx), 0.15f, "Road Map Curb",
                w => b.SetCurbEndHandle(c, w), label);
        }
    }

    private static void Slider(RoadMapBuilder b, Vector3 pos, Vector3 dir, float scale, string undo, Action<Vector3> set, string label)
    {
        float size = HandleUtility.GetHandleSize(pos) * scale;
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.Slider(pos, dir, size, Handles.SphereHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(b.Map, undo);
            set(moved);
            Apply(b);
        }
        if (label != null) Handles.Label(pos + Vector3.right * size * 1.5f, label);
    }

    private static void Apply(RoadMapBuilder b)
    {
        b.Build();
        EditorUtility.SetDirty(b.Map);
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
    }
}
