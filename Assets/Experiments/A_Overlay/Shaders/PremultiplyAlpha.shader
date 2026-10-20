// A1 실험 — DWM 합성을 위한 프리멀티플라이드 알파 변환.
//
// DWM은 창 내용이 프리멀티플라이드 알파라고 가정하고 합성한다.
//     결과 = src.rgb + dst.rgb * (1 - src.a)
// URP는 스트레이트 알파를 내놓으므로 알파가 1보다 작은 픽셀에서 src.rgb가
// 곱해지지 않은 채 더해진다 → 가산 혼합처럼 밝아진다.
//
// 그런데 어느 색공간에서 곱하는지가 중요하다. DWM은 sRGB 공간에서 합성하는데
// 프로젝트가 Linear면 이 패스의 출력은 최종 블릿에서 sRGB로 인코딩된다.
// Linear에서 그냥 곱하면
//     (rgb * a)^(1/2.2)   ≠   rgb^(1/2.2) * a
// 가 되어 알파가 작을수록 밝아진다. 곱셈과 감마 인코딩은 순서를 바꿀 수 없다.
// 그래서 sRGB로 변환해 곱한 뒤 다시 Linear로 되돌려 놓는다.
//
// FullScreenPassRendererFeature의 injection point를 AfterRenderingPostProcessing으로
// 두고 쓴다. 단 IMGUI(OnGUI)는 URP 이후에 백버퍼로 직접 그려지므로 이 패스를
// 거치지 않는다 — HUD는 보정 대상이 아니다.
Shader "Hidden/MalangLab/PremultiplyAlpha"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PremultiplyAlpha"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment Frag

            // Core.hlsl은 XR 의존성, Blit.hlsl은 Vert/Varyings/_BlitTexture를 준다.
            // URP 자체 CoreBlit.shader와 같은 순서로 넣는다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = FragBlit(input, sampler_PointClamp);

                #ifdef UNITY_COLORSPACE_GAMMA
                    // 이미 감마 공간이라 그대로 곱하면 된다.
                    return half4(c.rgb * c.a, c.a);
                #else
                    // Linear 공간이면 합성이 일어나는 sRGB 공간으로 옮겨 곱한다.
                    float3 srgb = LinearToSRGB(max(c.rgb, 0.0));
                    float3 premultiplied = srgb * c.a;
                    return half4(SRGBToLinear(premultiplied), c.a);
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
