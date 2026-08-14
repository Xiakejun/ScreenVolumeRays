using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class VolumetricLightScattering : ScriptableRendererFeature
{
    [Header("Debug")]
    [Tooltip("开启控制台日志，用于排查 Render Graph 是否真的执行了关键步骤")]
    public bool debugLog = false;

    [Tooltip("调试视图：直接把遮挡纹理显示到屏幕，跳过径向模糊")]
    public bool debugShowOccludersMap = false;

    class LightScatteringPass : ScriptableRenderPass
    {
        private class OccludersPassData
        {
            public RendererListHandle rendererListHandle;
        }

        private class RadialBlurPassData
        {
            public TextureHandle occludersTex;
            public TextureHandle cameraColorTex;
            public TextureHandle dstTex;
            public Material material;
            public Vector4 center;
            public float intensity;
            public float blurWidth;
            public Color lightColor;
            public float falloff;
            public bool isDebug;
        }

        private class CopyPassData
        {
            public TextureHandle srcTex;
            public TextureHandle dstTex;
            public Material copyMaterial;
            public bool isDebug;
            public TextureHandle debugOccludersTex;
        }

        private static readonly int ShaderID_CameraColorTex = Shader.PropertyToID("_CameraColorTex");
        private static readonly int ShaderID_OccludersMap = Shader.PropertyToID("_OccludersMap");
        private static readonly int ShaderID_Center = Shader.PropertyToID("_Center");
        private static readonly int ShaderID_Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int ShaderID_BlurWidth = Shader.PropertyToID("_BlurWidth");
        private static readonly int ShaderID_LightColor = Shader.PropertyToID("_LightColor");
        private static readonly int ShaderID_Falloff = Shader.PropertyToID("_Falloff");

        private readonly FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        private readonly List<ShaderTagId> shaderTagIdList = new List<ShaderTagId>();
        private readonly bool debugLog;
        private readonly bool debugShowOccludersMap;
        private Material occludersMaterial;
        private Material radialBlurMaterial;
        private Material blitCopyMaterial;

        private static int s_LastFrameLogged = -1;

        public LightScatteringPass(bool debugLog, bool debugShowOccludersMap)
        {
            this.debugLog = debugLog;
            this.debugShowOccludersMap = debugShowOccludersMap;

            shaderTagIdList.Add(new ShaderTagId("UniversalForward"));
            shaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
            shaderTagIdList.Add(new ShaderTagId("LightweightForward"));
            shaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));

            requiresIntermediateTexture = true;
        }

        // 延迟初始化材质：导入资源/重编译时 Shader.Find 可能临时返回 null
        // 每帧检查，材质为空就重新尝试创建，下一帧自动恢复
        private void EnsureMaterials()
        {
            if (!occludersMaterial)
            {
                Shader unlitShader = Shader.Find("Hidden/RW/UnlitColor");
                if (unlitShader != null)
                    occludersMaterial = new Material(unlitShader);
            }
            if (!radialBlurMaterial)
            {
                Shader radialShader = Shader.Find("Hidden/RW/RadialBlur");
                if (radialShader != null)
                    radialBlurMaterial = new Material(radialShader);
            }
            if (!blitCopyMaterial)
            {
                blitCopyMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/Blit");
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            EnsureMaterials();

            if (!occludersMaterial || !radialBlurMaterial || !blitCopyMaterial)
            {
                if (debugLog)
                    Debug.LogWarning("[VLS] 材质缺失，跳过本帧。");
                return;
            }

            var stack = VolumeManager.instance.stack;
            var volume = stack.GetComponent<VolumetricLightScatteringVolume>();
            if (volume == null || !volume.IsActive())
                return;

            float resolutionScale = volume.resolutionScale.value;
            float intensity = volume.intensity.value;
            float blurWidth = volume.blurWidth.value;
            Color lightColor = volume.lightColor.value;
            float falloff = volume.falloff.value;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            TextureHandle cameraColorTarget = resourceData.activeColorTexture;

            int frameCount = Time.frameCount;
            bool logThisFrame = debugLog && frameCount != s_LastFrameLogged;
            if (logThisFrame) s_LastFrameLogged = frameCount;

            // 步骤 1: 创建遮挡纹理（降采样，clear white）
            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.width = Mathf.RoundToInt(desc.width * resolutionScale);
            desc.height = Mathf.RoundToInt(desc.height * resolutionScale);

            TextureDesc texDesc = new TextureDesc(desc.width, desc.height)
            {
                colorFormat = desc.graphicsFormat,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                filterMode = FilterMode.Bilinear,
                clearBuffer = true,
                clearColor = Color.white,
                name = "_OccludersMap"
            };

            TextureHandle occludersTex = renderGraph.CreateTexture(texDesc);

            // 步骤 2: Pass A - 在遮挡纹理上绘制不透明物体为黑色剪影
            ShaderTagId mainTagId = new ShaderTagId("UniversalForward");
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(mainTagId, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            drawSettings.overrideMaterial = occludersMaterial;
            for (int i = 1; i < shaderTagIdList.Count; i++)
            {
                drawSettings.SetShaderPassName(i, shaderTagIdList[i]);
            }

            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, filteringSettings);
            RendererListHandle rendererListHandle = renderGraph.CreateRendererList(rendererListParams);

            using (var builder = renderGraph.AddRasterRenderPass<OccludersPassData>("Draw Occluder Silhouettes", out var passData))
            {
                passData.rendererListHandle = rendererListHandle;

                builder.UseRendererList(rendererListHandle);
                builder.SetRenderAttachment(occludersTex, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (OccludersPassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererListHandle);
                });
            }

            // 计算太阳视口位置
            Camera camera = cameraData.camera;
            Light sunLight = RenderSettings.sun;

            if (sunLight == null)
            {
                if (logThisFrame)
                    Debug.LogWarning("[VLS] RenderSettings.sun 为空，跳过本帧。");
                return;
            }

            Vector3 sunForward = sunLight.transform.forward;
            Vector3 toSunDir = -sunForward;
            Vector3 camForward = camera.transform.forward;

            float dotFront = Vector3.Dot(camForward, toSunDir);

            // 手动投影，safeZ 兜底避免除零跳变
            Vector3 viewDir = camera.worldToCameraMatrix.MultiplyVector(toSunDir);
            float viewZ = -viewDir.z;
            float safeZ = Mathf.Sign(viewZ) * Mathf.Max(Mathf.Abs(viewZ), 0.1f);
            float tanHalfFov = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)desc.width / (float)desc.height;
            float vpX = 0.5f + 0.5f * (viewDir.x / safeZ) / (tanHalfFov * aspect);
            float vpY = 0.5f + 0.5f * (viewDir.y / safeZ) / tanHalfFov;

            if (logThisFrame)
            {
                Debug.Log($"[VLS] Frame {frameCount}: vp=({vpX:F2},{vpY:F2}) dotFront={dotFront:F3} intensity={intensity:F3}");
            }

            // 步骤 3: Pass B - 径向模糊到临时纹理
            // 注意：不直接改 cameraColor，避免后续内置后处理拿到属性不匹配的纹理
            TextureDesc dstDesc = renderGraph.GetTextureDesc(cameraColorTarget);
            dstDesc.name = "_VolumetricLightTemp";
            dstDesc.clearBuffer = false;
            TextureHandle dstTex = renderGraph.CreateTexture(dstDesc);

            using (var builder = renderGraph.AddRasterRenderPass<RadialBlurPassData>("Radial Blur", out var blurPassData))
            {
                blurPassData.occludersTex = occludersTex;
                blurPassData.cameraColorTex = cameraColorTarget;
                blurPassData.dstTex = dstTex;
                blurPassData.material = radialBlurMaterial;
                blurPassData.center = new Vector4(vpX, vpY, 0, 0);
                blurPassData.intensity = intensity;
                blurPassData.blurWidth = blurWidth;
                blurPassData.lightColor = lightColor;
                blurPassData.falloff = falloff;
                blurPassData.isDebug = debugShowOccludersMap;

                builder.AllowGlobalStateModification(true);

                builder.UseTexture(occludersTex, AccessFlags.Read);
                builder.UseTexture(cameraColorTarget, AccessFlags.Read);
                builder.SetRenderAttachment(dstTex, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (RadialBlurPassData data, RasterGraphContext context) =>
                {
                    if (data.isDebug)
                    {
                        // 调试模式：直接把遮挡图 Blit 到 dst
                        Blitter.BlitTexture(context.cmd, data.occludersTex, new Vector4(1, 1, 0, 0), 0.0f, false);
                    }
                    else
                    {
                        data.material.SetVector(ShaderID_Center, data.center);
                        data.material.SetFloat(ShaderID_Intensity, data.intensity);
                        data.material.SetFloat(ShaderID_BlurWidth, data.blurWidth);
                        data.material.SetColor(ShaderID_LightColor, data.lightColor);
                        data.material.SetFloat(ShaderID_Falloff, data.falloff);
                        // 用 string 版 SetGlobalTexture（官方注释推荐的备选路径）
                        context.cmd.SetGlobalTexture("_CameraColorTex", data.cameraColorTex);
                        context.cmd.SetGlobalTexture("_OccludersMap", data.occludersTex);
                        CoreUtils.DrawFullScreen(context.cmd, data.material);
                    }
                });
            }

            // 步骤 4: Pass C - 把结果 Blit 回原始 cameraColorTarget
            // 关键：写入原句柄，后续内置后处理拿到的 texture 属性完全一致
            using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Copy Result Back", out var copyData))
            {
                copyData.srcTex = dstTex;
                copyData.dstTex = cameraColorTarget;
                copyData.copyMaterial = blitCopyMaterial;
                copyData.isDebug = debugShowOccludersMap;
                copyData.debugOccludersTex = occludersTex;

                builder.UseTexture(debugShowOccludersMap ? occludersTex : dstTex, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColorTarget, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext context) =>
                {
                    TextureHandle src = data.isDebug ? data.debugOccludersTex : data.srcTex;
                    Blitter.BlitTexture(context.cmd, src, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }
        }

        public void Dispose()
        {
            if (occludersMaterial != null) CoreUtils.Destroy(occludersMaterial);
            if (radialBlurMaterial != null) CoreUtils.Destroy(radialBlurMaterial);
            if (blitCopyMaterial != null) CoreUtils.Destroy(blitCopyMaterial);
        }
    }

    private LightScatteringPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new LightScatteringPass(debugLog, debugShowOccludersMap);
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Preview)
            return;

        // 提前检查 Volume，未启用时不浪费 Pass 入队
        var stack = VolumeManager.instance.stack;
        var volume = stack.GetComponent<VolumetricLightScatteringVolume>();
        if (volume == null || !volume.IsActive())
            return;

        renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (m_ScriptablePass != null)
        {
            m_ScriptablePass.Dispose();
            m_ScriptablePass = null;
        }
    }
}
