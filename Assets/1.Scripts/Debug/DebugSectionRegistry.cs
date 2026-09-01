using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 컴포넌트의 Debug 구역에 속한 직렬화 필드 하나입니다.
/// </summary>
public sealed class DebugFieldEntry
{
    /// <summary>값을 소유한 컴포넌트입니다.</summary>
    public Component Owner { get; internal set; }

    /// <summary>대상 필드입니다.</summary>
    public FieldInfo Field { get; internal set; }

    /// <summary>이 필드가 속한 구역 이름입니다. `Noise Debug`처럼 접두사가 붙기도 합니다.</summary>
    public string SectionName { get; internal set; }

    /// <summary>이 항목이 속한 탭 이름입니다.</summary>
    public string TabName { get; internal set; }

    /// <summary>화면에 보여 줄 한글 이름입니다. 표에 없는 필드는 영문 이름을 그대로 씁니다.</summary>
    public string DisplayName => DebugFieldNames.Resolve(Field.Name);
}

/// <summary>
/// 디버그 필드의 한글 표시 이름을 모아 둔 표입니다.
/// </summary>
/// <remarks>
/// 트레이너는 팀이 함께 보는 화면이라 항목 이름이 한글이어야 합니다. 필드 이름을 그대로 풀어 쓰면
/// `Debug Draw Howl Radius` 같은 영문이 나와 읽는 데 시간이 걸립니다.
///
/// 필드 이름만으로 키를 잡습니다. 같은 이름이면 다른 컴포넌트라도 뜻이 같기 때문입니다
/// (<c>m_debugLogHealth</c>가 적과 플레이어 양쪽에 있는 식). 표에 없으면 영문 이름으로 떨어지므로
/// 새 디버그 필드를 추가할 때 이 표를 손대지 않아도 화면은 깨지지 않습니다.
/// </remarks>
internal static class DebugFieldNames
{
    private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        // 공용
        { "m_debugDraw", "디버그 표시" },
        { "m_debugLog", "로그 출력" },
        { "m_debugLogHealth", "체력 로그" },
        { "m_debugSphereRadius", "디버그 구체 반지름" },

        // 적
        { "m_debugDrawAttackRange", "공격 사거리 표시" },
        { "m_debugDrawWanderRadius", "배회 반경 표시" },
        { "m_debugDrawHowlRadius", "하울링 반경 표시" },
        { "m_debugDrawStaggerGauge", "경직 게이지 표시" },
        { "m_debugLogStagger", "경직 로그" },
        { "m_debugDrawNoiseGauge", "소음 게이지 표시" },
        { "m_debugDrawSight", "시야 표시" },

        // 조준·사격
        { "m_drawAimPointSphere", "조준점 구체 표시" },
        { "m_drawLookPointSphere", "지향점 구체 표시" },
        { "m_drawImpactPointSphere", "탄착점 구체 표시" },
        { "m_drawAimTraceLine", "조준 트레이스 선 표시" },
        { "m_drawHitscanDebugRay", "히트스캔 레이 표시" },
        { "m_drawCameraForwardRay", "카메라 전방 레이 표시" },
        { "m_spawnImpactMarkerOnShot", "발사 시 탄착 마커 생성" },
        { "m_impactMarkerPrefab", "탄착 마커 프리팹" },
        { "m_impactMarkerLifetime", "탄착 마커 유지 시간" },
        { "m_impactMarkerSize", "탄착 마커 크기" },

        // 크로스헤어
        { "m_debugStance", "조준 자세 미리보기" },
        { "m_previewHeadshotColor", "헤드샷 색 미리보기" },
        { "m_previewHitFeedback", "적중 피드백 미리보기" },
        { "m_editModePreview", "에디트 모드 미리보기" },

        // 무기
        { "m_debugInfiniteMagazine", "무한 장탄수" },
        { "m_debugInfiniteReserveAmmo", "무한 예비탄" },
        { "m_debugLogSpread", "탄퍼짐 로그" },
        { "m_debugDrawDamageFalloff", "거리 감쇠 표시" },
        { "m_debugFalloffRingRadius", "감쇠 링 반지름" },
        { "m_debugDrawShotNoiseRange", "발사 소음 반경 표시" },
        { "m_debugDrawAudioDistance", "오디오 거리 표시" },

        // 플레이어
        { "m_debugInfiniteHealth", "무한 체력" },
        { "m_debugLogInjuryStateChange", "부상 상태 변화 로그" },
        { "m_debugDrawGroundCheck", "접지 판정 표시" },
        { "DebugDrawGroundCheck", "접지 판정 표시" },
        { "m_debugConeSegments", "상호작용 부채꼴 분할 수" },

        // 팀 AI
        { "m_debugDrawDetectionRadius", "감지 반경 표시" },
        { "m_debugDrawJoinDistances", "합류 거리 표시" },
        { "m_logSwitchDebug", "멤버 전환 로그" },

        // 시스템
        { "m_debugDrawNoiseRanges", "소음 반경 표시" },
        { "m_debugTrainerPrefab", "트레이너 프리팹" },
        { "developMode", "개발 모드" },
        { "m_logResult", "바인딩 결과 로그" },
    };

    /// <summary>필드 이름에 대응하는 한글 이름을 돌려줍니다. 없으면 영문 이름을 풀어 씁니다.</summary>
    internal static string Resolve(string fieldName)
    {
        if (!string.IsNullOrEmpty(fieldName) && Names.TryGetValue(fieldName, out string korean))
        {
            return korean;
        }

        return ObjectNames.NicifyFieldName(fieldName);
    }
}

/// <summary>
/// 필드 이름을 사람이 읽기 좋은 형태로 바꾸는 헬퍼입니다.
/// </summary>
/// <remarks>
/// <c>UnityEditor.ObjectNames.NicifyVariableName</c>과 같은 일을 하지만 런타임에서도 써야 해서 직접 구현합니다.
/// 트레이너는 빌드에서도 도는데 <c>UnityEditor</c>는 빌드에 포함되지 않습니다.
/// </remarks>
internal static class ObjectNames
{
    /// <summary>`m_debugDrawHowlRadius` 같은 이름을 `Debug Draw Howl Radius`로 바꿉니다.</summary>
    internal static string NicifyFieldName(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return string.Empty;
        }

        string trimmed = fieldName;

        if (trimmed.StartsWith("m_", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(2);
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder(trimmed.Length + 8);

        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];

            if (i == 0)
            {
                builder.Append(char.ToUpperInvariant(c));
                continue;
            }

            // 대문자 앞에서 띄웁니다. 연속 대문자(약어)는 붙여 둡니다.
            if (char.IsUpper(c) && !char.IsUpper(trimmed[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}

/// <summary>
/// 씬에 있는 컴포넌트들의 Debug 구역 필드를 모으는 공용 수집기입니다.
/// </summary>
/// <remarks>
/// 에디터 창(<c>DebugToggleWindow</c>)과 런타임 트레이너(<see cref="RuntimeDebugTrainer"/>)가 <b>같은 목록</b>을
/// 보게 하려고 수집을 한곳에 둡니다. 두 곳이 각자 훑으면 규칙이 갈라져 한쪽에만 뜨는 항목이 생깁니다.
/// 그래서 이 파일은 런타임 어셈블리에 있고 <c>UnityEditor</c>를 참조하지 않습니다.
///
/// <b>수집 기준은 "Debug 구역 소속"입니다.</b> 필드 이름이 아닙니다.
/// <see cref="HeaderAttribute"/>나 VInspector Foldout으로 열린 구역의 이름에 `Debug`가 들어가면,
/// 그 구역이 끝날 때까지의 직렬화 필드를 디버그 항목으로 봅니다. 인스펙터에서 눈에 보이는 구획과 같은 규칙이라
/// 목록이 인스펙터에서 본 것과 어긋나지 않습니다.
///
/// 이름 규칙(<c>m_debug...</c>)을 쓰지 않는 이유는, 이름은 규칙을 어겨도 컴파일이 되어 조용히 누락되고
/// 반대로 디버그가 아닌 필드가 이름만으로 끌려 들어오기 때문입니다.
/// </remarks>
public static class DebugSectionRegistry
{
    /// <summary>구역 이름이 디버그 구역인지 판정할 때 찾는 문구입니다.</summary>
    /// <remarks>
    /// 부분 일치인 이유는 `Noise Debug`처럼 앞에 대상 이름을 붙인 구역이 실제로 있기 때문입니다.
    /// 완전 일치로 두면 그런 구역이 통째로 빠집니다.
    /// </remarks>
    private const string DebugSectionKeyword = "debug";

    /// <summary>매핑에 없는 타입이 모이는 탭 이름입니다.</summary>
    public const string FallbackTabName = "기타";

    /// <summary>
    /// 탭 순서입니다. 표시 순서를 여기서 정합니다.
    /// </summary>
    public static readonly string[] TabNames =
    {
        "플레이어",
        "팀 AI",
        "무기",
        "적",
        "시스템",
        FallbackTabName,
    };

    /// <summary>
    /// 컴포넌트 타입 이름을 탭으로 잇는 표입니다.
    /// </summary>
    /// <remarks>
    /// 폴더 경로가 아니라 타입 이름으로 나눈 이유는, 같은 폴더에 플레이어와 동행 AI가 섞여 있고
    /// 반대로 같은 역할의 스크립트가 여러 폴더에 흩어져 있기 때문입니다.
    /// 여기 없는 타입은 <see cref="FallbackTabName"/>으로 모입니다. 새 스크립트를 표에 넣는 것을 잊어도
    /// 목록에서 사라지지 않게 하려는 것입니다.
    /// </remarks>
    private static readonly Dictionary<string, string> TabByTypeName = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "ThirdPersonController", "플레이어" },
        { "AimController", "플레이어" },
        { "PlayerHealth", "플레이어" },
        { "CrosshairController", "플레이어" },
        { "InteractionController", "플레이어" },
        { "PlayerbleUnitData", "플레이어" },

        { "SquadAIController", "팀 AI" },
        { "SquadMemberController", "팀 AI" },
        { "SquadManager", "팀 AI" },
        { "DownedAllyInteractable", "팀 AI" },

        { "Gun", "무기" },
        { "Melee", "무기" },
        { "WeaponFeedbackEmitter", "무기" },

        { "EnemyController", "적" },
        { "EnemyTargetSensor", "적" },
        { "EnemyAttack", "적" },
        { "EnemyHealth", "적" },
        { "EnemyFeedbackEmitter", "적" },
        { "EnemyManager", "적" },

        { "CharacterNoiseEmitter", "시스템" },
        { "NoiseSystem", "시스템" },
        { "HowlSystem", "시스템" },
        { "AudioManager", "시스템" },
        { "GameManager", "시스템" },
        { "FieldSceneDataManager", "시스템" },
        { "EffectManager", "시스템" },
    };

    /// <summary>
    /// 현재 씬의 컴포넌트에서 Debug 구역 필드를 모읍니다.
    /// </summary>
    /// <param name="components">훑을 컴포넌트 목록입니다. 호출부가 활성/비활성 범위를 정합니다.</param>
    /// <returns>오브젝트 이름과 타입 이름 순으로 정렬된 항목 목록입니다.</returns>
    public static List<DebugFieldEntry> Collect(IEnumerable<Component> components)
    {
        List<DebugFieldEntry> results = new List<DebugFieldEntry>();

        if (components == null)
        {
            return results;
        }

        foreach (Component component in components)
        {
            CollectFrom(component, results);
        }

        results.Sort(CompareEntries);
        return results;
    }

    /// <summary>표시 순서를 정합니다. 오브젝트 이름 -> 타입 이름 -> 선언 순서입니다.</summary>
    private static int CompareEntries(DebugFieldEntry a, DebugFieldEntry b)
    {
        int byObject = string.Compare(
            a.Owner.gameObject.name, b.Owner.gameObject.name, StringComparison.OrdinalIgnoreCase);

        if (byObject != 0)
        {
            return byObject;
        }

        return string.Compare(
            a.Owner.GetType().Name, b.Owner.GetType().Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 한 컴포넌트에서 Debug 구역에 속한 직렬화 필드를 모읍니다.
    /// </summary>
    /// <remarks>
    /// <see cref="HeaderAttribute"/>는 자기가 붙은 필드 하나만 꾸미는 것이 아니라 그 아래 구역을 엽니다.
    /// 그래서 선언 순서대로 훑으면서 "지금 어느 구역인지"를 들고 갑니다.
    ///
    /// Foldout과 Header는 서로 다른 도구의 구획이라 <b>따로</b> 추적합니다. 하나로 합치면 Foldout을 쓰는
    /// 파일에서 그 앞의 Header가 구역 이름으로 잘못 남습니다(실제로 `Gun`에서 그 증상이 나왔습니다).
    ///
    /// VInspector 타입은 직접 참조하지 않고 이름으로 확인합니다. 이 수집기 때문에 패키지가 필수 의존이 되면
    /// 패키지를 빼는 순간 프로젝트 전체가 컴파일되지 않습니다.
    /// </remarks>
    private static void CollectFrom(Component component, List<DebugFieldEntry> results)
    {
        if (component == null)
        {
            return;
        }

        Type type = component.GetType();

        // 유니티 기본 컴포넌트에는 우리가 정의한 Debug 구역이 없습니다. 훑을 이유가 없습니다.
        if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
        {
            return;
        }

        FieldInfo[] fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        string currentFoldout = null;
        string currentHeader = null;

        foreach (FieldInfo field in fields)
        {
            if (HasAttributeNamed(field, "EndFoldoutAttribute"))
            {
                currentFoldout = null;
            }

            string foldout = ReadFoldoutSectionName(field);
            if (foldout != null)
            {
                currentFoldout = foldout;
            }

            HeaderAttribute header = field.GetCustomAttribute<HeaderAttribute>();
            if (header != null)
            {
                currentHeader = header.header;
            }

            // Foldout이 열려 있으면 그것이 눈에 보이는 구획입니다. Foldout 안의 Header는 그 안의 소제목일 뿐이라
            // 구획 판정의 주체가 될 수 없습니다. Foldout이 없을 때만 Header로 판단합니다.
            string section = currentFoldout ?? currentHeader;

            if (section == null || !IsDebugSection(section) || !IsSerializedField(field))
            {
                continue;
            }

            results.Add(new DebugFieldEntry
            {
                Owner = component,
                Field = field,
                SectionName = section,
                TabName = ResolveTabName(type.Name),
            });
        }
    }

    /// <summary>이 타입이 속한 탭 이름을 정합니다.</summary>
    public static string ResolveTabName(string typeName)
    {
        return TabByTypeName.TryGetValue(typeName, out string tab) ? tab : FallbackTabName;
    }

    /// <summary>이 필드에 지정한 이름의 특성이 붙어 있는지 확인합니다.</summary>
    private static bool HasAttributeNamed(FieldInfo field, string attributeTypeName)
    {
        foreach (Attribute attribute in field.GetCustomAttributes())
        {
            if (attribute.GetType().Name == attributeTypeName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>이 필드가 Foldout 구역을 여는지 확인하고, 연다면 그 이름을 돌려줍니다.</summary>
    private static string ReadFoldoutSectionName(FieldInfo field)
    {
        foreach (Attribute attribute in field.GetCustomAttributes())
        {
            if (attribute.GetType().Name != "FoldoutAttribute")
            {
                continue;
            }

            string name = ReadFoldoutName(attribute);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Foldout 특성에서 표시 이름을 읽습니다.</summary>
    /// <remarks>
    /// 현재 VInspector는 public <c>string name</c> <b>필드</b>에 담으므로 필드를 먼저 봅니다.
    /// 프로퍼티도 함께 보는 것은 구현이 바뀌어도 조용히 빈 목록이 되지 않게 하기 위한 대비입니다.
    /// 값이 비어 있으면 다음 후보로 넘어갑니다 - 첫 문자열 멤버에서 곧장 반환하면 이름이 다른 멤버에 있을 때 놓칩니다.
    /// </remarks>
    private static string ReadFoldoutName(Attribute attribute)
    {
        Type type = attribute.GetType();

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.FieldType != typeof(string))
            {
                continue;
            }

            if (field.GetValue(attribute) is string value && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType != typeof(string) || !property.CanRead)
            {
                continue;
            }

            if (property.GetValue(attribute) is string value && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>구역 이름이 디버그 구역인지 판정합니다.</summary>
    private static bool IsDebugSection(string sectionName)
    {
        return sectionName.IndexOf(DebugSectionKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>이 필드가 Unity가 직렬화하는 필드인지 확인합니다.</summary>
    private static bool IsSerializedField(FieldInfo field)
    {
        if (field.IsStatic || field.IsInitOnly)
        {
            return false;
        }

        if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
        {
            return false;
        }

        return field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;
    }
}
