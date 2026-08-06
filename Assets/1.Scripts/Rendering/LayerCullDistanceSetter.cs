using UnityEngine;

/// <summary>
/// 레이어별로 카메라 컬링 거리를 개별 지정합니다.
/// Camera.layerCullDistances는 인스펙터 UI가 없어 스크립트로만 설정할 수 있습니다.
/// 지정한 거리를 넘어선 오브젝트는 컬링 단계에서 제외되어 드로우콜 자체가 발생하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public sealed class LayerCullDistanceSetter : MonoBehaviour
{
    [System.Serializable]
    public struct LayerDistance
    {
        [Tooltip("컬링 거리를 개별 지정할 레이어 이름입니다.")]
        public string LayerName;

        [Tooltip("이 거리를 넘어가면 그리지 않습니다. 0이면 카메라 Far Clip Plane을 그대로 사용합니다.")]
        [Min(0f)] public float Distance;

        public LayerDistance(string layerName, float distance)
        {
            LayerName = layerName;
            Distance = distance;
        }
    }

    [Header("Settings")]
    [Tooltip("레이어별 컬링 거리 목록입니다. 여기에 없는 레이어는 Far Clip Plane을 사용합니다.")]
    [SerializeField] private LayerDistance[] m_layerDistances =
    {
        new LayerDistance("Env_SmallProp", 40f),
        new LayerDistance("Env_Detail", 60f),
        new LayerDistance("Env_Interior", 100f),
    };

    [Tooltip("켜면 구(sphere) 기준으로 거리를 판정합니다. 끄면 카메라 평면 기준이라 화면 가장자리에서 팝핑이 보일 수 있습니다.")]
    [SerializeField] private bool m_useSphericalCulling = true;

    private Camera m_camera;

    private void OnEnable()
    {
        m_camera = GetComponent<Camera>();
        Apply();
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        m_camera = GetComponent<Camera>();
        Apply();
    }

    /// <summary>
    /// 런타임에 품질 옵션 등을 바꾼 뒤 다시 호출하면 컬링 거리가 갱신됩니다.
    /// </summary>
    public void Apply()
    {
        if (m_camera == null)
        {
            return;
        }

        // 인덱스는 레이어 번호와 1:1로 대응하며, 0은 "Far Clip Plane 사용"을 의미합니다.
        float[] distances = new float[32];

        for (int i = 0; i < m_layerDistances.Length; i++)
        {
            int layer = LayerMask.NameToLayer(m_layerDistances[i].LayerName);
            if (layer < 0)
            {
                continue;
            }

            distances[layer] = m_layerDistances[i].Distance;
        }

        m_camera.layerCullDistances = distances;
        m_camera.layerCullSpherical = m_useSphericalCulling;
    }
}
