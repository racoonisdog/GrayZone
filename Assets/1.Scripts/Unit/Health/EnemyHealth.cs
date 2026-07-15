using System;
using UnityEngine;
using VInspector;

/// <summary>
/// Enemy 전용 체력 컴포넌트입니다.
/// </summary>
/// <remarks>
/// 현재는 <see cref="HealthSystemBase"/>의 피해, 사망 이벤트, HP 처리를 그대로 사용합니다.
/// </remarks>
public class EnemyHealth : HealthSystemBase
{
    /// <summary>적 체력 컴포넌트가 활성화되어 배틀 집계 대상으로 진입했을 때 발생합니다.</summary>
    public static event Action<EnemyHealth> OnEnemyEnabled;

    /// <summary>현재 적 인스턴스의 활성화를 배틀 씬 데이터 수집기에 알립니다.</summary>
    private void OnEnable()
    {
        OnEnemyEnabled?.Invoke(this);
    }

#if UNITY_EDITOR
    [Foldout("Debug")]
    [Tooltip("켜면 Editor에서 피해/사망 로그를 출력합니다. Player 빌드에서는 호출 자체가 제거됩니다.")]
    [SerializeField] private bool m_debugLogHealth = false;

    protected override bool DebugLogHealthEnabled => m_debugLogHealth;
#endif
}
