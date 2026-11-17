// Toon shader for the cars — rebuilt to match the property layout of the ripped
// "Toony Colors Pro 2/User/Ugurtan" materials (texture ramp + offset/size,
// highlight/shadow colors, GGX specular, 6-direction ambient, curvature material layer,
// albedo saturation). _RampMode = 0 switches to a threshold/smoothing ramp (wheel, bumper).
Shader "PackingLoop/CarToon"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _HColor ("Highlight Color", Color) = (1,1,1,1)
        _SColor ("Shadow Color", Color) = (0.2,0.2,0.2,1)
        _BaseMap ("Albedo", 2D) = "white" {}
        _albedo_sat ("Albedo Saturation", Range(-2, 2)) = 1

        [Header(Ramp)]
        [Enum(Threshold,0,Texture,1)] _RampMode ("Ramp Mode", Float) = 1
        [NoScaleOffset] _Ramp ("Ramp Texture (RGB)", 2D) = "gray" {}
        _RampTextureOffset ("Ramp Offset", Range(0, 1)) = 0.5
        _RampTextureSize ("Ramp Size", Range(0.001, 1)) = 1
        _RampThreshold ("Threshold", Range(0.01, 1)) = 0.5
        _RampSmoothing ("Smoothing", Range(0.001, 1)) = 0.5

        [Header(Specular)]
        [Toggle] _UseSpecular ("Enable Specular", Float) = 0
        _SpecularColor ("Specular Color", Color) = (0.5,0.5,0.5,1)
        _SpecularRoughnessPBR ("Roughness", Range(0, 1)) = 0.5

        [Header(Ambient)]
        [Toggle] _UseAmbient ("Custom 6-Dir Ambient (off = scene SH)", Float) = 0
        _TCP2_AMBIENT_RIGHT ("+X (Right)", Color) = (0,0,0,1)
        _TCP2_AMBIENT_LEFT ("-X (Left)", Color) = (0,0,0,1)
        _TCP2_AMBIENT_TOP ("+Y (Top)", Color) = (0,0,0,1)
        _TCP2_AMBIENT_BOTTOM ("-Y (Bottom)", Color) = (0,0,0,1)
        _TCP2_AMBIENT_FRONT ("+Z (Front)", Color) = (0,0,0,1)
        _TCP2_AMBIENT_BACK ("-Z (Back)", Color) = (0,0,0,1)

        [Header(Material Layer (curvature mask on UV0))]
        [NoScaleOffset] _layer_materialLayer ("Layer Mask", 2D) = "black" {}
        _BaseColor_materialLayer ("Layer Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _HColor, _SColor;
            half _albedo_sat;
            half _RampMode, _RampTextureOffset, _RampTextureSize, _RampThreshold, _RampSmoothing;
            half _UseSpecular; half4 _SpecularColor; half _SpecularRoughnessPBR;
            half _UseAmbient;
            half4 _TCP2_AMBIENT_RIGHT, _TCP2_AMBIENT_LEFT, _TCP2_AMBIENT_TOP, _TCP2_AMBIENT_BOTTOM, _TCP2_AMBIENT_FRONT, _TCP2_AMBIENT_BACK;
            half4 _BaseColor_materialLayer;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Ramp); SAMPLER(sampler_Ramp);
            TEXTURE2D(_layer_materialLayer); SAMPLER(sampler_layer_materialLayer);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 Ramp(half ndl)
            {
                // texture ramp (TCP2 "Offset/Size" remap)
                half u = ndl * 0.5 + 0.5;
                half size = _RampTextureSize * 0.5;
                half mn = _RampTextureOffset - size;
                half diff = (_RampTextureOffset + size) - mn;
                u = saturate(u / diff - mn / diff);
                half3 texRamp = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, half2(u, 0.5)).rgb;
                // threshold/smoothing ramp
                half s = _RampSmoothing * 0.5;
                half3 slider = smoothstep(_RampThreshold - s, _RampThreshold + s, saturate(ndl)).xxx;
                return _RampMode > 0.5 ? texRamp : slider;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // albedo: base color with curvature material layer, then saturation
                half mask = SAMPLE_TEXTURE2D(_layer_materialLayer, sampler_layer_materialLayer, i.uv).r;
                half3 baseCol = lerp(_BaseColor.rgb, _BaseColor_materialLayer.rgb, mask);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(i.uv, _BaseMap)).rgb * baseCol;
                albedo = lerp(Luminance(albedo).xxx, albedo, _albedo_sat);

                Light light = GetMainLight();
                half ndl = dot(n, light.direction);
                half3 ramp = Ramp(ndl) * light.distanceAttenuation;

                half3 lightCol = light.color;
                half3 accumulatedColors = ramp * lightCol;
                half3 shadowColor = (1 - saturate(ramp)) * _SColor.rgb;
                half3 color = albedo * (accumulatedColors * _HColor.rgb + shadowColor);

                if (_UseSpecular > 0.5)
                {
                    float3 h = SafeNormalize(light.direction + v);
                    half nh = saturate(dot(n, h));
                    half lh = saturate(dot(light.direction, h));
                    half r = max(_SpecularRoughnessPBR * _SpecularRoughnessPBR, 0.002);
                    half r2 = r * r;
                    half d = nh * nh * (r2 - 1) + 1.00001;
                    half spec = r2 / ((d * d) * max(0.1, lh * lh) * (r * 4 + 2));
                    color += spec * _SpecularColor.rgb * lightCol * saturate(ramp);
                }

                half3 ambient;
                if (_UseAmbient > 0.5)
                {
                    ambient = saturate( n.x) * _TCP2_AMBIENT_RIGHT.rgb + saturate(-n.x) * _TCP2_AMBIENT_LEFT.rgb
                            + saturate( n.y) * _TCP2_AMBIENT_TOP.rgb   + saturate(-n.y) * _TCP2_AMBIENT_BOTTOM.rgb
                            + saturate( n.z) * _TCP2_AMBIENT_FRONT.rgb + saturate(-n.z) * _TCP2_AMBIENT_BACK.rgb;
                }
                else ambient = SampleSH(n);
                color += albedo * ambient;

                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A a) { V o; UNITY_SETUP_INSTANCE_ID(a); o.positionCS = TransformObjectToHClip(a.positionOS.xyz); return o; }
            half frag(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
