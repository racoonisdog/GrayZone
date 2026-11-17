using System;
using UnityEngine;

/// <summary>
/// 맵 가장자리 투명벽(MapGuard)에 조작 중인 캐릭터가 부딪히면, 그 캐릭터가 복귀하자는 대사를 한 줄 합니다.
/// </summary>
/// <remarks>
/// 투명벽은 트리거가 아닌 콜라이더이고 플레이어는 CharacterController로 움직여 벽 쪽에는 충돌 이벤트가 오지 않습니다.
/// 그래서 매 프레임 조작 중인 캐릭터의 캡슐이 벽 콜라이더에 닿았는지 거리로 봅니다.
/// 대사는 튜토리얼과 같은 대화창(<see cref="DialoguePlayer"/>)에 띄우고, 다른 대사가 나오는 중이면 끊지 않고 건너뜁니다. 튜토리얼 중에는 말하지 않습니다.
/// 쿨타임은 모든 투명벽이 같이 씁니다(터널·다리 벽을 번갈아 부딪혀도 연달아 말하지 않습니다).
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class MapGuardReturnLine : MonoBehaviour
{
    [Serializable]
    private struct CharacterLine
    {
        [Tooltip("이 대사를 하는 캐릭터입니다.")]
        public PlayerbleCharacterId characterId;

        [Tooltip("대화창에 표시할 화자입니다.")]
        public DialogueSpeaker speaker;

        [Tooltip("벽에 부딪혔을 때 할 대사입니다.")]
        [TextArea] public string text;
    }

    [Tooltip("캐릭터별 대사입니다. 목록에 없는 캐릭터는 말하지 않습니다.")]
    [SerializeField] private CharacterLine[] m_lines = Array.Empty<CharacterLine>();

    [Tooltip("한 번 말한 뒤 다시 말할 수 있을 때까지의 시간(초, 실제 시간)입니다. 모든 투명벽이 같이 씁니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_cooldownSeconds = 3.0f;

    [Tooltip("캐릭터 캡슐과 벽 사이가 이 거리(m) 이하이면 부딪힌 것으로 봅니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_contactDistance = 0.1f;

    /// <summary>마지막으로 대사를 시작한 시각(실제 시간)입니다. 모든 투명벽이 같이 씁니다.</summary>
    private static float s_lastSpokenTime = float.NegativeInfinity;

    private Collider m_collider;
    private DialoguePlayer m_dialogue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_lastSpokenTime = float.NegativeInfinity;
    }

    private void Awake()
    {
        m_collider = GetComponent<Collider>();
    }

    private void Update()
    {
        // 튜토리얼 중에는 말하지 않습니다. 튜토리얼 대사와 겹치고, 튜토리얼이 이동 범위를 따로 안내하기 때문입니다.
        if (TutorialFlowDirector.IsAnyRunning
            || Time.unscaledTime - s_lastSpokenTime < m_cooldownSeconds
            || !IsTouchedByPlayer(out PlayerbleCharacterId characterId))
        {
            return;
        }

        if (m_dialogue == null)
        {
            m_dialogue = FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
        }

        // 튜토리얼 대사 등 다른 대사가 나오는 중이면 끊지 않습니다. 쿨타임도 쓰지 않아 대사가 끝난 뒤 다시 부딪히면 말합니다.
        if (m_dialogue == null || m_dialogue.IsPlaying || !TryGetLine(characterId, out CharacterLine line))
        {
            return;
        }

        s_lastSpokenTime = Time.unscaledTime;
        m_dialogue.PlayLine(new DialogueLine(line.speaker, line.text, DialogueAdvanceMode.Auto, 0.0f), false, null);
    }

    /// <summary>조작 중인 캐릭터의 캡슐이 이 벽에 닿았는지 봅니다.</summary>
    private bool IsTouchedByPlayer(out PlayerbleCharacterId characterId)
    {
        characterId = PlayerbleCharacterId.Unknown;
        SquadManager squad = SquadManager.Instance;
        SquadMemberController member = squad != null ? squad.PlayerSquadMember : null;
        PlayerbleUnitData data = squad != null ? squad.PlayerSquadMemberData : null;
        if (member == null || data == null || m_collider == null || !m_collider.enabled)
        {
            return false;
        }

        CharacterController controller = member.GetComponent<CharacterController>();
        Vector3 center = controller != null ? member.transform.TransformPoint(controller.center) : member.transform.position + Vector3.up;
        float radius = controller != null ? controller.radius : 0.5f;
        float halfHeight = controller != null ? Mathf.Max(0.0f, controller.height * 0.5f - radius) : 0.5f;

        // 캡슐 축 위에서 벽에 가장 가까운 점을 잡고, 그 점과 벽 사이 거리를 반지름과 비교합니다.
        Vector3 bottom = center - Vector3.up * halfHeight;
        Vector3 top = center + Vector3.up * halfHeight;
        Vector3 wallPoint = m_collider.ClosestPoint(center);
        Vector3 axisPoint = ClosestPointOnSegment(bottom, top, wallPoint);
        Vector3 closest = m_collider.ClosestPoint(axisPoint);
        if ((closest - axisPoint).sqrMagnitude > (radius + m_contactDistance) * (radius + m_contactDistance))
        {
            return false;
        }

        characterId = data.CharacterId;
        return true;
    }

    private bool TryGetLine(PlayerbleCharacterId characterId, out CharacterLine line)
    {
        foreach (CharacterLine entry in m_lines)
        {
            if (entry.characterId == characterId && !string.IsNullOrWhiteSpace(entry.text))
            {
                line = entry;
                return true;
            }
        }

        line = default;
        return false;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            return a;
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared);
        return a + ab * t;
    }
}
