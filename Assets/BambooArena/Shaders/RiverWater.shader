// River surface: planar reflection + scrolling ripple normals + duckweed mats.
//
// Unlit on purpose. Still water is essentially a mirror, so almost everything the eye reads
// comes from the reflection texture and the Fresnel falloff, not from diffuse shading. That
// keeps the fragment cost low enough for mobile.
//
// The duckweed mask comes from the mesh's vertex colour red channel, which is baked to
// "how close is this vertex to the bank", so weed grows at the edges and open water stays
// clear. That follows the geometry, so widening the river does not need the mask redone.

Shader "BambooArena/RiverWater"
{
    Properties
    {
        _DeepColor      ("Deep water colour", Color) = (0.031, 0.045, 0.030, 1)
        _ReflectionTex  ("Reflection (set by script)", 2D) = "black" {}
        _RippleMap      ("Ripple normal map", 2D) = "bump" {}
        _DuckweedTex    ("Duckweed albedo", 2D) = "black" {}

        _RippleScale    ("Ripple world scale", Float) = 0.11
        _RippleSpeed    ("Ripple speed", Float) = 0.012
        _RippleStrength ("Ripple normal strength", Range(0,3)) = 0.55
        _Distort        ("Reflection distortion", Range(0,0.08)) = 0.014

        _FresnelPower   ("Fresnel power", Range(0.5,8)) = 3.4
        _FresnelBias    ("Fresnel bias", Range(0,1)) = 0.08
        _ReflStrength   ("Reflection strength", Range(0,1)) = 0.92
        _ReflTint       ("Reflection tint", Color) = (1,1,1,1)

        _WeedTiling     ("Duckweed tiling (per metre)", Float) = 0.32
        _WeedAmount     ("Duckweed amount", Range(0,2)) = 1.0
        _WeedTint       ("Duckweed tint", Color) = (1,1,1,1)

        _ShadowTint     ("Colour of water in shadow", Color) = (0.42, 0.50, 0.58, 1)
        _SunSpecPower   ("Sun glitter tightness", Range(16,1024)) = 260
        _SunSpecStrength("Sun glitter strength", Range(0,8)) = 2.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Cull Back
        ZWrite On

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0

            // the surface is unlit, but it still has to be *shadowed*: a mirror in shade
            // reads dark, and without these keywords the culm shadows simply stop at the bank
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            TEXTURE2D(_RippleMap);     SAMPLER(sampler_RippleMap);
            TEXTURE2D(_DuckweedTex);   SAMPLER(sampler_DuckweedTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ReflTint;
                float4 _WeedTint;
                float4 _ShadowTint;
                float  _SunSpecPower;
                float  _SunSpecStrength;
                float  _RippleScale;
                float  _RippleSpeed;
                float  _RippleStrength;
                float  _Distort;
                float  _FresnelPower;
                float  _FresnelBias;
                float  _ReflStrength;
                float  _WeedTiling;
                float  _WeedAmount;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.screenPos  = ComputeScreenPos(p.positionCS);
                OUT.color      = IN.color;
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // two ripple layers drifting against each other so the surface never
                // looks like a single sliding texture
                float2 wxz = IN.positionWS.xz * _RippleScale;
                float2 uv1 = wxz + float2( 1.0,  0.35) * _Time.y * _RippleSpeed;
                float2 uv2 = wxz * 1.63 + float2(-0.42, 0.87) * _Time.y * _RippleSpeed * 0.77;

                float3 n1 = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv1).xyz * 2.0 - 1.0;
                float3 n2 = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv2).xyz * 2.0 - 1.0;
                float2 slope = (n1.xy + n2.xy) * 0.5 * _RippleStrength;

                float3 N = normalize(float3(slope.x, 1.0, slope.y));
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // reflection is sampled in screen space, nudged by the ripple slope
                float2 sUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                sUV += slope * _Distort;
                half3 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, saturate(sUV)).rgb;
                refl *= _ReflTint.rgb;

                float fres = _FresnelBias + (1.0 - _FresnelBias) *
                             pow(saturate(1.0 - saturate(dot(N, V))), _FresnelPower);

                half3 col = lerp(_DeepColor.rgb, refl, saturate(fres * _ReflStrength));

                // duckweed: tiles by world position, gated by the baked bank-distance in vertex red
                float2 wuv = IN.positionWS.xz * _WeedTiling;
                half4 weed = SAMPLE_TEXTURE2D(_DuckweedTex, sampler_DuckweedTex, wuv);
                float wmask = saturate(weed.a * IN.color.r * _WeedAmount);
                col = lerp(col, weed.rgb * _WeedTint.rgb, wmask);

                // main-light shadow: this is what puts the culm silhouettes onto the river
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation;
                col *= lerp(_ShadowTint.rgb, half3(1.0, 1.0, 1.0), shadow);

                // glancing sun glitter, killed inside the shadow so the silhouettes stay clean
                float3 Hv = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, Hv)), _SunSpecPower) * _SunSpecStrength;
                col += mainLight.color * spec * shadow;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Without this pass the water contributes nothing to URP's depth prepass. With depth
        // priming on, the forward pass then runs at ZTest Equal, no water pixel ever matches, and
        // the river vanishes with the terrain showing through. Writing depth here is what lets
        // priming be switched on, which is worth roughly a threefold cut in shading work.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVaryings   { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            DepthVaryings DepthOnlyVertex (DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                // must match the forward pass exactly, or ZTest Equal rejects the surface again
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                return OUT;
            }

            half4 DepthOnlyFragment (DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Same reason, for the depth-normals prepass URP uses when screen-space effects are on.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DNAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DNVaryings   { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            DNVaryings DepthNormalsVertex (DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = GetVertexPositionInputs(IN.positionOS.xyz).positionCS;
                return OUT;
            }

            half4 DepthNormalsFragment (DNVaryings IN) : SV_Target
            {
                return half4(0.0, 1.0, 0.0, 0.0);   // the surface is flat, its normal is straight up
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
