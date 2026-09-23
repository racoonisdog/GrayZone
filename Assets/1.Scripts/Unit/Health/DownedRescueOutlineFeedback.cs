using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구조 가능한 다운 상태인 캐릭터의 RealToon 아웃라인을 강조합니다.
/// </summary>
/// <remarks>
/// 공유 머테리얼은 수정하지 않습니다. 렌더러의 MaterialPropertyBlock만 잠시 덮어쓰고,
/// 구조되거나 타임아웃으로 사망하거나 오브젝트가 비활성화되면 원래 표시값으로 복원합니다.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class DownedRescueOutlineFeedback : MonoBehaviour
{
    private sealed class OutlineTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public float OriginalWidth;
        public Color OriginalColor;
    }

    private static readonly int OutlineEnabledId = Shader.PropertyToID("_N_F_O");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    [Tooltip("다운 상태에서 사용할 아웃라인 색상입니다.")]
    [SerializeField] private Color m_downedOutlineColor = new(0.0f, 1.0f, 0.0f, 1.0f);

    [Tooltip("각 머테리얼에 설정된 원래 아웃라인 굵기에 곱할 값입니다.")]
    [Min(1.0f)]
    [SerializeField] private float m_downedOutlineWidthMultiplier = 2.0f;

    [SerializeField] private PlayerHealth m_playerHealth;

    private readonly List<OutlineTarget> m_targets = new();
    private MaterialPropertyBlock m_propertyBlock;
    private bool m_isApplied;

    private void Reset()
    {
        m_playerHealth = GetComponent<PlayerHealth>();
    }

    private void Awake()
    {
        if (m_playerHealth == null)
        {
            m_playerHealth = GetComponent<PlayerHealth>();
        }

        m_propertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (m_playerHealth == null)
        {
            m_playerHealth = GetComponent<PlayerHealth>();
        }

        if (m_playerHealth == null)
        {
            return;
        }

        m_playerHealth.OnDown += HandleDown;
        m_playerHealth.OnRevive += RestoreOutline;
        m_playerHealth.OnDeath += RestoreOutline;
        m_playerHealth.OnDebugInstantRevive += RestoreOutline;

        if (m_playerHealth.IsDowned && !m_playerHealth.IsDead)
        {
            ApplyDownedOutline();
        }
    }

    private void OnDisable()
    {
        if (m_playerHealth != null)
        {
            m_playerHealth.OnDown -= HandleDown;
            m_playerHealth.OnRevive -= RestoreOutline;
            m_playerHealth.OnDeath -= RestoreOutline;
            m_playerHealth.OnDebugInstantRevive -= RestoreOutline;
        }

        RestoreOutline();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        m_downedOutlineWidthMultiplier = Mathf.Max(1.0f, m_downedOutlineWidthMultiplier);

        if (m_playerHealth == null)
        {
            m_playerHealth = GetComponent<PlayerHealth>();
        }
    }
#endif

    private void HandleDown()
    {
        if (m_playerHealth == null || m_playerHealth.IsDead || !m_playerHealth.IsDowned)
        {
            return;
        }

        ApplyDownedOutline();
    }

    private void ApplyDownedOutline()
    {
        if (m_isApplied)
        {
            return;
        }

        CacheOutlineTargets();

        foreach (OutlineTarget target in m_targets)
        {
            if (target.Renderer == null)
            {
                continue;
            }

            m_propertyBlock.Clear();
            target.Renderer.GetPropertyBlock(m_propertyBlock, target.MaterialIndex);
            m_propertyBlock.SetFloat(
                OutlineWidthId,
                target.OriginalWidth * m_downedOutlineWidthMultiplier);
            m_propertyBlock.SetColor(OutlineColorId, m_downedOutlineColor);
            target.Renderer.SetPropertyBlock(m_propertyBlock, target.MaterialIndex);
        }

        m_isApplied = true;
    }

    private void RestoreOutline()
    {
        if (!m_isApplied)
        {
            return;
        }

        foreach (OutlineTarget target in m_targets)
        {
            if (target.Renderer == null)
            {
                continue;
            }

            m_propertyBlock.Clear();
            target.Renderer.GetPropertyBlock(m_propertyBlock, target.MaterialIndex);
            m_propertyBlock.SetFloat(OutlineWidthId, target.OriginalWidth);
            m_propertyBlock.SetColor(OutlineColorId, target.OriginalColor);
            target.Renderer.SetPropertyBlock(m_propertyBlock, target.MaterialIndex);
        }

        m_targets.Clear();
        m_isApplied = false;
    }

    private void CacheOutlineTargets()
    {
        m_targets.Clear();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer targetRenderer in renderers)
        {
            Material[] materials = targetRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (!IsActiveRealToonOutline(material))
                {
                    continue;
                }

                m_propertyBlock.Clear();
                targetRenderer.GetPropertyBlock(m_propertyBlock, materialIndex);

                m_targets.Add(new OutlineTarget
                {
                    Renderer = targetRenderer,
                    MaterialIndex = materialIndex,
                    OriginalWidth = GetEffectiveFloat(material, OutlineWidthId),
                    OriginalColor = GetEffectiveColor(material, OutlineColorId)
                });
            }
        }
    }

    private float GetEffectiveFloat(Material material, int propertyId)
    {
        return m_propertyBlock.HasProperty(propertyId)
            ? m_propertyBlock.GetFloat(propertyId)
            : material.GetFloat(propertyId);
    }

    private Color GetEffectiveColor(Material material, int propertyId)
    {
        return m_propertyBlock.HasProperty(propertyId)
            ? m_propertyBlock.GetColor(propertyId)
            : material.GetColor(propertyId);
    }

    private static bool IsActiveRealToonOutline(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        if (material.shader.name.IndexOf("RealToon", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return material.HasProperty(OutlineEnabledId)
            && material.GetFloat(OutlineEnabledId) > 0.5f
            && material.HasProperty(OutlineWidthId)
            && material.HasProperty(OutlineColorId);
    }
}
