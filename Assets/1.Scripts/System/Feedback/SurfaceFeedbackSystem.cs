using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 필드 씬에서 사용할 기본 표면 피드백과 표면 타입별 피드백 목록을 보관합니다.
/// </summary>
/// <remarks>
/// 물리 머티리얼을 표면 피드백으로 해석하고 실제 RaycastHit 위치에 이펙트·사운드·데칼을 출력합니다.
/// 필드 생명주기와 함께 존재하도록 <see cref="FieldManager"/>와 같은 GameObject에 배치합니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class SurfaceFeedbackSystem : MonoBehaviour
{
    [Tooltip("등록된 표면 타입과 일치하지 않을 때 사용할 기본 표면 피드백입니다.")]
    [FormerlySerializedAs("m_defaultProfile")]
    [SerializeField] private SurfaceFeedbackSO m_defaultFeedback;

    [Tooltip("필드에서 판별할 표면 타입별 피드백 목록입니다.")]
    [FormerlySerializedAs("m_profiles")]
    [SerializeField] private SurfaceFeedbackSO[] m_feedbacks = Array.Empty<SurfaceFeedbackSO>();

    private readonly Dictionary<SurfaceFeedbackSO, int> m_lastSoundIndices =
        new Dictionary<SurfaceFeedbackSO, int>();

    /// <summary>일치하는 표면 타입이 없을 때 사용할 기본 피드백입니다.</summary>
    public SurfaceFeedbackSO DefaultFeedback => m_defaultFeedback;

    /// <summary>필드에서 사용할 표면 타입별 피드백 목록입니다.</summary>
    public IReadOnlyList<SurfaceFeedbackSO> Feedbacks => m_feedbacks;

    /// <summary>Raycast로 맞은 표면을 판별하고 해당 위치에 이펙트·사운드·데칼을 출력합니다.</summary>
    /// <returns>사용할 표면 피드백을 찾았으면 true입니다.</returns>
    public bool PlayImpact(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return false;
        }

        SurfaceFeedbackSO feedback = ResolveFeedback(hit.collider.sharedMaterial);
        if (feedback == null)
        {
            return false;
        }

        FeedbackPlaybackUtility.SpawnAligned(
            feedback.ImpactEffectPrefab,
            hit.point,
            hit.normal,
            feedback.ImpactEffectLifetime,
            parent: hit.collider.transform);
        FeedbackPlaybackUtility.SpawnAligned(
            feedback.DecalPrefab,
            hit.point,
            hit.normal,
            feedback.DecalLifetime,
            parent: hit.collider.transform);

        PlayImpactSound(feedback, hit.point);
        return true;
    }

    /// <summary>물리 머티리얼과 일치하는 피드백을 찾고, 없으면 기본 피드백을 반환합니다.</summary>
    public SurfaceFeedbackSO ResolveFeedback(PhysicsMaterial material)
    {
        if (material != null && m_feedbacks != null)
        {
            for (int feedbackIndex = 0; feedbackIndex < m_feedbacks.Length; feedbackIndex++)
            {
                SurfaceFeedbackSO feedback = m_feedbacks[feedbackIndex];
                if (feedback == null || feedback.PhysicsMaterials == null)
                {
                    continue;
                }

                for (int materialIndex = 0; materialIndex < feedback.PhysicsMaterials.Count; materialIndex++)
                {
                    if (feedback.PhysicsMaterials[materialIndex] == material)
                    {
                        return feedback;
                    }
                }
            }
        }

        return m_defaultFeedback;
    }

    private void PlayImpactSound(SurfaceFeedbackSO feedback, Vector3 position)
    {
        m_lastSoundIndices.TryGetValue(feedback, out int lastIndex);
        if (!m_lastSoundIndices.ContainsKey(feedback))
        {
            lastIndex = -1;
        }

        if (!FeedbackPlaybackUtility.TryPickClip(feedback.ImpactSounds, ref lastIndex, out AudioClip clip))
        {
            return;
        }

        m_lastSoundIndices[feedback] = lastIndex;
        FieldAudioSystem fieldAudio = FieldManager.Instance != null ? FieldManager.Instance.AudioFeedback : null;
        fieldAudio?.PlayOneShotAt(clip, position);
    }
}
