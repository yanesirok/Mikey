// Fighter shader. URP/Lit would do everything here except the one thing the fighters actually
// need: a rim. In a scene whose whole background is pale mist, a lit edge along the silhouette
// is what separates a character from it — and separating the character from the background is
// the single most important job the renderer has in a fighting game.
//
// The rim is keyed to the main light rather than applied evenly. An even rim is a cartoon
// outline; one that brightens on the side the key comes from reads as light wrapping around
// the body, which is what it is meant to be.
Shader "Mikey/Character"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        // This character is black tactical gear whose armour diffuse has a median of 10/255 —
        // 0.003 in linear. Multiplied by any sane tint and run through ACES it lands at zero,
        // and the fighters render as glossy black cut-outs with nothing but a specular
        // highlight. Lifting the albedo's gamma opens the dark end while keeping every panel
        // and seam in the texture, which multiplying the tint by twenty would not.
        _AlbedoGamma ("Albedo Gamma", Range(0.2, 1)) = 0.45
        _BumpScale ("Normal Scale", Range(0, 3)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.18
        _SpecStrength ("Specular Strength", Range(0, 3)) = 0.5
        _RimColor ("Rim Colour", Color) = (0.85, 0.92, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 12)) = 3.5
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _RimColor;
                float  _AlbedoGamma;
                float  _BumpScale;
                float  _Smoothness;
                float  _SpecStrength;
                float  _RimPower;
                float  _RimStrength;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionWS = positions.positionWS;
                o.positionCS = positions.positionCS;
                o.normalWS = normals.normalWS;
                o.tangentWS = float4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 bitangentWS = cross(normalWS, input.tangentWS.xyz) * input.tangentWS.w;
                float3x3 tangentToWorld = float3x3(input.tangentWS.xyz, bitangentWS, normalWS);

                float3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 shadingNormal = normalize(mul(normalTS, tangentToWorld));

                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float3 albedo = pow(saturate(sampled), _AlbedoGamma) * _BaseColor.rgb;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float shadow = lerp(1.0, mainLight.shadowAttenuation, 0.9);
                float diffuse = saturate(dot(shadingNormal, mainLight.direction) * 0.7 + 0.3) * shadow;

                float3 halfWS = normalize(mainLight.direction + viewWS);
                float specPower = exp2(_Smoothness * 10.0 + 1.0);
                float3 specular = mainLight.color * (_SpecStrength * _Smoothness * shadow
                                * pow(saturate(dot(shadingNormal, halfWS)), specPower));

                float3 color = albedo * (mainLight.color * diffuse + SampleSH(shadingNormal)) + specular;

                #ifdef _ADDITIONAL_LIGHTS
                    uint count = GetAdditionalLightsCount();
                    for (uint i = 0u; i < count; i++)
                    {
                        Light light = GetAdditionalLight(i, input.positionWS);
                        float wrap = saturate(dot(shadingNormal, light.direction) * 0.7 + 0.3);
                        color += albedo * light.color * wrap * light.distanceAttenuation;
                    }
                #endif

                // Rim. The geometric normal, not the mapped one — a normal map would break the
                // silhouette band into speckle, and the band is the entire point.
                float rim = pow(1.0 - saturate(dot(normalWS, viewWS)), _RimPower);
                // Only where the key can reach: a rim on the shadow side is a light with no
                // source, and it flattens the form instead of describing it.
                float keyed = saturate(dot(normalWS, -mainLight.direction) * 0.5 + 0.5);
                color += _RimColor.rgb * (rim * keyed * _RimStrength);

                color = MixFog(color, input.fogCoord);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    Fallback "Universal Render Pipeline/Lit"
}
