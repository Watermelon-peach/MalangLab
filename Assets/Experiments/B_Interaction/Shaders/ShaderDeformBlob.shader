// B1 후보 ② — 셰이더 정점 변형.
//
// CPU 물리 없이 정점 셰이더에서만 민다. 스프링 상태는 C# 쪽에서 값 몇 개로만
// 들고 있고(잡은 지점, 당긴 방향, 출렁임 위상), 셰이더는 그걸 받아 모양을 만든다.
//
// 메시 스프링과의 차이:
//   - 훨씬 가볍다. 정점 수가 늘어도 CPU 비용이 안 늘어난다
//   - 충돌 반응이 없다. 꾹 눌러 움푹 들어가는 건 흉내만 낸다
//   - 변형이 수식이라 "예측 가능한 모양"만 나온다
// A4(대기 성능)를 생각하면 이쪽이 매력적이라 비교 대상에 넣었다.
Shader "MalangLab/ShaderDeformBlob"
{
    Properties
    {
        _BaseColor      ("Base Color", Color) = (1, 0.55, 0.62, 1)
        _Smoothness     ("Smoothness", Range(0,1)) = 0.5

        _GrabPoint      ("Grab Point (object)", Vector) = (0,0,0,0)
        _GrabVector     ("Grab Vector (object)", Vector) = (0,0,0,0)
        _GrabRadius     ("Grab Radius", Float) = 0.55
        _GrabAmount     ("Grab Amount", Range(0,1)) = 0
        _BodyFollow     ("Body Follow", Range(0,1)) = 0.35
        _GrabPoint2     ("Residual Point (object)", Vector) = (0,0,0,0)
        _GrabVector2    ("Residual Vector (object)", Vector) = (0,0,0,0)

        _WobbleAxis     ("Wobble Axis (object)", Vector) = (0,1,0,0)
        _WobbleAmount   ("Wobble Amount", Float) = 0
        _WobblePhase    ("Wobble Phase", Float) = 0
        _WobbleFreq     ("Wobble Frequency", Float) = 12

        _SquashAmount   ("Squash Amount", Float) = 0

        _ContactPlane0  ("Contact Plane 0 (object: xyz=n, w=dist)", Vector) = (0,1,0,-1000000)
        _ContactPlane1  ("Contact Plane 1 (object: xyz=n, w=dist)", Vector) = (0,1,0,-1000000)
        _ContactBulge   ("Contact Bulge", Range(0,1.5)) = 0.9
        _ContactDepth   ("Contact Depth (x=plane0, y=plane1)", Vector) = (0,0,0,0)
        _ContactFalloff ("Contact Falloff", Range(0.05,2)) = 0.9
        _ContactSoft    ("Contact Softness", Range(0.001,0.5)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Smoothness;
                float4 _GrabPoint;
                float4 _GrabVector;
                float  _GrabRadius;
                float  _GrabAmount;
                float  _BodyFollow;
                float4 _GrabPoint2;
                float4 _GrabVector2;
                float4 _WobbleAxis;
                float  _WobbleAmount;
                float  _WobblePhase;
                float  _WobbleFreq;
                float  _SquashAmount;
                float4 _ContactPlane0;
                float4 _ContactPlane1;
                float  _ContactBulge;
                float4 _ContactDepth;
                float  _ContactFalloff;
                float  _ContactSoft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;   // 얼굴을 같은 메시에 넣기 위한 것 (B3)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color      : TEXCOORD2;
            };

            // 한 지점을 중심으로 당긴다.
            //
            // falloff를 제곱하면 가운데로 몰려 점으로 꼬집히는 모양이 되므로
            // smoothstep을 쓴다. 거기에 _BodyFollow만큼은 몸 전체가 따라간다.
            //
            // 이걸 두 번 적용한다(현재 잡기 + 직전 잡기의 잔여). 슬롯이 하나면
            // 변형이 남아 있는 중에 새로 잡을 때 이전 변형을 버려야 해서 툭 끊긴다.
            float3 ApplyPull(float3 p, float3 anchor, float3 vec, float amount)
            {
                float d = distance(p, anchor);
                float t = saturate(1.0 - d / max(_GrabRadius, 1e-4));
                float falloff = t * t * (3.0 - 2.0 * t);
                float weight = lerp(_BodyFollow, 1.0, falloff);
                return p + vec * (weight * amount);
            }

            // 벽에 눌린다. 평면은 안쪽이 양수인 표현이고, 벽 너머로 나간 정점을 민다.
            //
            // 벽면까지 밀어내면 접촉면이 평평해진다. 그것만 하면 칼로 자른 것처럼
            // 보이므로, 눌린 만큼 벽을 따라 옆으로 퍼뜨려 부피를 대충 유지한다.
            // 실제 말랑한 걸 벽에 누르면 닿은 면이 넓어지는 그 모양이다.
            //
            // 퍼지는 양은 몸 전체가 얼마나 눌렸는가(depth) 하나로 정한다.
            // 정점마다 자기 깊이로 퍼뜨렸더니 이웃한 정점이 서로 다른 배율로
            // 밀려나 접촉 테두리가 조개껍데기처럼 들쭉날쭉해졌다.
            // 벽에서 멀어질수록 부드럽게 줄여야 테두리가 매끈하다.
            // 벽면까지 "딱 잘라" 밀면 접촉 테두리에 꺾임이 생긴다. 이 셰이더는
            // 노멀을 이웃 두 점의 변형 결과로 다시 구하므로, 그 꺾임에서 이웃이
            // 벽 위냐 아래냐에 따라 노멀이 튀어 테두리가 조개껍데기처럼 보였다.
            // 부드럽게 수렴하는 함수로 밀면 꺾임 자체가 없어진다.
            // k가 0에 가까울수록 max(sd, 0)에 가깝고, 크면 접촉면이 둥글어진다.
            float SoftAbove(float sd, float k)
            {
                return 0.5 * (sd + sqrt(sd * sd + k * k));
            }

            float3 ApplyContact(float3 p, float4 plane, float depth, float bulge)
            {
                float sd = dot(p, plane.xyz) - plane.w;
                if (sd >= _ContactFalloff) return p;

                float t = saturate(1.0 - sd / _ContactFalloff);
                float w = t * t * (3.0 - 2.0 * t);          // 가장자리에서만 떨어지게
                float3 lateral = p - plane.xyz * dot(p, plane.xyz);
                p += lateral * (depth * bulge * w);

                // 부드럽게 벽면까지. k/2만큼 떠서 멎으므로 그만큼 미리 당겨둔다.
                sd = dot(p, plane.xyz) - plane.w + _ContactSoft * 0.5;
                return p + plane.xyz * (SoftAbove(sd, _ContactSoft) - sd);
            }

            // 정점 하나를 변형한다. 노멀을 다시 구하려고 이웃 두 점에도 같은 걸
            // 적용해야 하므로 함수로 뺐다.
            float3 Deform(float3 p)
            {
                // 1) 잡아당기기. 슬롯이 둘인 이유는 아래 ApplyPull 주석 참고.
                p = ApplyPull(p, _GrabPoint.xyz,  _GrabVector.xyz,  _GrabAmount);
                p = ApplyPull(p, _GrabPoint2.xyz, _GrabVector2.xyz, 1.0);

                // 2) 출렁임 — 놓은 뒤 축을 따라 사인파로 진동한다.
                //    축 방향 위치에 따라 위상을 틀어 파도처럼 번지게 한다.
                float along = dot(p, normalize(_WobbleAxis.xyz + 1e-6));
                float wave = sin(along * 2.2 + _WobblePhase * _WobbleFreq);
                p += normalize(_WobbleAxis.xyz + 1e-6) * (wave * _WobbleAmount);

                // 3) 눌림 — 세로로 눌리면 가로로 퍼진다. 부피를 대충 유지한다.
                float s = _SquashAmount;
                p.y *= (1.0 - s);
                p.xz *= (1.0 + s * 0.5);

                // 4) 벽에 눌리기. 맨 마지막에 해야 접촉면이 평평하게 남는다.
                //    옆으로 퍼진 결과가 다른 벽을 뚫을 수 있으므로(모서리),
                //    퍼뜨린 뒤 한 번 더 평평하게 눌러 마무리한다.
                p = ApplyContact(p, _ContactPlane0, _ContactDepth.x, _ContactBulge);
                p = ApplyContact(p, _ContactPlane1, _ContactDepth.y, _ContactBulge);

                return p;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;

                float3 p = Deform(input.positionOS.xyz);

                // 변형 후 노멀을 다시 구한다. 안 하면 조명이 원래 구 모양으로
                // 남아서 아무리 찌그러뜨려도 입체감이 안 따라온다.
                float3 t1 = normalize(abs(input.normalOS.y) < 0.99
                    ? cross(input.normalOS, float3(0,1,0))
                    : cross(input.normalOS, float3(1,0,0)));
                float3 t2 = cross(input.normalOS, t1);

                const float eps = 0.01;
                float3 pa = Deform(input.positionOS.xyz + t1 * eps);
                float3 pb = Deform(input.positionOS.xyz + t2 * eps);
                float3 nOS = normalize(cross(pa - p, pb - p));
                // 바깥을 향하도록 뒤집어 준다.
                if (dot(nOS, input.normalOS) < 0) nOS = -nOS;

                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(nOS);
                o.color      = input.color;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.positionWS;
                lightingInput.normalWS = normalize(input.normalWS);
                lightingInput.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lightingInput.shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                SurfaceData surface = (SurfaceData)0;
                // 얼굴을 몸과 같은 메시에 넣는 방법. 정점이 변형되면 얼굴도
                // 같이 변형된다 — 따로 띄우는 스프라이트로는 안 되는 것.
                // (실제로는 정점 색 대신 텍스처를 쓴다. 정점 색은 메시 해상도만큼만 나온다.)
                surface.albedo = _BaseColor.rgb * input.color.rgb;
                surface.alpha = 1.0;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1.0;

                return UniversalFragmentPBR(lightingInput, surface);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
