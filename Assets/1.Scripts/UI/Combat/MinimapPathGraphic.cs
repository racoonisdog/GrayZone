using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 위에 점들을 차례로 잇는 꺾은선을 그립니다. 미니맵의 경로선 표시에 씁니다.
/// </summary>
/// <remarks>
/// 구간마다 사각형 하나를 만들어 잇습니다. 꺾이는 곳은 둥글리지 않습니다(임시 표시용).
/// 점 좌표는 이 RectTransform의 로컬 좌표입니다.
/// </remarks>
[RequireComponent(typeof(CanvasRenderer))]
public class MinimapPathGraphic : MaskableGraphic
{
    [Tooltip("선 두께(UI 단위)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_thickness = 2.0f;

    private readonly List<Vector2> m_points = new List<Vector2>();

    /// <summary>선 두께(UI 단위)입니다.</summary>
    public float Thickness
    {
        get => m_thickness;
        set
        {
            m_thickness = Mathf.Max(0.1f, value);
            SetVerticesDirty();
        }
    }

    /// <summary>그릴 점을 바꿉니다. 점이 둘 미만이면 아무것도 그리지 않습니다.</summary>
    /// <param name="points">이 RectTransform의 로컬 좌표 점입니다.</param>
    public void SetPoints(List<Vector2> points)
    {
        m_points.Clear();
        if (points != null)
        {
            m_points.AddRange(points);
        }

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float half = m_thickness * 0.5f;
        for (int i = 1; i < m_points.Count; i++)
        {
            Vector2 a = m_points[i - 1];
            Vector2 b = m_points[i];
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            dir.Normalize();
            Vector2 normal = new Vector2(-dir.y, dir.x) * half;
            // 끝을 두께 절반만큼 늘려 꺾이는 곳의 틈을 메웁니다.
            Vector2 ext = dir * half;

            int start = vh.currentVertCount;
            vh.AddVert(a - ext - normal, color, Vector2.zero);
            vh.AddVert(a - ext + normal, color, Vector2.zero);
            vh.AddVert(b + ext + normal, color, Vector2.zero);
            vh.AddVert(b + ext - normal, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
