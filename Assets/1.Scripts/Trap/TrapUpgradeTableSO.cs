using System;
using UnityEngine;

/// <summary>
/// 셸터 Scramble 업그레이드 레벨에 따라 방어전 함정이 얼마나 강해지는지 모아 둔 표입니다.
/// </summary>
/// <remarks>
/// 레벨 0은 함정 프리팹에 적힌 기본값 그대로입니다. 배열의 첫 칸이 레벨 1, 두 번째 칸이 레벨 2입니다.
/// 레벨이 배열보다 높으면 마지막 칸을 씁니다. 셸터에서 최대 레벨을 올렸는데 표를 늘리지 않았을 때
/// 강화가 사라지는 것보다 마지막 값을 유지하는 편이 덜 이상하기 때문입니다.
///
/// 배율 항목은 프리팹 기본값에 곱하고, 내구도 항목은 프리팹의 최대 내구도에 정수로 더합니다.
/// 내구도는 "적 몇 마리를 더 버티는가"로 기획하므로 배율보다 정수가 읽기 쉽습니다.
///
/// 어떤 업그레이드가 어떤 함정에 붙는지:
/// <list type="bullet">
/// <item><see cref="ScrambleUpgradeType.Explosive"/>: 클레이모어·지뢰의 폭발 범위</item>
/// <item><see cref="ScrambleUpgradeType.Wire"/>: 윤형 철조망의 감속과 내구도</item>
/// <item><see cref="ScrambleUpgradeType.Spike"/>: 스파이크의 피해량과 내구도</item>
/// <item><see cref="ScrambleUpgradeType.Trap"/>: 화염 드럼통 화염의 범위·지속 시간과 적 대상 추가 피해</item>
/// </list>
/// <see cref="ScrambleUpgradeType.Shooter"/>는 이 표를 쓰지 않습니다.
/// 지정사수는 <see cref="DefenseManager"/>가 배치로 처리합니다.
/// </remarks>
[CreateAssetMenu(fileName = "TrapUpgradeTable", menuName = "GrayZone/Defense/Trap Upgrade Table")]
public sealed class TrapUpgradeTableSO : ScriptableObject
{
    /// <summary>폭발 함정(클레이모어·지뢰) 한 레벨의 강화값입니다.</summary>
    [Serializable]
    public struct ExplosiveLevel
    {
        [Tooltip("폭발 범위에 곱할 배율입니다. 지뢰는 반지름, 클레이모어는 사거리와 앞뒤 폭에 곱합니다. 높이는 바꾸지 않습니다.")]
        [Min(0.0f)]
        public float RangeMultiplier;
    }

    /// <summary>윤형 철조망 한 레벨의 강화값입니다.</summary>
    [Serializable]
    public struct WireLevel
    {
        [Tooltip("철조망의 이동 속도 배율에 곱할 값입니다. 0.5면 남는 속도가 절반이 되어 더 느려집니다(예: 0.65 → 0.325). 1이면 그대로입니다.")]
        [Range(0.0f, 1.0f)]
        public float MoveSpeedScale;

        [Tooltip("최대 내구도에 더할 값입니다. 철조망은 적 한 마리가 걸릴 때마다 내구도가 깎입니다.")]
        [Min(0)]
        public int BonusDurability;
    }

    /// <summary>스파이크 한 레벨의 강화값입니다.</summary>
    [Serializable]
    public struct SpikeLevel
    {
        [Tooltip("피해량에 곱할 배율입니다. 결과는 올림합니다.")]
        [Min(0.0f)]
        public float DamageMultiplier;

        [Tooltip("최대 내구도에 더할 값입니다. 스파이크는 적 한 마리가 밟을 때마다 내구도가 깎입니다.")]
        [Min(0)]
        public int BonusDurability;
    }

    /// <summary>화염 드럼통 한 레벨의 강화값입니다. 드럼통이 남기는 화염 지대에만 적용합니다(화염병 화염은 그대로).</summary>
    [Serializable]
    public struct FireBarrelLevel
    {
        [Tooltip("화염 지대 범위에 곱할 배율입니다. 판정과 불 이펙트가 함께 커집니다. 1.15면 15% 넓어집니다.")]
        [Min(0.0f)]
        public float RangeMultiplier;

        [Tooltip("화염 지대 지속 시간에 더할 초입니다.")]
        [Min(0.0f)]
        public float BonusDuration;

        [Tooltip("적에게만 틱마다 더하는 피해입니다. 팀원이 받는 피해는 바뀌지 않습니다.")]
        [Min(0.0f)]
        public float EnemyBonusDamagePerTick;
    }

    [Header("Explosive (클레이모어·지뢰)")]
    [Tooltip("Explosive 업그레이드 레벨 1, 2, ... 순서입니다.")]
    [SerializeField] private ExplosiveLevel[] m_explosiveLevels =
    {
        new ExplosiveLevel { RangeMultiplier = 1.25f },
    };

    [Header("Wire (윤형 철조망)")]
    [Tooltip("Wire 업그레이드 레벨 1, 2, ... 순서입니다.")]
    [SerializeField] private WireLevel[] m_wireLevels =
    {
        new WireLevel { MoveSpeedScale = 0.5f, BonusDurability = 50 },
    };

    [Header("Spike (스파이크)")]
    [Tooltip("Spike 업그레이드 레벨 1, 2, ... 순서입니다.")]
    [SerializeField] private SpikeLevel[] m_spikeLevels =
    {
        new SpikeLevel { DamageMultiplier = 1.25f, BonusDurability = 10 },
    };

    [Header("Trap (화염 드럼통)")]
    [Tooltip("Trap 업그레이드 레벨 1, 2, ... 순서입니다.")]
    [SerializeField] private FireBarrelLevel[] m_fireBarrelLevels =
    {
        new FireBarrelLevel { RangeMultiplier = 1.15f, BonusDuration = 2.0f, EnemyBonusDamagePerTick = 3.0f },
    };

    /// <summary>Explosive 레벨의 강화값을 찾습니다. 레벨 0이거나 표가 비었으면 false입니다.</summary>
    public bool TryGetExplosive(int level, out ExplosiveLevel value) => TryGet(m_explosiveLevels, level, out value);

    /// <summary>Wire 레벨의 강화값을 찾습니다. 레벨 0이거나 표가 비었으면 false입니다.</summary>
    public bool TryGetWire(int level, out WireLevel value) => TryGet(m_wireLevels, level, out value);

    /// <summary>Spike 레벨의 강화값을 찾습니다. 레벨 0이거나 표가 비었으면 false입니다.</summary>
    public bool TryGetSpike(int level, out SpikeLevel value) => TryGet(m_spikeLevels, level, out value);

    /// <summary>Trap(화염 드럼통) 레벨의 강화값을 찾습니다. 레벨 0이거나 표가 비었으면 false입니다.</summary>
    public bool TryGetFireBarrel(int level, out FireBarrelLevel value) => TryGet(m_fireBarrelLevels, level, out value);

    private static bool TryGet<T>(T[] levels, int level, out T value)
    {
        if (level <= 0 || levels == null || levels.Length == 0)
        {
            value = default;
            return false;
        }

        value = levels[Mathf.Min(level, levels.Length) - 1];
        return true;
    }
}
