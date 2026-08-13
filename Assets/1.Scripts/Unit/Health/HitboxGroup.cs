using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 유닛의 부위별 히트박스를 묶어, 필요한 순간에만 켜고 끄는 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 부위 히트박스는 애니메이션 뼈에 붙어 있어 <b>매 프레임 위치가 바뀝니다.</b> 켜 두면 개체 하나당 십수 개의
/// 콜라이더가 매 프레임 브로드페이즈 경계를 갱신하고, 그 비용이 개체 수만큼 곱해집니다.
/// 실제로 필요한 순간은 <b>사격 레이가 그 개체를 지나갈 때뿐</b>이므로, 평소에는 꺼 두고 그때만 켭니다.
///
/// <para>
/// <b>전신 판정은 이동용 캡슐이 대신합니다.</b> 이동 캡슐은 어차피 켜져 있어야 하고(개체끼리 밀어내기),
/// 몸 전체를 감싸므로 "이 개체를 지나갔는가"를 판정하기에 충분합니다. 그래서 별도의 전신 콜라이더를
/// 새로 두지 않았습니다.
/// </para>
///
/// <para>
/// <b>사망 경로와 겹치지 않습니다.</b> 사망 시 <see cref="RagdollController"/>가 게임플레이 콜라이더를
/// 통째로 끄고 래그돌 뼈 콜라이더를 켭니다. 이 컴포넌트는 원래 상태를 기억했다가 되돌리는 방식이 아니라
/// "지금 켜라/꺼라"만 수행하므로, 사망 후 이 컴포넌트가 다시 켜는 일이 없도록
/// <see cref="IsUsable"/>로 사망 여부를 함께 봅니다.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public class HitboxGroup : MonoBehaviour
{
    [Tooltip("이 유닛의 부위별 히트박스입니다. 비어 있으면 자식에서 자동으로 모읍니다.")]
    [SerializeField] private Collider[] m_hitboxes;

    [Tooltip("게임 시작 시 부위 히트박스를 꺼 둘지 여부입니다. 끄면 사격 레이가 지나갈 때만 켜집니다.")]
    [SerializeField] private bool m_disableOnStart = true;

    /// <summary>지금 부위 히트박스가 켜져 있는지 여부입니다.</summary>
    private bool m_enabled;

    /// <summary>이 유닛의 체력 컴포넌트입니다. 사망 여부 확인에 씁니다.</summary>
    private IDamageable m_owner;

    /// <summary>부위 히트박스를 켜고 끌 수 있는 상태인지 여부입니다.</summary>
    /// <remarks>사망한 유닛은 래그돌이 콜라이더를 소유하므로 건드리지 않습니다.</remarks>
    public bool IsUsable => m_owner == null || !m_owner.IsDead;

    private void Awake()
    {
        m_owner = GetComponentInParent<IDamageable>();

        if (m_hitboxes == null || m_hitboxes.Length == 0)
        {
            CollectHitboxes();
        }

        // 직렬화된 Collider.enabled와 런타임 캐시를 먼저 맞춥니다. 기본값 false만 믿으면
        // 프리팹에 실수로 켜진 히트박스가 있어도 아래 SetHitboxesEnabled(false)가 조기 반환합니다.
        m_enabled = HasEnabledHitbox();

        if (m_disableOnStart)
        {
            SetHitboxesEnabled(false);
        }
    }

    /// <summary>관리 대상 중 하나라도 켜져 있는지 확인합니다.</summary>
    private bool HasEnabledHitbox()
    {
        if (m_hitboxes == null)
        {
            return false;
        }

        for (int i = 0; i < m_hitboxes.Length; i++)
        {
            if (m_hitboxes[i] != null && m_hitboxes[i].enabled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>자식에서 <see cref="Hitbox"/>가 붙은 콜라이더를 모읍니다.</summary>
    /// <remarks>
    /// <see cref="Hitbox"/>가 붙은 것만 모읍니다. 이동 캡슐이나 근접 판정 콜라이더까지 끄면
    /// 개체가 서로를 통과하거나 공격이 사라집니다.
    ///
    /// <see cref="Hitbox.AlwaysActive"/>가 켜진 부위는 제외합니다. 그 부위는 몸통 캡슐 밖으로 나가
    /// 1차 필터에 걸리지 않으므로, 껐다 켜는 대상이 되면 영영 맞지 않습니다.
    /// </remarks>
    private void CollectHitboxes()
    {
        List<Collider> found = new List<Collider>();

        foreach (Hitbox hitbox in GetComponentsInChildren<Hitbox>(true))
        {
            if (hitbox.AlwaysActive || !hitbox.TryGetComponent(out Collider collider))
            {
                continue;
            }

            found.Add(collider);
        }

        m_hitboxes = found.ToArray();
    }

    /// <summary>
    /// 부위 히트박스를 한꺼번에 켜거나 끕니다.
    /// </summary>
    /// <param name="enabled">켜면 true입니다.</param>
    /// <remarks>
    /// <c>GameObject.SetActive</c>가 아니라 <see cref="Collider.enabled"/>를 씁니다. 활성 전환은 그 오브젝트의
    /// 모든 컴포넌트에 생명주기 콜백을 돌리고 자식까지 번지지만, 콜라이더 플래그는 물리 씬의 도형 하나만
    /// 등록·해제합니다. 우리가 원하는 것이 정확히 후자입니다.
    ///
    /// 이미 같은 상태면 아무것도 하지 않습니다. 연사처럼 같은 프레임에 여러 번 불릴 수 있어,
    /// 중복 호출이 물리 씬을 흔들지 않게 합니다.
    /// </remarks>
    public void SetHitboxesEnabled(bool enabled)
    {
        if (m_enabled == enabled || m_hitboxes == null)
        {
            return;
        }

        if (enabled && !IsUsable)
        {
            return;
        }

        for (int i = 0; i < m_hitboxes.Length; i++)
        {
            Collider hitbox = m_hitboxes[i];
            if (hitbox != null)
            {
                hitbox.enabled = enabled;
            }
        }

        m_enabled = enabled;
    }
}
