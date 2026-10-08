using System;
using UnityEngine;
using VInspector;

/// <summary>
/// 조준선 설정의 기본값 템플릿입니다. 캐릭터별 기본 조준선과 공용 투척물 조준선 기본값을 담습니다.
/// </summary>
/// <remarks>
/// <see cref="DefaultSaveData"/>와 같은 방식입니다. 이 에셋은 런타임 사용자 값을 소유하지 않고, 조회할 때마다 독립 사본을 돌려줍니다.
/// 설정 화면의 기본값 버튼과, 사용자가 저장한 적 없는 캐릭터의 첫 값이 이 에셋을 읽습니다.
/// 외부 형태·중앙 간격·링 지름은 게임이 시작될 때 이 값으로 각 캐릭터 <see cref="AimController"/>에 들어갑니다.
/// </remarks>
[CreateAssetMenu(fileName = "DefaultCrosshairSettings", menuName = "GrayZone/Settings/Default Crosshair Settings")]
public sealed class DefaultCrosshairSettings : ScriptableObject
{
    /// <summary>Resources 기본 에셋 경로입니다. GameSettingManager에 지정하지 않았을 때 여기서 읽습니다.</summary>
    public const string ResourcesPath = "Settings/DefaultCrosshairSettings";

    [Serializable]
    private sealed class CharacterDefault
    {
        [Tooltip("이 기본값을 쓰는 캐릭터입니다.")]
        public PlayerbleCharacterId characterId;

        [Tooltip("이 캐릭터의 기본 조준선입니다.")]
        public CrosshairStyle style = new CrosshairStyle();
    }

    [Header("Characters")]
    [Tooltip("캐릭터별 기본 조준선입니다. 목록에 없는 캐릭터는 씬의 조준선 HUD 값을 기본값으로 씁니다.")]
    [SerializeField] private CharacterDefault[] m_characters = Array.Empty<CharacterDefault>();

    [Header("Throwable")]
    [Tooltip("켜면 아래 값을 투척물 조준선 기본값으로 씁니다. 끄면 씬의 ExplosiveProjectileShooter 값을 씁니다.")]
    [SerializeField] private bool m_hasThrowable = true;

    [Tooltip("모든 캐릭터가 같이 쓰는 투척 모드 조준선 기본값입니다.")]
    [SerializeField] private CrosshairStyle m_throwable = new CrosshairStyle();

    /// <summary>Resources 기본 에셋을 읽습니다. 없으면 null입니다.</summary>
    public static DefaultCrosshairSettings LoadFromResources()
    {
        return Resources.Load<DefaultCrosshairSettings>(ResourcesPath);
    }

    /// <summary>캐릭터 기본 조준선의 사본을 찾습니다.</summary>
    public bool TryGetCharacterStyle(PlayerbleCharacterId characterId, out CrosshairStyle style)
    {
        foreach (CharacterDefault entry in m_characters)
        {
            if (entry != null && entry.style != null && entry.characterId == characterId)
            {
                style = entry.style.Clone();
                return true;
            }
        }

        style = null;
        return false;
    }

    /// <summary>투척물 조준선 기본값의 사본을 찾습니다.</summary>
    public bool TryGetThrowableStyle(out CrosshairStyle style)
    {
        style = m_hasThrowable && m_throwable != null ? m_throwable.Clone() : null;
        return style != null;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 열린 씬의 현재 값으로 이 에셋을 채웁니다. 캐릭터 값은 조준선 HUD 값에 각 캐릭터 AimController의
    /// 외부 형태·중앙 간격·링 지름을 얹고, 투척물 값은 첫 ExplosiveProjectileShooter에서 가져옵니다.
    /// </summary>
    [Button("열린 씬 값으로 채우기")]
    private void CaptureFromOpenScene()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[DefaultCrosshairSettings] 실행 중에는 HUD 값이 이미 바뀌었을 수 있어 Edit 모드에서만 채웁니다.", this);
            return;
        }

        CrosshairController crosshair = FindFirstObjectByType<CrosshairController>(FindObjectsInactive.Include);
        if (crosshair == null)
        {
            Debug.LogWarning("[DefaultCrosshairSettings] 열린 씬에 CrosshairController가 없습니다.", this);
            return;
        }

        UnityEditor.Undo.RecordObject(this, "Capture Crosshair Defaults");
        CrosshairStyle hudStyle = CrosshairStyle.Capture(crosshair);
        var characters = new System.Collections.Generic.List<CharacterDefault>();
        foreach (PlayerbleUnitData data in FindObjectsByType<PlayerbleUnitData>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            AimController aim = data.GetComponentInChildren<AimController>(true);
            if (aim == null || data.CharacterId == PlayerbleCharacterId.Unknown)
            {
                continue;
            }

            CrosshairStyle style = hudStyle.Clone();
            style.subShape = aim.CrosshairSubShape;
            style.centerSpacePixels = aim.CrosshairCenterSpacePixels;
            style.subRingSizePixels = aim.CrosshairSubRingBaseDiameterPixels;
            characters.Add(new CharacterDefault { characterId = data.CharacterId, style = style });
        }

        characters.Sort((a, b) => a.characterId.CompareTo(b.characterId));
        m_characters = characters.ToArray();

        ExplosiveProjectileShooter shooter = FindFirstObjectByType<ExplosiveProjectileShooter>(FindObjectsInactive.Include);
        if (shooter != null && shooter.ThrowCrosshairStyle != null)
        {
            m_throwable = shooter.ThrowCrosshairStyle;
            m_hasThrowable = true;
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[DefaultCrosshairSettings] 캐릭터 {m_characters.Length}명과 투척물 기본값을 채웠습니다.", this);
    }
#endif
}
