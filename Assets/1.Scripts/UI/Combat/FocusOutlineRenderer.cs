using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 바라본 대상의 메시를 한 번 더 그려 외곽선을 표시합니다.
/// </summary>
/// <remarks>
/// 대상의 머티리얼이나 렌더러는 건드리지 않습니다. 매 프레임 <see cref="Graphics.RenderMesh"/>로 마스크와 외곽선을
/// 덧그리기만 하므로, 그리기를 멈추면 따로 되돌릴 것이 없습니다. 청사진 머티리얼이 반투명이라
/// 마스크(GrayZone/FocusOutlineMask)로 대상 영역을 먼저 막고 외곽선(GrayZone/FocusOutline)은 그 바깥에만 그립니다.
///
/// 켜져 있는 렌더러만 그립니다. 함정은 청사진과 설치 상태에서 서로 다른 렌더러를 켜고 끄기 때문에,
/// 대상이 바뀔 때만 목록을 모으고 그릴지는 매 프레임 렌더러 상태로 정합니다. SkinnedMeshRenderer는 그리지 않습니다.
/// </remarks>
public sealed class FocusOutlineRenderer
{
    private readonly struct MeshEntry
    {
        public readonly MeshFilter Filter;
        public readonly Renderer Renderer;

        public MeshEntry(MeshFilter filter, Renderer renderer)
        {
            Filter = filter;
            Renderer = renderer;
        }
    }

    private readonly Material m_maskMaterial;
    private readonly Material m_outlineMaterial;
    private readonly List<MeshEntry> m_entries = new List<MeshEntry>();
    private GameObject m_target;

    /// <param name="maskMaterial">대상 영역을 스텐실에 표시하는 머티리얼입니다.</param>
    /// <param name="outlineMaterial">외곽선 머티리얼입니다. 호출부가 소유하며 색·굵기를 바꿀 수 있습니다.</param>
    public FocusOutlineRenderer(Material maskMaterial, Material outlineMaterial)
    {
        m_maskMaterial = maskMaterial;
        m_outlineMaterial = outlineMaterial;
    }

    /// <summary>머티리얼이 모두 있어 그릴 수 있는지 여부입니다.</summary>
    public bool IsValid => m_maskMaterial != null && m_outlineMaterial != null;

    /// <summary>
    /// 이번 프레임에 대상의 외곽선을 그립니다. 매 프레임 불러야 계속 보입니다.
    /// </summary>
    /// <param name="target">외곽선을 그릴 대상의 루트입니다. <c>null</c>이면 아무것도 그리지 않습니다.</param>
    public void Draw(GameObject target)
    {
        if (!IsValid || target == null)
        {
            return;
        }

        if (target != m_target)
        {
            Collect(target);
        }

        for (int i = 0; i < m_entries.Count; i++)
        {
            MeshEntry entry = m_entries[i];
            if (entry.Filter == null || entry.Renderer == null || !entry.Renderer.enabled
                || !entry.Renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Mesh mesh = entry.Filter.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            Matrix4x4 matrix = entry.Renderer.localToWorldMatrix;
            RenderParams maskParams = CreateParams(m_maskMaterial, entry.Renderer.gameObject.layer);
            RenderParams outlineParams = CreateParams(m_outlineMaterial, entry.Renderer.gameObject.layer);
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                Graphics.RenderMesh(maskParams, mesh, subMesh, matrix);
                Graphics.RenderMesh(outlineParams, mesh, subMesh, matrix);
            }
        }
    }

    private void Collect(GameObject target)
    {
        m_target = target;
        m_entries.Clear();

        foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(true))
        {
            Renderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                m_entries.Add(new MeshEntry(filter, renderer));
            }
        }
    }

    private static RenderParams CreateParams(Material material, int layer)
    {
        return new RenderParams(material)
        {
            layer = layer,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };
    }
}
