using UnityEngine;

/// <summary>
/// 튜토리얼 이동 안내의 도착 범위입니다. 같은 오브젝트의 콜라이더(Capsule·Sphere·Box)가 범위이고, 표시용 이펙트는 그 범위에 맞춰 놓입니다.
/// </summary>
/// <remarks>
/// 도착 조건(ReachArea·AllyInArea)과 경로선 안내(경로선 숨기기·다시 띄우기)가 모두 이 범위로 도착을 판정합니다.
/// 판정은 물리 충돌이 아니라 콜라이더 값(위치·중심·반경·크기)으로 직접 계산하고, 높이는 보지 않습니다. 그래서 Play 중에는 콜라이더를 꺼서 다른 물리 판정에 끼지 않게 합니다.
/// 이 오브젝트는 켜 둔 채로 두고, 경로선이 떠 있는 동안 표시용 이펙트만 켭니다.
/// </remarks>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TutorialArriveArea : MonoBehaviour
{
    [Tooltip("도착 범위를 보여 줄 이펙트(자식 오브젝트)입니다. 씬에서는 꺼 두고, 경로선이 떠 있는 동안만 켭니다.")]
    [SerializeField] private Transform m_visual;

    [Tooltip("켜면 이펙트의 위치와 가로 크기를 콜라이더에 1:1로 맞춥니다. 이펙트는 지름 1(스케일 1일 때) 기준 원형이어야 합니다(기본 Cylinder).")]
    [SerializeField] private bool m_fitVisualToCollider = true;

    [Tooltip("이펙트를 콜라이더 바닥에서 이만큼(m) 띄웁니다. 바닥에 묻혀 깜빡이지 않게 합니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_visualLift = 0.02f;

    private Collider m_collider;

    /// <summary>범위 바닥 가운데(월드 좌표)입니다. 경로선이 이 지점으로 안내합니다.</summary>
    public Vector3 GroundCenter
    {
        get
        {
            Collider area = ResolveCollider();
            return area switch
            {
                CapsuleCollider capsule => transform.TransformPoint(capsule.center - Vector3.up * (capsule.height * 0.5f)),
                SphereCollider sphere => transform.TransformPoint(sphere.center - Vector3.up * sphere.radius),
                BoxCollider box => transform.TransformPoint(box.center - Vector3.up * (box.size.y * 0.5f)),
                _ => transform.position,
            };
        }
    }

    /// <summary>범위를 원으로 볼 때의 대략적인 반경(m)입니다. Scene 뷰 표시와 요약 문구에 씁니다.</summary>
    public float ApproxRadius
    {
        get
        {
            Collider area = ResolveCollider();
            float scale = HorizontalScale;
            return area switch
            {
                CapsuleCollider capsule => capsule.radius * scale,
                SphereCollider sphere => sphere.radius * scale,
                BoxCollider box => Mathf.Max(box.size.x * Mathf.Abs(transform.lossyScale.x), box.size.z * Mathf.Abs(transform.lossyScale.z)) * 0.5f,
                _ => 0.0f,
            };
        }
    }

    private float HorizontalScale => Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));

    /// <summary>하이어라키 경로로 도착 범위를 찾습니다. 꺼져 있는 오브젝트도 찾습니다. 경로가 비었거나 컴포넌트가 없으면 null입니다.</summary>
    public static TutorialArriveArea Find(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        GameObject found = TutorialEvents.FindSceneObject(path);
        return found != null ? found.GetComponent<TutorialArriveArea>() : null;
    }

    /// <summary>
    /// 월드 위치가 범위 안인지 여부입니다. 높이는 보지 않습니다.
    /// </summary>
    /// <param name="point">판정할 월드 위치입니다.</param>
    /// <param name="margin">범위를 사방으로 이만큼(m) 넓혀 봅니다. 벗어났는지 볼 때 경계에서 깜빡이지 않게 씁니다.</param>
    /// <remarks>Capsule은 세운 원기둥으로 봅니다(방향은 Y축 기준).</remarks>
    public bool Contains(Vector3 point, float margin = 0.0f)
    {
        Collider area = ResolveCollider();
        switch (area)
        {
            case CapsuleCollider capsule:
                return IsWithinCircle(point, capsule.center, capsule.radius * HorizontalScale + margin);

            case SphereCollider sphere:
                return IsWithinCircle(point, sphere.center, sphere.radius * HorizontalScale + margin);

            case BoxCollider box:
                Vector3 local = transform.InverseTransformPoint(point) - box.center;
                float scaleX = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
                float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
                return Mathf.Abs(local.x) <= box.size.x * 0.5f + margin / scaleX
                    && Mathf.Abs(local.z) <= box.size.z * 0.5f + margin / scaleZ;

            default:
                return false;
        }
    }

    /// <summary>표시용 이펙트를 켜거나 끕니다.</summary>
    public void SetVisualVisible(bool visible)
    {
        if (m_visual != null && m_visual.gameObject.activeSelf != visible)
        {
            m_visual.gameObject.SetActive(visible);
        }
    }

    private void Awake()
    {
        if (Application.isPlaying)
        {
            // 판정은 콜라이더 값으로 직접 하므로 물리 충돌은 쓰지 않습니다. 켜 두면 레이캐스트·트리거 판정에 끼어듭니다.
            ResolveCollider().enabled = false;
            SetVisualVisible(false);
        }
    }

    private void OnValidate()
    {
        FitVisual();
    }

    private void Update()
    {
        // Scene 뷰에서 콜라이더를 고치면 이펙트가 바로 따라오게 합니다. Play 중에는 움직이지 않으므로 하지 않습니다.
        if (!Application.isPlaying)
        {
            FitVisual();
        }
    }

    private void FitVisual()
    {
        if (!m_fitVisualToCollider || m_visual == null || m_visual.parent != transform)
        {
            return;
        }

        Collider area = ResolveCollider();
        Vector3 bottom;
        Vector2 size;
        switch (area)
        {
            case CapsuleCollider capsule:
                bottom = capsule.center - Vector3.up * (capsule.height * 0.5f);
                size = Vector2.one * capsule.radius * 2.0f;
                break;

            case SphereCollider sphere:
                bottom = sphere.center - Vector3.up * sphere.radius;
                size = Vector2.one * sphere.radius * 2.0f;
                break;

            case BoxCollider box:
                bottom = box.center - Vector3.up * (box.size.y * 0.5f);
                size = new Vector2(box.size.x, box.size.z);
                break;

            default:
                return;
        }

        Vector3 position = bottom + Vector3.up * (m_visualLift / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y)));
        if (m_visual.localPosition != position)
        {
            m_visual.localPosition = position;
        }

        Vector3 scale = new(size.x, m_visual.localScale.y, size.y);
        if (m_visual.localScale != scale)
        {
            m_visual.localScale = scale;
        }
    }

    private bool IsWithinCircle(Vector3 point, Vector3 localCenter, float radius)
    {
        Vector3 offset = point - transform.TransformPoint(localCenter);
        offset.y = 0.0f;
        return offset.sqrMagnitude <= radius * radius;
    }

    private Collider ResolveCollider()
    {
        if (m_collider == null)
        {
            m_collider = GetComponent<Collider>();
        }

        return m_collider;
    }
}
