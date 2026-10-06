using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 피해가 실제로 들어간 위치에 피해 숫자를 띄우는 디버그 표시입니다.
/// </summary>
/// <remarks>
/// <see cref="HealthSystemBase"/>를 거치는 모든 체력 피해(적, 플레이어, 방어 목표물)와 함정 내구도 감소를 표시합니다.
/// 총탄은 박힌 지점에, 그 외(근접, 폭발, 화염, 함정)는 맞은 대상의 머리 위에 띄웁니다.
/// 조준 중에는 조준선 아래에 조준점까지의 거리와 그 거리에서 들어갈 피해도 씁니다.
///
/// Play 시작 때 스스로 만들어지고 씬 전환에도 남습니다. 씬에 미리 배치할 필요가 없어서, 씬 파일을 고치지 않고
/// 어느 씬에서나 쓸 수 있습니다. 토글은 이 오브젝트의 인스펙터와 F9 트레이너(시스템 탭)에서 바꿉니다.
///
/// 개발 모드가 켜진 Editor/Development Build에서만 동작합니다. 빌드에서는 기록 호출 자체가 제거됩니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class DamageNumberDebug : MonoBehaviour
{
    /// <summary>피해를 받은 대상의 종류입니다. 숫자 색을 정합니다.</summary>
    public enum TargetKind
    {
        Enemy,
        Player,
        Objective,
        Trap
    }

    private struct Entry
    {
        public Vector3 Position;
        public float Damage;
        public TargetKind Kind;
        public bool Headshot;
        public float StartTime;
    }

    private const int MaxEntries = 128;

    [Header("Debug")]
    [Tooltip("피해 숫자 표시 전체를 켜고 끕니다.")]
    [SerializeField] private bool m_showDamageNumbers = true;

    [Tooltip("Game 뷰에 피해 숫자를 표시합니다.")]
    [SerializeField] private bool m_showInGameView = true;

    [Tooltip("Scene 뷰에 피해 숫자를 표시합니다(에디터 전용).")]
    [SerializeField] private bool m_showInSceneView = true;

    [Tooltip("숫자가 떠 있는 시간(초)입니다.")]
    [Min(0.1f)]
    [SerializeField] private float m_damageNumberLifetime = 1.5f;

    [Tooltip("숫자가 위로 떠오르는 속도(m/초)입니다.")]
    [SerializeField] private float m_damageNumberRiseSpeed = 0.6f;

    [Tooltip("숫자 글자 크기입니다. 1080p 기준이며 화면 높이에 비례해 커집니다.")]
    [Min(8)]
    [SerializeField] private int m_damageNumberFontSize = 28;

    [Tooltip("조준 중 조준선 아래에 조준점까지의 거리와 그 거리에서 들어갈 피해를 표시합니다.")]
    [SerializeField] private bool m_showAimDamagePreview = true;

    private static DamageNumberDebug s_instance;

    // 다음 체력 피해 한 번에만 쓰는 위치입니다. 총이 피해를 넣기 직전에 박힌 지점을 넘겨 둡니다.
    private static bool s_hasPendingPoint;
    private static Vector3 s_pendingPoint;
    private static int s_pendingFrame;
    private static bool s_pendingHeadshot;

    private const float AimControllerRefreshInterval = 1.0f;

    private readonly List<Entry> m_entries = new List<Entry>();
    private GUIStyle m_style;
    private AimController[] m_aimControllers;
    private float m_nextAimControllerRefreshTime;

    private bool IsActive => m_showDamageNumbers && GameDevMode.DebugFeaturesEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_instance = null;
        s_hasPendingPoint = false;
        s_pendingHeadshot = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            return;
        }

        if (s_instance != null)
        {
            return;
        }

        GameObject go = new GameObject("[DamageNumberDebug]");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<DamageNumberDebug>();
    }

    /// <summary>
    /// 다음 체력 피해를 표시할 위치를 지정합니다. 총탄처럼 박힌 지점을 아는 경로가 피해 직전에 부릅니다.
    /// </summary>
    /// <remarks>같은 프레임 안의 다음 피해 한 번에만 쓰입니다. 피해가 들어가지 않아도 다음 프레임에는 버려집니다.</remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void SetPendingHitPoint(Vector3 point)
    {
        s_hasPendingPoint = true;
        s_pendingPoint = point;
        s_pendingFrame = Time.frameCount;
    }

    /// <summary>다음 체력 피해가 헤드샷인지 지정합니다. 헤드샷 숫자는 크고 노랗게 표시합니다.</summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void SetPendingHeadshot(bool headshot)
    {
        s_pendingHeadshot = headshot;
    }

    /// <summary>지정해 둔 위치와 헤드샷 여부를 버립니다. 피해 한 번을 처리한 뒤 부릅니다.</summary>
    /// <remarks>피해가 들어가지 않은 명중의 위치가 같은 프레임의 다른 피해(화염 틱 등)에 붙지 않게 합니다.</remarks>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void ClearPending()
    {
        s_hasPendingPoint = false;
        s_pendingHeadshot = false;
    }

    /// <summary>
    /// 피해 숫자를 기록합니다. 지정된 박힌 지점이 있으면 그 위치를, 없으면 대상의 머리 위를 씁니다.
    /// </summary>
    /// <param name="target">피해를 받은 대상입니다.</param>
    /// <param name="damage">실제로 들어간 피해량입니다.</param>
    /// <param name="kind">대상 종류입니다.</param>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Report(Component target, float damage, TargetKind kind)
    {
        bool usePending = s_hasPendingPoint && s_pendingFrame == Time.frameCount;
        bool headshot = s_pendingHeadshot;
        s_hasPendingPoint = false;
        s_pendingHeadshot = false;

        if (s_instance == null || !s_instance.IsActive || target == null || damage <= 0.0f)
        {
            return;
        }

        Vector3 position = usePending ? s_pendingPoint : ResolveAbovePoint(target);
        s_instance.Add(position, damage, kind, headshot);
    }

    private static Vector3 ResolveAbovePoint(Component target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null && collider.enabled)
        {
            Bounds bounds = collider.bounds;
            return new Vector3(bounds.center.x, bounds.max.y + 0.2f, bounds.center.z);
        }

        return target.transform.position + Vector3.up * 2.0f;
    }

    private void Add(Vector3 position, float damage, TargetKind kind, bool headshot)
    {
        if (m_entries.Count >= MaxEntries)
        {
            m_entries.RemoveAt(0);
        }

        m_entries.Add(new Entry
        {
            Position = position,
            Damage = damage,
            Kind = kind,
            Headshot = headshot,
            StartTime = Time.time
        });
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui += DrawSceneView;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        SceneView.duringSceneGui -= DrawSceneView;
#endif
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void Update()
    {
        float now = Time.time;
        m_entries.RemoveAll(e => now - e.StartTime > m_damageNumberLifetime);
    }

    private void OnGUI()
    {
        if (!IsActive || !m_showInGameView || Event.current.type != EventType.Repaint)
        {
            return;
        }

        EnsureStyle();
        DrawAimDamagePreview();

        Camera camera = Camera.main;
        if (camera == null || m_entries.Count == 0)
        {
            return;
        }

        float now = Time.time;
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            Vector3 screen = camera.WorldToScreenPoint(ResolveDrawPosition(entry, now));
            if (screen.z <= 0.0f)
            {
                continue;
            }

            float t = Mathf.Clamp01((now - entry.StartTime) / m_damageNumberLifetime);
            m_style.fontSize = ResolveGameFontSize(entry.Headshot ? 1.3f : 1.0f);
            string text = FormatDamage(entry.Damage);
            Vector2 size = m_style.CalcSize(new GUIContent(text));
            Rect rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y * 0.5f, size.x, size.y);

            Color color = ResolveColor(entry);
            color.a = 1.0f - t * t;

            DrawShadowedLabel(rect, text, color);
        }
    }

    /// <summary>
    /// 조작 중인 대원이 조준 중이면 조준선 아래에 조준점까지의 거리와 그 거리의 피해를 씁니다.
    /// </summary>
    /// <remarks>
    /// 거리는 총구에서 조준점까지입니다. 실제 사격도 총구에서 레이를 쏘고 그 명중 거리로 감쇠를 계산하므로
    /// 같은 기준입니다. 산탄은 산탄 1개 피해와 개수를, 헤드샷 배율이 1보다 크면 헤드샷 피해를 함께 씁니다.
    /// </remarks>
    private void DrawAimDamagePreview()
    {
        if (!m_showAimDamagePreview)
        {
            return;
        }

        AimController aim = ResolveControlledAimController();
        Gun gun = aim != null ? aim.EquippedGun : null;
        if (gun == null || !aim.IsInCombatStance)
        {
            return;
        }

        Vector3 origin = gun.FirePos != null ? gun.FirePos.position : gun.transform.position;
        float distance = Vector3.Distance(origin, aim.CurrentAimPoint);
        float damage = gun.DamageFalloff != null
            ? gun.DamageFalloff.ResolveDamage(distance, gun.HitscanDamage)
            : gun.HitscanDamage;

        string text = $"{distance:0.0}m  피해 {FormatDamage(damage)}";
        if (gun.PelletCount > 1)
        {
            text += $" x{gun.PelletCount}";
        }

        if (gun.HeadshotDamageMultiplier > 1.0f)
        {
            text += $"  (헤드 {FormatDamage(damage * gun.HeadshotDamageMultiplier)})";
        }

        if (distance > gun.HitscanRange)
        {
            text += "  사거리 밖";
        }

        m_style.fontSize = ResolveGameFontSize(0.9f);
        Vector2 size = m_style.CalcSize(new GUIContent(text));
        Rect rect = new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.5f + Screen.height * 0.06f, size.x, size.y);
        DrawShadowedLabel(rect, text, new Color(0.7f, 1.0f, 0.7f));
    }

    private AimController ResolveControlledAimController()
    {
        if (m_aimControllers == null || Time.unscaledTime >= m_nextAimControllerRefreshTime)
        {
            m_aimControllers = FindObjectsByType<AimController>(FindObjectsSortMode.None);
            m_nextAimControllerRefreshTime = Time.unscaledTime + AimControllerRefreshInterval;
        }

        for (int i = 0; i < m_aimControllers.Length; i++)
        {
            AimController aim = m_aimControllers[i];
            if (aim != null && aim.isActiveAndEnabled && aim.IsPlayerControlled)
            {
                return aim;
            }
        }

        return null;
    }

    private void DrawShadowedLabel(Rect rect, string text, Color color)
    {
        // 배경과 섞여도 읽히도록 검은 그림자를 먼저 그립니다.
        m_style.normal.textColor = new Color(0.0f, 0.0f, 0.0f, color.a);
        GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, m_style);
        m_style.normal.textColor = color;
        GUI.Label(rect, text, m_style);
    }

    private int ResolveGameFontSize(float scale)
    {
        return Mathf.RoundToInt(m_damageNumberFontSize * scale * Mathf.Max(0.5f, Screen.height / 1080.0f));
    }

#if UNITY_EDITOR
    private void DrawSceneView(SceneView sceneView)
    {
        if (!IsActive || !m_showInSceneView || m_entries.Count == 0)
        {
            return;
        }

        EnsureStyle();
        float now = Time.time;
        for (int i = 0; i < m_entries.Count; i++)
        {
            Entry entry = m_entries[i];
            m_style.fontSize = entry.Headshot ? Mathf.RoundToInt(m_damageNumberFontSize * 1.3f) : m_damageNumberFontSize;
            m_style.normal.textColor = ResolveColor(entry);
            Handles.Label(ResolveDrawPosition(entry, now), FormatDamage(entry.Damage), m_style);
        }

        // Play 중 Scene 뷰가 매 프레임 다시 그려지지 않으면 숫자가 멈춰 보여서 갱신을 요청합니다.
        sceneView.Repaint();
    }
#endif

    private Vector3 ResolveDrawPosition(Entry entry, float now)
    {
        return entry.Position + Vector3.up * ((now - entry.StartTime) * m_damageNumberRiseSpeed);
    }

    private void EnsureStyle()
    {
        if (m_style != null)
        {
            return;
        }

        m_style = new GUIStyle(GUI.skin != null ? GUI.skin.label : GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
    }

    private static string FormatDamage(float damage)
    {
        // 0.5, 0.2 같은 소수 피해가 있어서 정수면 정수로, 아니면 소수 둘째 자리까지 씁니다.
        return Mathf.Approximately(damage, Mathf.Round(damage))
            ? Mathf.RoundToInt(damage).ToString()
            : damage.ToString("0.##");
    }

    private static Color ResolveColor(Entry entry)
    {
        if (entry.Headshot)
        {
            return new Color(1.0f, 0.85f, 0.1f);
        }

        switch (entry.Kind)
        {
            case TargetKind.Player:
                return new Color(1.0f, 0.3f, 0.3f);
            case TargetKind.Objective:
                return new Color(1.0f, 0.55f, 0.1f);
            case TargetKind.Trap:
                return new Color(0.6f, 0.8f, 1.0f);
            default:
                return Color.white;
        }
    }
}
