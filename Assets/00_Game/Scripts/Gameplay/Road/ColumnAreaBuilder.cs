using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Vùng cột xe nằm giữa 2 bãi cỏ (dưới vòng đường). Cột tự dãn đều để lấp đầy bề rộng giữa 2 mép,
/// vạch chia nằm giữa khoảng Space X. Dựng trong mặt phẳng XY local (object xoay X=90 để nằm trên mặt đất,
/// giống RoadLoopBuilder). Pivot = giữa cạnh trên của vùng, cột kéo dài xuống dưới (local -Y).
/// </summary>
public class ColumnAreaBuilder : MonoBehaviour
{
    private const string GeneratedName = "Generated";
    private const float DividerCol = 56.5f; // tâm viền trong của profile Road_Vertical → màu viền phẳng
    private const float CapRow = 230f;

    [BoxGroup("Bề rộng"), LabelText("Mép trái"), Tooltip("Renderer bên trái (bãi cỏ). Cột dãn tới mép phải của nó.")]
    [SerializeField, OnValueChanged(nameof(Build))] private Renderer leftBound;
    [BoxGroup("Bề rộng"), LabelText("Mép phải"), Tooltip("Renderer bên phải (bãi cỏ). Cột dãn tới mép trái của nó.")]
    [SerializeField, OnValueChanged(nameof(Build))] private Renderer rightBound;
    [BoxGroup("Bề rộng"), LabelText("Rộng khi thiếu mép (unit)")]
    [SerializeField, Min(0.1f), OnValueChanged(nameof(Build))] private float fallbackWidth = 10f;
    [BoxGroup("Bề rộng"), LabelText("Lề trái/phải (unit)")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float paddingX = 0.2f;

    [BoxGroup("Cột"), LabelText("Số cột")]
    [SerializeField, Min(1), OnValueChanged(nameof(Build))] private int columnCount = 4;
    [BoxGroup("Cột"), LabelText("Space X (unit)"), Tooltip("Khoảng cách giữa 2 cột liền nhau. Vạch chia nằm giữa khoảng này.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float columnSpacingX = 0.3f;
    [BoxGroup("Cột"), LabelText("Cao cột (unit)"), Tooltip("Tính từ pivot xuống dưới.")]
    [SerializeField, Min(0.1f), OnValueChanged(nameof(Build))] private float columnHeight = 9f;

    [BoxGroup("Vạch chia"), LabelText("Dày vạch (unit)")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float dividerWidth = 0.08f;
    [BoxGroup("Vạch chia"), LabelText("Màu vạch")]
    [SerializeField, OnValueChanged(nameof(Build))] private Color dividerColor = Color.white;
    [FoldoutGroup("Art"), Tooltip("Tuỳ chọn: lấy màu viền từ Road_Vertical. Trống = màu trắng thuần (nhân với Màu vạch).")]
    [SerializeField] private Sprite profile;
    [FoldoutGroup("Art"), Tooltip("Sprite-Unlit-Default (không texture) → vạch trắng.")]
    [SerializeField] private Material material;
    [FoldoutGroup("Art")][SerializeField] private int sortingOrder = 1;
    [FoldoutGroup("Art"), Tooltip("Nền asphalt phía dưới cột (asphalt-texture_0), giống Board-Fill trong road. Trống = không có nền.")]
    [SerializeField, OnValueChanged(nameof(Build))] private Sprite fillSprite;
    [FoldoutGroup("Art")][SerializeField, OnValueChanged(nameof(Build))] private Material fillMaterial; // BoardMesh
    [FoldoutGroup("Art")][SerializeField] private int fillSortingOrder = -1;
    [FoldoutGroup("Art"), LabelText("Nền luồn dưới cỏ (unit)"), Tooltip("Nền kéo dài thêm vào dưới 2 bãi cỏ để bóng của cỏ đổ lên asphalt, không hở khe.")]
    [SerializeField, Min(0f), OnValueChanged(nameof(Build))] private float fillExtendX = 0.3f;

    [BoxGroup("Xe trong cột"), LabelText("Gốc xe"), Tooltip("Chứa các xe tên \"Car-{cột}-{hàng}\" (hàng 0 = trên cùng).")]
    [SerializeField, OnValueChanged(nameof(LayoutCars))] private Transform carsRoot;
    [BoxGroup("Xe trong cột"), LabelText("Space Y (unit)"), Tooltip("Khoảng hở giữa 2 xe liền nhau theo chiều dọc.")]
    [SerializeField, OnValueChanged(nameof(LayoutCars))] private float carSpacingY = 0.85f;

    // ================= LAYOUT =================

    public int ColumnCount => columnCount;
    public Transform CarsRoot => carsRoot;
    public float ColumnSpacingX => columnSpacingX;

    /// <summary>Kích thước 1 cột (local unit) — rộng đã dãn theo bề rộng vùng.</summary>
    public Vector2 ColumnSize => new(ColumnWidth, columnHeight);

    /// <summary>Tâm cột i (local).</summary>
    public Vector3 GetColumnCenter(int i)
    {
        GetRange(out float x0, out _);
        return new Vector3(x0 + ColumnWidth * 0.5f + i * (ColumnWidth + columnSpacingX), -columnHeight * 0.5f, 0f);
    }

    /// <summary>Tâm cột i (world).</summary>
    public Vector3 GetColumnCenterWorld(int i) => transform.TransformPoint(GetColumnCenter(i));

    private float ColumnWidth
    {
        get
        {
            GetRange(out float x0, out float x1);
            return Mathf.Max(0.01f, (x1 - x0 - (columnCount - 1) * columnSpacingX) / columnCount);
        }
    }

    /// <summary>Khoảng X (local) cột được phép chiếm: giữa mép trong 2 bãi cỏ, trừ lề.</summary>
    private void GetRange(out float x0, out float x1)
    {
        float left = -fallbackWidth * 0.5f, right = fallbackWidth * 0.5f;
        Vector3 p = transform.position;
        if (leftBound != null) left = transform.InverseTransformPoint(new Vector3(leftBound.bounds.max.x, p.y, p.z)).x;
        if (rightBound != null) right = transform.InverseTransformPoint(new Vector3(rightBound.bounds.min.x, p.y, p.z)).x;
        if (right < left) (left, right) = (right, left);

        x0 = left + paddingX;
        x1 = Mathf.Max(x0 + 0.01f, right - paddingX);
    }

    private float DividerX(int i)
    {
        GetRange(out float x0, out _);
        return x0 + (i + 1) * ColumnWidth + (i + 0.5f) * columnSpacingX;
    }

    // ================= BUILD =================

    [Button("Build", ButtonSizes.Large), GUIColor(0.55f, 0.85f, 0.55f), PropertyOrder(-1)]
    [ContextMenu("Build")]
    public void Build()
    {
        Clear();
        var root = new GameObject(GeneratedName).transform;
        root.SetParent(transform, false);

        BuildFill(root);
        BuildDividers(root);
        LayoutCars();
    }

    /// <summary>
    /// Xếp xe trong carsRoot vào cột: giữa cột theo X, hàng 0 sát mép trên, mỗi hàng cách nhau Space Y.
    /// Không đổi scale xe (giữ cùng cỡ xe trên board).
    /// Vị trí tính theo bounds renderer nên đúng với mọi hướng xoay của xe.
    /// </summary>
    [BoxGroup("Xe trong cột"), Button("Xếp xe")]
    public void LayoutCars()
    {
        if (carsRoot == null) return;

        var cars = new List<(int c, int i, Transform t, Renderer r)>();
        foreach (Transform t in carsRoot)
        {
            string[] parts = t.name.Split('-');
            if (parts.Length < 3 || parts[0] != "Car") continue;
            if (!int.TryParse(parts[1], out int c) || !int.TryParse(parts[2], out int i) || c >= columnCount) continue;
            Renderer r = t.GetComponentInChildren<Renderer>();
            if (r != null) cars.Add((c, i, t, r));
        }
        if (cars.Count == 0) return;

        Vector3 top = transform.position;
        foreach (var e in cars)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RecordObject(e.t, "Layout Cars");
#endif
            Bounds b = e.r.bounds;
            float len = b.size.z;
            float x = GetColumnCenterWorld(e.c).x;
            float zTop = top.z - e.i * (len + carSpacingY);
            e.t.position += new Vector3(x - b.center.x, 0f, zTop - b.max.z);
        }
    }

    /// <summary>Nền asphalt lấp kín vùng giữa 2 mép (không trừ lề), cao bằng cột.</summary>
    private void BuildFill(Transform root)
    {
        if (fillSprite == null || fillMaterial == null) return;

        GetRange(out float x0, out float x1);
        x0 -= paddingX + fillExtendX; x1 += paddingX + fillExtendX;

        Rect r = fillSprite.textureRect;
        Texture t = fillSprite.texture;
        Vector2 Uv(float u, float v) => new((r.x + u * r.width) / t.width, (r.y + v * r.height) / t.height);

        var v3 = new List<Vector3>
        {
            new(x0, 0f, 0f), new(x1, 0f, 0f), new(x1, -columnHeight, 0f), new(x0, -columnHeight, 0f)
        };
        var uv = new List<Vector2> { Uv(0f, 1f), Uv(1f, 1f), Uv(1f, 0f), Uv(0f, 0f) };
        CreateMesh(root, "Fill", v3, uv, new List<int> { 0, 1, 2, 0, 2, 3 }, fillMaterial, fillSortingOrder);
    }

    private void BuildDividers(Transform root)
    {
        if (columnCount < 2 || dividerWidth <= 0f) return;
        if (material == null)
        {
            Debug.LogError("[ColumnAreaBuilder] Thiếu material vạch chia.", this);
            return;
        }

        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();

        Vector2 c = Vector2.zero;
        if (profile != null)
        {
            Rect r = profile.textureRect;
            Texture t = profile.texture;
            c = new Vector2((r.x + DividerCol) / t.width, (r.y + r.height - CapRow) / t.height);
        }
        float half = dividerWidth * 0.5f;

        for (int i = 0; i < columnCount - 1; i++)
        {
            float x = DividerX(i);
            int k = v.Count;
            v.Add(new Vector3(x - half, 0f, 0f));
            v.Add(new Vector3(x + half, 0f, 0f));
            v.Add(new Vector3(x + half, -columnHeight, 0f));
            v.Add(new Vector3(x - half, -columnHeight, 0f));
            uv.Add(c); uv.Add(c); uv.Add(c); uv.Add(c);
            tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
            tri.Add(k); tri.Add(k + 2); tri.Add(k + 3);
        }

        CreateMesh(root, "Dividers", v, uv, tri, material, sortingOrder, dividerColor);
    }

    private static void CreateMesh(Transform parent, string name, List<Vector3> v, List<Vector2> uv, List<int> tri,
        Material mat, int order, Color? color = null)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        var colors = new Color32[v.Count];
        Color32 col = color ?? Color.white;
        for (int i = 0; i < colors.Length; i++) colors[i] = col;
        mesh.colors32 = colors;
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
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

    private static void SafeDestroy(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    private void OnDrawGizmos()
    {
        if (columnCount < 1) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Vector3 size = new Vector3(ColumnWidth, columnHeight, 0f);
        for (int i = 0; i < columnCount; i++)
        {
            Vector3 c = GetColumnCenter(i);
            Gizmos.color = new Color(1f, 0f, 0f, 0.12f);
            Gizmos.DrawCube(c, size);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(c, size);
        }
    }
}
