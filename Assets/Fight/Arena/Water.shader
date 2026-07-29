// Still, dark forest water seen from a fixed side camera at a 5-10° grazing angle.
//
// That angle decides everything. Fresnel puts 60-90% of what you see into reflection, so there
// is nothing worth showing through the surface: no caustics, no refraction, no depth fade of a
// bottom nobody can see. All of the effort goes into the reflection and into the surface not
// being uniform.
//
// The ripples are analytic — three summed sine layers whose derivative *is* the normal. No
// normal maps: on a plane 80 by 54 units three tiling maps would show their repeat, and this
// costs nine cosines with no texture fetch. The one texture here is a tiling noise map, used
// twice at different scales: large and slow for wind gusts, mid-frequency for foam.
//
// The reflection is a real planar pass (see WaterReflection) rather than a baked texture. The
// camera pans along the fight line and dollies between 6 and 8.5 units, and a baked screen-space
// reflection slides against the scene the moment either happens.
Shader "Mikey/Water"
{
    Properties
    {
        // The two ends of the jade palette. Everything between them is the formula's job, not a
        // gradient texture's: a texture would be right at one depth and wrong at every other, and
        // wrong in particular at the shore and under the bridge, which is where the eye checks it.
        _DeepColor ("Deep Water", Color) = (0.002, 0.050, 0.061, 1)
        _ShallowColor ("Shallow Water", Color) = (0.274, 0.808, 0.715, 1)
        // Per metre, per channel. Red is absorbed seven times faster than green — that ratio is
        // why deep water is green, and it is not a stylistic choice.
        _Absorption ("Absorption per metre", Vector) = (1.15, 0.16, 0.28, 0)
        _ScatterDensity ("Scatter Density", Range(0.05, 2)) = 0.45
        _BedTint ("Bed Tint", Color) = (0.55, 0.50, 0.45, 1)
        // Tilts the normal toward the camera for the Fresnel term only. 0.22 is a 12° lie. Without
        // it a 5-10° camera sees 60-90% reflection and there is no body left to look at.
        _FresnelTilt ("Fresnel Tilt", Range(0, 0.5)) = 0.22
        _SkyColor ("Reflected Sky", Color) = (0.784, 0.824, 0.800, 1)
        _FoamColor ("Foam Colour", Color) = (0.769, 0.812, 0.788, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4.5
        _FresnelBias ("Fresnel Bias", Range(0, 0.3)) = 0.03
        _RippleStrength ("Ripple Strength", Range(0, 4)) = 0.9
        _RippleScale ("Ripple Scale", Range(0.2, 4)) = 1.4
        _RippleSpeed ("Ripple Speed", Range(0.05, 2)) = 0.45
        _WaveAmp ("Vertex Wave", Range(0, 0.2)) = 0.02
        _Glint ("Sun Glint", Range(0, 8)) = 2.0
        _GlintSharpness ("Glint Sharpness", Range(32, 1024)) = 380
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.9
        _ReflectionDistortion ("Reflection Distortion", Range(0, 0.3)) = 0.022
        _GustScale ("Gust Scale", Range(0.002, 0.1)) = 0.012
        // Downstream, in world XZ. The channel runs along Z and the camera looks down it, so
        // (0,1) is both "away from the camera" and "along the river" — they are the same axis
        // here by construction, not by coincidence.
        _FlowDir ("Flow Direction (world XZ)", Vector) = (0, 1, 0, 0)
        // Only a fallback for a freshly created material. The value that actually runs is written
        // by FightSceneSetup, which owns it alongside the speed of everything floating on top.
        _FlowSpeed ("Flow Speed (m/s)", Range(0, 3)) = 0.5
        _FoamCutoff ("Foam Cutoff", Range(0, 1)) = 0.42
        _NoiseMap ("Noise (R foam, G gust, B caustics)", 2D) = "gray" {}
        _BedMap ("Riverbed (the bank's own map)", 2D) = "grey" {}
        // Written from BambooArena.BankUvScale. Not a free-standing number: the bank projects its
        // UVs at exactly this scale, and the bed is the bank continuing under the water.
        _BedUvScale ("Bed UV Scale", Range(0.05, 1)) = 0.25
        _RefractStrength ("Refraction Strength", Range(0, 0.2)) = 0.035
        // A material property defaulting to black rather than a global, so that when nothing
        // has written it — edit mode, or before the reflection camera's first frame — the
        // sampler resolves to black and the water falls back to its own sky colour. An unbound
        // global resolves to *white* on some platforms, flooding the surface with a reflection.
        [HideInInspector] _ReflectionTex ("Reflection", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

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
            // The water has never sampled a shadow. It could get away with that while the surface
            // was a mirror — a mirror does not care what light falls on it. A lit riverbed does,
            // and the deck is directly over the middle of it.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                // r: foam mask baked at the piles and the shoreline
                // g: metres of water above the bed, baked from BambooArena.BedDepthAt
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  foam       : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                float  bedDepth   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _Absorption;
                float4 _BedTint;
                float  _ScatterDensity;
                float  _FresnelTilt;
                float  _BedUvScale;
                float  _RefractStrength;
                float4 _SkyColor;
                float4 _FoamColor;
                float4 _NoiseMap_ST;
                float  _FresnelPower;
                float  _FresnelBias;
                float  _RippleStrength;
                float  _RippleScale;
                float  _RippleSpeed;
                float  _WaveAmp;
                float  _Glint;
                float  _GlintSharpness;
                float  _ReflectionStrength;
                float  _ReflectionDistortion;
                float  _GustScale;
                float  _FoamCutoff;
                float4 _FlowDir;
                float  _FlowSpeed;
            CBUFFER_END

            // Where the surface field is sampled: the river carries the whole pattern downstream,
            // so every layer of it — chop, gusts, foam — reads from one advected coordinate
            // rather than drifting on three unrelated timers of its own.
            //
            // Sampling at p - flow*t also gives each wave layer a phase term dot(d, -flow)*k*t.
            // That is not a side effect to be corrected: it is the Doppler shift a current puts
            // on wind chop, speeding up the layers that run with it and slowing those against.
            float2 Advect(float2 p)
            {
                float2 dir = _FlowDir.xy / max(length(_FlowDir.xy), 1e-5);
                return p - dir * (_FlowSpeed * _Time.y);
            }

            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            TEXTURE2D(_NoiseMap);      SAMPLER(sampler_NoiseMap);
            TEXTURE2D(_BedMap);        SAMPLER(sampler_BedMap);

            // Three directional waves. Returns the surface slope in x and z; the height they
            // describe is only needed in the vertex stage, hence the split.
            //
            // Each layer above the coarsest fades out with distance. A ripple layer is only
            // detail for as long as its wavelength covers more than a few pixels; past that it
            // is aliasing, and aliasing on water does not read as fine ripples, it reads as the
            // whole surface crawling and flickering. At a 5-10° view angle the compression with
            // distance is brutal — the 0.46 m layer is under a pixel by fifteen units out — and
            // with the reflection distortion stretched 3.5× vertically on top, that flicker
            // became a hard vertical judder in the reflection.
            float2 Slope(float2 p, float t, float viewDist)
            {
                p *= _RippleScale;
                float2 d1 = normalize(float2(1.00, 0.42));
                float2 d2 = normalize(float2(-0.55, 1.00));
                float2 d3 = normalize(float2(0.85, -0.75));

                float mid = 1.0 - smoothstep(12.0, 26.0, viewDist);  // 1.04 m layer
                float fine = 1.0 - smoothstep(6.0, 14.0, viewDist);  // 0.46 m layer

                t *= _RippleSpeed;
                float2 s = 0;
                s += d1 * (0.030 * 1.9 * cos(dot(d1, p) * 1.9 + t * 0.65));
                s += d2 * (0.016 * 4.3 * cos(dot(d2, p) * 4.3 + t * 1.05)) * mid;
                s += d3 * (0.006 * 9.7 * cos(dot(d3, p) * 9.7 + t * 1.75)) * fine;
                return s * _RippleScale;
            }

            float Height(float2 p, float t)
            {
                float2 d1 = normalize(float2(1.00, 0.42));
                return sin(dot(d1, p * _RippleScale) * 1.9 + t * _RippleSpeed * 0.65);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Advected here too, or the geometric wave drifts apart from the normals the
                // fragment stage derives — the crest stops matching the shading on it.
                positionWS.y += Height(Advect(positionWS.xz), _Time.y) * _WaveAmp;

                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.foam = input.color.r;
                o.bedDepth = input.color.g;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y;

                // Gusts. One very large, very slow noise sample decides where the surface is
                // glassy and where it is ruffled. This is the single thing that separates a
                // water shader from water: a plane rippling evenly everywhere reads as a
                // material, and real still water is always patchy with wind.
                float viewDist = distance(_WorldSpaceCameraPos, input.positionWS);

                // One advected coordinate for the whole surface. The gust layer used to scroll
                // on its own at (0.006, 0.002) and the foam on two more timers again; three
                // unrelated drifts read as a material, one shared drift reads as a medium.
                float2 q = Advect(input.positionWS.xz);

                float gust = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, q * _GustScale).g;
                gust = smoothstep(0.35, 0.75, gust);

                float2 slope = Slope(q, time, viewDist)
                             * _RippleStrength * lerp(0.3, 1.0, gust);
                float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Path length through the water, along the *unrefracted* view ray.
                //
                // Real refraction squeezes everything into Snell's window: an 85° incidence leaves
                // the surface at 42° to it and the path comes out 1.05 m, against 0.92 m at 30°.
                // Physically honest, and it means no gradient anywhere in the frame — a 70 cm
                // channel would be one flat colour from the deck to the mist.
                //
                // Taken along the view ray instead, the same channel spans 1.4 m at the bottom of
                // the frame and 7.8 m at the far clamp: exactly the 1-8 m range the reference
                // palette is drawn over. The grazing angle that killed refraction is what pays for
                // the depth gradient. The clamp is 5°, past which fog owns the pixel anyway.
                float h = input.bedDepth;
                float L = h / max(viewWS.y, 0.09);

                // Where the ray actually lands on the bed, by Snell — and this one is *not*
                // cheated, unlike the path length above.
                //
                // The two want different things. The path length is a scalar feeding an
                // exponential; all it needs is a gradient, and the long ray supplies one. This is
                // a position on the ground. Computed from the same long ray it would swing metres
                // as the camera pans along the fight line, and the riverbed would swim under the
                // surface. Snell caps the offset inside a 48.6° cone, so it never exceeds 1.13*h —
                // eighty centimetres at the deepest point of the channel.
                float  sinI  = saturate(length(viewWS.xz));
                float  sinT  = sinI / 1.333;
                float2 bedUV = input.positionWS.xz
                             - normalize(viewWS.xz + 1e-6) * (h * sinT * rsqrt(max(1.0 - sinT * sinT, 1e-4)));
                // Ripple wobble. No shallow-water damping term is needed: the offset is already
                // proportional to h, and h goes to zero at the shore on its own.
                bedUV += slope * _RefractStrength;

                // The bed is lit, not raw albedo. The bank carries the same texture and the same
                // tint and is lit by the same sun, and at the waterline the two are the same
                // surface a centimetre apart — an unlit bed puts a dark band along every metre of
                // shoreline. The bed is flat enough that its normal is up everywhere; using a
                // constant one costs nothing and saves reconstructing a gradient of Ground().
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 bedLight = SampleSH(float3(0, 1, 0))
                                + mainLight.color * saturate(mainLight.direction.y)
                                                  * mainLight.shadowAttenuation;
                float3 bed = SAMPLE_TEXTURE2D(_BedMap, sampler_BedMap, bedUV * _BedUvScale).rgb
                           * _BedTint.rgb * bedLight;
                float3 transmittance = exp(-_Absorption.rgb * L);
                // Scatter that fades along the path as well as accumulating along it. Without the
                // lerp the colour converges on the scatter tint, so distance makes the water
                // *brighter* and more saturated — the opposite of every reference photograph, and
                // the one real error in the guide this was built from.
                float scatter = 1.0 - exp(-_ScatterDensity * L);
                float3 body = bed * transmittance
                            + lerp(_ShallowColor.rgb, _DeepColor.rgb, scatter) * scatter;

                // Grazing angle -> sky, steep angle -> the body of the water. The bias keeps a
                // little reflection even looking straight down, which is what real water does.
                //
                // The normal is tilted toward the camera first, and that lie is told once, here,
                // and nowhere else: the reflection is sampled and the glint is lit with the real
                // normal, or the whole reflected grove slides up the screen along with it.
                float3 fresnelN = normalize(normalWS + viewWS * _FresnelTilt);
                float fresnel = _FresnelBias
                              + (1.0 - _FresnelBias) * pow(1.0 - saturate(dot(fresnelN, viewWS)), _FresnelPower);

                float3 sky = _SkyColor.rgb * (0.85 + 0.15 * normalWS.y);
                float3 color = lerp(body, sky, saturate(fresnel));

                // Planar reflection, distorted anisotropically: three and a half times as much
                // along the screen's vertical as its horizontal. Reflections on water stretch
                // downward into streaks rather than blurring evenly, and this one line is most
                // of why a reflection reads as being *in* water rather than painted on it.
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                // The 3.5× vertical stretch stays, but the whole term falls away with distance.
                // Applied flat it hit the far water just as hard as the near, and there the
                // stretch had nothing left to describe — only the aliasing to amplify. Fading
                // it also gives the distance what it should have anyway: a near-mirror.
                float distortFade = lerp(0.25, 1.0, 1.0 - smoothstep(10.0, 30.0, viewDist));
                float2 distort = float2(slope.x, slope.y * 3.5)
                               * _ReflectionDistortion * distortFade;
                // Blur tracks distortion, not distance. Ripple scatter is what softens a
                // reflection, and ripples matter less the more grazing the angle — so the far
                // water comes out nearly mirror-like on its own.
                float mip = saturate(length(distort) * 40.0) * 2.5;
                float4 reflection = SAMPLE_TEXTURE2D_LOD(_ReflectionTex, sampler_ReflectionTex,
                                                         screenUV + distort, mip);
                // Weighted by Fresnel alone. The old 0.12 floor put a ninth of a bright reflection
                // into water viewed straight down, where physics says two percent — and that floor
                // was most of why the near water read as a pale sheet with no colour in it.
                color = lerp(color, reflection.rgb, saturate(_ReflectionStrength * fresnel));

                // Sun glint. Squeezing the normal along the depth axis spreads the highlight
                // through z, which projects as the vertical glitter path real water has —
                // a round specular blob is one of the strongest tells of a fake surface.
                float3 specNormal = normalize(float3(normalWS.x, normalWS.y, normalWS.z * 0.3));
                float3 halfWS = normalize(mainLight.direction + viewWS);
                float glossy = lerp(0.45, 1.0, 1.0 - gust); // glassy patches glint harder
                color += mainLight.color * _Glint * glossy
                       * pow(saturate(dot(specNormal, halfWS)), _GlintSharpness);

                // Foam. The mask is baked into the mesh where the piles stand and along the
                // shoreline — both are known at build time, so this needs no depth texture and
                // the water can stay in the opaque queue where the depth of field still sees
                // it. Two noise samples crossing each other make the edge breathe.
                if (input.foam > 0.001)
                {
                    // The mask itself stays where the piles are — it is baked into the vertex
                    // colours and does not move. What moves is the noise inside it, so the foam
                    // crawls downstream within its own ring instead of simmering in place.
                    float2 foamUV = q * 0.35;
                    float f1 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, foamUV).r;
                    float f2 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, foamUV * 1.7).r;
                    float foam = smoothstep(_FoamCutoff, _FoamCutoff + 0.2,
                                            input.foam * f1 * f2 * 2.6);
                    color = lerp(color, _FoamColor.rgb, saturate(foam));
                }

                color = MixFog(color, input.fogCoord);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
