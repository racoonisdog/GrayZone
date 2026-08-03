using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 표면 타입에 대응하는 충돌 이펙트, 사운드, 데칼 에셋 참조를 보관합니다.
/// </summary>
/// <remarks>
/// 실제 표면 판별과 피드백 생성은 필드 씬의 <see cref="SurfaceFeedbackSystem"/>이 담당합니다.
/// 이 에셋은 표면 타입별 할당 데이터를 제공하며 아직 실행 로직은 포함하지 않습니다.
/// </remarks>
[CreateAssetMenu(fileName = "SurfaceFeedback", menuName = "GrayZone/Feedback/Surface Feedback")]
public sealed class SurfaceFeedbackSO : ScriptableObject, IFeedbackData
{
    [Header("Surface Identity")]
    [Tooltip("인스펙터와 디버그 로그에서 표면 타입을 구분할 식별 이름입니다.")]
    [SerializeField] private string m_surfaceId = "Default";

    [Tooltip("이 Feedback과 연결할 물리 머티리얼 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.SurfaceMaterial, "물리 머티리얼 목록")]
    [SerializeField] private PhysicsMaterial[] m_physicsMaterials = Array.Empty<PhysicsMaterial>();

    [Header("Impact Feedback")]
    [Tooltip("표면 피격 위치에서 재생하거나 생성할 이펙트 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.VisualEffect, "표면 피격 이펙트 프리팹")]
    [SerializeField] private GameObject m_impactEffectPrefab;

    [Tooltip("생성한 표면 피격 이펙트를 자동 제거하기까지의 시간(초)입니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_impactEffectLifetime = 2.0f;

    [Tooltip("표면이 피격됐을 때 후보 중 하나를 선택해 재생할 사운드 목록입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Audio, "표면 피격 사운드 목록")]
    [SerializeField] private AudioClip[] m_impactSounds = Array.Empty<AudioClip>();

    [Tooltip("표면 피격 지점에 남길 데칼 프리팹입니다.")]
    [FeedbackReference(FeedbackReferenceKind.Decal, "표면 데칼 프리팹")]
    [SerializeField] private GameObject m_decalPrefab;

    [Tooltip("생성한 표면 데칼을 자동 제거하기까지의 시간(초)입니다. 0 이하면 자동 제거하지 않습니다.")]
    [Min(0.0f)]
    [SerializeField] private float m_decalLifetime = 20.0f;

    /// <summary>표면 타입을 식별하는 이름입니다.</summary>
    public string SurfaceId => m_surfaceId;

    /// <summary>이 Feedback과 연결된 물리 머티리얼 목록입니다.</summary>
    public IReadOnlyList<PhysicsMaterial> PhysicsMaterials => m_physicsMaterials;

    /// <summary>표면 피격 이펙트 프리팹입니다.</summary>
    public GameObject ImpactEffectPrefab => m_impactEffectPrefab;

    /// <summary>표면 피격 이펙트의 런타임 수명(초)입니다.</summary>
    public float ImpactEffectLifetime => m_impactEffectLifetime;

    /// <summary>표면 피격 사운드 후보 목록입니다.</summary>
    public IReadOnlyList<AudioClip> ImpactSounds => m_impactSounds;

    /// <summary>표면 피격 데칼 프리팹입니다.</summary>
    public GameObject DecalPrefab => m_decalPrefab;

    /// <summary>표면 피격 데칼의 런타임 수명(초)입니다.</summary>
    public float DecalLifetime => m_decalLifetime;
}
