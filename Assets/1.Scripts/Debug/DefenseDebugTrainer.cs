using UnityEngine;

/// <summary>
/// 방어전 씬 전용 런타임 디버그 트레이너입니다. 필드 트레이너(<see cref="RuntimeDebugTrainer"/>)를 토대로,
/// 다섯 번째 탭을 방어전 진행·함정·스킬 제어로 바꿉니다.
/// </summary>
/// <remarks>
/// 창 골격, 입력 잠금, 캐릭터·애니메이션·무기·조준 탭, 디버그 항목 탭은 필드 트레이너와 같습니다.
/// 씬 컨트롤러가 <see cref="DefenseManager"/>인 씬에서만 동작하고, 필드 씬에서는 키를 받지 않습니다.
/// 방어전 데이터 매니저(<see cref="CombatSceneDataManager"/>)의 Debug 칸에 이 프리팹을 넣으면 시작할 때 생성됩니다.
/// </remarks>
public sealed class DefenseDebugTrainer : RuntimeDebugTrainer
{
    private static readonly string[] DefenseTabNames =
    {
        "캐릭터",
        "애니메이션",
        "무기",
        "조준·반동",
        "방어전",
        "디버그",
    };

    /// <summary>마지막으로 누른 방어전 버튼의 결과 문구입니다.</summary>
    private string m_defenseActionResult = string.Empty;

    /// <inheritdoc />
    protected override string[] TabNames => DefenseTabNames;

    /// <inheritdoc />
    protected override bool IsSupportedScene()
    {
        return HasSceneInputModeController() && CombatSceneManager.Instance is DefenseManager;
    }

    /// <inheritdoc />
    protected override void DrawSceneTab()
    {
        DrawDefenseStateSection();
        GUILayout.Space(6);
        DrawDefenseLevelSection();
        GUILayout.Space(6);
        DrawDefenseFlowSection();
        GUILayout.Space(6);
        DrawDefenseEnemySection();
        GUILayout.Space(6);
        DrawTrapSection();
        GUILayout.Space(6);
        DrawSkillSection();
        GUILayout.Space(6);
        DrawEnemySpawnSection();
        GUILayout.Space(6);
        DrawSquadAiControlSection();

        if (!string.IsNullOrEmpty(m_defenseActionResult))
        {
            GUILayout.Space(4);
            GUILayout.Label(m_defenseActionResult);
        }
    }

    private void DrawDefenseStateSection()
    {
        GUILayout.Label("■ 방어전 상태", m_headerStyle);

        DefenseManager defense = DefenseManager.Instance;
        if (defense == null)
        {
            GUILayout.Label("DefenseManager를 찾을 수 없습니다.");
            return;
        }

        GUILayout.Label($"단계: {DescribePhase(defense)}   웨이브 {defense.CurrentWave} / {defense.TotalWaveCount}");
        GUILayout.Label($"전투 남은 시간 {defense.RoundTimer:0.0}초   휴식 남은 시간 {defense.RestTimer:0.0}초   남은 적 {defense.RemainingEnemyCount}");
        GUILayout.Label($"함정 설치 구간: {(defense.IsTrapBuildWindowOpen ? "열림" : "닫힘")}");

        DefenseSceneDataManager data = DefenseSceneDataManager.Instance;
        if (data != null)
        {
            string stage = data.DefenseStage != null ? data.DefenseStage.name : "없음";
            GUILayout.Label($"회차 {data.DefenseRound} ({stage})   기록 단계 {data.Phase}   처치 {data.KillCount}   임무 완료 {data.MissionCompleted}");
        }
    }

    /// <summary>
    /// 방어전 업그레이드 레벨을 보여 주고, 지정사수 레벨을 바로 바꿉니다.
    /// </summary>
    /// <remarks>
    /// 방어전 씬에서 효과가 있는 업그레이드는 지금 지정사수(Shooter)뿐이라 그것만 바꿀 수 있습니다.
    /// 나머지는 입장 데이터에 들어온 값을 확인하는 용도입니다. 바꾼 값은 Play를 끄면 사라집니다.
    /// </remarks>
    private void DrawDefenseLevelSection()
    {
        GUILayout.Label("■ 방어전 레벨", m_headerStyle);

        DefenseManager defense = DefenseManager.Instance;
        if (defense == null)
        {
            return;
        }

        int maxLevel = defense.MarksmanTierCount;
        GUILayout.Label($"지정사수 레벨 {defense.MarksmanLevel} / {maxLevel}   ({(defense.HasMarksmanLevelOverride ? "디버그 지정" : "입장 데이터")})");

        GUILayout.BeginHorizontal();
        for (int level = 0; level <= maxLevel; level++)
        {
            if (GUILayout.Button($"{level}레벨"))
            {
                defense.DebugSetMarksmanLevel(level);
                m_defenseActionResult = $"지정사수를 {level}레벨로 다시 배치했습니다.";
            }
        }

        if (GUILayout.Button("입장 데이터 값으로"))
        {
            defense.DebugSetMarksmanLevel(-1);
            m_defenseActionResult = $"지정사수 레벨을 입장 데이터 값({defense.MarksmanLevel})으로 되돌렸습니다.";
        }
        GUILayout.EndHorizontal();

        DefenseSceneDataManager data = DefenseSceneDataManager.Instance;
        if (data != null)
        {
            GUILayout.Label(
                $"입장 데이터: 함정 {data.GetUpgradeLevel(ScrambleUpgradeType.Trap)}   가시 {data.GetUpgradeLevel(ScrambleUpgradeType.Spike)}   " +
                $"폭발물 {data.GetUpgradeLevel(ScrambleUpgradeType.Explosive)}   지정사수 {data.GetUpgradeLevel(ScrambleUpgradeType.Shooter)}   " +
                $"철조망 {data.GetUpgradeLevel(ScrambleUpgradeType.Wire)}");
        }
    }

    private void DrawDefenseFlowSection()
    {
        GUILayout.Label("■ 진행", m_headerStyle);

        DefenseManager defense = DefenseManager.Instance;
        if (defense == null)
        {
            return;
        }

        GUILayout.BeginHorizontal();
        // 게임오버 뒤에는 정산 데이터와 오버레이가 남아 있어 다시 시작해도 온전하지 않습니다. 씬을 다시 불러야 합니다.
        GUI.enabled = !defense.IsGameOver;
        if (GUILayout.Button(defense.IsGameStarted ? "처음부터 다시 시작" : "방어전 시작"))
        {
            defense.StartDefense();
            m_defenseActionResult = defense.IsGameStarted
                ? "방어전을 시작했습니다."
                : "방어전을 시작하지 못했습니다(방어전 단계 데이터 확인).";
        }

        if (GUILayout.Button("라운드 스킵"))
        {
            string before = DescribePhase(defense);
            int waveBefore = defense.CurrentWave;
            defense.SkipRound();
            string after = DescribePhase(defense);
            m_defenseActionResult = before == after && waveBefore == defense.CurrentWave
                ? $"넘길 라운드가 없습니다(지금 단계: {after})."
                : $"라운드를 넘겼습니다. 지금 단계: {after}";
        }
        GUI.enabled = true;

        if (GUILayout.Button("강제 승리"))
        {
            defense.DebugForceVictory();
            m_defenseActionResult = defense.IsVictoryReady ? "승리 처리했습니다. 귀환 구역이 열렸습니다." : "강제 승리를 하지 못했습니다(진행 중일 때만 가능).";
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("즉시 귀환 (결과 화면)"))
        {
            EscapeSystem escape = FindFirstObjectByType<EscapeSystem>(FindObjectsInactive.Include);
            if (escape != null)
            {
                CloseMenu();
                escape.ForceEscape();
                m_defenseActionResult = "귀환 정산을 요청했습니다.";
            }
            else
            {
                m_defenseActionResult = "EscapeSystem(귀환 구역)을 찾지 못했습니다.";
            }
        }

        if (GUILayout.Button("정문 파괴로 패배"))
        {
            CombatSceneDataManager data = CombatSceneDataManager.Instance;
            if (data == null)
            {
                m_defenseActionResult = "전투 데이터 매니저를 찾지 못했습니다.";
            }
            else if (data.IsFinalized)
            {
                m_defenseActionResult = "이미 정산이 끝나 게임오버를 다시 낼 수 없습니다.";
            }
            else
            {
                CloseMenu();
                data.RequestGameOver();
                m_defenseActionResult = "게임오버를 요청했습니다.";
            }
        }
        GUILayout.EndHorizontal();

        DrawForceGameOverButton();
    }

    private void DrawDefenseEnemySection()
    {
        GUILayout.Label("■ 적", m_headerStyle);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("적 전부 처치 (처치로 셈)"))
        {
            KillAllEnemies();
            m_defenseActionResult = "적을 모두 처치했습니다.";
        }

        if (GUILayout.Button("적 치우기 (처치로 세지 않음)"))
        {
            DefenseManager defense = DefenseManager.Instance;
            int count = defense != null ? defense.DebugDespawnManagedEnemies() : 0;
            m_defenseActionResult = $"방어전 스포너가 낸 적 {count}마리를 회수했습니다.";
        }
        GUILayout.EndHorizontal();
    }

    private void DrawTrapSection()
    {
        GUILayout.Label("■ 함정", m_headerStyle);

        Trap[] traps = FindObjectsByType<Trap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int built = 0;
        for (int i = 0; i < traps.Length; i++)
        {
            if (traps[i] != null && traps[i].IsBuilt)
            {
                built++;
            }
        }

        GUILayout.Label($"함정 {traps.Length}개 (설치됨 {built})");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("전부 다시 설치 가능하게"))
        {
            // 소모 상태를 풀고 설치 횟수도 채웁니다. 횟수가 바닥난 청사진은 Rearm만으로는 계속 숨겨집니다.
            int buildable = 0;
            for (int i = 0; i < traps.Length; i++)
            {
                if (traps[i] == null)
                {
                    continue;
                }

                traps[i].Rearm();
                traps[i].DebugRefillBuildCharges();
                if (traps[i].IsBuildable)
                {
                    buildable++;
                }
            }

            DefenseManager defense = DefenseManager.Instance;
            bool windowClosed = defense != null && !defense.IsTrapBuildWindowOpen;
            m_defenseActionResult = $"지금 설치할 수 있는 청사진 {buildable}개."
                + (windowClosed ? " 전투 중이라 휴식 때만 설치하는 함정(OnRest)은 휴식이 시작되면 보입니다." : string.Empty);
        }

        if (GUILayout.Button("설치 횟수 채우기"))
        {
            for (int i = 0; i < traps.Length; i++)
            {
                traps[i]?.DebugRefillBuildCharges();
            }

            m_defenseActionResult = "모든 함정의 설치 횟수를 처음 값으로 채웠습니다.";
        }
        GUILayout.EndHorizontal();

        DrawFireBarrelChainRow(traps);
    }

    /// <summary>
    /// 화염 드럼통 연쇄 반경을 한꺼번에 조절하고 반경 표시를 켜고 끕니다.
    /// </summary>
    /// <remarks>
    /// 드럼통이 여러 개라 하나씩 고치지 않도록 첫 드럼통의 값을 보여 주고, 슬라이더를 움직이면 전부에 적용합니다.
    /// 런타임 값만 바뀌고 프리팹이나 씬에는 저장되지 않습니다.
    /// </remarks>
    private void DrawFireBarrelChainRow(Trap[] traps)
    {
        FireBarrelTrap first = null;
        for (int i = 0; i < traps.Length && first == null; i++)
        {
            first = traps[i] as FireBarrelTrap;
        }

        if (first == null)
        {
            return;
        }

        float current = first.ChainRadius;
        float next = SliderRow("드럼통 연쇄 반경(m)", current, 0f, 15f, "0.0");
        if (!Mathf.Approximately(next, current))
        {
            for (int i = 0; i < traps.Length; i++)
            {
                (traps[i] as FireBarrelTrap)?.DebugSetChainRadius(next);
            }
        }

        FireBarrelTrap.DebugShowChainRadius = GUILayout.Toggle(
            FireBarrelTrap.DebugShowChainRadius,
            " 드럼통 연쇄 반경 표시 (Scene 뷰, Gizmos 켠 Game 뷰)");
    }

    private void DrawSkillSection()
    {
        GUILayout.Label("■ 스킬", m_headerStyle);

        CharacterSkill[] skills = FindObjectsByType<CharacterSkill>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < skills.Length; i++)
        {
            CharacterSkill skill = skills[i];
            if (skill == null)
            {
                continue;
            }

            string owner = skill.Owner != null ? skill.Owner.name : skill.name;
            // IsReady는 효과가 도는 동안에도 거짓이라, 쿨다운 0초로 보이지 않게 활성 상태를 따로 적습니다.
            string state = skill.IsReady
                ? "사용 가능"
                : skill.IsActive
                    ? "효과 진행 중 (끝나야 다시 사용 가능)"
                    : $"쿨다운 {skill.CooldownRemaining:0.0}초";
            GUILayout.Label($"{owner} / {skill.GetType().Name}: {state}");
        }

        if (GUILayout.Button("스킬 쿨다운 모두 초기화"))
        {
            for (int i = 0; i < skills.Length; i++)
            {
                skills[i]?.DebugResetCooldown();
            }

            int active = 0;
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null && skills[i].IsActive)
                {
                    active++;
                }
            }

            m_defenseActionResult = $"스킬 {skills.Length}개의 쿨다운을 없앴습니다."
                + (active > 0 ? $" 효과가 진행 중인 {active}개는 끝나야 다시 쓸 수 있습니다." : string.Empty);
        }
    }

    private static string DescribePhase(DefenseManager defense)
    {
        if (defense.IsGameOver)
        {
            return "게임오버";
        }

        if (!defense.IsGameStarted)
        {
            return "시작 전";
        }

        if (defense.IsVictoryReady)
        {
            return "승리 (귀환 대기)";
        }

        if (defense.IsPlaying)
        {
            return "전투";
        }

        return defense.IsClearing ? "정리" : "휴식";
    }
}
