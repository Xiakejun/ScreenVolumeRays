using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[VolumeComponentMenu("Custom/Volumetric Light Scattering")]
[VolumeRequiresRendererFeatures(typeof(VolumetricLightScattering))]
public class VolumetricLightScatteringVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("遮挡图分辨率缩放，值越低性能越好但质量越低")]
    public ClampedFloatParameter resolutionScale = new ClampedFloatParameter(0.5f, 0.1f, 1f);

    [Tooltip("体积光整体强度。注意：必须在 Volume Override 中勾选左侧启用框（overrideState）才生效")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(1.0f, 0f, 1f);

    [Tooltip("径向模糊宽度，控制光线散射范围")]
    public ClampedFloatParameter blurWidth = new ClampedFloatParameter(0.85f, 0f, 1f);

    [Tooltip("体积光颜色（HDR，RGB 可超 1.0 控制最大亮度），独立于场景光源")]
    public ColorParameter lightColor = new ColorParameter(new Color(1.0f, 0.95f, 0.85f, 1.0f));

    [Tooltip("距离衰减强度：值越大，光越集中在太阳附近；0=无衰减（铺满全屏，可能像滤镜）")]
    public ClampedFloatParameter falloff = new ClampedFloatParameter(5.0f, 0f, 50f);

    /// <summary>
    /// 关键：必须同时满足 ① Volume Override 上 intensity 勾选了启用（overrideState=true）
    /// ② 勾选后的值 > 0，才算真正启用。否则 stack.GetComponent 会返回默认实例，
    /// 导致永远用默认参数而读不到用户在 Inspector 里实际配置的值。
    /// </summary>
    public bool IsActive() => active && intensity.overrideState && intensity.value > 0f;
}

