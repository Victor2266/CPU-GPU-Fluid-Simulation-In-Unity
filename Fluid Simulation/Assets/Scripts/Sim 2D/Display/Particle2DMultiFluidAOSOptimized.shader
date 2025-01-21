Shader "Instanced/MultiFluidParticle2DOptimized"
{
    Properties
    {
        [NoScaleOffset] _GradientArray ("Gradient Array", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        ZWrite Off

        CGINCLUDE
        #pragma target 4.5
        #include "UnityCG.cginc"
            
        // Keep original structure to match compute shader output
        struct Particle
        {
            float2 density;
            float2 velocity;
            float2 predictedPosition;
            float2 position;
            float temperature;
            int type;             // Keep as int to match compute shader
        };

        struct VisualParams
        {
            int visualStyle;      // Keep as int for exact matching
            float visualScale;
            float baseOpacity;
            float noiseScale;
            float timeScale;
            float glowIntensity;
            float glowFalloff;
            float minValue;
            float maxValue;
        };
            
        StructuredBuffer<Particle> Particles;
        StructuredBuffer<VisualParams> VisualParamsBuffer;
        UNITY_DECLARE_TEX2DARRAY(_GradientArray);
        SamplerState linear_clamp_sampler;

        struct v2f
        {
            float4 pos : SV_POSITION;
            float4 uv_worldPos : TEXCOORD0;    // xy: uv, zw: worldPos (packed)
            float4 visualParams1 : TEXCOORD1;   // x: visualStyle, y: baseOpacity, z: noiseScale, w: timeScale
            float4 visualParams2 : TEXCOORD2;   // x: glowIntensity, y: glowFalloff, z: mappedValue, w: type
        };

        // Optimized hash function
        float2 hash2D(float2 p)
        {
            p = float2(dot(p, float2(127.1f, 311.7f)), dot(p, float2(269.5f, 183.3f)));
            return frac(sin(p) * 43758.5453123f) * 2.0 - 1.0;
        }

        // Optimized noise
        float fastNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            
            float2 h00 = hash2D(i);
            float2 h10 = hash2D(i + float2(1, 0));
            float2 h01 = hash2D(i + float2(0, 1));
            float2 h11 = hash2D(i + float2(1, 1));
            
            return lerp(
                lerp(dot(h00, f), dot(h10, f - float2(1, 0)), u.x),
                lerp(dot(h01, f - float2(0, 1)), dot(h11, f - float2(1, 1)), u.x),
                u.y) * 0.5 + 0.5;
        }

        float GetMappedValue(Particle particle, VisualParams visualData)
        {
            if (visualData.visualStyle == 0)
                return saturate(length(particle.velocity) / visualData.maxValue);
            else if (visualData.visualStyle == 1)
                return saturate((particle.temperature - visualData.minValue) / (visualData.maxValue - visualData.minValue));
            return 0;
        }

        v2f vert(appdata_full v, uint instanceID : SV_InstanceID, bool isGlowPass)
        {
            v2f o;
            Particle particle = Particles[instanceID];
            
            // Early out for invalid particles
            if (particle.type == 0)
            {
                o.pos = float4(100000, 100000, 100000, 1);
                return o;
            }

            VisualParams visualData = VisualParamsBuffer[particle.type - 1];
            bool isGlowingParticle = (visualData.visualStyle == 2 || visualData.visualStyle == 1);
            
            if (isGlowPass != isGlowingParticle)
            {
                o.pos = float4(100000, 100000, 100000, 1);
                return o;
            }

            // Pack parameters efficiently
            o.visualParams1 = float4(
                visualData.visualStyle,
                visualData.baseOpacity,
                visualData.noiseScale,
                visualData.timeScale
            );
            
            o.visualParams2 = float4(
                visualData.glowIntensity,
                visualData.glowFalloff,
                GetMappedValue(particle, visualData),
                particle.type - 1
            );

            // Calculate position
            float3 worldPos = float3(particle.position, 0);
            worldPos += mul((float3x3)unity_ObjectToWorld, v.vertex.xyz * visualData.visualScale);
            
            o.uv_worldPos = float4(v.texcoord.x, v.texcoord.y, worldPos.x, worldPos.y);
            o.pos = UnityObjectToClipPos(float4(worldPos, 1));
            return o;
        }
        ENDCG

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            
            CGPROGRAM
            #pragma vertex vert_main
            #pragma fragment frag
            #pragma fastmath
            #pragma multi_compile_instancing

            v2f vert_main(appdata_full v, uint instanceID : SV_InstanceID)
            {
                return vert(v, instanceID, false);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 centreOffset = mad(i.uv_worldPos.xy, 2, -1);
                float sqrDst = dot(centreOffset, centreOffset);
                float delta = fwidth(sqrt(sqrDst));
                
                float3 gradientUV = float3(i.visualParams2.z, 0.5, i.visualParams2.w);
                float4 color = _GradientArray.SampleLevel(linear_clamp_sampler, gradientUV, 0);
                
                float alpha = (1 - smoothstep(1 - delta, 1 + delta, sqrDst)) * i.visualParams1.y;

                if (i.visualParams1.x > 2.5) // Fuzzy style
                {
                    float2 noiseCoord = mad(i.uv_worldPos.zw, i.visualParams1.z, _Time.y * i.visualParams1.w);
                    alpha *= fastNoise(noiseCoord);
                }

                return float4(color.rgb, alpha);
            }
            ENDCG
        }

        Pass
        {
            Blend One One
            
            CGPROGRAM
            #pragma vertex vert_main
            #pragma fragment frag
            #pragma fastmath
            #pragma multi_compile_instancing

            v2f vert_main(appdata_full v, uint instanceID : SV_InstanceID)
            {
                return vert(v, instanceID, true);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 centreOffset = mad(i.uv_worldPos.xy, 2, -1);
                float sqrDst = dot(centreOffset, centreOffset);
                float delta = fwidth(sqrt(sqrDst));
                
                float3 gradientUV = float3(i.visualParams2.z, 0.5, i.visualParams2.w);
                float4 color = _GradientArray.SampleLevel(linear_clamp_sampler, gradientUV, 0);
                
                float normalizedDist = sqrt(sqrDst);
                float glowFactor = pow(1 - saturate(normalizedDist), i.visualParams2.y);
                glowFactor *= mad(sin(_Time.y * 2), 0.2, 0.8); // Optimized pulse
                
                float coreAlpha = 1 - smoothstep(1 - delta, 1 + delta, sqrDst);
                float alpha = max(coreAlpha, glowFactor * 0.5) * i.visualParams1.y;
                
                color.rgb *= 1 + glowFactor * i.visualParams2.x;

                return float4(color.rgb, alpha);
            }
            ENDCG
        }
    }
}