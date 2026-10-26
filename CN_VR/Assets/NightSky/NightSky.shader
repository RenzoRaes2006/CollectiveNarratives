// Procedural night skybox: gradient sky, twinkling stars and a moon.
// No textures needed; works with URP and single-pass instanced VR.
Shader "Skybox/Night Sky"
{
    Properties
    {
        [Header(Sky)]
        _TopColor ("Top Color", Color) = (0.005, 0.01, 0.04, 1)
        _HorizonColor ("Horizon Color", Color) = (0.03, 0.05, 0.12, 1)
        _GroundColor ("Ground Color", Color) = (0.01, 0.01, 0.015, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5, 10)) = 2.5

        [Header(Stars)]
        _StarDensity ("Star Density", Range(50, 600)) = 250
        _StarAmount ("Star Amount", Range(0, 1)) = 0.12
        _StarSize ("Star Size", Range(0.01, 0.4)) = 0.12
        _StarBrightness ("Star Brightness", Range(0, 10)) = 3
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 2

        [Header(Moon)]
        _MoonDirection ("Moon Direction", Vector) = (0.4, 0.45, 0.8, 0)
        _MoonColor ("Moon Color", Color) = (1, 0.97, 0.88, 1)
        _MoonSize ("Moon Size", Range(0.005, 0.1)) = 0.03
        _MoonGlow ("Moon Glow", Range(0, 2)) = 0.35

        _Exposure ("Exposure", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _TopColor, _HorizonColor, _GroundColor, _MoonColor, _MoonDirection;
            float _HorizonSharpness, _StarDensity, _StarAmount, _StarSize, _StarBrightness, _TwinkleSpeed;
            float _MoonSize, _MoonGlow, _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash13 (float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 hash33 (float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float stars (float3 dir)
            {
                float3 p = dir * _StarDensity;
                float3 cell = floor(p);
                float3 local = frac(p);

                float rnd = hash13(cell);
                if (rnd > _StarAmount) return 0;

                float3 starPos = 0.2 + hash33(cell) * 0.6;
                float d = length(local - starPos);
                float star = saturate(1 - d / _StarSize);
                star = star * star * star;

                float twinkle = 0.6 + 0.4 * sin(_Time.y * _TwinkleSpeed * (0.5 + rnd * 8) + rnd * 100);
                return star * twinkle * (0.3 + 0.7 * (rnd / max(_StarAmount, 1e-4)));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 dir = normalize(i.dir);
                float h = dir.y;

                float3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(h), 1 / _HorizonSharpness));
                float3 col = h >= 0 ? sky : lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-h * 8));

                // Stars fade out near the horizon
                col += stars(dir) * _StarBrightness * saturate(h * 4);

                float3 moonDir = normalize(_MoonDirection.xyz);
                float moonDot = dot(dir, moonDir);
                float moonDisc = smoothstep(1 - _MoonSize, 1 - _MoonSize * 0.85, moonDot);
                float moonHalo = pow(saturate(moonDot), 200) * _MoonGlow;
                col = lerp(col, _MoonColor.rgb * 1.5, moonDisc);
                col += _MoonColor.rgb * moonHalo;

                return fixed4(col * _Exposure, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
