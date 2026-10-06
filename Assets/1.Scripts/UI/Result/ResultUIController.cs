using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 귀환 정산(임무 완료) 오버레이입니다. 귀환 구역에 들어가 전투가 정상 종료되면 표시합니다.
/// </summary>
/// <remarks>
/// 디자인 기준: Figma `Misson Complete Prototype 2`. 배경과 "임무 완료" 타이틀은 이미지이고, 아래에
/// 처치한 적 수, 획득 자원 슬롯, 캐릭터별 상태(초상화·이름·상태), "쉘터로 복귀" 버튼이 있습니다.
///
/// 이 컴포넌트는 UI를 만들지 않고, 씬에 배치된 오브젝트를 인스펙터 참조로 받아 값만 채웁니다.
/// 캐릭터 칸은 캐릭터 ID로 찾습니다. 결과에 순서가 섞여 들어와도 나린·청솔·서하 칸이 자기 자리에 채워집니다.
/// 전투 이탈 또는 치명상인 캐릭터는 부상 초상화와 붉은 상태 문구를 씁니다.
///
/// 표시되는 동안 <see cref="Time.timeScale"/>을 0으로 멈춥니다. "쉘터로 복귀"를 누르면 시간을 되돌린 뒤 씬을 옮깁니다.
/// </remarks>
[DisallowMultipleComponent]
public class ResultUIController : MonoBehaviour
{
    /// <summary>결과창에 표시할 파티원 한 명의 정보입니다.</summary>
    [Serializable]
    public struct CharacterResult
    {
        public string Name;
        public CharacterInjuryState InjuryState;
        public bool IsCombatOut;

        /// <summary>캐릭터 칸 선택에 사용하는 고정 식별자입니다. 알 수 없으면 <see cref="PlayerbleCharacterId.Unknown"/>입니다.</summary>
        public PlayerbleCharacterId CharacterId;

        /// <summary>PlayerbleUnitData에서 필드 결과로 직접 전달한 초상화입니다. 칸에 초상화가 지정되지 않았을 때만 씁니다.</summary>
        public Sprite Portrait;
    }

    /// <summary>결과창에 표시할 획득 자원 한 종류의 정보입니다.</summary>
    [Serializable]
    public struct ResourceResult
    {
        /// <summary>자원 ID입니다. 아이콘이 비어 있으면 이 ID로 자원 목록(<see cref="ResourceDefinitionCatalog"/>)에서 찾습니다.</summary>
        public string ResourceId;
        public Texture2D Icon;
        public int Count;
    }

    /// <summary>캐릭터 상태 한 칸입니다. 캐릭터 ID로 결과와 짝을 맞춥니다.</summary>
    [Serializable]
    public struct CharacterColumn
    {
        [Tooltip("이 칸에 표시할 캐릭터입니다.")]
        public PlayerbleCharacterId CharacterId;

        [Tooltip("칸 전체 오브젝트입니다. 결과에 이 캐릭터가 없으면 숨깁니다.")]
        public GameObject Root;

        [Tooltip("초상화 이미지입니다.")]
        public RawImage Portrait;

        [Tooltip("이름 텍스트입니다.")]
        public TextMeshProUGUI NameText;

        [Tooltip("상태 텍스트입니다(정상/부상/치명상/전투이탈).")]
        public TextMeshProUGUI StateText;

        [Tooltip("정상·부상일 때의 초상화입니다.")]
        public Texture2D NormalPortrait;

        [Tooltip("전투 이탈일 때의 초상화입니다(노이즈판). 비어 있으면 일반 초상화를 씁니다. 치명상이어도 전투에 남았으면 일반 초상화를 씁니다.")]
        public Texture2D InjuredPortrait;
    }

    /// <summary>획득 자원 한 칸입니다.</summary>
    [Serializable]
    public struct ResourceSlot
    {
        public RawImage Icon;
        public TextMeshProUGUI Count;
    }

    [Header("Kills")]
    [Tooltip("처치한 적 수를 크게 표시할 텍스트입니다. 숫자만 씁니다.")]
    [SerializeField] private TextMeshProUGUI m_killCountText;

    [Header("Resources")]
    [Tooltip("획득 자원 칸들입니다. 자원이 칸보다 적으면 남는 칸은 비웁니다.")]
    [SerializeField] private ResourceSlot[] m_resourceSlots = Array.Empty<ResourceSlot>();

    [Tooltip("자원 수량 표시 형식입니다. {0}에 수량이 들어갑니다.")]
    [SerializeField] private string m_resourceCountFormat = "x{0}";

    [Tooltip("결과에 아이콘이 실려 오지 않은 자원의 아이콘을 찾을 자원 목록입니다. 셸터 UI와 같은 ResourceDefinitionCatalog를 씁니다.")]
    [SerializeField] private ResourceDefinitionCatalog m_resourceCatalog;

    [Header("Characters")]
    [Tooltip("캐릭터 상태 칸들입니다. 캐릭터 ID로 결과와 짝을 맞춥니다.")]
    [SerializeField] private CharacterColumn[] m_characterColumns = Array.Empty<CharacterColumn>();

    [Header("Return")]
    [Tooltip("\"쉘터로 복귀\" 버튼입니다.")]
    [SerializeField] private Button m_returnButton;

    [Tooltip("'셸터로 복귀' 버튼을 누르면 전환할 씬 이름입니다(Build Settings에 등록되어 있어야 합니다).")]
    [SerializeField] private string m_returnSceneName = "TEst";

    [Header("Behaviour")]
    [Tooltip("켜면 화면이 떠 있는 동안 시간을 멈춥니다.")]
    [SerializeField] private bool m_pauseTimeWhileShown = true;

    [Header("State Colors")]
    [SerializeField] private Color m_normalColor = Color.white;
    [SerializeField] private Color m_injuredColor = new Color(0.95f, 0.65f, 0.15f);
    [SerializeField] private Color m_criticalColor = new Color(1.0f, 0.0f, 0.0f);

    /// <summary>'셸터로 복귀' 버튼을 눌렀을 때 발생합니다.</summary>
    public event Action OnReturnToShelter;

    private float m_timeScaleBeforeShow = 1.0f;
    private bool m_pausedTime;
    private bool m_isLeaving;

    private void Awake()
    {
        if (m_returnButton != null)
        {
            m_returnButton.onClick.AddListener(ReturnToShelter);
        }
    }

    private void OnDisable()
    {
        RestoreTime();
        MissionOverlayVisibility.Unregister(this);
    }

    /// <summary>
    /// 결과 데이터를 채우고 결과창을 표시합니다.
    /// </summary>
    /// <param name="kills">적 처치 수입니다.</param>
    /// <param name="characters">파티원 상태 목록입니다. 캐릭터 ID로 칸을 찾습니다.</param>
    /// <param name="resources">획득 자원 목록입니다. 칸 순서대로 채웁니다.</param>
    public void ShowResult(int kills, IList<CharacterResult> characters, IList<ResourceResult> resources)
    {
        SetKills(kills);
        SetCharacters(characters);
        SetResources(resources);

        m_isLeaving = false;
        gameObject.SetActive(true);
        MissionOverlayVisibility.Register(this);

        if (m_pauseTimeWhileShown && !m_pausedTime)
        {
            m_timeScaleBeforeShow = Time.timeScale > 0.0f ? Time.timeScale : 1.0f;
            Time.timeScale = 0.0f;
            m_pausedTime = true;
        }

        TestSceneUiEventSystemBridge.EnableForResultUI();
    }

    /// <summary>전투 매니저가 확정한 귀환 정산 스냅샷을 표시합니다.</summary>
    public void ShowResult(CombatSceneDataManager.ResultSnapshot result)
    {
        if (result == null)
        {
            return;
        }

        List<CharacterResult> characters = new();
        for (int i = 0; i < result.Characters.Count; i++)
        {
            CombatSceneDataManager.PlayerbleResult character = result.Characters[i];
            characters.Add(new CharacterResult
            {
                Name = character.DisplayName,
                InjuryState = character.InjuryState,
                IsCombatOut = character.IsCombatOut,
                CharacterId = character.CharacterId,
                Portrait = character.Portrait
            });
        }

        List<ResourceResult> resources = new();
        for (int i = 0; i < result.Resources.Count; i++)
        {
            CombatSceneDataManager.ResourceResult resource = result.Resources[i];
            resources.Add(new ResourceResult
            {
                ResourceId = resource.ResourceId,
                Icon = resource.Icon,
                Count = resource.Count
            });
        }

        ShowResult(result.KillCount, characters, resources);
    }

    /// <summary>결과창을 숨기고 멈춘 시간을 되돌립니다.</summary>
    /// <remarks>
    /// 인게임 HUD는 여기서 끄지 않습니다. 조준선 패널을 이 캔버스보다 아래(sortingOrder −1)로
    /// 두어 UI 레이어가 가리는 쪽으로 처리합니다. 무엇이 무엇 위에 오는지를 UI 시스템이
    /// 담당하는 축에서 선언하는 편이 읽기 쉽기 때문입니다.
    /// 표시 자체를 끄는 경로가 필요해지면 <see cref="FieldHudVisibility"/>가 준비되어 있습니다.
    /// </remarks>
    public void Hide()
    {
        RestoreTime();
        gameObject.SetActive(false);
    }

    /// <summary>"쉘터로 복귀" 버튼의 진입점입니다. 시간을 되돌린 뒤 셸터 씬으로 옮깁니다.</summary>
    public void ReturnToShelter()
    {
        if (m_isLeaving)
        {
            return;
        }

        m_isLeaving = true;
        OnReturnToShelter?.Invoke();
        RestoreTime();
        TestSceneUiEventSystemBridge.DisableBeforeShelterTransition();
        // 귀환 정산(FinalizeField)이 GameDataManager에 반영한 부상·HP·자원을 셸터가 그대로 이어받게 합니다.
        // 예전에는 반복 테스트용으로 true를 넣어 셸터 시작 때 DefaultSaveData가 다시 덮였고, 부상이 넘어가지 않았습니다.
        GameDataManager.Instance?.SetUseDefaultSaveDataOnShelterStart(false);
        SceneTransitionController.LoadScene(m_returnSceneName);
    }

    private void SetKills(int kills)
    {
        if (m_killCountText != null)
        {
            m_killCountText.text = Mathf.Max(0, kills).ToString();
        }
    }

    private void SetCharacters(IList<CharacterResult> characters)
    {
        if (m_characterColumns == null)
        {
            return;
        }

        for (int i = 0; i < m_characterColumns.Length; i++)
        {
            CharacterColumn column = m_characterColumns[i];
            bool found = TryFindCharacter(characters, column.CharacterId, out CharacterResult character);

            if (column.Root != null)
            {
                column.Root.SetActive(found);
            }

            if (!found)
            {
                continue;
            }

            // 부상 초상화(노이즈판)는 전투 이탈일 때만 씁니다. 치명상이어도 끝까지 남았으면 일반 초상화입니다.
            bool isOut = character.IsCombatOut;

            // 칸에는 디자인대로 한글 이름(나린·청솔·서하)이 미리 들어 있습니다. 출격 데이터의 표시 이름은 셸터의
            // 영문 ID(Cheongsol 등)일 수 있어 덮어쓰면 폰트에 없는 글자까지 깨져 보이므로, 칸이 비어 있을 때만 씁니다.
            if (column.NameText != null && string.IsNullOrWhiteSpace(column.NameText.text))
            {
                column.NameText.text = character.Name;
            }

            if (column.StateText != null)
            {
                column.StateText.text = ResolveStateLabel(character.InjuryState, character.IsCombatOut);
                column.StateText.color = ResolveStateColor(character.InjuryState, character.IsCombatOut);
            }

            if (column.Portrait != null)
            {
                // 칸에 지정한 초상화를 먼저 쓰고, 없으면 결과에 실려 온 캐릭터 초상화 텍스처를 씁니다.
                Texture portrait = isOut && column.InjuredPortrait != null
                    ? column.InjuredPortrait
                    : column.NormalPortrait != null
                        ? column.NormalPortrait
                        : character.Portrait != null ? character.Portrait.texture : null;
                column.Portrait.texture = portrait;
                column.Portrait.enabled = portrait != null;
            }
        }
    }

    private static bool TryFindCharacter(IList<CharacterResult> characters, PlayerbleCharacterId id, out CharacterResult result)
    {
        if (characters != null)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharacterId == id)
                {
                    result = characters[i];
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private void SetResources(IList<ResourceResult> resources)
    {
        if (m_resourceSlots == null)
        {
            return;
        }

        for (int i = 0; i < m_resourceSlots.Length; i++)
        {
            ResourceSlot slot = m_resourceSlots[i];
            bool hasData = resources != null && i < resources.Count;

            if (slot.Icon != null)
            {
                ApplyResourceIcon(slot.Icon, hasData ? resources[i] : default, hasData);
            }

            if (slot.Count != null)
            {
                slot.Count.text = hasData ? string.Format(m_resourceCountFormat, resources[i].Count) : string.Empty;
            }
        }
    }

    /// <summary>
    /// 자원 칸 아이콘을 채웁니다. 결과에 아이콘이 실려 왔으면 그것을, 없으면 자원 목록의 스프라이트를 씁니다.
    /// </summary>
    /// <remarks>
    /// 방어전 승리 보상처럼 아이콘 없이 기록된 자원은 예전에 개수만 보였습니다. 셸터 UI가 쓰는 자원 목록에서
    /// 같은 ID의 아이콘을 찾아, 결과창과 셸터의 자원 그림이 같게 합니다. 칸이 RawImage라 스프라이트가 아틀라스
    /// 일부일 때도 맞게 보이도록 uvRect를 스프라이트 영역으로 맞춥니다.
    /// </remarks>
    private void ApplyResourceIcon(RawImage icon, ResourceResult resource, bool hasData)
    {
        Texture texture = null;
        Rect uv = new Rect(0.0f, 0.0f, 1.0f, 1.0f);

        if (hasData)
        {
            if (resource.Icon != null)
            {
                texture = resource.Icon;
            }
            else if (m_resourceCatalog != null
                     && m_resourceCatalog.TryGetPresentation(resource.ResourceId, out ResourcePresentation presentation)
                     && presentation.Icon != null)
            {
                Sprite sprite = presentation.Icon;
                texture = sprite.texture;
                Rect rect = sprite.textureRect;
                uv = new Rect(
                    rect.x / texture.width,
                    rect.y / texture.height,
                    rect.width / texture.width,
                    rect.height / texture.height);
            }
        }

        icon.texture = texture;
        icon.uvRect = uv;
        icon.enabled = texture != null;
    }

    private Color ResolveStateColor(CharacterInjuryState injuryState, bool isCombatOut)
    {
        if (isCombatOut || injuryState == CharacterInjuryState.Critical)
        {
            return m_criticalColor;
        }

        return injuryState == CharacterInjuryState.Normal ? m_normalColor : m_injuredColor;
    }

    private static string ResolveStateLabel(CharacterInjuryState injuryState, bool isCombatOut)
    {
        if (isCombatOut)
        {
            return "전투이탈";
        }

        if (injuryState == CharacterInjuryState.Critical)
        {
            return "치명상";
        }

        return injuryState == CharacterInjuryState.Normal ? "정상" : "부상";
    }

    private void RestoreTime()
    {
        if (!m_pausedTime)
        {
            return;
        }

        Time.timeScale = m_timeScaleBeforeShow;
        m_pausedTime = false;
    }
}
