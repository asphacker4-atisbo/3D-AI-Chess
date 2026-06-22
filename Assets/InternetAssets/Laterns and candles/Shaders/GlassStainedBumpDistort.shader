Shader "Custom/URP/GlassStainedBumpDistort"
{
    Properties
    {
        _BumpAmt ("Distortion", Range(0,128)) = 10
        _MainTex ("Tint Color (RGB)", 2D) = "white" {}
        _BumpMap ("Normalmap", 2D) = "bump" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvMain      : TEXCOORD0;
                float2 uvBump      : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
            };

            float _BumpAmt;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            float4 _MainTex_ST;
            float4 _BumpMap_ST;

            Varyings vert (Attributes v)
            {
                Varyings o;

                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionHCS);

                o.uvMain = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvBump = TRANSFORM_TEX(v.uv, _BumpMap);

                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;

                // Normal map distortion
                float3 normal = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uvBump));
                float2 offset = normal.xy * _BumpAmt * 0.001;

                float2 distortedUV = screenUV + offset;

                // Sample scene color
                half4 col = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, distortedUV);

                // Tint
                half4 tint = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uvMain);
                col *= tint;

                return col;
            }

            ENDHLSL
        }
    }
}