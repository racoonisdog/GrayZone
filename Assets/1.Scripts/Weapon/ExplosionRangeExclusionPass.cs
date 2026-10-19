using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 폭발 범위 미리보기에서 제외할 Layer가 화면에 보이는 영역을 스텐실에 표시합니다.
/// </summary>
/// <remarks>
/// 렌더러 에셋에 Renderer Feature로 등록하지 않습니다. <see cref="ExplosiveProjectileShooter"/>가
/// 카메라 렌더 직전에 <see cref="ScriptableRenderer.EnqueuePass"/>로 이 패스를 끼워 넣습니다.
/// 대상 Layer의 불투명 렌더러를 미리보기 머티리얼의 ExclusionStencil 패스로 한 번 더 그려 색은 쓰지 않고
/// 스텐실 비트만 남기면, 뒤이어 그려지는 범위 패스가 그 비트가 있는 픽셀을 건너뜁니다.
/// </remarks>
public sealed class ExplosionRangeExclusionPass : ScriptableRenderPass
{
    // 덮어 그릴 대상 렌더러를 고르는 기준입니다. 원래 머티리얼에 이 LightMode 패스가 있어야 그려집니다.
    private static readonly List<ShaderTagId> s_shaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("SRPDefaultUnlit"),
    };

    private sealed class PassData
    {
        public RendererListHandle rendererList;
    }

    private Material m_material;
    private int m_passIndex = -1;
    private LayerMask m_layerMask;

    public ExplosionRangeExclusionPass()
    {
        // 불투명 렌더링과 깊이 복사가 끝난 뒤, 범위 패스(반투명)보다 먼저 스텐실을 채웁니다.
        renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        profilingSampler = new ProfilingSampler("Explosion Range Exclusion Stencil");
    }

    /// <param name="material">ExclusionStencil 패스가 있는 GrayZone/ExplosionRangeSurface 머티리얼입니다.</param>
    /// <param name="passIndex">머티리얼에서 ExclusionStencil 패스의 인덱스입니다.</param>
    /// <param name="layerMask">스텐실에 표시할 Layer입니다.</param>
    public void Setup(Material material, int passIndex, LayerMask layerMask)
    {
        m_material = material;
        m_passIndex = passIndex;
        m_layerMask = layerMask;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (m_material == null || m_passIndex < 0 || m_layerMask.value == 0)
        {
            return;
        }

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalLightData lightData = frameData.Get<UniversalLightData>();

        using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                   "Explosion Range Exclusion Stencil",
                   out PassData passData,
                   profilingSampler))
        {
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                s_shaderTagIds,
                renderingData,
                cameraData,
                lightData,
                cameraData.defaultOpaqueSortFlags);
            drawingSettings.overrideMaterial = m_material;
            drawingSettings.overrideMaterialPassIndex = m_passIndex;

            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, m_layerMask);
            RendererListParams listParams = new RendererListParams(
                renderingData.cullResults,
                drawingSettings,
                filteringSettings);

            passData.rendererList = renderGraph.CreateRendererList(listParams);
            builder.UseRendererList(passData.rendererList);

            // 색은 쓰지 않지만(ColorMask 0) 같은 렌더 타깃을 묶어 두어야 반투명 패스와 합쳐집니다.
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

            builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.rendererList);
            });
        }
    }
}
