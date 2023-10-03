Shader "Unlit/GPUTextureCompress"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile_local _COMPRESS_ASTC4x4 _COMPRESS_ASTC5x5 _COMPRESS_ASTC6x6
            #pragma shader_feature_local __ _DECOMPRESS_RGB     // 目前仅在编辑器中测试用，不需要打包，因此改为shader_feature
            #pragma multi_compile __ _GPU_COMPRESS_SRGB

            #include "GPUTextureCompress.hlsl"
            
            // #pragma enable_d3d11_debug_symbols

            struct appdata
            {
                float3 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex, 1.0);
                return o;
            }
            
            uint4 frag (v2f i) : SV_Target
            {
                return Compress(floor(i.vertex.xy) * PIXEL_COUNT_1D);
            }
            ENDHLSL
        }
    }
}
