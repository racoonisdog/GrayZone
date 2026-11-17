using System;
using System.Collections.Generic;
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

        [Tooltip("캐릭터가 없는 씬(타이틀·셸터) 설정 화면에서 캐릭터 버튼에 쓸 이름입니다.")]
        public string displayName;

        [Tooltip("캐릭터가 없는 씬의 미리보기가 간격 계산에 쓰는 값이 있는지 여부입니다.")]
        public bool hasPreviewBasis;

        [Tooltip("캐릭터가 없는 씬의 미리보기가 간격 계산에 쓰는 값입니다. '열린 씬 값으로 미리보기 기준 채우기'로 채웁니다.")]
        public CrosshairPreviewBasis previewBasis = new CrosshairPreviewBasis();
    }

    [Header("Characters")]
    [Tooltip("캐릭터별 기본 조준선입니다. 목록에 없는 캐릭터는 씬의 조준선 HUD 값을 기본값으로 씁니다.")]
    [SerializeField] private CharacterDefault[] m_characters = Array.Empty<CharacterDefault>();

    [Header("Throwable")]
    [Tooltip("켜면 아래 값을 투척물 조준선 기본값으로 씁니다. 끄면 씬의 ExplosiveProjectileShooter 값을 씁니다.")]
    [SerializeField] private bool m_hasThrowable = true;

    [Tooltip("모든 캐릭터가 같이 쓰는 투척 모드 조준선 기본값입니다.")]
    [SerializeField] private CrosshairStyle m_throwable = new CrosshairStyle();

    [Header("Preview")]
    [Tooltip("조준선 HUD가 없는 씬(타이틀·셸터)에서 설정 화면 미리보기가 복제할 조준선 HUD 프리팹입니다.")]
    [SerializeField] private CrosshairController m_previewHud;

    /// <summary>조준선 HUD가 없는 씬에서 미리보기가 복제할 HUD 프리팹입니다. 없으면 null입니다.</summary>
    public CrosshairController PreviewHud => m_previewHud;

    /// <summary>기본값이 있는 캐릭터 목록(식별자와 표시 이름)입니다. 캐릭터가 없는 씬의 설정 화면이 씁니다.</summary>
    public List<KeyValuePair<PlayerbleCharacterId, string>> GetCharacters()
    {
        var result = new List<KeyValuePair<PlayerbleCharacterId, string>>();
        foreach (CharacterDefault entry in m_characters)
        {
            if (entry != null && entry.characterId != PlayerbleCharacterId.Unknown)
            {
                string name = string.IsNullOrWhiteSpace(entry.displayName) ? entry.characterId.ToString() : entry.displayName.Trim();
                result.Add(new KeyValuePair<PlayerbleCharacterId, string>(entry.characterId, name));
            }
        }

        return result;
    }

    /// <summary>캐릭터가 없는 씬의 미리보기 간격 값 사본을 찾습니다.</summary>
    public bool TryGetPreviewBasis(PlayerbleCharacterId characterId, out CrosshairPreviewBasis basis)
    {
        foreach (CharacterDefault entry in m_characters)
        {
            if (entry != null && entry.hasPreviewBasis && entry.previewBasis != null && entry.characterId == characterId)
            {
                basis = entry.previewBasis.Clone();
                return true;
            }
        }

        basis = null;
        return false;
    }

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
            characters.Add(new CharacterDefault
            {
                characterId = data.CharacterId,
                style = style,
                displayName = data.DisplayName,
                hasPreviewBasis = true,
                previewBasis = aim.CaptureCrosshairPreviewBasis(),
            });
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

    /// <summary>
    /// 기본 조준선은 그대로 두고, 캐릭터 이름과 미리보기 간격 값만 열린 씬 캐릭터에서 채웁니다.
    /// </summary>
    /// <remarks>
    /// 쏘지 않고 서 있을 때의 카메라 FOV는 실행 중에만 정해지므로, 전투 씬을 실행하고 자유 시점으로 잠시 둔 뒤 누르는 것이 정확합니다.
    /// Edit 모드에서 누르면 FOV는 각 캐릭터의 힙파이어 FOV로 채워집니다. 실행 중 바꾼 이 에셋 값은 실행을 끝내도 남습니다.
    /// </remarks>
    [Button("열린 씬 값으로 미리보기 기준 채우기")]
    public void CapturePreviewBasisFromOpenScene()
    {
        UnityEditor.Undo.RecordObject(this, "Capture Crosshair Preview Basis");
        int filled = 0;
        foreach (PlayerbleUnitData data in FindObjectsByType<PlayerbleUnitData>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            AimController aim = data.GetComponentInChildren<AimController>(true);
            if (aim == null || data.CharacterId == PlayerbleCharacterId.Unknown)
            {
                continue;
            }

            foreach (CharacterDefault entry in m_characters)
            {
                if (entry != null && entry.characterId == data.CharacterId)
                {
                    entry.displayName = data.DisplayName;
                    entry.previewBasis = aim.CaptureCrosshairPreviewBasis();
                    entry.hasPreviewBasis = true;
                    filled++;
                }
            }
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[DefaultCrosshairSettings] 캐릭터 {filled}명의 이름과 미리보기 기준을 채웠습니다.", this);
    }
#endif
}
