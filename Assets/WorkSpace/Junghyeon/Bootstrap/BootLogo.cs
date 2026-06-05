using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BootLogo : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private CanvasGroup backgroundGroup;
    [SerializeField] private CanvasGroup logoGroup;
    [SerializeField] private Image logoImage;

    [Header("로고 시퀀스")]
    [Tooltip("여러 로고를 순서대로 재생합니다. 비어 있으면 현재 로고 이미지 스프라이트를 사용합니다.")]
    [SerializeField] private List<Sprite> logoSequence = new List<Sprite>();

    [Header("타이밍")]
    [SerializeField] private float fadeInDuration = 1f;
    [SerializeField] private float holdDuration = 1f;
    [SerializeField] private float fadeOutDuration = 1f;

    [Header("배경 페이드 (선택)")]
    [SerializeField] private bool useBackgroundFade = false;
    [SerializeField] private float backgroundFadeInDuration = 0.2f;
    [SerializeField] private float backgroundFadeOutDuration = 0.35f;

    [Header("스킵 (선택)")]
    [SerializeField] private bool allowSkip = false;
    [SerializeField] private KeyCode skipKey = KeyCode.Space;

    [Header("연출 종료 후")]
    [SerializeField] private bool loadNextScene = false;
    [SerializeField] private string nextSceneName = string.Empty;
    [SerializeField] private UnityEvent onSequenceCompleted;

    private Coroutine sequenceCoroutine;
    private bool skipRequested;
    private bool sequenceFinished;

    private void Awake()
    {
        AutoAssignReferencesIfMissing();

        if (backgroundGroup == null || logoGroup == null || logoImage == null)
        {
            Debug.LogWarning("[BootstrapLogoUI] Missing UI references. Sequence disabled.");
            enabled = false;
            return;
        }

        // Clamp time values to avoid negative durations.
        fadeInDuration = Mathf.Max(0f, fadeInDuration);
        holdDuration = Mathf.Max(0f, holdDuration);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        backgroundFadeInDuration = Mathf.Max(0f, backgroundFadeInDuration);
        backgroundFadeOutDuration = Mathf.Max(0f, backgroundFadeOutDuration);

        // Start fully black background and transparent logo.
        backgroundGroup.alpha = useBackgroundFade ? 0f : 1f;
        logoGroup.alpha = 0f;
    }

    private void Start()
    {
        if (!enabled)
        {
            return;
        }

        sequenceCoroutine = StartCoroutine(PlaySequenceRoutine());
    }

    private void Update()
    {
        if (!allowSkip || sequenceFinished)
        {
            return;
        }

        if (Input.GetKeyDown(skipKey))
        {
            RequestSkip();
        }
    }

    [ContextMenu("Auto Assign References")]
    public void AutoAssignReferencesIfMissing()
    {
        if (backgroundGroup == null)
        {
            Transform backgroundTransform = transform.Find("Background");
            if (backgroundTransform != null)
            {
                backgroundGroup = backgroundTransform.GetComponent<CanvasGroup>();
            }
        }

        if (logoGroup == null || logoImage == null)
        {
            Transform logoTransform = transform.Find("Logo");
            if (logoTransform != null)
            {
                if (logoGroup == null)
                {
                    logoGroup = logoTransform.GetComponent<CanvasGroup>();
                }

                if (logoImage == null)
                {
                    logoImage = logoTransform.GetComponent<Image>();
                }
            }
        }
    }

    public void SetReferences(CanvasGroup background, CanvasGroup logo, Image logoImg)
    {
        backgroundGroup = background;
        logoGroup = logo;
        logoImage = logoImg;
    }

    public void RequestSkip()
    {
        if (!allowSkip)
        {
            return;
        }

        skipRequested = true;
    }

    private IEnumerator PlaySequenceRoutine()
    {
        if (useBackgroundFade)
        {
            yield return FadeCanvasGroup(backgroundGroup, backgroundGroup.alpha, 1f, backgroundFadeInDuration);
        }
        else
        {
            backgroundGroup.alpha = 1f;
        }

        List<Sprite> sequenceSprites = BuildSequenceSprites();
        if (sequenceSprites.Count == 0)
        {
            Debug.LogWarning("[BootstrapLogoUI] No logo sprite assigned.");
        }

        for (int i = 0; i < sequenceSprites.Count; i++)
        {
            if (skipRequested)
            {
                break;
            }

            logoImage.sprite = sequenceSprites[i];
            logoGroup.alpha = 0f;

            yield return FadeCanvasGroup(logoGroup, 0f, 1f, fadeInDuration);
            yield return WaitWithSkip(holdDuration);
            yield return FadeCanvasGroup(logoGroup, logoGroup.alpha, 0f, fadeOutDuration);
        }

        if (skipRequested)
        {
            logoGroup.alpha = 0f;
            backgroundGroup.alpha = useBackgroundFade ? 0f : backgroundGroup.alpha;
        }
        else if (useBackgroundFade)
        {
            yield return FadeCanvasGroup(backgroundGroup, backgroundGroup.alpha, 0f, backgroundFadeOutDuration);
        }

        CompleteSequence();
    }

    private List<Sprite> BuildSequenceSprites()
    {
        List<Sprite> result = new List<Sprite>();

        if (logoSequence != null)
        {
            for (int i = 0; i < logoSequence.Count; i++)
            {
                if (logoSequence[i] != null)
                {
                    result.Add(logoSequence[i]);
                }
            }
        }

        if (result.Count == 0 && logoImage != null && logoImage.sprite != null)
        {
            result.Add(logoImage.sprite);
        }

        return result;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
        {
            yield break;
        }

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        group.alpha = from;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (skipRequested)
            {
                group.alpha = to;
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        group.alpha = to;
    }

    private IEnumerator WaitWithSkip(float duration)
    {
        if (duration <= 0f)
        {
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (skipRequested)
            {
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void CompleteSequence()
    {
        if (sequenceFinished)
        {
            return;
        }

        sequenceFinished = true;

        onSequenceCompleted?.Invoke();

        if (loadNextScene)
        {
            if (string.IsNullOrWhiteSpace(nextSceneName))
            {
                Debug.LogWarning("[BootstrapLogoUI] loadNextScene is enabled but nextSceneName is empty.");
            }
            else
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }
    }

    // Example post-process hook that can be bound from UnityEvent.
    public void HandleBootstrapCompleted()
    {
        Debug.Log("[BootstrapLogoUI] Bootstrap logo sequence completed.");
    }
}
