using UnityEngine;

/// <summary>셸터 체크포인트 ID와 Scene에 배치된 플레이어 스폰 위치를 연결합니다.</summary>
public sealed class ShelterPlayerSpawnPoints : MonoBehaviour
{
    [SerializeField] private Transform m_beforeDefense1;
    [SerializeField] private Transform m_afterDefense1Clear;

    public Transform BeforeDefense1 => m_beforeDefense1;
    public Transform AfterDefense1Clear => m_afterDefense1Clear;

    public bool TryGetSpawnPoint(
        ShelterCheckpointId checkpointId,
        out Transform spawnPoint)
    {
        spawnPoint = checkpointId switch
        {
            ShelterCheckpointId.BeforeDefense1 => m_beforeDefense1,
            ShelterCheckpointId.AfterDefense1Clear => m_afterDefense1Clear,
            _ => null
        };

        return spawnPoint != null;
    }
}
