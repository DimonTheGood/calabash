// Контур «инвертированной оболочкой»: меш раздувается по нормалям и рисуется изнанкой.
// По style bible только у героя цветной (оранжевый) контур — по нему игрок находит себя в толпе.
Shader "Calabash/InkOutline"
{
    Properties
    {
        _Color ("Цвет контура", Color) = (1, 0.478, 0.094, 1)
        _Width ("Толщина, м", Float) = 0.012
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = TransformWorldToHClip(positionWS + normalWS * _Width);
                return o;
            }

            half4 frag (Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
    FallBack Off
}
