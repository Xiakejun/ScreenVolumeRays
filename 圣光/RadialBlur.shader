Shader "Hidden/RW/RadialBlur"
{
    Properties
    {
        _BlurWidth("Blur Width", Range(0,1)) = 0.85
        _Intensity("Intensity", Range(0,1)) = 1
        _Center("Center", Vector) = (0.5,0.5,0,0)
        _LightColor("Light Color", Color) = (1,1,1,1)
        _Falloff("Falloff", Range(0,50)) = 20
    }
    SubShader
    {
        // 全屏绘制必需的 Render State：
        // - ZWrite Off：不写深度
        // - ZTest Always：永远通过深度测试
        // - Cull Off：双面绘制
        // 不再使用 Blend One One：改为在 Frag 中手动加法（cameraColor + radialResult）
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "RadialBlur"
            Tags { "RenderPipeline" = "UniversalPipeline" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define NUM_SAMPLES 100

            // 两个纹理输入：相机颜色 + 遮挡图
            // 注意：sampler_LinearClamp 已在 URP Core.hlsl 中全局声明，此处禁止重复 SAMPLER(...)
            TEXTURE2D_X(_CameraColorTex);
            TEXTURE2D_X(_OccludersMap);

            float _BlurWidth;
            float _Intensity;
            float4 _Center;
            float4 _LightColor;
            float _Falloff;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            // 自写全屏三角形顶点着色器（不再依赖 Blit.hlsl）
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.texcoord = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 纹理读取前先快速验证 Shader 是否执行：
                // 如果下方注释打开后看到全屏红，说明 Shader 执行了但纹理读取失败
                // 如果还是灰黑，说明 RenderAttachment/DrawFullScreen 本身有问题
                // return half4(1, 0, 0, 1);

                // 采样相机颜色（作为加法的基础）
                half3 cameraColor = SAMPLE_TEXTURE2D_X(_CameraColorTex, sampler_LinearClamp, input.texcoord).rgb;

                // 径向模糊采样遮挡图，只取 R 通道作为遮挡权重
                half weightSum = 0;
                float2 ray = input.texcoord - _Center.xy;

                [loop]
                for (int i = 0; i < NUM_SAMPLES; i++)
                {
                    float scale = 1.0 - _BlurWidth * (float(i) / float(NUM_SAMPLES - 1));
                    float2 sampleUV = (ray * scale) + _Center.xy;
                    // 屏幕外的采样点视为无遮挡（返回1.0），避免 clamp 到边缘黑色物体导致整体偏暗
                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                        weightSum += 1.0;
                    else
                        weightSum += SAMPLE_TEXTURE2D_X(_OccludersMap, sampler_LinearClamp, sampleUV).r;
                }

                half weight = weightSum / float(NUM_SAMPLES);
                // 距离衰减：离太阳中心越远，体积光越弱
                float distToCenter = length(ray);
                half distAtten = 1.0 / (1.0 + distToCenter * distToCenter * _Falloff);
                half3 radialResult = weight * distAtten * _LightColor.rgb * _Intensity;

                // 手动加法替代 Blend One One
                return half4(cameraColor + radialResult, 1.0);
            }
            ENDHLSL
        }
    }
}
