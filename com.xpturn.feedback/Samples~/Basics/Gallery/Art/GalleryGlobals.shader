// the Shader Global entries' reader: the three globals the gallery's items set — _GalleryGlobalColor tints the surface (added to
// the base color), _GalleryGlobalFloat grows the mesh (1 + the value), _GalleryGlobalVector moves it (its xyz, in object space).
// A global is read only while no material declares it, so they stay out of Properties
Shader "xpTURN Feedback Samples/Gallery Globals"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.25, 0.5, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            half4 _GalleryGlobalColor;
            float _GalleryGlobalFloat;
            float4 _GalleryGlobalVector;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half shade : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 position = input.positionOS.xyz * (1.0 + _GalleryGlobalFloat) + _GalleryGlobalVector.xyz;
                output.positionCS = TransformObjectToHClip(position);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                output.shade = 0.65 + 0.35 * saturate(dot(normal, normalize(float3(0.4, 1.0, -0.6))));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4((_BaseColor.rgb + _GalleryGlobalColor.rgb) * input.shade, 1.0);
            }
            ENDHLSL
        }
    }
}
