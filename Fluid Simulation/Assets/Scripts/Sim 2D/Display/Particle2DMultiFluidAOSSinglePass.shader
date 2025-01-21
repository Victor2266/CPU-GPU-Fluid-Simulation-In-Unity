Shader "Instanced/MultiFluidParticle2DSinglePass"
{
    Properties
    {
        [NoScaleOffset] _GradientArray ("Gradient Array", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma fastmath
            
            #include "UnityCG.cginc"
            
            struct Particle
            {
                float2 density;
                float2 velocity;
                float2 predictedPosition;
                float2 position;
                float temperature;
                int type;
            };
            
            struct VisualParams
            {
                int visualStyle;
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
                float2 uv : TEXCOORD0;
                float2 worldPos : TEXCOORD1;
                float4 particleData : TEXCOORD2;  // x: mapped value, y: type index, z: visualStyle, w: valid
                float4 visualData : TEXCOORD3;    // x: baseOpacity, y: glowIntensity, z: glowFalloff, w: noiseScale
            };

            // Fast value mapping without branching
            float GetMappedValue(float speed, float temp, float minVal, float maxVal, int style)
            {
                float velocityMapped = saturate(speed / maxVal);
                float tempMapped = saturate((temp - minVal) / (maxVal - minVal));
                return lerp(velocityMapped, tempMapped, style == 1 ? 1 : 0);
            }

            v2f vert(appdata_full v, uint instanceID : SV_InstanceID)
            {
                v2f o = (v2f)0;
                Particle particle = Particles[instanceID];
                
                // Early out for invalid particles
                if (particle.type == 0)
                {
                    o.pos = float4(100000, 100000, 100000, 1);
                    o.particleData.w = 0;
                    return o;
                }

                VisualParams visualData = VisualParamsBuffer[particle.type - 1];
                
                float speed = length(particle.velocity);
                float mappedValue = GetMappedValue(
                    speed,
                    particle.temperature,
                    visualData.minValue,
                    visualData.maxValue,
                    visualData.visualStyle
                );

                // Calculate world position
                float3 centreWorld = float3(particle.position, 0);
                float3 worldPos = centreWorld + mul(unity_ObjectToWorld, v.vertex * visualData.visualScale);
                
                o.pos = UnityObjectToClipPos(float4(worldPos, 1));
                o.uv = v.texcoord;
                o.worldPos = worldPos.xy;
                o.particleData = float4(
                    mappedValue,
                    particle.type - 1,
                    visualData.visualStyle,
                    1  // valid flag
                );
                o.visualData = float4(
                    visualData.baseOpacity,
                    visualData.glowIntensity,
                    visualData.glowFalloff,
                    visualData.noiseScale
                );
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                if (!i.particleData.w) discard;
                
                float2 centreOffset = (i.uv - 0.5) * 2;
                float sqrDst = dot(centreOffset, centreOffset);
                float delta = fwidth(sqrt(sqrDst));
                
                // Sample base color
                float3 gradientUV = float3(i.particleData.x, 0.5, i.particleData.y);
                float4 color = _GradientArray.SampleLevel(linear_clamp_sampler, gradientUV, 0);
                
                // Calculate base alpha
                float alpha = 1 - smoothstep(1 - delta, 1 + delta, sqrDst);
                
                // Simplified additive glow for style 1 and 2
                if (i.particleData.z <= 2)
                {
                    float glowStr = i.visualData.y * (i.particleData.z >= 1 ? 1 : 0);
                    float glowFactor = pow(1 - sqrt(sqrDst), i.visualData.z) * glowStr;
                    color.rgb *= 1 + glowFactor;
                }
                
                return float4(color.rgb, alpha * i.visualData.x);
            }
            ENDCG
        }
    }
}