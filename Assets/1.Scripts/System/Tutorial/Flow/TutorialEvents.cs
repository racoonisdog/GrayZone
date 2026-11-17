using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// 튜토리얼 시퀀스에서 고를 수 있는 이벤트 메서드임을 표시합니다. 시퀀스 Inspector의 드롭다운에 이 이름으로 나옵니다.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TutorialEventAttribute : Attribute
{
    /// <summary>드롭다운에 보일 이름입니다.</summary>
    public string DisplayName { get; }

    public TutorialEventAttribute(string displayName)
    {
        DisplayName = displayName;
    }
}

/// <summary>
/// 이벤트 메서드의 string 파라미터가 시네마틱 ID임을 표시합니다. Inspector가 텍스트 칸 대신 씬의 시네마틱 목록 드롭다운을 그립니다.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TutorialCinematicIdAttribute : Attribute
{
}

/// <summary>
/// 이벤트 메서드의 string 파라미터가 씬 함정의 하이어라키 경로임을 표시합니다. Inspector가 씬의 함정 목록(종류별) 드롭다운을 그립니다.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TutorialTrapPathAttribute : Attribute
{
}

/// <summary>
/// 이벤트 메서드의 string 파라미터가 씬 오브젝트의 하이어라키 경로임을 표시합니다. Inspector가 텍스트 칸 옆에 오브젝트 칸을 그려, 씬 오브젝트를 끌어 놓으면 경로를 채웁니다.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TutorialScenePathAttribute : Attribute
{
}

/// <summary>자원 지급 이벤트에서 고르는 자원 종류입니다. 순서대로 정수로 저장되므로 새 종류는 끝에 붙입니다.</summary>
public enum TutorialResourceKind
{
    TrapMaterial = 0,
    UpgradeMaterial = 1,
    CraftingMaterial = 2,
    TrapUpgradeMaterial = 3,
}

/// <summary>
/// 튜토리얼 이벤트 메서드를 모아 둔 곳입니다. 시퀀스의 줄마다 여기 메서드를 골라 값만 채워 실행합니다.
/// </summary>
/// <remarks>
/// 새 이벤트는 여기에 public 메서드를 만들고 <see cref="TutorialEventAttribute"/>를 붙이면 드롭다운에 나옵니다.
/// 파라미터는 string, float, int, bool, Vector3, 열거형, 에셋(Sprite 등)만 받습니다. 시퀀스가 에셋이라 씬 오브젝트는 저장할 수 없어서입니다.
/// <para>
/// 메서드가 <see cref="IEnumerator"/>를 돌려주면 끝날 때까지 튜토리얼이 다음으로 넘어가지 않습니다(창을 닫을 때까지 대기 등).
/// 메서드 이름이 에셋에 저장되므로, 이미 쓰는 메서드의 이름을 바꾸지 마세요.
/// </para>
/// </remarks>
[DisallowMultipleComponent]
public sealed class TutorialEvents : MonoBehaviour
{
    [Header("References")]
    [Tooltip("미션 목표 변경이 바꿀 HUD입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private MissionObjectiveHud m_objective;

    [Tooltip("튜토리얼 창 띄우기가 쓸 창입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private DefenseTutorialOverlay m_window;

    [Tooltip("화살표 마커 표시가 쓸 안내 컴포넌트입니다(화면 안 마커, 화면 밖 방향 화살표). " +
             "공격로 마커와 겹치지 않게 튜토리얼 전용으로 하나 둡니다. 비어 있으면 마커 이벤트는 경고만 남깁니다.")]
    [SerializeField] private ObjectiveIndicatorController m_marker;

    [Tooltip("마커 위치로 옮길 오브젝트입니다. 보통 안내 컴포넌트의 월드 마커 루트를 넣습니다. 비어 있으면 빈 오브젝트를 만들어 씁니다.")]
    [SerializeField] private Transform m_markerAnchor;

    [Tooltip("경로선 안내가 쓸 발밑 경로 화살표입니다. 비어 있으면 씬에서 찾습니다.")]
    [SerializeField] private ObjectivePathArrow m_pathArrow;

    [Tooltip("목표 대상 위 마커 띄우기가 대상마다 복제할 마커입니다(공격로 마커를 복제해 아이콘만 바꾼 것). 씬에서는 꺼 둡니다. " +
             "비어 있으면 대상 마커 이벤트는 경고만 남깁니다.")]
    [SerializeField] private ObjectiveIndicatorController m_targetMarkerTemplate;

    [Min(0.0f)]
    [Tooltip("대상 마커를 대상의 보이는 모습 맨 위에서 이만큼(m) 더 띄웁니다.")]
    [SerializeField] private float m_targetMarkerHeight = 0.5f;

    [Header("Enemy Spawn")]
    [Min(0.1f)]
    [Tooltip("적 생성 위치를 NavMesh 위로 붙일 때 찾는 반경(m)입니다.")]
    [SerializeField] private float m_spawnSampleRadius = 3.0f;

    [Header("Cinematic")]
    [Tooltip("시네마틱 카메라에 줄 우선순위입니다. 평소 카메라보다 높아야 화면이 넘어갑니다(시설 업그레이드 컷신과 같은 100).")]
    [SerializeField] private int m_cinematicPriority = 100;

    [Header("Window")]
    [Tooltip("튜토리얼 창을 닫는 키입니다.")]
    [SerializeField] private Key m_closeKey = Key.F;

    [Min(0.0f)]
    [Tooltip("닫기 키를 이 시간(초) 동안 누르고 있어야 창이 닫힙니다. 누르는 동안 창의 키 게이지가 찹니다. 중간에 떼면 처음부터 다시 셉니다. 0이면 누르는 순간 닫힙니다.")]
    [SerializeField] private float m_closeHoldSeconds = 0.5f;

    [Min(0.0f)]
    [Tooltip("창이 뜬 직후 닫기 입력을 무시하는 시간(초)입니다. 앞 대사를 넘긴 입력으로 창이 바로 닫히지 않게 합니다.")]
    [SerializeField] private float m_closeGuardSeconds = 0.3f;

    [Min(0.0f)]
    [Tooltip("튜토리얼 창이 뜰 때 화면 가운데 알림이 떠 있으면, 이 시간(초) 동안 페이드아웃해 내립니다. 창과 알림이 겹쳐 남지 않게 합니다.")]
    [SerializeField] private float m_windowBannerFadeSeconds = 0.25f;

    private static readonly Dictionary<string, MethodInfo> s_methods = new();

    private bool m_isHoldingPause;
    private float m_resumeTimeScale = 1.0f;
    /// <summary>튜토리얼 창이 떠 있는지 여부입니다. 상호작용 대상 잡기(함정 안내 패널)도 이 값을 보고 멈추므로 static으로 둡니다.</summary>
    private static bool s_isWindowOpen;

    /// <summary>튜토리얼 창이 떠서 닫기 입력을 기다리는 중인지 여부입니다.</summary>
    public bool IsWindowOpen => s_isWindowOpen;

    /// <summary>튜토리얼 창이 떠 있는지 여부입니다. 창이 떠 있는 동안 함정 설치·수리 안내가 뜨지 않게 할 때 봅니다.</summary>
    public static bool IsAnyWindowOpen => s_isWindowOpen;

    /// <summary>"방어 목표 수리 켜기/끄기"로 수리를 막아 둔 대상입니다. 튜토리얼이 끝나면 다시 켭니다.</summary>
    private readonly List<DefenseRepairInteractable> m_blockedRepairs = new();

    /// <summary>"지점 이펙트 띄우기"로 띄운 이펙트입니다. 숨기거나 튜토리얼이 끝나면 없앱니다.</summary>
    private readonly List<GameObject> m_pointEffects = new();

    /// <summary>"씬 오브젝트 켜기/끄기"로 바꾼 오브젝트와 처음 상태입니다. 튜토리얼이 끝나면 처음 상태로 되돌립니다.</summary>
    private readonly Dictionary<GameObject, bool> m_sceneObjectStates = new();

    /// <summary>지금 경로선과 함께 켜 둔 도착 범위 표시 오브젝트의 경로입니다. 없으면 null입니다.</summary>
    private string m_pathAreaObject;

    /// <summary>지금 경로선의 도착 범위입니다. 있으면 위치·반경 대신 이 범위로 도착을 판정합니다. 없으면 null입니다.</summary>
    private TutorialArriveArea m_pathArea;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_isWindowOpen = false;
    }

    /// <summary>경로선 목표 위치로 옮길 오브젝트입니다. 처음 경로선을 띄울 때 만듭니다.</summary>
    private Transform m_pathAnchor;

    /// <summary>가장 가까운 대상 안내에서 대상이 바뀔 때 번갈아 쓰는 두 번째 안내 기준점입니다.</summary>
    private Transform m_pathAnchorAlt;

    /// <summary>기다리지 않는 경로선 안내의 도착 감시입니다. 도착하면 경로선을 숨깁니다.</summary>
    private Coroutine m_pathWatch;

    /// <summary>지금 띄운 경로선의 목표 위치와 도착 반경입니다. Scene 뷰에 도착 범위를 그릴 때 씁니다. 반경이 0이면 경로선이 없습니다.</summary>
    private Vector3 m_pathTarget;
    private float m_pathArriveRadius;

    /// <summary>목표 대상 위에 띄운 마커입니다. 대상마다 하나씩, 템플릿을 복제해 만듭니다.</summary>
    private readonly List<TargetMarker> m_targetMarkers = new();

    private sealed class TargetMarker
    {
        /// <summary>마커를 띄울 대상(함정, 정문, 분대원 등)입니다. 이 아래 켜진 렌더러의 맨 위를 따라갑니다.</summary>
        public Transform Root;
        public Trap Trap;

        /// <summary>분대원 마커면 그 대원의 체력입니다. 다운됐다가 일어나면 마커를 내립니다.</summary>
        public PlayerHealth Member;
        public ObjectiveIndicatorController View;
        public Transform Anchor;

        /// <summary>할 일이 남은 상태(함정은 청사진·깎인 내구도, 분대원은 다운)였던 적이 있는지 여부입니다. 그 뒤에 완료돼야 마커를 내립니다.</summary>
        /// <remarks>같은 트리거의 함정 켜기가 내구도를 한 프레임 늦게 깎으므로, 띄우자마자 "이미 가득 참"으로 내리지 않게 합니다.</remarks>
        public bool WasPending;
    }

    /// <summary>적 생성으로 만든 적입니다. 튜토리얼 적 제거가 여기서만 고릅니다.</summary>
    private readonly List<EnemyController> m_spawnedEnemies = new();

    /// <summary>스킬 쿨타임 변경으로 쿨타임을 바꿔 둔 스킬입니다. 튜토리얼이 끝나면 되돌립니다.</summary>
    private readonly HashSet<CharacterSkill> m_cooldownOverrides = new();

    /// <summary>"모든 함정 청사진 끄기"로 끈 함정입니다. 튜토리얼이 끝날 때 다시 켭니다.</summary>
    private readonly List<Trap> m_hiddenBlueprints = new();

    /// <summary>시네마틱으로 켠 카메라와 켜기 전 상태입니다. 끝나거나 튜토리얼이 끝나면 되돌립니다.</summary>
    private readonly List<CinematicCameraState> m_cinematicCameras = new();

    /// <summary>재생 중인 시네마틱 Timeline입니다. 없으면 null입니다.</summary>
    private PlayableDirector m_cinematicDirector;

    /// <summary>시네마틱이 분대 조작을 막아 두었는지 여부입니다.</summary>
    private bool m_cinematicLockedInput;

    /// <summary>시네마틱 재생을 <see cref="InGameCutscene"/>에 알려 두었는지 여부입니다.</summary>
    private bool m_cinematicPlaying;

    /// <summary><see cref="SkipCinematic"/>으로 재생 중인 시네마틱을 끝내라는 요청이 있었는지 여부입니다.</summary>
    private bool m_cinematicSkipRequested;

    /// <summary>시네마틱 시작 페이드인(검은 화면 유지 후 밝히기) 코루틴입니다. 진행 중일 때만 값이 있습니다.</summary>
    private Coroutine m_startFadeRoutine;

    /// <summary>시네마틱 끝 전환에 쓰는 화면 전체 검은 막입니다. 처음 쓸 때 만듭니다.</summary>
    private CanvasGroup m_fadeOverlay;

    /// <summary>"벗어나면 다시 띄움" 경로선이 다시 뜨는 기준을 도착 반경보다 넓힐 거리(m)입니다.</summary>
    private const float PathReshowMargin = 1.0f;

    /// <summary>창 닫기 키를 누른 시간을 셀 때 한 프레임에 더하는 최대 시간(초)입니다.</summary>
    private const float MaxHoldStepSeconds = 0.1f;

    /// <summary>시네마틱이 끝난 뒤 플레이어 카메라로 돌아오는 전환을 기다리는 최대 시간(초)입니다.</summary>
    private const float CameraReturnTimeout = 3.0f;

    private struct CinematicCameraState
    {
        public CinemachineCamera Camera;
        public bool WasActive;
        public bool PriorityEnabled;
        public int Priority;
    }

    /// <summary>
    /// 이벤트 하나를 실행합니다. 메서드가 기다리는 이벤트면 끝날 때까지 이어집니다.
    /// </summary>
    /// <remarks>부르는 쪽은 코루틴 안에서 <c>yield return Run(action)</c>으로 씁니다.</remarks>
    public IEnumerator Run(TutorialAction action)
    {
        if (action == null || string.IsNullOrEmpty(action.Method))
        {
            yield break;
        }

        MethodInfo method = FindMethod(action.Method);
        if (method == null)
        {
            Debug.LogWarning($"튜토리얼 이벤트 '{action.Method}'를 {nameof(TutorialEvents)}에서 찾지 못했습니다. 메서드 이름이 바뀌었는지 확인하세요.", this);
            yield break;
        }

        ParameterInfo[] parameters = method.GetParameters();
        object[] values = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type type = parameters[i].ParameterType;
            values[i] = i < action.Args.Count
                ? action.Args[i].GetValue(type)
                : default(TutorialEventArg).GetValue(type);
        }

        if (method.Invoke(this, values) is IEnumerator wait)
        {
            yield return wait;
        }
    }

    /// <summary>튜토리얼이 끝날 때 이벤트가 남긴 상태(멈춘 시간, 열린 창, 마커)를 되돌립니다.</summary>
    public void ReleaseAll()
    {
        if (s_isWindowOpen)
        {
            s_isWindowOpen = false;
            ResolveWindow()?.Hide();
        }

        HideMarker();
        HidePathLine();
        HideTargetMarkers();
        DespawnEnemies(null, 0);
        StopCinematic();
        SetFadeAlpha(0.0f);
        ResumeTime();
        SquadInventoryManager.FreeUse = false;
        SquadInventoryManager.UseWhenEmpty = false;
        PlayerHealth.KeepMinimumHp = false;
        PlayerHealth.KeepDownedOnTimeout = false;
        SquadManager.AutoRescueBlocked = false;
        DownedAllyInteractable.RescueBlocked = false;
        SetDefenseMarkerShown(true);
        ChungSolDragonBreathSkill.TutorialInfiniteRounds = false;
        SetPlayerWalk(false);
        RestoreHiddenBlueprints();
        HidePointEffects();
        RestoreSceneObjects();
        ClearSquadOrderLocks();
        foreach (DefenseRepairInteractable repair in m_blockedRepairs)
        {
            if (repair != null)
            {
                repair.RepairEnabled = true;
            }
        }

        m_blockedRepairs.Clear();
        foreach (CharacterSkill skill in m_cooldownOverrides)
        {
            if (skill != null)
            {
                skill.ClearCooldownOverride();
            }
        }

        m_cooldownOverrides.Clear();
    }

    /// <summary>조작 중인 캐릭터가 도착 범위 안에 있는지 여부입니다. 높이는 보지 않습니다.</summary>
    /// <param name="margin">범위를 사방으로 이만큼(m) 넓혀 봅니다.</param>
    public static bool IsPlayerInArea(TutorialArriveArea area, float margin = 0.0f)
    {
        SquadMemberController member = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        return member != null && area != null && area.Contains(member.transform.position, margin);
    }

    /// <summary>조작 중인 캐릭터가 위치의 반경 안에 있는지 여부입니다. 높이는 보지 않고 평면 거리로 잽니다.</summary>
    public static bool IsPlayerInArea(Vector3 center, float radius)
    {
        SquadMemberController member = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        if (member == null)
        {
            return false;
        }

        Vector3 offset = member.transform.position - center;
        offset.y = 0.0f;
        return offset.sqrMagnitude <= radius * radius;
    }

    /// <summary>
    /// 적이 정한 프리팹으로 만든 것인지 여부입니다. 프리팹이 null이면 늘 true입니다.
    /// </summary>
    /// <remarks>
    /// 만든 적은 프리팹을 기억하지 않아서 이름으로 봅니다. Instantiate는 "프리팹 이름(Clone)"으로 이름을 붙이므로,
    /// 스포너가 만든 적도 같은 방식으로 맞습니다. 이름이 앞부분이 같은 다른 프리팹(Zombie, Zombie_Fast)은 구분합니다.
    /// </remarks>
    public static bool IsFromPrefab(Component enemy, EnemyController prefab)
    {
        if (prefab == null)
        {
            return true;
        }

        if (enemy == null)
        {
            return false;
        }

        string name = enemy.gameObject.name;
        string prefabName = prefab.gameObject.name;
        return name == prefabName || name.StartsWith(prefabName + "(", StringComparison.Ordinal)
            || name.StartsWith(prefabName + " (", StringComparison.Ordinal);
    }

    /// <summary>
    /// 이벤트로 고를 수 있는 메서드를 이름으로 찾습니다. Inspector 드롭다운도 같은 목록을 씁니다.
    /// </summary>
    public static MethodInfo FindMethod(string name)
    {
        if (s_methods.Count == 0)
        {
            foreach (MethodInfo method in GetEventMethods())
            {
                s_methods[method.Name] = method;
            }
        }

        return s_methods.TryGetValue(name, out MethodInfo found) ? found : null;
    }

    /// <summary><see cref="TutorialEventAttribute"/>가 붙은 public 메서드 목록입니다.</summary>
    public static IEnumerable<MethodInfo> GetEventMethods()
    {
        foreach (MethodInfo method in typeof(TutorialEvents).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.GetCustomAttribute<TutorialEventAttribute>() != null)
            {
                yield return method;
            }
        }
    }

    // ───────────── 이벤트 메서드 ─────────────

    /// <summary>미션 목표 문구를 바꿉니다. 비운 칸은 지금 문구를 둡니다.</summary>
    /// <param name="title">목표 제목입니다.</param>
    /// <param name="subtitle">목표 부제입니다.</param>
    /// <param name="bannerMessage">
    /// 채우면 화면 가운데에 "전투가 시작됩니다"·"방어전이 종료되었습니다"와 같은 자리·방식(페이드인 → 유지 → 페이드아웃)으로 알림을 띄웁니다. 비우면 띄우지 않습니다.
    /// </param>
    [TutorialEvent("미션 목표 변경")]
    public void SetObjective(string title, string subtitle, string bannerMessage)
    {
        if (m_objective == null)
        {
            m_objective = FindFirstObjectByType<MissionObjectiveHud>(FindObjectsInactive.Include);
        }

        if (m_objective != null)
        {
            m_objective.SetObjective(title, subtitle);
        }

        if (!string.IsNullOrEmpty(bannerMessage) && DefenseManager.Instance != null)
        {
            DefenseManager.Instance.ShowBannerMessage(bannerMessage);
        }
    }

    /// <summary>
    /// 예전 튜토리얼 방식의 창을 띄우고, 닫기 키를 정한 시간만큼 누르고 있을 때까지 기다립니다. 닫으면 창을 내립니다.
    /// </summary>
    /// <param name="pauseTime">켜면 창이 떠 있는 동안 게임 시간을 멈추고, 닫을 때 되돌립니다.</param>
    /// <remarks>
    /// 창이 뜰 때 이미 눌려 있던 닫기 키는 한 번 뗀 뒤부터 셉니다. 앞 대사를 넘기려고 누른 채로 있던 키로 창이 닫히지 않게 하기 위해서입니다.
    /// </remarks>
    [TutorialEvent("튜토리얼 창 띄우기 (닫을 때까지 대기)")]
    public IEnumerator ShowWindow(string title, string body, Sprite image, string hint, bool pauseTime)
    {
        DefenseTutorialOverlay window = ResolveWindow();
        if (window == null)
        {
            yield break;
        }

        window.Show(TutorialPage.CreateWindowPage(title, body, image, hint));
        window.SetGaugeProgress(0.0f);
        s_isWindowOpen = true;
        if (DefenseManager.Instance != null)
        {
            DefenseManager.Instance.FadeOutBannerMessage(m_windowBannerFadeSeconds);
        }
        if (pauseTime)
        {
            PauseTime();
        }

        // 시간이 멈춰 있어도 닫을 수 있도록 실제 시간으로 셉니다.
        float elapsed = 0.0f;
        float held = 0.0f;
        bool released = false;
        while (s_isWindowOpen)
        {
            float deltaTime = Time.unscaledDeltaTime;
            elapsed += deltaTime;
            Keyboard keyboard = Keyboard.current;
            bool pressed = keyboard != null && m_closeKey != Key.None && keyboard[m_closeKey].isPressed;
            released |= !pressed;
            if (elapsed >= m_closeGuardSeconds && released && pressed)
            {
                // 프레임이 한 번 길게 멈추면 그 시간을 한꺼번에 더해 짧게 누른 것도 닫히므로, 한 프레임에 더하는 시간을 제한합니다.
                held += Mathf.Min(deltaTime, MaxHoldStepSeconds);
            }
            else
            {
                held = 0.0f;
            }

            window.SetGaugeProgress(m_closeHoldSeconds > 0.0f ? Mathf.Clamp01(held / m_closeHoldSeconds) : 0.0f);
            if (held > 0.0f && held >= m_closeHoldSeconds)
            {
                break;
            }

            yield return null;
        }

        s_isWindowOpen = false;
        window.SetGaugeProgress(0.0f);
        window.Hide();
        if (pauseTime)
        {
            ResumeTime();
        }
    }

    /// <summary>게임 시간을 멈추고 분대 조작을 막습니다. 시간 재개를 부르거나 튜토리얼이 끝날 때까지 유지합니다.</summary>
    [TutorialEvent("시간 멈춤")]
    public void PauseTime()
    {
        if (!m_isHoldingPause && Time.timeScale > 0.0f)
        {
            m_resumeTimeScale = Time.timeScale;
        }

        m_isHoldingPause = true;
        Time.timeScale = 0.0f;
        SetSquadGameplayInput(false);
    }

    /// <summary>시간 멈춤으로 멈춘 게임 시간과 분대 조작을 되돌립니다. 멈춘 적이 없으면 아무것도 하지 않습니다.</summary>
    [TutorialEvent("시간 재개")]
    public void ResumeTime()
    {
        if (!m_isHoldingPause)
        {
            return;
        }

        m_isHoldingPause = false;
        if (Time.timeScale <= 0.0f)
        {
            Time.timeScale = m_resumeTimeScale > 0.0f ? m_resumeTimeScale : 1.0f;
        }

        SetSquadGameplayInput(true);
    }

    /// <summary>
    /// 튜토리얼 대상 위에 목표 마커(함정 재료 아이콘)를 띄웁니다. 대상마다 한 번씩 부릅니다.
    /// </summary>
    /// <param name="targetId">마커를 띄울 대상의 ID입니다(씬 오브젝트의 <see cref="TutorialTarget"/>). 꺼져 있는 대상도 찾습니다.</param>
    /// <param name="icon">마커에 쓸 이미지입니다. 비우면 템플릿의 기본 이미지(함정 재료 아이콘)를 씁니다.</param>
    /// <param name="sizeMultiplier">마커 크기 배율입니다. 0이나 1이면 템플릿 크기 그대로입니다.</param>
    /// <param name="iconScale">테두리 안 아이콘만 줄이거나 키우는 배율입니다. 0이나 1이면 그대로입니다. 아이콘이 테두리 밖으로 나가면 1보다 작게 둡니다.</param>
    /// <remarks>
    /// 마커는 대상의 보이는 모습(켜진 렌더러) 맨 위를 따라갑니다. 청사진과 설치된 모습의 높이가 달라도 맞춰 움직입니다.
    /// 대상이 함정이면 설치하거나 수리해 내구도를 가득 채우는 순간 그 마커만 내립니다. 나머지는 목표 대상 마커 모두 내리기나 튜토리얼 종료 때 내립니다.
    /// </remarks>
    [TutorialEvent("목표 대상 위 마커 띄우기")]
    public void ShowTargetMarker(string targetId, Sprite icon, float sizeMultiplier, float iconScale)
    {
        TutorialTarget target = TutorialTarget.Find(targetId);
        if (target == null)
        {
            Debug.LogWarning($"목표 대상 위 마커 띄우기: 대상 '{targetId}'를 찾지 못했습니다.", this);
            return;
        }

        CreateTargetMarker(target.transform, targetId, icon, sizeMultiplier, iconScale, target.GetComponent<Trap>(), null);
    }

    /// <summary>
    /// 분대원 머리 위에 목표 마커를 띄웁니다(함정 마커와 같은 모양). 다운된 동료를 구조할 때처럼 찾아갈 대원을 표시합니다.
    /// </summary>
    /// <param name="memberNumber">마커를 띄울 캐릭터 번호입니다(캐릭터 변경 키 1~3과 같은 번호).</param>
    /// <param name="icon">마커에 쓸 이미지입니다. 비우면 템플릿의 기본 이미지를 씁니다.</param>
    /// <param name="sizeMultiplier">마커 크기 배율입니다. 0이나 1이면 템플릿 크기 그대로입니다.</param>
    /// <remarks>그 대원이 다운된 상태였다가 구조돼 일어나면 마커를 내립니다. 그 밖에는 목표 대상 마커 모두 내리기나 튜토리얼 종료 때 내립니다.</remarks>
    [TutorialEvent("대원 머리 위 마커 띄우기 (구조되면 내림)")]
    public void ShowSquadMemberMarker(int memberNumber, Sprite icon, float sizeMultiplier)
    {
        SquadManager squad = SquadManager.Instance;
        int index = memberNumber - 1;
        SquadMemberController member = squad != null && index >= 0 && index < squad.SquadMembers.Count ? squad.SquadMembers[index] : null;
        if (member == null)
        {
            Debug.LogWarning($"대원 머리 위 마커 띄우기: {memberNumber}번 캐릭터를 찾지 못했습니다.", this);
            return;
        }

        CreateTargetMarker(member.transform, member.name, icon, sizeMultiplier, 1.0f, null, member.GetComponent<PlayerHealth>());
    }

    /// <summary>대상 위에 마커를 하나 만듭니다. 같은 대상에 이미 있으면 만들지 않습니다.</summary>
    private void CreateTargetMarker(Transform root, string label, Sprite icon, float sizeMultiplier, float iconScale, Trap trap, PlayerHealth member)
    {
        if (m_targetMarkerTemplate == null)
        {
            Debug.LogWarning($"목표 대상 위 마커 띄우기: {nameof(TutorialEvents)}에 대상 마커 템플릿이 비어 있습니다.", this);
            return;
        }

        foreach (TargetMarker existing in m_targetMarkers)
        {
            if (existing.Root == root)
            {
                return;
            }
        }

        Transform anchor = new GameObject($"TutorialTargetMarkerAnchor_{label}").transform;
        ObjectiveIndicatorController view = Instantiate(m_targetMarkerTemplate, m_targetMarkerTemplate.transform.parent);
        view.name = $"TutorialTargetMarker_{label}";
        view.gameObject.SetActive(true);
        view.SetTarget(anchor);
        view.SetOnscreenMarkerSprite(icon);
        view.SetOnscreenMarkerScale(sizeMultiplier);
        view.SetOnscreenIconScale(iconScale);
        view.SetVisible(true);

        TargetMarker marker = new()
        {
            Root = root,
            Trap = trap,
            Member = member,
            View = view,
            Anchor = anchor,
        };
        m_targetMarkers.Add(marker);
        UpdateTargetMarkerAnchor(marker);
    }

    /// <summary>목표 대상 위 마커 띄우기로 띄운 마커를 모두 내립니다.</summary>
    [TutorialEvent("목표 대상 마커 모두 내리기")]
    public void HideTargetMarkers()
    {
        for (int i = m_targetMarkers.Count - 1; i >= 0; i--)
        {
            DestroyTargetMarker(i);
        }
    }

    /// <summary>
    /// 정한 위치에 화살표 마커를 띄웁니다. 화면 밖이면 가장자리에 방향 화살표가 나옵니다. 마커 숨기기를 부르거나 튜토리얼이 끝날 때까지 유지합니다.
    /// </summary>
    /// <param name="position">마커를 띄울 월드 좌표입니다.</param>
    [TutorialEvent("화살표 마커 표시")]
    public void ShowMarker(Vector3 position)
    {
        if (m_marker == null)
        {
            Debug.LogWarning($"{nameof(TutorialEvents)}에 마커용 {nameof(ObjectiveIndicatorController)}가 연결되지 않아 마커를 띄우지 못했습니다.", this);
            return;
        }

        if (m_markerAnchor == null)
        {
            m_markerAnchor = new GameObject("TutorialMarkerAnchor").transform;
        }

        m_markerAnchor.position = position;
        m_marker.SetTarget(m_markerAnchor);
        m_marker.SetVisible(true);
    }

    /// <summary>화살표 마커를 숨깁니다.</summary>
    [TutorialEvent("화살표 마커 숨기기")]
    public void HideMarker()
    {
        if (m_marker != null)
        {
            m_marker.SetVisible(false);
        }
    }

    /// <summary>
    /// 캐릭터 발밑에서 정한 위치까지 NavMesh 경로선과 방향 화살표를 띄웁니다. 캐릭터가 도착 반경에 들어가면 숨깁니다.
    /// </summary>
    /// <param name="position">안내할 위치(월드 좌표)입니다.</param>
    /// <param name="arriveRadius">도착으로 볼 반경(m)입니다. 높이는 보지 않습니다.</param>
    /// <param name="waitForArrival">켜면 도착할 때까지 다음 줄로 넘어가지 않습니다.</param>
    /// <param name="arriveAreaObject">
    /// 도착 범위를 보여 줄 씬 오브젝트(표시용 이펙트 등)의 하이어라키 경로입니다. 씬에서는 꺼 두고, 경로선이 떠 있는 동안만 켭니다.
    /// 위치와 크기는 씬에 놓은 그대로 씁니다. 비우면 표시하지 않습니다.
    /// 그 오브젝트에 <see cref="TutorialArriveArea"/>가 있으면 위치·반경 칸은 쓰지 않고, 그 콜라이더 범위로 안내·도착 판정을 하며 이펙트만 켜고 끕니다.
    /// </param>
    /// <param name="reshowOnLeave">
    /// 켜면 도착해도 경로선만 감췄다가, 반경을 벗어나면 다시 띄웁니다. 경로선 숨기기를 부를 때까지 계속합니다.
    /// 동료 구조나 정문 수리처럼 도착한 뒤 할 일이 남은 안내에 씁니다. 끄면 도착하는 순간 경로선을 숨기고 끝냅니다.
    /// </param>
    [TutorialEvent("경로선 안내 (도착하면 숨김)")]
    public IEnumerator ShowPathLine(Vector3 position, float arriveRadius, bool waitForArrival, [TutorialScenePath] string arriveAreaObject, bool reshowOnLeave)
    {
        ObjectivePathArrow arrow = ResolvePathArrow();
        if (arrow == null)
        {
            Debug.LogWarning($"씬에 {nameof(ObjectivePathArrow)}가 없어 경로선을 띄우지 못했습니다.", this);
            yield break;
        }

        if (m_pathAnchor == null)
        {
            m_pathAnchor = new GameObject("TutorialPathAnchor").transform;
        }

        StopPathWatch();
        HidePathAreaObject();

        // 도착 범위 오브젝트(TutorialArriveArea)가 있으면 위치·반경 대신 그 콜라이더 범위로 안내하고 도착을 판정합니다.
        m_pathArea = TutorialArriveArea.Find(arriveAreaObject);
        if (m_pathArea != null)
        {
            position = m_pathArea.GroundCenter;
            arriveRadius = m_pathArea.ApproxRadius;
        }

        m_pathTarget = position;
        m_pathArriveRadius = Mathf.Max(0.1f, arriveRadius);
        m_pathAnchor.position = position;
        arrow.SetTarget(m_pathAnchor);
        arrow.SetVisible(true);
        if (m_pathArea != null)
        {
            m_pathArea.SetVisualVisible(true);
        }
        else if (!string.IsNullOrEmpty(arriveAreaObject))
        {
            m_pathAreaObject = arriveAreaObject;
            SetSceneObjectActive(arriveAreaObject, true);
        }

        if (reshowOnLeave)
        {
            m_pathWatch = StartCoroutine(WatchArrivalAndLeave(arrow));
            while (waitForArrival && !IsPlayerAtPathTarget(0.0f))
            {
                yield return null;
            }

            yield break;
        }

        IEnumerator watch = WatchArrival();
        if (waitForArrival)
        {
            yield return watch;
        }
        else
        {
            m_pathWatch = StartCoroutine(watch);
        }
    }

    /// <summary>경로선과 방향 화살표를 숨깁니다.</summary>
    [TutorialEvent("경로선 숨기기")]
    public void HidePathLine()
    {
        StopPathWatch();
        HidePathAreaObject();
        m_pathArriveRadius = 0.0f;
        if (m_pathArrow != null)
        {
            m_pathArrow.SetVisible(false);
        }
    }

    /// <summary>
    /// 고른 대상 중 아직 마치지 않은 것 가운데 캐릭터와 가장 가까운 곳으로 경로선을 띄웁니다. 가장 가까운 대상이 바뀌면 경로선도 바꿉니다.
    /// </summary>
    /// <param name="targetIds">안내할 대상 ID들입니다(씬 오브젝트의 <see cref="TutorialTarget"/>). 쉼표로 나눕니다. 예: Trap_BarbedWire,Trap_Mine</param>
    /// <param name="arriveRadius">이 거리(m) 안으로 들어가면 그 대상에 도착한 것으로 보고 경로선을 감춥니다. 멀어지면 다시 띄웁니다. 높이는 보지 않습니다.</param>
    /// <remarks>
    /// 함정은 설치돼 있고 내구도가 가득 차면, 방어 목표 수리는 체력이 가득 차면 마친 것으로 봅니다. 모두 마치면 경로선을 숨기고 끝냅니다.
    /// 기다리지 않고 바로 다음 줄로 넘어갑니다. 중간에 멈추려면 경로선 숨기기를 부릅니다.
    /// </remarks>
    [TutorialEvent("경로선 안내 (남은 대상 중 가장 가까운 곳)")]
    public void ShowPathLineToNearestTarget(string targetIds, float arriveRadius)
    {
        ObjectivePathArrow arrow = ResolvePathArrow();
        if (arrow == null)
        {
            Debug.LogWarning($"씬에 {nameof(ObjectivePathArrow)}가 없어 경로선을 띄우지 못했습니다.", this);
            return;
        }

        List<TutorialTarget> targets = new();
        foreach (string id in (targetIds ?? string.Empty).Split(','))
        {
            TutorialTarget target = TutorialTarget.Find(id.Trim());
            if (target != null)
            {
                targets.Add(target);
            }
            else if (!string.IsNullOrWhiteSpace(id))
            {
                Debug.LogWarning($"경로선 안내 (가장 가까운 곳): 대상 '{id.Trim()}'를 찾지 못했습니다.", this);
            }
        }

        StopPathWatch();
        HidePathAreaObject();
        m_pathWatch = StartCoroutine(WatchNearestTarget(arrow, targets, Mathf.Max(0.1f, arriveRadius)));
    }

    /// <summary>남은 대상 중 가장 가까운 곳으로 경로선을 옮기며 따라갑니다. 모두 마치면 경로선을 숨기고 끝냅니다.</summary>
    /// <remarks>대상이 바뀔 때 경로선이 페이드아웃한 뒤 새 대상으로 바뀌도록, 안내 기준점 두 개를 번갈아 씁니다.</remarks>
    private IEnumerator WatchNearestTarget(ObjectivePathArrow arrow, List<TutorialTarget> targets, float arriveRadius)
    {
        if (m_pathAnchor == null)
        {
            m_pathAnchor = new GameObject("TutorialPathAnchor").transform;
        }

        if (m_pathAnchorAlt == null)
        {
            m_pathAnchorAlt = new GameObject("TutorialPathAnchorAlt").transform;
        }

        TutorialTarget current = null;
        Transform anchor = m_pathAnchor;
        bool shown = false;
        while (true)
        {
            SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
            TutorialTarget nearest = null;
            float nearestSqr = float.MaxValue;
            foreach (TutorialTarget target in targets)
            {
                if (target == null || IsTargetDone(target) || player == null)
                {
                    continue;
                }

                Vector3 offset = WorldMarkerAnchor.GetBottomCenter(target.transform) - player.transform.position;
                offset.y = 0.0f;
                if (offset.sqrMagnitude < nearestSqr)
                {
                    nearestSqr = offset.sqrMagnitude;
                    nearest = target;
                }
            }

            if (nearest == null && player != null)
            {
                break;
            }

            if (nearest != null && nearest != current)
            {
                current = nearest;
                anchor = anchor == m_pathAnchor ? m_pathAnchorAlt : m_pathAnchor;
                anchor.position = WorldMarkerAnchor.GetBottomCenter(current.transform);
                m_pathTarget = anchor.position;
                m_pathArriveRadius = arriveRadius;
                arrow.SetTarget(anchor);
            }

            // 도착하면 경로선만 감추고, 반경을 벗어나면 다시 띄웁니다. 경계에서 깜빡이지 않도록 다시 띄우는 기준은 조금 넓게 잡습니다.
            bool wantShown = nearest != null && nearestSqr > Square(shown ? arriveRadius : arriveRadius + PathReshowMargin);
            if (wantShown != shown)
            {
                shown = wantShown;
                arrow.SetVisible(shown);
            }

            yield return null;
        }

        m_pathWatch = null;
        HidePathLine();
    }

    private static float Square(float value)
    {
        return value * value;
    }

    /// <summary>안내 대상을 마쳤는지 여부입니다. 함정은 설치돼 있고 내구도가 가득 차면, 방어 목표 수리는 체력이 가득 차면 마친 것입니다.</summary>
    private static bool IsTargetDone(TutorialTarget target)
    {
        Trap trap = target.GetComponent<Trap>();
        if (trap != null)
        {
            return trap.IsBuilt && trap.Health >= trap.MaxHealth;
        }

        DefenseRepairInteractable repair = target.GetComponentInChildren<DefenseRepairInteractable>(true);
        return repair != null && repair.IsFullHealth;
    }

    /// <summary>경로선과 함께 켠 도착 범위 표시 오브젝트를 끕니다.</summary>
    private void HidePathAreaObject()
    {
        if (m_pathArea != null)
        {
            m_pathArea.SetVisualVisible(false);
            m_pathArea = null;
        }

        if (!string.IsNullOrEmpty(m_pathAreaObject))
        {
            SetSceneObjectActive(m_pathAreaObject, false);
            m_pathAreaObject = null;
        }
    }

    /// <summary>
    /// 씬 오브젝트를 켜거나 끕니다. 위치 사수 지점처럼 그 파트에서만 보일 표시용 오브젝트(이펙트 등)를 다룹니다. 꺼져 있는 오브젝트도 찾습니다.
    /// </summary>
    /// <param name="objectPath">켜고 끌 씬 오브젝트의 하이어라키 경로입니다. Inspector에서 씬 오브젝트를 끌어 놓으면 채워집니다.</param>
    /// <param name="active">켜면 보입니다.</param>
    /// <remarks>처음 바꿀 때의 상태를 기억해 두었다가 튜토리얼이 끝나면 그 상태로 되돌립니다.</remarks>
    [TutorialEvent("씬 오브젝트 켜기/끄기 (표시용 이펙트 등)")]
    public void SetSceneObjectActive([TutorialScenePath] string objectPath, bool active)
    {
        GameObject target = FindSceneObject(objectPath);
        if (target == null)
        {
            Debug.LogWarning($"씬 오브젝트 켜기/끄기: 씬에서 '{objectPath}'를 찾지 못했습니다.", this);
            return;
        }

        if (!m_sceneObjectStates.ContainsKey(target))
        {
            m_sceneObjectStates[target] = target.activeSelf;
        }

        target.SetActive(active);
    }

    /// <summary>하이어라키 경로가 같은 씬 오브젝트를 찾습니다. 꺼진 오브젝트도 찾습니다. 없으면 null입니다.</summary>
    public static GameObject FindSceneObject(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.name == GetLeafName(path) && GetHierarchyPath(candidate) == path)
            {
                return candidate.gameObject;
            }
        }

        return null;
    }

    /// <summary>하이어라키 경로의 마지막 이름입니다. 경로 전체를 만들기 전에 이름으로 먼저 거르는 데 씁니다.</summary>
    private static string GetLeafName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }

    /// <summary>
    /// 정한 위치 둘레에 적을 만듭니다. 위치는 NavMesh 위로 붙이고, 조작 중인 캐릭터를 바라보게 세웁니다.
    /// </summary>
    /// <param name="prefab">만들 적 프리팹입니다.</param>
    /// <param name="count">만들 수입니다.</param>
    /// <param name="position">만들 위치(월드 좌표)입니다.</param>
    /// <param name="spreadRadius">위치 둘레로 흩어 놓을 반경(m)입니다. 0이면 모두 같은 위치입니다.</param>
    /// <param name="targetMode">켜면 튜토리얼용 과녁 모드로 만듭니다. 감지·추적·공격을 하지 않고 시킨 이동만 합니다.</param>
    /// <param name="killRule">과녁 모드일 때 이 적을 죽일 수 있는 공격입니다. 그 밖의 공격은 HP 1까지만 깎습니다.</param>
    /// <param name="returnRadius">과녁 모드일 때 돌아올 위치에서 이만큼(m) 벗어나면 걸어서 되돌아옵니다. 넉백으로 밀려난 과녁을 제자리로 돌리는 용도이며, 0이면 되돌아오지 않습니다. 돌아올 위치는 생성된 자리이고, 걸어 나오기를 마치면 도착한 자리로 바뀝니다.</param>
    [TutorialEvent("적 생성")]
    public void SpawnEnemies(EnemyController prefab, int count, Vector3 position, float spreadRadius, bool targetMode, TutorialTargetKillRule killRule, float returnRadius)
    {
        if (prefab == null)
        {
            Debug.LogWarning("적 생성 이벤트에 적 프리팹이 비어 있습니다.", this);
            return;
        }

        SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = spreadRadius > 0.0f ? UnityEngine.Random.insideUnitCircle * spreadRadius : Vector2.zero;
            Vector3 spawnPosition = position + new Vector3(offset.x, 0.0f, offset.y);
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, m_spawnSampleRadius, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
            }

            Quaternion rotation = Quaternion.identity;
            if (player != null)
            {
                Vector3 look = player.transform.position - spawnPosition;
                look.y = 0.0f;
                if (look.sqrMagnitude > 0.0001f)
                {
                    rotation = Quaternion.LookRotation(look);
                }
            }

            EnemyController enemy = Instantiate(prefab, spawnPosition, rotation);
            SetTargetMode(enemy, targetMode, killRule, returnRadius);
            m_spawnedEnemies.Add(enemy);
        }
    }

    /// <summary>
    /// 적 생성으로 만든 적의 튜토리얼용 과녁 모드를 켜거나 끕니다. 켜면 감지·추적·공격을 하지 않고 시킨 이동만 한 뒤 제자리에 섭니다.
    /// </summary>
    /// <param name="prefab">바꿀 적 프리팹입니다. 비우면 프리팹을 가리지 않습니다.</param>
    /// <param name="enabled">켜면 과녁 모드, 끄면 평소 판단과 평소 사망 규칙으로 돌아갑니다.</param>
    /// <param name="killRule">과녁 모드일 때 죽일 수 있는 공격입니다. 그 밖의 공격은 HP 1까지만 깎습니다.</param>
    /// <param name="returnRadius">과녁 모드일 때 돌아올 위치(지금 선 자리)에서 이만큼(m) 벗어나면 걸어서 되돌아옵니다. 0이면 되돌아오지 않습니다.</param>
    [TutorialEvent("튜토리얼 적 과녁 모드 (켜기/끄기)")]
    public void SetEnemyTargetMode(EnemyController prefab, bool enabled, TutorialTargetKillRule killRule, float returnRadius)
    {
        foreach (EnemyController enemy in GetSpawnedEnemies(prefab))
        {
            SetTargetMode(enemy, enabled, killRule, returnRadius);
        }
    }

    /// <summary>적 하나의 과녁 모드(행동), 되돌아오기 거리, 처치 조건(체력)을 함께 바꿉니다.</summary>
    private static void SetTargetMode(EnemyController enemy, bool enabled, TutorialTargetKillRule killRule, float returnRadius)
    {
        enemy.TutorialTargetMode = enabled;
        enemy.SetTargetReturnRadius(enabled ? returnRadius : 0.0f);
        if (enemy.TryGetComponent(out EnemyHealth health))
        {
            health.SetTargetKillRule(enabled, killRule);
        }
    }

    /// <summary>
    /// 적 생성으로 만든 과녁 모드 적을 조작 중인 캐릭터 쪽으로 정한 거리만큼 걸어 나오게 합니다. 도착하면 그 자리에 섭니다.
    /// </summary>
    /// <param name="prefab">움직일 적 프리팹입니다. 비우면 프리팹을 가리지 않습니다.</param>
    /// <param name="distance">걸어 나올 거리(m)입니다.</param>
    /// <remarks>과녁 모드가 꺼진 적은 평소처럼 스스로 움직이므로 건드리지 않습니다.</remarks>
    [TutorialEvent("튜토리얼 적 걸어 나오기")]
    public void WalkEnemiesForward(EnemyController prefab, float distance)
    {
        SquadMemberController player = SquadManager.Instance != null ? SquadManager.Instance.PlayerSquadMember : null;
        foreach (EnemyController enemy in GetSpawnedEnemies(prefab))
        {
            Vector3 direction = player != null ? player.transform.position - enemy.transform.position : enemy.transform.forward;
            direction.y = 0.0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Vector3 destination = enemy.transform.position + direction.normalized * distance;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, m_spawnSampleRadius, NavMesh.AllAreas))
            {
                destination = hit.position;
            }

            enemy.TargetMoveTo(destination);
        }
    }

    /// <summary>적 생성으로 만든 적 중 살아 있고 프리팹이 맞는 적입니다.</summary>
    private IEnumerable<EnemyController> GetSpawnedEnemies(EnemyController prefab)
    {
        foreach (EnemyController enemy in m_spawnedEnemies)
        {
            if (enemy != null && IsFromPrefab(enemy, prefab))
            {
                yield return enemy;
            }
        }
    }

    /// <summary>튜토리얼 연출이 직접 만든 적을 튜토리얼 적 목록에 넣습니다. 튜토리얼이 끝날 때 시체(래그돌)까지 함께 없앱니다.</summary>
    /// <param name="enemy">넣을 적입니다.</param>
    public void TrackSpawnedEnemy(EnemyController enemy)
    {
        if (enemy != null && !m_spawnedEnemies.Contains(enemy))
        {
            m_spawnedEnemies.Add(enemy);
        }
    }

    /// <summary>
    /// 적 생성으로 만든 적(이미 죽어 시체로 남은 것 포함)을 없앱니다. 스포너나 씬에 원래 있던 적은 건드리지 않습니다.
    /// </summary>
    /// <remarks>튜토리얼이 끝날 때 프리팹·수 없이 불러, 남은 과녁과 래그돌 시체를 모두 치우고 본 게임을 시작합니다.</remarks>
    /// <param name="prefab">없앨 적 프리팹입니다. 비우면 프리팹을 가리지 않습니다.</param>
    /// <param name="count">없앨 수입니다. 0이면 모두 없앱니다.</param>
    [TutorialEvent("튜토리얼 적 제거")]
    public void DespawnEnemies(EnemyController prefab, int count)
    {
        int removed = 0;
        for (int i = m_spawnedEnemies.Count - 1; i >= 0; i--)
        {
            EnemyController enemy = m_spawnedEnemies[i];
            if (enemy == null)
            {
                m_spawnedEnemies.RemoveAt(i);
                continue;
            }

            if (!IsFromPrefab(enemy, prefab))
            {
                continue;
            }

            m_spawnedEnemies.RemoveAt(i);
            Destroy(enemy.gameObject);
            removed++;
            if (count > 0 && removed >= count)
            {
                return;
            }
        }
    }

    /// <summary>분대 인벤토리에 아이템(수류탄, 화염병, 회복약 등)을 넣습니다.</summary>
    /// <param name="item">넣을 아이템 정의입니다(Grenade_Definition, Firebomb_Definition, Potion_Definition 등).</param>
    /// <param name="count">넣을 수량입니다.</param>
    [TutorialEvent("아이템 지급")]
    public void GiveItem(ItemDefinition item, int count)
    {
        if (item == null || count <= 0)
        {
            Debug.LogWarning("아이템 지급 이벤트에 아이템이 비어 있거나 수량이 0입니다.", this);
            return;
        }

        SquadInventoryManager inventory = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        if (inventory == null)
        {
            Debug.LogWarning("아이템 지급: 씬에서 SquadInventoryManager를 찾지 못했습니다.", this);
            return;
        }

        inventory.TryAcquire(item.ItemDefinitionId, count, out int leftover);
        if (leftover > 0)
        {
            Debug.LogWarning($"아이템 지급: 인벤토리 공간이 부족해 {item.DisplayName} {leftover}개를 넣지 못했습니다.", this);
        }
    }

    /// <summary>분대 인벤토리의 아이템 수량을 정한 값으로 맞춥니다. 모자라면 넣고, 많으면 뺍니다.</summary>
    /// <param name="item">맞출 아이템 정의입니다(Grenade_Definition, Firebomb_Definition, Potion_Definition 등).</param>
    /// <param name="count">맞출 수량입니다.</param>
    [TutorialEvent("아이템 수량 맞추기")]
    public void SetItemCount(ItemDefinition item, int count)
    {
        SquadInventoryManager inventory = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        if (item == null || inventory == null)
        {
            Debug.LogWarning("아이템 수량 맞추기: 아이템이 비어 있거나 씬에서 SquadInventoryManager를 찾지 못했습니다.", this);
            return;
        }

        string id = item.ItemDefinitionId;
        int current = inventory.CountOf(id);
        if (current < count)
        {
            GiveItem(item, count - current);
            return;
        }

        // 소모 없이 사용이 켜져 있어도 실제로 빼야 하므로 TryConsumeOne 대신 칸에서 직접 뺍니다.
        int excess = current - count;
        SlotContainer<string> slots = inventory.Inventory;
        for (int i = slots.SlotCount - 1; i >= 0 && excess > 0; i--)
        {
            if (slots.IsEmpty(i) || !string.Equals(slots.GetContent(i), id, StringComparison.Ordinal))
            {
                continue;
            }

            int remove = Mathf.Min(excess, slots.GetQuantity(i));
            if (slots.TryRemove(i, remove) == SlotOpResult.Success)
            {
                excess -= remove;
            }
        }
    }

    /// <summary>분대 인벤토리에 그 아이템이 하나도 없을 때만 정한 수량을 넣습니다. 하나라도 있으면 아무것도 하지 않습니다.</summary>
    /// <param name="item">확인할 아이템 정의입니다(Grenade_Definition, Firebomb_Definition 등).</param>
    /// <param name="count">없을 때 넣을 수량입니다.</param>
    /// <remarks>제조실에서 만들어 온 아이템을 덮어쓰거나 빼지 않고, 다 써서 없는 경우만 채우는 방어용입니다.</remarks>
    [TutorialEvent("아이템이 없으면 지급")]
    public void GiveItemIfEmpty(ItemDefinition item, int count)
    {
        SquadInventoryManager inventory = FindFirstObjectByType<SquadInventoryManager>(FindObjectsInactive.Include);
        if (item == null || inventory == null)
        {
            Debug.LogWarning("아이템이 없으면 지급: 아이템이 비어 있거나 씬에서 SquadInventoryManager를 찾지 못했습니다.", this);
            return;
        }

        if (inventory.CountOf(item.ItemDefinitionId) <= 0)
        {
            GiveItem(item, count);
        }
    }

    /// <summary>셸터 창고에 자원(함정 설치 자원 등)을 넣습니다.</summary>
    /// <param name="resource">넣을 자원 종류입니다.</param>
    /// <param name="amount">넣을 수량입니다.</param>
    [TutorialEvent("자원 지급 (함정 설치 자원 등)")]
    public void GiveResource(TutorialResourceKind resource, int amount)
    {
        StorageFacility storage = ShelterSceneDataManager.Instance?.Storage;
        if (storage == null || amount <= 0)
        {
            Debug.LogWarning("자원 지급: 창고(ShelterSceneDataManager)가 없거나 수량이 0입니다.", this);
            return;
        }

        string id = resource switch
        {
            TutorialResourceKind.UpgradeMaterial => ResourceIds.UpgradeMaterial,
            TutorialResourceKind.CraftingMaterial => ResourceIds.CraftingMaterial,
            TutorialResourceKind.TrapUpgradeMaterial => ResourceIds.TrapUpgradeMaterial,
            _ => ResourceIds.TrapMaterial,
        };

        if (!storage.TryAddResource(id, amount))
        {
            Debug.LogWarning($"자원 지급: {id} {amount}개를 넣지 못했습니다.", this);
        }
    }

    /// <summary>
    /// 아이템(투척물, 회복약 등)과 함정 설치·수리 자원을 소모하지 않고 쓰게 하거나 원래대로 돌립니다.
    /// </summary>
    /// <param name="enabled">켜면 소모 없이 쓰고 수량이 0이어도 씁니다. 끄면 원래대로 소모합니다.</param>
    /// <remarks>표시 수량과 UI는 바꾸지 않습니다. 튜토리얼이 끝나면 자동으로 꺼집니다.</remarks>
    [TutorialEvent("아이템·자원 소모 없이 사용 (켜기/끄기)")]
    public void SetFreeUse(bool enabled)
    {
        SquadInventoryManager.FreeUse = enabled;
    }

    /// <summary>
    /// 아이템(투척물, 회복약 등)을 평소처럼 소모하되, 다 써서 0개여도 계속 쓸 수 있게 하거나 원래대로 돌립니다.
    /// </summary>
    /// <param name="enabled">켜면 0개여도 씁니다. 끄면 원래대로 0개일 때 못 씁니다.</param>
    /// <remarks>함정 설치·수리 자원에는 적용하지 않습니다. 튜토리얼이 끝나면 자동으로 꺼집니다.</remarks>
    [TutorialEvent("아이템 다 써도 사용 (켜기/끄기)")]
    public void SetUseWhenEmpty(bool enabled)
    {
        SquadInventoryManager.UseWhenEmpty = enabled;
    }

    /// <summary>
    /// 모든 대원의 HP가 1 아래로 내려가지 않게 하거나 원래대로 돌립니다. 투척물 자폭 등으로 다운되는 것을 막습니다.
    /// </summary>
    /// <param name="enabled">켜면 HP 1에서 더 깎이지 않습니다. 끄면 원래대로 피해를 받습니다.</param>
    /// <remarks>튜토리얼이 끝나면 자동으로 꺼집니다.</remarks>
    [TutorialEvent("반무적 (HP 1 아래로 안 떨어짐, 켜기/끄기)")]
    public void SetKeepMinimumHp(bool enabled)
    {
        PlayerHealth.KeepMinimumHp = enabled;
    }

    /// <summary>
    /// 다운 타이머가 다 닳아도 전투 이탈로 넘어가지 않고 다운 상태를 유지하게 하거나 원래대로 돌립니다.
    /// </summary>
    /// <param name="enabled">켜면 타이머가 0이 돼도 다운 상태로 남습니다(구조하면 일어남).</param>
    /// <remarks>튜토리얼이 끝나면 자동으로 꺼집니다.</remarks>
    [TutorialEvent("구조 유지 (다운 타이머가 끝나도 이탈 안 함, 켜기/끄기)")]
    public void SetKeepDownedOnTimeout(bool enabled)
    {
        PlayerHealth.KeepDownedOnTimeout = enabled;
    }

    /// <summary>AI 동료가 다운된 동료를 자동으로 구조하지 못하게 하거나 원래대로 돌립니다.</summary>
    /// <param name="blocked">켜면 플레이어만 구조할 수 있습니다.</param>
    /// <remarks>튜토리얼이 끝나면 자동으로 꺼집니다.</remarks>
    [TutorialEvent("AI 자동 구조 막기 (켜기/끄기)")]
    public void SetAutoRescueBlocked(bool blocked)
    {
        SquadManager.AutoRescueBlocked = blocked;
    }

    /// <summary>
    /// 분대원 한 명을 정한 위치로 옮기고 다운 상태로 만듭니다. 사고 현장에 쓰러진 동료를 깔아 두는 용도입니다.
    /// </summary>
    /// <param name="memberNumber">다운시킬 캐릭터 번호입니다(캐릭터 변경 키 1~3과 같은 번호).</param>
    /// <param name="position">옮길 위치입니다.</param>
    /// <remarks>
    /// 그 캐릭터를 조작 중이었으면 다운과 함께 평소처럼 다른 캐릭터로 자동 전환됩니다.
    /// 반무적이 켜져 있어도 다운시킵니다.
    /// </remarks>
    [TutorialEvent("대원 다운 (위치 지정)")]
    public void DownSquadMember(int memberNumber, Vector3 position)
    {
        SquadManager squad = SquadManager.Instance;
        int index = memberNumber - 1;
        if (squad == null || index < 0 || index >= squad.SquadMembers.Count || squad.SquadMembers[index] == null)
        {
            Debug.LogWarning($"대원 다운: {memberNumber}번 캐릭터를 찾지 못했습니다.", this);
            return;
        }

        SquadMemberController member = squad.SquadMembers[index];
        UnityEngine.AI.NavMeshAgent agent = member.GetComponent<UnityEngine.AI.NavMeshAgent>();
        CharacterController controller = member.GetComponent<CharacterController>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.Warp(position);
            agent.ResetPath();
        }
        else
        {
            // CharacterController가 켜져 있으면 Transform 이동을 덮어써서 잠시 끄고 옮깁니다.
            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controllerWasEnabled)
            {
                controller.enabled = false;
            }

            member.transform.position = position;
            if (controllerWasEnabled)
            {
                controller.enabled = true;
            }
        }

        Physics.SyncTransforms();

        PlayerHealth health = member.GetComponent<PlayerHealth>();
        if (health == null || !health.ForceDown())
        {
            Debug.LogWarning($"대원 다운: {member.name}을(를) 다운시키지 못했습니다(체력 컴포넌트가 없거나 이미 다운·이탈 상태).", this);
        }
    }

    /// <summary>
    /// 분대원 전원과 방어 목표(정문 등)의 체력을 가득 채웁니다. 다운된 대원은 일으켜 세운 뒤 채웁니다. 전투에서 이탈한 대원과 파괴된 방어 목표는 건드리지 않습니다.
    /// </summary>
    /// <remarks>튜토리얼을 마치고 본 방어전을 시작할 때 한 번 부릅니다(<see cref="TutorialFlowDirector"/> 완료 처리).</remarks>
    [TutorialEvent("분대·방어 목표 체력 가득 채우기")]
    public void RestoreSquadAndDefenseFull()
    {
        SquadManager squad = SquadManager.Instance;
        if (squad != null)
        {
            foreach (SquadMemberController member in squad.SquadMembers)
            {
                PlayerHealth health = member != null ? member.GetComponent<PlayerHealth>() : null;
                if (health == null || health.IsDead)
                {
                    continue;
                }

                if (health.IsDowned)
                {
                    health.ReviveFromDown(health.MaxHP);
                }

                if (!health.IsDead && !health.IsDowned)
                {
                    health.RestoreFull();
                }
            }
        }

        foreach (DefenseEventHealth target in FindObjectsByType<DefenseEventHealth>(FindObjectsSortMode.None))
        {
            if (!target.IsDead)
            {
                target.RestoreFull();
            }
        }
    }

    /// <summary>다운된 동료를 F 상호작용이나 서하 스킬로 구조하지 못하게 하거나 원래대로 돌립니다.</summary>
    /// <param name="blocked">켜면 구조할 수 없습니다. 구조 안내 창을 띄운 뒤 끕니다.</param>
    /// <remarks>AI 자동 구조는 "AI 자동 구조 막기"가 따로 막습니다. 튜토리얼이 끝나면 자동으로 꺼집니다.</remarks>
    [TutorialEvent("구조 막기 (F·서하 스킬, 켜기/끄기)")]
    public void SetRescueBlocked(bool blocked)
    {
        DownedAllyInteractable.RescueBlocked = blocked;
    }

    /// <summary>
    /// 시네마틱의 지뢰가 터지는 순간(<see cref="TutorialMineBlast"/>) 캐릭터를 정한 위치로 옮기고 다운시킵니다. "대원 다운 (위치 지정)"과 같은 동작을 폭발 시점에 합니다.
    /// </summary>
    /// <param name="memberNumber">다운시킬 캐릭터 번호입니다(캐릭터 변경 키 1~3과 같은 번호).</param>
    /// <param name="position">옮길 위치입니다.</param>
    /// <remarks>
    /// 시네마틱 재생과 같은 트리거에 넣습니다. 폭발 전에 시네마틱이 끝나면(건너뛰기 등) 그때 다운시킵니다.
    /// 시네마틱이 시작되지도 않으면 <see cref="BlastDownTimeoutSeconds"/>초 뒤에 다운시킵니다. 다운시킬 때까지 다음 블록으로 넘어가지 않습니다.
    /// </remarks>
    [TutorialEvent("대원 다운 (시네마틱 지뢰 폭발 때)")]
    public IEnumerator DownSquadMemberOnMineBlast(int memberNumber, Vector3 position)
    {
        bool exploded = false;
        void HandleExploded() => exploded = true;
        TutorialMineBlast.Exploded += HandleExploded;
        try
        {
            bool cinematicSeen = false;
            float elapsed = 0.0f;
            while (!exploded)
            {
                if (m_cinematicPlaying)
                {
                    cinematicSeen = true;
                }
                else if (cinematicSeen || elapsed >= BlastDownTimeoutSeconds)
                {
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        finally
        {
            TutorialMineBlast.Exploded -= HandleExploded;
        }

        DownSquadMember(memberNumber, position);
    }

    /// <summary>"대원 다운 (시네마틱 지뢰 폭발 때)"가 시네마틱이 시작되지 않을 때 기다리는 최대 시간(초)입니다.</summary>
    private const float BlastDownTimeoutSeconds = 5.0f;

    /// <summary>청솔 특수탄을 쏴도 줄지 않게 합니다. 특수탄 사격 파트에서만 켜고, 끝나면 끕니다.</summary>
    /// <param name="infinite">켜면 스킬로 장전한 특수탄이 줄지 않습니다.</param>
    /// <remarks>튜토리얼이 끝날 때도 끕니다.</remarks>
    [TutorialEvent("청솔 특수탄 무한 켜기/끄기")]
    public void SetInfiniteSpecialAmmo(bool infinite)
    {
        ChungSolDragonBreathSkill.TutorialInfiniteRounds = infinite;
    }

    /// <summary>조작 캐릭터를 정한 캐릭터로 바꿉니다. 캐릭터 변경 키를 막아 둔 챕터에서도 바꿉니다.</summary>
    /// <param name="memberNumber">조작할 캐릭터 번호입니다(캐릭터 변경 키 1~3과 같은 번호).</param>
    /// <remarks>그 캐릭터가 다운·이탈 상태면 바꾸지 못하고 경고만 남깁니다.</remarks>
    [TutorialEvent("조작 캐릭터 변경 (강제)")]
    public void SwitchToSquadMember(int memberNumber)
    {
        SquadManager squad = SquadManager.Instance;
        int index = memberNumber - 1;
        if (squad == null || index < 0 || index >= squad.SquadMembers.Count)
        {
            Debug.LogWarning($"조작 캐릭터 변경: {memberNumber}번 캐릭터를 찾지 못했습니다.", this);
            return;
        }

        squad.SwitchToMember(index);
        if (squad.PlayerSquadMemberIndex != index)
        {
            Debug.LogWarning($"조작 캐릭터 변경: {memberNumber}번 캐릭터로 바꾸지 못했습니다(다운·이탈 상태 등).", this);
        }
    }

    /// <summary>
    /// AI 동료에게 정한 위치로 걸어가 그 자리를 지키라는 명령을 내립니다. 위치 사수(Q/E) 명령과 같은 명령입니다.
    /// </summary>
    /// <param name="memberNumber">명령할 캐릭터 번호입니다(캐릭터 변경 키 1~3과 같은 번호). 조작 중인 캐릭터면 경고만 남깁니다.</param>
    /// <param name="position">갈 위치입니다. NavMesh 위로 붙여 씁니다.</param>
    /// <remarks>"대원 이동 명령 취소"나 복귀(Q/E 길게, CapsLock)로 풀립니다. 명령 중에는 플레이어를 따라오지 않습니다.</remarks>
    [TutorialEvent("대원 이동 명령 (위치 지정)")]
    public void OrderSquadMemberMove(int memberNumber, Vector3 position)
    {
        SquadAIController ai = ResolveMemberAi(memberNumber, "대원 이동 명령");
        if (ai == null)
        {
            return;
        }

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, m_spawnSampleRadius, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        ai.IssueMoveOrder(position);
    }

    /// <summary>위치 사수 지점에 도착해 막아 둔 동료의 Q/E 명령 키를 모두 풉니다. 복귀(Q/E 길게) 안내 전에 부릅니다.</summary>
    /// <remarks>막는 쪽은 "동료 도착" 조건입니다. 튜토리얼이 끝날 때도 자동으로 풉니다.</remarks>
    [TutorialEvent("위치 사수 키 잠금 풀기")]
    public void ClearSquadOrderLocks()
    {
        if (SquadManager.Instance != null)
        {
            SquadManager.Instance.ClearOrderLocks();
        }
    }

    /// <summary>"대원 이동 명령"으로 내린 명령을 풀어 평소처럼 플레이어를 따라오게 합니다.</summary>
    /// <param name="memberNumber">명령을 풀 캐릭터 번호입니다(캐릭터 변경 키 1~3과 같은 번호).</param>
    [TutorialEvent("대원 이동 명령 취소")]
    public void CancelSquadMemberMove(int memberNumber)
    {
        ResolveMemberAi(memberNumber, "대원 이동 명령 취소")?.CancelMoveOrder();
    }

    /// <summary>캐릭터 번호의 AI 컨트롤러입니다. 없거나 조작 중인 캐릭터면 경고를 남기고 null입니다.</summary>
    private SquadAIController ResolveMemberAi(int memberNumber, string eventName)
    {
        SquadManager squad = SquadManager.Instance;
        int index = memberNumber - 1;
        SquadMemberController member = squad != null && index >= 0 && index < squad.SquadMembers.Count ? squad.SquadMembers[index] : null;
        SquadAIController ai = member != null ? member.GetComponent<SquadAIController>() : null;
        if (ai == null || member.IsPlayerSquadMember)
        {
            Debug.LogWarning($"{eventName}: {memberNumber}번 캐릭터의 AI를 찾지 못했거나 조작 중인 캐릭터입니다.", this);
            return null;
        }

        return ai;
    }

    /// <summary>기본 게임의 방어선 마커(<see cref="DefenseTargetMarker"/>)를 켜거나 끕니다. 정문 안내 전까지 꺼 두고, 안내할 때 켭니다.</summary>
    /// <param name="shown">켜면 마커가 보입니다.</param>
    /// <remarks>꺼 둔 마커는 튜토리얼이 끝날 때 다시 켭니다.</remarks>
    [TutorialEvent("방어선 마커 켜기/끄기")]
    public void SetDefenseMarkerShown(bool shown)
    {
        foreach (DefenseTargetMarker marker in FindObjectsByType<DefenseTargetMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            marker.SetShown(shown);
        }
    }

    /// <summary>방어 목표(정문 등)의 수리 상호작용을 켜거나 끕니다. 끄면 다가가도 수리 안내가 뜨지 않고 수리할 수 없습니다.</summary>
    /// <param name="targetId">수리를 켜고 끌 대상의 ID입니다(씬 오브젝트의 <see cref="TutorialTarget"/>, 예: Gate_Repair).</param>
    /// <param name="enabled">켜면 수리할 수 있습니다.</param>
    /// <remarks>끈 대상은 튜토리얼이 끝날 때 다시 켭니다.</remarks>
    [TutorialEvent("방어 목표 수리 켜기/끄기")]
    public void SetRepairEnabled(string targetId, bool enabled)
    {
        TutorialTarget target = TutorialTarget.Find(targetId);
        DefenseRepairInteractable repair = target != null ? target.GetComponentInChildren<DefenseRepairInteractable>(true) : null;
        if (repair == null)
        {
            Debug.LogWarning($"방어 목표 수리 켜기/끄기: 대상 '{targetId}'에서 {nameof(DefenseRepairInteractable)}를 찾지 못했습니다.", this);
            return;
        }

        repair.RepairEnabled = enabled;
        if (enabled)
        {
            m_blockedRepairs.Remove(repair);
        }
        else if (!m_blockedRepairs.Contains(repair))
        {
            m_blockedRepairs.Add(repair);
        }
    }

    /// <summary>
    /// 씬 오브젝트 위치에 이펙트를 띄워 숨길 때까지 반복 재생합니다. 동료를 세울 지점처럼 목표 위치를 보여 주는 용도입니다.
    /// </summary>
    /// <param name="prefab">띄울 이펙트 프리팹입니다. 안의 파티클은 반복 재생으로 바꿔 씁니다(프리팹 에셋은 바뀌지 않음).</param>
    /// <param name="objectPath">이펙트를 띄울 씬 오브젝트의 하이어라키 경로입니다(예: ----- Event -----/Defense Point1).</param>
    [TutorialEvent("지점 이펙트 띄우기 (숨길 때까지 반복)")]
    public void ShowPointEffect(GameObject prefab, [TutorialScenePath] string objectPath)
    {
        GameObject point = FindSceneObject(objectPath);
        if (prefab == null || point == null)
        {
            Debug.LogWarning($"지점 이펙트 띄우기: 이펙트 프리팹이 비어 있거나 씬 오브젝트 '{objectPath}'를 찾지 못했습니다.", this);
            return;
        }

        GameObject effect = Instantiate(prefab, point.transform.position, Quaternion.identity);
        foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particle.main;
            main.loop = true;
            main.stopAction = ParticleSystemStopAction.None;
        }

        m_pointEffects.Add(effect);
    }

    /// <summary>"지점 이펙트 띄우기"로 띄운 이펙트를 모두 없앱니다.</summary>
    [TutorialEvent("지점 이펙트 모두 숨기기")]
    public void HidePointEffects()
    {
        foreach (GameObject effect in m_pointEffects)
        {
            if (effect != null)
            {
                Destroy(effect);
            }
        }

        m_pointEffects.Clear();
    }

    /// <summary>분대원 모두의 평소 이동을 걷기 또는 조깅(기본)으로 바꿉니다. 속도와 애니메이션을 함께 바꿉니다.</summary>
    /// <param name="walk">켜면 걷기, 끄면 조깅입니다.</param>
    /// <remarks>조작 캐릭터를 바꿔도 같은 걸음이 되도록 분대원 전부에 겁니다. 튜토리얼이 끝나면 조깅으로 돌립니다.</remarks>
    [TutorialEvent("걷기 모드 (켜기: 걷기 / 끄기: 조깅)")]
    public void SetPlayerWalk(bool walk)
    {
        foreach (ThirdPersonController controller in FindObjectsByType<ThirdPersonController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            controller.UseWalk = walk;
        }
    }

    /// <summary>이번 챕터가 끝날 때까지 정한 조작을 엽니다. 챕터 잠금에 들어 있어도 엽니다.</summary>
    /// <param name="inputs">열 조작입니다.</param>
    /// <remarks>챕터 잠금은 챕터 단위라, 안내 창을 닫은 뒤처럼 챕터 중간에 조작 하나를 열 때 씁니다.</remarks>
    [TutorialEvent("이번 챕터 동안 조작 열기")]
    public void UnlockInputsForStep(PlayerInputLock inputs)
    {
        if (TutorialFlowDirector.Instance != null)
        {
            TutorialFlowDirector.Instance.UnlockInputsForStep(inputs);
        }
    }

    /// <summary>스킬 쿨타임을 잠시 바꿉니다. 이미 돌고 있는 쿨타임이 더 길면 새 값으로 줄입니다.</summary>
    /// <param name="skill">바꿀 스킬입니다.</param>
    /// <param name="seconds">바꿀 쿨타임(초)입니다.</param>
    /// <remarks>"스킬 쿨타임 복구"로 되돌립니다. 튜토리얼이 끝나면 자동으로 되돌립니다.</remarks>
    [TutorialEvent("스킬 쿨타임 변경")]
    public void SetSkillCooldown(CharacterSkillType skill, float seconds)
    {
        foreach (CharacterSkill target in FindObjectsByType<CharacterSkill>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (target.SkillType == skill)
            {
                target.SetCooldownOverride(seconds);
                m_cooldownOverrides.Add(target);
            }
        }
    }

    /// <summary>"스킬 쿨타임 변경"으로 바꾼 쿨타임을 원래 값으로 되돌립니다.</summary>
    /// <param name="skill">되돌릴 스킬입니다.</param>
    [TutorialEvent("스킬 쿨타임 복구")]
    public void RestoreSkillCooldown(CharacterSkillType skill)
    {
        foreach (CharacterSkill target in FindObjectsByType<CharacterSkill>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (target.SkillType == skill)
            {
                target.ClearCooldownOverride();
                m_cooldownOverrides.Remove(target);
            }
        }
    }

    /// <summary>청솔이 스킬로 특수탄을 장전하는 중이면 장전이 끝날 때까지 기다립니다. 장전 중이 아니면 바로 넘어갑니다.</summary>
    /// <remarks>스킬 사용 조건은 C를 누른 순간 완료되므로, 장전이 끝난 뒤에 안내를 띄우려면 이 이벤트로 한 번 기다립니다.</remarks>
    [TutorialEvent("청솔 특수탄 장전 끝날 때까지 대기")]
    public IEnumerator WaitForSpecialAmmoLoaded()
    {
        ChungSolDragonBreathSkill[] skills = FindObjectsByType<ChungSolDragonBreathSkill>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        while (System.Array.Exists(skills, skill => skill != null && skill.IsLoadingSpecialAmmo))
        {
            yield return null;
        }
    }

    /// <summary>씬의 청사진 상태인 함정 오브젝트를 모두 끕니다. 이미 설치된 함정은 그대로 둡니다.</summary>
    /// <remarks>끈 함정은 기억해 두었다가 튜토리얼이 끝날 때 다시 켭니다. 켜진 청사진은 평소처럼 설치 구간(준비 시간)에만 보입니다.</remarks>
    [TutorialEvent("모든 함정 청사진 끄기")]
    public void HideAllTrapBlueprints()
    {
        foreach (Trap trap in FindObjectsByType<Trap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (trap.IsBlueprint)
            {
                trap.gameObject.SetActive(false);
                m_hiddenBlueprints.Add(trap);
            }
        }
    }

    /// <summary>"모든 함정 청사진 끄기"로 끈 함정 중 아직 꺼져 있는 것을 다시 켭니다.</summary>
    private void RestoreHiddenBlueprints()
    {
        // 씬이 내려가는 중(OnDisable에서 불림)에는 켜지 않고 목록만 비웁니다.
        if (!gameObject.scene.isLoaded)
        {
            m_hiddenBlueprints.Clear();
            return;
        }

        foreach (Trap trap in m_hiddenBlueprints)
        {
            if (trap != null && !trap.gameObject.activeSelf)
            {
                trap.gameObject.SetActive(true);
            }
        }

        m_hiddenBlueprints.Clear();
    }

    /// <summary>"씬 오브젝트 켜기/끄기"로 바꾼 오브젝트를 처음 상태로 되돌립니다.</summary>
    private void RestoreSceneObjects()
    {
        // 씬이 내려가는 중(OnDisable에서 불림)에는 바꾸지 않고 목록만 비웁니다.
        if (gameObject.scene.isLoaded)
        {
            foreach (KeyValuePair<GameObject, bool> pair in m_sceneObjectStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }
        }

        m_sceneObjectStates.Clear();
    }

    /// <summary>꺼져 있는 함정 오브젝트를 모두 켭니다.</summary>
    [TutorialEvent("모든 함정 청사진 켜기")]
    public void ShowAllTrapBlueprints()
    {
        foreach (Trap trap in FindObjectsByType<Trap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            trap.gameObject.SetActive(true);
        }
    }

    /// <summary>고른 함정 오브젝트 하나를 켭니다.</summary>
    /// <param name="trapPath">켤 함정의 하이어라키 경로입니다. Inspector에서 씬의 함정 목록으로 고릅니다.</param>
    [TutorialEvent("특정 함정 청사진 켜기")]
    public void ShowTrapBlueprint([TutorialTrapPath] string trapPath)
    {
        Trap trap = FindTrap(trapPath);
        if (trap == null)
        {
            Debug.LogWarning($"특정 함정 청사진 켜기: 씬에서 함정 '{trapPath}'를 찾지 못했습니다.", this);
            return;
        }

        trap.gameObject.SetActive(true);
    }

    /// <summary>
    /// 스크립트를 꺼 두어 외형만 보이던 함정을 켜고, 남은 내구도를 정한 비율로 맞춥니다.
    /// </summary>
    /// <param name="trapPath">켤 함정의 하이어라키 경로입니다. Inspector에서 씬의 함정 목록으로 고릅니다.</param>
    /// <param name="durabilityPercent">남길 내구도(%)입니다. 0 이하면 내구도를 바꾸지 않습니다. 설치된 함정에만 적용합니다.</param>
    /// <remarks>
    /// 함정의 Start(업그레이드 적용)가 내구도를 옮길 수 있어서, 켠 다음 프레임에 내구도를 맞춥니다.
    /// 수리할 거리를 미리 깔아 두는 용도입니다(예: 처음부터 놓인 스파이크를 30%로 켜기).
    /// </remarks>
    [TutorialEvent("함정 켜기 (꺼 둔 함정, 내구도 % 지정)")]
    public IEnumerator EnableTrap([TutorialTrapPath] string trapPath, float durabilityPercent)
    {
        Trap trap = FindTrap(trapPath);
        if (trap == null)
        {
            Debug.LogWarning($"함정 켜기: 씬에서 함정 '{trapPath}'를 찾지 못했습니다.", this);
            yield break;
        }

        trap.gameObject.SetActive(true);
        trap.enabled = true;
        yield return null;

        if (trap != null && durabilityPercent > 0.0f)
        {
            trap.SetDurabilityRatio(durabilityPercent / 100.0f);
        }
    }

    /// <summary>
    /// 방어 목표(정문 등)의 현재 체력을 최대 체력의 정한 비율로 맞춥니다. 수리할 거리를 미리 깔아 두는 용도입니다.
    /// </summary>
    /// <param name="targetId">체력을 맞출 대상의 ID입니다(씬 오브젝트의 <see cref="TutorialTarget"/>, 예: Gate_Repair).</param>
    /// <param name="hpPercent">남길 체력(%)입니다. 1~100 사이로 맞춥니다.</param>
    /// <remarks>
    /// 피해로 깎는 것이 아니라서 정문 손상 경고는 뜨지 않습니다.
    /// 튜토리얼 첫 블록에서 부르면 대상의 Start(체력 가득 채우기)보다 먼저 돌 수 있어서, 다음 프레임에 맞춥니다.
    /// </remarks>
    [TutorialEvent("방어 목표 체력 % 맞추기")]
    public IEnumerator SetDefenseTargetHpPercent(string targetId, float hpPercent)
    {
        TutorialTarget target = TutorialTarget.Find(targetId);
        DefenseEventHealth health = target != null ? target.GetComponentInChildren<DefenseEventHealth>(true) : null;
        if (health == null)
        {
            Debug.LogWarning($"방어 목표 체력 % 맞추기: 대상 '{targetId}'에서 {nameof(DefenseEventHealth)}를 찾지 못했습니다.", this);
            yield break;
        }

        yield return null;

        float ratio = Mathf.Clamp(hpPercent, 1.0f, 100.0f) / 100.0f;
        health.SetCurrentHP(Mathf.Max(1, Mathf.RoundToInt(health.MaxHP * ratio)));
    }

    /// <summary>하이어라키 경로가 같은 함정을 찾습니다. 꺼진 오브젝트도 찾습니다. 없으면 null입니다.</summary>
    public static Trap FindTrap(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (Trap trap in FindObjectsByType<Trap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (GetHierarchyPath(trap.transform) == path)
            {
                return trap;
            }
        }

        return null;
    }

    /// <summary>"부모/자식" 형태의 하이어라키 경로입니다. 함정을 고르는 칸이 이 값으로 저장합니다.</summary>
    public static string GetHierarchyPath(Transform target)
    {
        string path = target.name;
        for (Transform parent = target.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }

        return path;
    }

    /// <summary>
    /// 인게임 시네마틱을 재생하고 끝날 때까지 기다립니다. 끝나면 카메라와 조작을 원래대로 돌립니다.
    /// </summary>
    /// <param name="cinematicId">재생할 시네마틱의 ID입니다. 씬의 <see cref="TutorialCinematicSettings"/> 목록에서 드롭다운으로 고릅니다.
    /// Timeline이 있으면 Timeline을 끝까지 재생하고, 없으면 카메라 목록을 위에서부터 차례로 비춥니다.</param>
    /// <remarks>
    /// 카메라는 시설 업그레이드 컷신처럼 우선순위를 올려 켜는 방식이라, 평소 카메라를 바꾸는 쪽(ThirdPersonController 등)과 부딪히지 않습니다.
    /// 카메라 사이 전환은 CinemachineBrain의 기본 블렌드를 따릅니다.
    /// </remarks>
    [TutorialEvent("시네마틱 재생 (끝날 때까지 대기)")]
    public IEnumerator PlayCinematic([TutorialCinematicId] string cinematicId)
    {
        TutorialCinematicSettings settings = FindFirstObjectByType<TutorialCinematicSettings>(FindObjectsInactive.Include);
        TutorialCinematic cinematic = settings != null ? settings.Find(cinematicId) : null;
        if (cinematic == null)
        {
            Debug.LogWarning(settings == null
                ? $"씬에 {nameof(TutorialCinematicSettings)}가 없어 시네마틱을 건너뜁니다."
                : $"시네마틱 '{cinematicId}'를 {nameof(TutorialCinematicSettings)} 목록에서 찾지 못해 건너뜁니다.", this);
            yield break;
        }

        StopCinematic();
        m_cinematicPlaying = true;
        m_cinematicSkipRequested = false;
        m_cinematicLockedInput = cinematic.LockInput;
        InGameCutscene.Begin(m_cinematicLockedInput);
        if (m_cinematicLockedInput)
        {
            SetSquadGameplayInput(false);
        }

        // 시작 페이드: 플레이어 시점에서 먼저 검게 덮고, 시네마틱 카메라로 바꾼 뒤 재생과 함께 밝힙니다.
        if (cinematic.FadeFromPlayer)
        {
            yield return FadeTo(1.0f, cinematic.FadeOutSeconds * (1.0f - GetFadeAlpha()));
        }

        PlayableDirector director = cinematic.Timeline;
        if (director != null && director.playableAsset != null)
        {
            m_cinematicDirector = director;
            if (!director.gameObject.activeSelf)
            {
                director.gameObject.SetActive(true);
            }

            director.time = 0.0;
            director.Play();
            if (cinematic.FadeFromPlayer)
            {
                CutToLiveCamera();
                m_startFadeRoutine = StartCoroutine(FadeInAfterBlack(cinematic));
            }

            double duration = director.duration;

            // 검은 화면으로 끝내면 마지막 카메라 클립의 Ease Out(플레이어 카메라로 옮겨 가는 구간)은 재생하지 않고, 그 직전까지 검게 덮습니다.
            double endTime = cinematic.FadeToPlayer ? Math.Max(0.0, duration - GetTailEaseOut(director)) : duration;
            double fadeStart = endTime - cinematic.FadeOutSeconds;
            while (m_cinematicDirector == director && director != null && director.state == PlayState.Playing && director.time < endTime)
            {
                if (cinematic.FadeToPlayer && cinematic.FadeOutSeconds > 0.0f && director.time >= fadeStart)
                {
                    SetFadeAlpha((float)((director.time - fadeStart) / cinematic.FadeOutSeconds));
                }

                yield return null;
            }
        }
        else
        {
            IReadOnlyList<CinemachineCamera> cameras = cinematic.Cameras;
            if (cameras.Count == 0)
            {
                Debug.LogWarning($"시네마틱 '{cinematic.Name}'에 Timeline도 카메라도 없어 건너뜁니다.", settings);
            }

            CinemachineCamera previous = null;
            for (int i = 0; i < cameras.Count && !m_cinematicSkipRequested; i++)
            {
                if (cameras[i] == null)
                {
                    continue;
                }

                // 다음 카메라를 먼저 켜고 앞 카메라를 돌려야, 그 사이에 평소 카메라가 끼지 않고 바로 이어서 블렌드됩니다.
                ActivateCinematicCamera(cameras[i]);
                if (previous != null)
                {
                    RestoreCinematicCamera(previous);
                }
                else if (cinematic.FadeFromPlayer)
                {
                    CutToLiveCamera();
                    m_startFadeRoutine = StartCoroutine(FadeInAfterBlack(cinematic));
                }

                previous = cameras[i];
                float elapsed = 0.0f;
                while (elapsed < cinematic.SecondsPerCamera && !m_cinematicSkipRequested)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        StopStartFade();

        if (cinematic.FadeToPlayer)
        {
            // 건너뛰어서 아직 덜 덮였으면 마저 덮은 뒤, 검은 화면 뒤에서 플레이어 카메라로 바로 바꿉니다.
            yield return FadeTo(1.0f, cinematic.FadeOutSeconds * (1.0f - GetFadeAlpha()));
            StopCinematicPlayback();
            SwitchAfterCinematic(cinematic);
            CutToLiveCamera();
            yield return Wait(cinematic.BlackSeconds);
            yield return FadeTo(0.0f, cinematic.FadeInSeconds);
            ReleaseCinematicState();
            yield break;
        }

        // 시작 페이드인이 끝나기 전에 건너뛰었으면 남은 검은 막을 걷습니다.
        if (GetFadeAlpha() > 0.0f)
        {
            yield return FadeTo(0.0f, cinematic.FadeInSeconds * GetFadeAlpha());
        }

        // 시네마틱 카메라가 아직 비추는 동안 캐릭터를 바꿔, 돌아오는 카메라가 바뀐 캐릭터로 바로 향하게 합니다.
        SwitchAfterCinematic(cinematic);

        // 화면이 플레이어 카메라로 다 돌아올 때까지 조작 잠금과 컷신 상태를 유지한 뒤 풉니다.
        StopCinematicPlayback();
        yield return WaitForCameraReturn();
        ReleaseCinematicState();
    }

    /// <summary>시네마틱에 끝난 뒤 조작할 캐릭터가 정해져 있으면 그 캐릭터로 바꿉니다.</summary>
    private void SwitchAfterCinematic(TutorialCinematic cinematic)
    {
        if (cinematic.SwitchToMemberNumber > 0)
        {
            SwitchToSquadMember(cinematic.SwitchToMemberNumber);
        }
    }

    /// <summary>
    /// 정한 시간 뒤에 대사 한 줄을 띄우고, 표시 시간이 지나면 내립니다. 같은 트리거의 시네마틱 재생과 함께 넣으면 시네마틱 도중에 대사가 나옵니다.
    /// </summary>
    /// <param name="speaker">말하는 인물입니다.</param>
    /// <param name="text">대사입니다.</param>
    /// <param name="delaySeconds">이벤트가 시작된 뒤 대사를 띄울 때까지의 시간(초, 실제 시간)입니다.</param>
    /// <param name="showSeconds">대사를 띄워 둘 시간(초)입니다. 0이면 글자 수로 계산합니다.</param>
    /// <remarks>대사 블록과 같은 대화창을 씁니다. 대사가 끝날 때까지 기다리므로, 트리거는 시네마틱과 대사가 모두 끝나야 넘어갑니다.</remarks>
    [TutorialEvent("대사 한 줄 띄우기 (지연, 시네마틱 중 대사용)")]
    public IEnumerator PlayDialogueLine(DialogueSpeaker speaker, string text, float delaySeconds, float showSeconds)
    {
        yield return Wait(delaySeconds);

        DialoguePlayer dialogue = FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
        if (dialogue == null)
        {
            Debug.LogWarning($"대사 한 줄 띄우기: 씬에 {nameof(DialoguePlayer)}가 없습니다.", this);
            yield break;
        }

        bool done = false;
        dialogue.PlayLine(new DialogueLine(speaker, text, DialogueAdvanceMode.Auto, showSeconds), false, () => done = true);
        while (!done)
        {
            yield return null;
        }
    }

    /// <summary>정한 시간(초, 실제 시간)만큼 기다립니다.</summary>
    [TutorialEvent("대기")]
    public IEnumerator Wait(float seconds)
    {
        float elapsed = 0.0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ───────────── 내부 ─────────────

    private void Update()
    {
        // 로딩 화면처럼 자기가 기억한 배율로 시간을 되돌리는 UI가 늦게 닫힐 수 있어, 멈춘 동안에는 매 프레임 다시 멈춥니다.
        if (m_isHoldingPause && Time.timeScale > 0.0f)
        {
            m_resumeTimeScale = Time.timeScale;
            Time.timeScale = 0.0f;
        }
    }

    private void LateUpdate()
    {
        UpdateTargetMarkers();
    }

    private void OnDisable()
    {
        ReleaseAll();
    }

    /// <summary>대상 마커를 대상 위로 옮기고, 설치·수리를 마친 함정의 마커는 내립니다.</summary>
    private void UpdateTargetMarkers()
    {
        for (int i = m_targetMarkers.Count - 1; i >= 0; i--)
        {
            TargetMarker marker = m_targetMarkers[i];
            if (marker.Root == null || marker.View == null)
            {
                DestroyTargetMarker(i);
                continue;
            }

            if (marker.Trap != null || marker.Member != null)
            {
                bool done = marker.Trap != null
                    ? marker.Trap.IsBuilt && marker.Trap.Health >= marker.Trap.MaxHealth
                    : !marker.Member.IsDowned && !marker.Member.IsDead;
                if (!done)
                {
                    marker.WasPending = true;
                }
                else if (marker.WasPending)
                {
                    DestroyTargetMarker(i);
                    continue;
                }
            }

            UpdateTargetMarkerAnchor(marker);
        }
    }

    /// <summary>마커 기준점을 대상의 켜진 렌더러를 모두 감싸는 범위의 맨 위 가운데로 옮깁니다(<see cref="WorldMarkerAnchor"/>).</summary>
    private void UpdateTargetMarkerAnchor(TargetMarker marker)
    {
        marker.Anchor.position = WorldMarkerAnchor.GetTopCenter(marker.Root, m_targetMarkerHeight);
    }

    private void DestroyTargetMarker(int index)
    {
        TargetMarker marker = m_targetMarkers[index];
        m_targetMarkers.RemoveAt(index);
        if (marker.View != null)
        {
            Destroy(marker.View.gameObject);
        }

        if (marker.Anchor != null)
        {
            Destroy(marker.Anchor.gameObject);
        }
    }

    /// <summary>Play 중 경로선을 띄우고 있으면 목표 위치와 도착 반경을 Scene 뷰에 그립니다.</summary>
    private void OnDrawGizmos()
    {
        if (m_pathArriveRadius <= 0.0f)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 1.0f, 0.4f, 1.0f);
        Gizmos.DrawWireSphere(m_pathTarget, m_pathArriveRadius);
        Gizmos.DrawLine(m_pathTarget, m_pathTarget + Vector3.up * 2.0f);
    }

    /// <summary>조작 중인 캐릭터가 지금 경로선의 도착 범위 안인지 여부입니다. 범위 오브젝트가 있으면 그 범위, 없으면 위치·반경으로 봅니다.</summary>
    /// <param name="margin">범위를 사방으로 이만큼(m) 넓혀 봅니다.</param>
    private bool IsPlayerAtPathTarget(float margin)
    {
        return m_pathArea != null
            ? IsPlayerInArea(m_pathArea, margin)
            : IsPlayerInArea(m_pathTarget, m_pathArriveRadius + margin);
    }

    private IEnumerator WatchArrival()
    {
        while (!IsPlayerAtPathTarget(0.0f))
        {
            yield return null;
        }

        // 기다리지 않는 안내는 감시 코루틴이 자기 자신이라, 숨기면서 멈추지 않도록 먼저 비웁니다.
        m_pathWatch = null;
        HidePathLine();
    }

    /// <summary>
    /// 도착하면 경로선만 감추고, 반경을 벗어나면 다시 띄웁니다. 경로선 숨기기가 이 코루틴을 멈출 때까지 반복합니다.
    /// </summary>
    /// <remarks>반경 경계에서 서성일 때 깜빡이지 않도록, 다시 띄우는 기준은 도착 반경보다 <see cref="PathReshowMargin"/>만큼 넓게 잡습니다.</remarks>
    private IEnumerator WatchArrivalAndLeave(ObjectivePathArrow arrow)
    {
        while (true)
        {
            while (!IsPlayerAtPathTarget(0.0f))
            {
                yield return null;
            }

            arrow.SetVisible(false);
            while (IsPlayerAtPathTarget(PathReshowMargin))
            {
                yield return null;
            }

            arrow.SetTarget(m_pathAnchor);
            arrow.SetVisible(true);
        }
    }

    private void ActivateCinematicCamera(CinemachineCamera camera)
    {
        m_cinematicCameras.Add(new CinematicCameraState
        {
            Camera = camera,
            WasActive = camera.gameObject.activeSelf,
            PriorityEnabled = camera.Priority.Enabled,
            Priority = camera.Priority.Value,
        });

        camera.Priority.Enabled = true;
        camera.Priority.Value = m_cinematicPriority;
        camera.gameObject.SetActive(true);
    }

    private void RestoreCinematicCamera(CinemachineCamera camera)
    {
        for (int i = m_cinematicCameras.Count - 1; i >= 0; i--)
        {
            CinematicCameraState state = m_cinematicCameras[i];
            if (state.Camera != camera)
            {
                continue;
            }

            m_cinematicCameras.RemoveAt(i);
            if (state.Camera != null)
            {
                state.Camera.Priority.Enabled = state.PriorityEnabled;
                state.Camera.Priority.Value = state.Priority;
                state.Camera.gameObject.SetActive(state.WasActive);
            }
        }
    }

    /// <summary>
    /// 재생 중인 시네마틱을 바로 끝냅니다. 재생 중이 아니면 아무것도 하지 않습니다.
    /// </summary>
    /// <remarks>
    /// 재생하던 <see cref="PlayCinematic"/>가 이어서 카메라 복귀를 기다린 뒤 조작 잠금을 풉니다. 디버그 넘기기에서 씁니다.
    /// </remarks>
    public void SkipCinematic()
    {
        if (!m_cinematicPlaying)
        {
            return;
        }

        m_cinematicSkipRequested = true;
        StopCinematicPlayback();
    }

    /// <summary>재생 중인 시네마틱을 멈추고 카메라와 조작을 원래대로 돌립니다. 재생 중이 아니면 아무것도 하지 않습니다.</summary>
    private void StopCinematic()
    {
        StopStartFade();
        StopCinematicPlayback();
        ReleaseCinematicState();
    }

    /// <summary>
    /// 시네마틱이 끝난 뒤 Main Camera가 플레이어 카메라로 넘어가는 전환이 끝날 때까지 기다립니다.
    /// 시간이 멈춰 전환이 진행되지 않는 경우를 위해 최대 <see cref="CameraReturnTimeout"/>초만 기다립니다.
    /// </summary>
    private static IEnumerator WaitForCameraReturn()
    {
        Camera mainCamera = Camera.main;
        CinemachineBrain brain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;
        if (brain == null)
        {
            yield break;
        }

        // 시네마틱 카메라를 내린 다음 프레임에 전환이 시작되므로 한 프레임 넘기고 봅니다.
        yield return null;
        float elapsed = 0.0f;
        while (brain != null && brain.IsBlending && elapsed < CameraReturnTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>Timeline 마지막 카메라 클립의 Ease Out 길이(초)입니다. 카메라 클립이 없으면 0입니다.</summary>
    private static double GetTailEaseOut(PlayableDirector director)
    {
        if (director.playableAsset is not UnityEngine.Timeline.TimelineAsset timeline)
        {
            return 0.0;
        }

        double easeOut = 0.0;
        double lastEnd = double.MinValue;
        foreach (UnityEngine.Timeline.TrackAsset track in timeline.GetOutputTracks())
        {
            if (track is not CinemachineTrack)
            {
                continue;
            }

            foreach (UnityEngine.Timeline.TimelineClip clip in track.GetClips())
            {
                if (clip.end > lastEnd)
                {
                    lastEnd = clip.end;
                    easeOut = clip.easeOutDuration;
                }
            }
        }

        return easeOut;
    }

    /// <summary>Main Camera가 블렌드 없이 지금 우선순위가 가장 높은 카메라(플레이어 카메라)를 비추게 합니다.</summary>
    /// <remarks>
    /// 블렌드 상태만 지우고, 카메라 갱신은 브레인이 자기 갱신 시점에 합니다.
    /// ManualUpdate는 브레인이 수동 갱신 모드일 때만 쓸 수 있어 부르지 않습니다. 화면이 검은 동안이라 한 프레임 늦어도 보이지 않습니다.
    /// </remarks>
    private static void CutToLiveCamera()
    {
        Camera mainCamera = Camera.main;
        CinemachineBrain brain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;
        if (brain != null)
        {
            brain.ResetState();
        }
    }

    private float GetFadeAlpha()
    {
        return m_fadeOverlay != null ? m_fadeOverlay.alpha : 0.0f;
    }

    /// <summary>화면 전체 검은 막의 알파를 정합니다. 0이면 막을 끕니다.</summary>
    private void SetFadeAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (m_fadeOverlay == null)
        {
            if (alpha <= 0.0f)
            {
                return;
            }

            var root = new GameObject("TutorialFadeOverlay", typeof(Canvas), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            m_fadeOverlay = root.GetComponent<CanvasGroup>();
            m_fadeOverlay.blocksRaycasts = false;
            m_fadeOverlay.interactable = false;

            var image = new GameObject("Black", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            image.transform.SetParent(root.transform, false);
            RectTransform rect = (RectTransform)image.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            UnityEngine.UI.Image graphic = image.GetComponent<UnityEngine.UI.Image>();
            graphic.color = Color.black;
            graphic.raycastTarget = false;
        }

        m_fadeOverlay.alpha = alpha;
        m_fadeOverlay.gameObject.SetActive(alpha > 0.0f);
    }

    /// <summary>시작 페이드: 검은 화면을 잠시 유지한 뒤 시네마틱 화면으로 밝힙니다.</summary>
    private IEnumerator FadeInAfterBlack(TutorialCinematic cinematic)
    {
        yield return Wait(cinematic.BlackSeconds);
        yield return FadeTo(0.0f, cinematic.FadeInSeconds);
        m_startFadeRoutine = null;
    }

    /// <summary>진행 중인 시작 페이드인을 멈춥니다. 검은 막 알파는 그대로 둡니다.</summary>
    private void StopStartFade()
    {
        if (m_startFadeRoutine != null)
        {
            StopCoroutine(m_startFadeRoutine);
            m_startFadeRoutine = null;
        }
    }

    /// <summary>검은 막을 정한 시간(초, 실제 시간) 동안 목표 알파까지 바꿉니다.</summary>
    private IEnumerator FadeTo(float target, float seconds)
    {
        float start = GetFadeAlpha();
        for (float t = 0.0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetFadeAlpha(Mathf.Lerp(start, target, t / seconds));
            yield return null;
        }

        SetFadeAlpha(target);
    }

    /// <summary>시네마틱 카메라와 Timeline을 멈춥니다. 조작 잠금과 컷신 상태는 그대로 둡니다.</summary>
    private void StopCinematicPlayback()
    {
        while (m_cinematicCameras.Count > 0)
        {
            RestoreCinematicCamera(m_cinematicCameras[m_cinematicCameras.Count - 1].Camera);
        }

        if (m_cinematicDirector != null)
        {
            m_cinematicDirector.Stop();
            m_cinematicDirector = null;
        }
    }

    /// <summary>컷신 상태를 끝내고 시네마틱이 막아 둔 분대 조작을 풉니다.</summary>
    private void ReleaseCinematicState()
    {
        if (m_cinematicPlaying)
        {
            m_cinematicPlaying = false;
            InGameCutscene.End(m_cinematicLockedInput);
        }

        if (m_cinematicLockedInput)
        {
            m_cinematicLockedInput = false;
            // 시간 멈춤이 조작을 막고 있으면 그대로 둡니다. 시간 재개가 풀어 줍니다.
            if (!m_isHoldingPause)
            {
                SetSquadGameplayInput(true);
            }
        }
    }

    private void StopPathWatch()
    {
        if (m_pathWatch != null)
        {
            StopCoroutine(m_pathWatch);
            m_pathWatch = null;
        }
    }

    private ObjectivePathArrow ResolvePathArrow()
    {
        if (m_pathArrow == null)
        {
            m_pathArrow = FindFirstObjectByType<ObjectivePathArrow>(FindObjectsInactive.Include);
        }

        return m_pathArrow;
    }

    private DefenseTutorialOverlay ResolveWindow()
    {
        if (m_window == null)
        {
            m_window = FindFirstObjectByType<DefenseTutorialOverlay>(FindObjectsInactive.Include);
        }

        return m_window;
    }

    private static void SetSquadGameplayInput(bool enabled)
    {
        SquadManager squadManager = SquadManager.Instance != null
            ? SquadManager.Instance
            : FindFirstObjectByType<SquadManager>(FindObjectsInactive.Include);
        if (squadManager != null)
        {
            squadManager.ApplyInputModeToSquad(enabled);
        }
    }
}
