using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defense 라운드의 플레이/휴식 상태와 스폰 포인트 활성화를 관리합니다.
/// </summary>
/// <remarks>
/// 라운드 진행 규칙과 UI가 읽을 런타임 카운트만 소유합니다. 적 생산 규칙은 각
/// <see cref="EnemySpawnPoint"/>와 <see cref="EnemySpawnEntrySO"/>가 소유합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DefenseManager : MonoBehaviour
{
    [Header("Defense Round")]
    [Tooltip("Defense 게임을 시작한 뒤 스포너를 활성화해 둘 라운드 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_roundDuration = 60.0f;

    [Tooltip("라운드가 끝난 뒤 다음 라운드를 시작하기 전 스포너를 비활성화해 둘 휴식 시간(초)입니다.")]
    [Min(0.01f)]
    [SerializeField] private float m_restDuration = 10.0f;

    [Tooltip("라운드 시작/휴식 전환 때 켜고 끌 EnemySpawnPoint 목록입니다. 비어 있는 항목은 무시합니다.")]
    [SerializeField] private List<EnemySpawnPoint> m_spawnPoints = new List<EnemySpawnPoint>();

    /// <summary>Defense 게임이 시작될 때 발생합니다. 튜토리얼 안내처럼 시작 시점에 붙는 UI가 구독합니다.</summary>
    /// <remarks>
    /// <see cref="StartDefenseGame"/>를 다시 부르면 재시작으로 보고 매번 발생합니다.
    /// 구독자가 중복 표시를 원하지 않으면 자기 쪽에서 한 번만 처리하도록 판단합니다.
    /// </remarks>
    public event Action OnDefenseStarted;

    /// <summary>게임이 시작되어 라운드 매니저가 타이머를 갱신 중인지 여부입니다.</summary>
    private bool m_isGameStarted;

    /// <summary>현재 라운드 플레이 구간인지 여부입니다. false이면 휴식 구간입니다.</summary>
    private bool m_isPlaying;

    /// <summary>현재 라운드에 남은 시간(초)입니다.</summary>
    private float m_roundTimer;

    /// <summary>현재 휴식 시간에 남은 시간(초)입니다.</summary>
    private float m_restTimer;

    /// <summary>Defense 게임이 시작되었는지 여부입니다.</summary>
    public bool IsGameStarted => m_isGameStarted;

    /// <summary>현재 적 스폰이 허용된 라운드 플레이 구간인지 여부입니다.</summary>
    public bool IsPlaying => m_isGameStarted && m_isPlaying;

    /// <summary>현재 라운드에 남은 시간(초)입니다. 플레이 구간이 아니면 0입니다.</summary>
    public float RoundTimer => IsPlaying ? m_roundTimer : 0.0f;

    /// <summary>현재 휴식에 남은 시간(초)입니다. 휴식 구간이 아니면 0입니다.</summary>
    public float RestTimer => m_isGameStarted && !m_isPlaying ? m_restTimer : 0.0f;

    /// <summary>이 매니저가 제어하는 스폰 포인트 목록입니다.</summary>
    public IReadOnlyList<EnemySpawnPoint> SpawnPoints => m_spawnPoints;

    private void Update()
    {
        if (!m_isGameStarted)
        {
            return;
        }

        float remainingDelta = Time.deltaTime;
        while (remainingDelta > 0.0f)
        {
            if (m_isPlaying)
            {
                if (m_roundTimer > remainingDelta)
                {
                    m_roundTimer -= remainingDelta;
                    return;
                }

                remainingDelta -= m_roundTimer;
                m_roundTimer = 0.0f;
                BeginRest();
            }
            else
            {
                if (m_restTimer > remainingDelta)
                {
                    m_restTimer -= remainingDelta;
                    return;
                }

                remainingDelta -= m_restTimer;
                m_restTimer = 0.0f;
                BeginRound();
            }
        }
    }

    /// <summary>Defense 게임을 시작하고 첫 라운드 타이머를 시작합니다.</summary>
    /// <remarks>이미 실행 중이면 현재 라운드와 휴식 카운트를 초기화하고 첫 라운드부터 다시 시작합니다.</remarks>
    [ContextMenu("Start Defense Game")]
    public void StartDefenseGame()
    {
        m_isGameStarted = true;
        BeginRound();
        OnDefenseStarted?.Invoke();
    }

    /// <summary>Defense 게임을 중지하고 이 매니저가 제어한 스포너를 끕니다.</summary>
    [ContextMenu("Stop Defense Game")]
    public void StopDefenseGame()
    {
        m_isGameStarted = false;
        m_isPlaying = false;
        m_roundTimer = 0.0f;
        m_restTimer = 0.0f;
        SetSpawnPointsEnabled(false);
    }

    /// <summary>다음 플레이 라운드를 시작합니다.</summary>
    private void BeginRound()
    {
        m_isPlaying = true;
        m_roundTimer = Mathf.Max(0.01f, m_roundDuration);
        m_restTimer = 0.0f;
        SetSpawnPointsEnabled(true);
    }

    /// <summary>플레이 라운드를 끝내고 휴식 구간을 시작합니다.</summary>
    private void BeginRest()
    {
        m_isPlaying = false;
        m_roundTimer = 0.0f;
        m_restTimer = Mathf.Max(0.01f, m_restDuration);
        SetSpawnPointsEnabled(false);
    }

    /// <summary>할당된 모든 스폰 포인트의 신규 생산 허용 상태를 바꿉니다.</summary>
    private void SetSpawnPointsEnabled(bool isEnabled)
    {
        if (m_spawnPoints == null)
        {
            return;
        }

        for (int i = 0; i < m_spawnPoints.Count; i++)
        {
            m_spawnPoints[i]?.SetSpawnEnabled(isEnabled);
        }
    }

    private void OnValidate()
    {
        m_roundDuration = Mathf.Max(0.01f, m_roundDuration);
        m_restDuration = Mathf.Max(0.01f, m_restDuration);
    }
}
