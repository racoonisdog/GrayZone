using UnityEngine;

/// <summary>
/// 미니맵 위에서 카메라가 바라보는 방향으로 UI를 회전 (부모 마커 회전과 무관)
/// </summary>
public class MinimapCameraDirection : MonoBehaviour
{
    [Header("World")]
    [Tooltip("카메라 좌우 방향 기준 (Player/CameraTarget). 비워두면 씬의 CameraLook을 자동 탐색")]
    [SerializeField] private Transform cameraTarget;
    [Tooltip("맵 영역 기준 Transform (Shelter_Inner_Perimeter_Fences). 비워두면 이름으로 자동 탐색")]
    [SerializeField] private Transform mapSpace;
    [Tooltip("MinimapPlayerMarker의 World Aligned와 같은 값으로 설정")]
    [SerializeField] private bool worldAligned;

    [Header("UI")]
    [Tooltip("탑뷰 이미지 RectTransform. 비워두면 MinimapPlayerMarker의 부모를 사용")]
    [SerializeField] private RectTransform mapRect;
    [Tooltip("부모(플레이어 마커) 중심에서 떨어진 거리 (미니맵 UI 단위). 0이면 제자리 회전")]
    [SerializeField] private float orbitRadius = 20f;
    [Tooltip("궤도 위에서 이미지도 카메라 방향을 향하도록 회전")]
    [SerializeField] private bool faceDirection = true;

    private void Awake()
    {
        if (cameraTarget == null)
        {
            CameraLook cameraLook = FindFirstObjectByType<CameraLook>();
            if (cameraLook != null)
                cameraTarget = cameraLook.transform;
        }

        if (mapSpace == null)
        {
            GameObject fences = GameObject.Find("Shelter_Inner_Perimeter_Fences");
            if (fences != null)
                mapSpace = fences.transform;
        }

        if (mapRect == null)
        {
            MinimapPlayerMarker marker = GetComponentInParent<MinimapPlayerMarker>();
            Transform mapParent = marker != null ? marker.transform.parent : transform.parent;
            mapRect = mapParent as RectTransform;
        }
    }

    private void LateUpdate()
    {
        if (cameraTarget == null || mapRect == null)
            return;

        float mapYaw = worldAligned || mapSpace == null ? 0f : mapSpace.eulerAngles.y;
        float yaw = cameraTarget.eulerAngles.y - mapYaw - MinimapPlayerMarker.NorthYaw;

        Quaternion mapRotation = Quaternion.Euler(0f, 0f, -yaw);

        // 부모 회전을 무시하고 부모 중심 기준 원 위의 절대 위치로 덮어씀
        Vector3 offset = mapRotation * Vector3.up * orbitRadius;
        transform.position = transform.parent.position + mapRect.TransformVector(offset);

        transform.rotation = faceDirection ? mapRect.rotation * mapRotation : mapRect.rotation;
    }
}
