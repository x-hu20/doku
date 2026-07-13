using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

/// <summary>
/// 支持 UI 圆角矩形的 Image。继承 Image，重写 OnPopulateMesh 生成圆角矩形网格。
/// 纯色（Sprite 为空）与图片 Sprite 均支持；cornerRadius 控制四角圆角半径。
/// </summary>
[AddComponentMenu("UI/Rounded Image")]
public class RoundedImage : Image
{
    [SerializeField] private float cornerRadius = 12f;
    [SerializeField] [Range(2, 32)] private int cornerSegments = 8;

    // 圆角顶点复用缓冲：UGUI 重建在主线程同步顺序遍历 dirty Graphic、逐个调 OnPopulateMesh，
    // 不重入（一个未填完不会被另一个打断），故同一静态 List 可被各实例顺序复用。
    // 每次 populate 开头 Clear()（Count=0、capacity 保留），消除切关时 N 格各 new List 的分配。
    // 规划支持 10×10=100 格，单次峰值约 4×8×4+4=132 顶点，按 132 预分配避免扩容。
    private static readonly List<Vector2> _arcPoints = new List<Vector2>(132);

    public float CornerRadius
    {
        get => cornerRadius;
        set { cornerRadius = Mathf.Max(0f, value); SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;

        float radius = Mathf.Min(cornerRadius, r.width * 0.5f, r.height * 0.5f);
        // 半径过小则回退到普通矩形，避免退化几何
        if (radius <= 0.5f)
        {
            base.OnPopulateMesh(vh);
            return;
        }

        // Sprite 的 outer UV；无 Sprite 时用 0~1（纯色不受 UV 影响）
        Vector4 uv = sprite != null ? DataUtility.GetOuterUV(sprite) : new Vector4(0f, 0f, 1f, 1f);
        float u0 = uv.x, v0 = uv.y, u1 = uv.z, v1 = uv.w;
        Color32 col = color;

        // 四角圆心（顺时针：左上、右上、右下、左下）
        Vector2 tl = new Vector2(r.xMin + radius, r.yMax - radius);
        Vector2 tr = new Vector2(r.xMax - radius, r.yMax - radius);
        Vector2 br = new Vector2(r.xMax - radius, r.yMin + radius);
        Vector2 bl = new Vector2(r.xMin + radius, r.yMin + radius);

        var points = _arcPoints;
        points.Clear(); // 复用静态缓冲：Count 清零、capacity 保留，零分配
        AddArc(points, tl, 180f, 90f, radius);
        AddArc(points, tr, 90f, 0f, radius);
        AddArc(points, br, 0f, -90f, radius);
        AddArc(points, bl, -90f, -180f, radius);

        Vector2 center = r.center;
        // 中心顶点（索引 0），三角扇剖分（圆角矩形为凸形，扇形剖分正确）
        vh.AddVert(new Vector3(center.x, center.y, 0f), col, new Vector2((u0 + u1) * 0.5f, (v0 + v1) * 0.5f));

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = points[i];
            float u = Mathf.LerpUnclamped(u0, u1, (p.x - r.xMin) / r.width);
            float v = Mathf.LerpUnclamped(v0, v1, (p.y - r.yMin) / r.height);
            vh.AddVert(new Vector3(p.x, p.y, 0f), col, new Vector2(u, v));
        }

        int n = points.Count;
        for (int i = 0; i < n; i++)
        {
            int cur = 1 + i;
            int next = 1 + (i + 1) % n;
            vh.AddTriangle(0, cur, next);
        }
    }

    private void AddArc(List<Vector2> points, Vector2 center, float fromDeg, float toDeg, float radius)
    {
        for (int i = 0; i <= cornerSegments; i++)
        {
            float t = (float)i / cornerSegments;
            float deg = Mathf.Lerp(fromDeg, toDeg, t);
            float rad = deg * Mathf.Deg2Rad;
            points.Add(center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius);
        }
    }
}
