using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 현재 필드의 적 진영 유닛에 적용할 시체 처리 설정을 감염체 종류별로 보관합니다.
/// </summary>
/// <remarks>
/// 적 개체는 사망 상태 진입 시 자기 <see cref="EnemyType"/>에 해당하는 슬롯을 한 번 읽습니다.
/// 이미 사망한 시체에는 이후 Inspector 변경이 소급 적용되지 않습니다.
/// 사망 연출 순서는 사망 즉시 래그돌 전환 → 시체 유지 시간 → 삭제입니다. 사망 애니메이션은 쓰지 않습니다.
///
/// 슬롯은 <see cref="EnemyType"/> 멤버와 1:1로 고정됩니다. 목록을 자유롭게 늘리는 구조가 아니라
/// enum 멤버 수를 그대로 따라가므로, 같은 종류를 두 번 넣거나 종류를 빼먹는 상태 자체가 생기지 않습니다.
/// 동기화는 <see cref="OnValidate"/>가 담당하고, Inspector의 추가·삭제 UI는 전용 에디터가 감춥니다.
///
/// 여기 값은 기획 밸런스 테이블 대상이 아니라 Inspector에서 직접 만지는 연출 설정입니다.
/// 밸런스 수치는 <see cref="EnemyBalanceSO"/>, 사운드·이펙트 같은 리소스 참조는 <see cref="EnemyFeedbackSO"/>가
/// 담당하므로 그 둘에는 이 값을 두지 않습니다.
///
/// 이 컴포넌트는 <see cref="FieldManager"/>와 같은 GameObject에 배치합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemyManager : MonoBehaviour
{
    /// <summary>
    /// 감염체 한 종류에 적용할 시체 처리 설정입니다.
    /// </summary>
    [Serializable]
    public sealed class Entry
    {
        // 어느 종류의 슬롯인지는 코드가 정합니다. Inspector에서 고칠 수 있으면 두 슬롯이 같은 종류를
        // 가리키는 상태를 만들 수 있어, 전용 에디터가 이 필드를 그리지 않습니다.
        [SerializeField] private EnemyType m_enemyType;

        [Tooltip("켜면 적 사망 후 시체 오브젝트를 지정한 시간이 지난 뒤 삭제합니다. 끄면 필드가 끝날 때까지 유지합니다.")]
        [SerializeField] private bool m_destroyCorpse = true;

        [Tooltip("시체 오브젝트를 삭제하기까지 기다리는 시간(초)입니다. 사망 시점부터 셉니다. 시체 삭제가 꺼져 있으면 사용하지 않습니다.")]
        [Min(0.0f)]
        [SerializeField] private float m_corpseLifetime = 3.0f;

        [Tooltip("켜면 사망 즉시 물리 골격이 준비된 적을 래그돌로 전환합니다. 끄거나 골격이 없으면 마지막 자세 그대로 굳습니다.")]
        [SerializeField] private bool m_useRagdoll = true;

        [Tooltip("이 종류가 사망할 때 시체가 받는 기본 충격량(N·s)입니다. 유효 질량 60kg 기준이라 240이면 약 4m/s로 날아갑니다. 무기 넉백이 이보다 크면 그쪽을 씁니다. 0이면 시체가 밀리지 않고 제자리에서 무너집니다.")]
        [Min(0.0f)]
        [FormerlySerializedAs("m_minKnockbackImpulse")]
        [SerializeField] private float m_deathKnockbackImpulse = 240.0f;

        /// <summary>이 설정을 적용할 감염체 종류입니다.</summary>
        public EnemyType EnemyType => m_enemyType;

        /// <summary>적 시체 오브젝트를 시간 경과 후 삭제할지 여부입니다.</summary>
        public bool DestroyCorpse => m_destroyCorpse;

        /// <summary>사망 시점부터 시체 오브젝트를 삭제하기까지의 시간(초)입니다.</summary>
        public float CorpseLifetime => Mathf.Max(0.0f, m_corpseLifetime);

        /// <summary>사망 즉시 래그돌로 전환할지 여부입니다.</summary>
        public bool UseRagdoll => m_useRagdoll;

        /// <summary>사망 시 시체가 받는 기본 충격량(N·s)입니다.</summary>
        /// <remarks>
        /// 무기가 정한 넉백과 비교해 큰 쪽을 씁니다. 기획 문서에 넉백 개념이 없어 무기 값은 0으로 두고,
        /// 실제 사망 연출 세기는 종류마다 이 값으로 조절합니다. 무기 넉백이 도입되면 그 값이 이 기본값을
        /// 넘어설 때만 우선합니다.
        /// </remarks>
        public float DeathKnockbackImpulse => Mathf.Max(0.0f, m_deathKnockbackImpulse);

        /// <summary>이 슬롯이 담당할 종류를 지정합니다. 동기화 코드만 사용합니다.</summary>
        internal void SetEnemyType(EnemyType value)
        {
            m_enemyType = value;
        }
    }

    [Header("Enemy Corpse Options")]
    [Tooltip("감염체 종류별 시체 설정입니다. 슬롯은 EnemyType 멤버와 1:1로 고정되어 추가·삭제·중복이 되지 않습니다. Unknown 슬롯은 종류를 지정하지 않은 개체가 씁니다.")]
    [SerializeField] private Entry[] m_perTypeSettings = Array.Empty<Entry>();

    /// <summary>컴포넌트를 처음 붙였을 때 종류별 슬롯을 채웁니다.</summary>
    private void Reset()
    {
        SyncEntriesToEnemyTypes();
    }

    /// <summary>
    /// Inspector 변경과 씬 로드 시 슬롯 구성을 <see cref="EnemyType"/>과 맞춥니다.
    /// </summary>
    /// <remarks>
    /// enum에 종류를 추가하면 여기서 슬롯이 늘어나므로 씬을 직접 손대지 않아도 됩니다.
    /// 에디터에서만 호출되는 함수라, 씬을 저장하지 않은 채 빌드하면 슬롯이 부족할 수 있습니다.
    /// 그 경우의 처리는 <see cref="Resolve"/>가 담당합니다.
    /// </remarks>
    private void OnValidate()
    {
        SyncEntriesToEnemyTypes();
    }

    /// <summary>
    /// 지정한 종류에 적용할 시체 설정을 찾습니다.
    /// </summary>
    /// <param name="enemyType">설정을 찾을 감염체 종류입니다.</param>
    /// <returns>해당 종류의 슬롯이며, 없으면 Unknown 슬롯입니다. null을 반환하지 않습니다.</returns>
    /// <remarks>
    /// 슬롯은 항상 enum 전체를 덮으므로 정상 상태에서는 첫 조회가 성공합니다.
    /// 뒤의 대비 경로는 종류를 추가한 뒤 씬을 저장하지 않고 빌드한 경우에만 쓰입니다.
    /// 슬롯 수가 종류 수만큼이고 사망 시 한 번만 호출되므로 선형 탐색으로 충분합니다.
    /// </remarks>
    public Entry Resolve(EnemyType enemyType)
    {
        Entry match = FindEntry(enemyType);
        if (match != null)
        {
            return match;
        }

        return FindEntry(EnemyType.Unknown) ?? new Entry();
    }

    /// <summary>지정한 종류를 담당하는 첫 슬롯을 찾습니다. 없으면 null입니다.</summary>
    private Entry FindEntry(EnemyType enemyType)
    {
        if (m_perTypeSettings == null)
        {
            return null;
        }

        for (int i = 0; i < m_perTypeSettings.Length; i++)
        {
            Entry entry = m_perTypeSettings[i];
            if (entry != null && entry.EnemyType == enemyType)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 슬롯 배열을 <see cref="EnemyType"/> 멤버와 1:1로 맞춥니다.
    /// </summary>
    /// <remarks>
    /// 종류마다 기존 슬롯을 이름이 아니라 종류 값으로 찾아 그대로 재사용하므로, enum 중간에 멤버가
    /// 끼어들어도 이미 조정해 둔 값이 다른 종류로 밀리지 않습니다.
    /// 같은 종류가 둘 이상이면 첫 슬롯만 남고, enum에서 사라진 종류의 슬롯은 버립니다.
    ///
    /// 구성이 이미 맞으면 배열을 다시 대입하지 않습니다. OnValidate가 매번 새 배열을 넣으면
    /// 아무것도 고치지 않았는데 씬이 변경 상태로 남습니다.
    /// </remarks>
    private void SyncEntriesToEnemyTypes()
    {
        EnemyType[] types = (EnemyType[])Enum.GetValues(typeof(EnemyType));

        bool alreadySynced = m_perTypeSettings != null && m_perTypeSettings.Length == types.Length;
        Entry[] synced = new Entry[types.Length];

        for (int i = 0; i < types.Length; i++)
        {
            Entry existing = FindEntry(types[i]);
            synced[i] = existing ?? new Entry();
            synced[i].SetEnemyType(types[i]);

            if (alreadySynced && !ReferenceEquals(m_perTypeSettings[i], synced[i]))
            {
                alreadySynced = false;
            }
        }

        if (alreadySynced)
        {
            return;
        }

        m_perTypeSettings = synced;
    }
}
