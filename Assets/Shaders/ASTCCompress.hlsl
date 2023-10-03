#include "GPUCompressCommon.hlsl"

float integer_from_quints[125];
float color_quant_table[256];

#if _COMPRESS_ASTC6x6
static const uint REV_BITS[4] = { 0, 2, 1, 3 };
#define SCALE_RANGE 3
#define SCALE_RANGE_F 3.0
#else
static const uint REV_BITS[8] = { 0, 4, 2, 6, 1, 5, 3, 7 };
#define SCALE_RANGE 7
#define SCALE_RANGE_F 7.0
#endif

#define ASTC6x6_COLOR_BIT_COUNT 4
void ASTC6x6_SplitHighLow(uint3 n, out uint3 high, out uint3 low)
{
    uint lowMask = (1 << ASTC6x6_COLOR_BIT_COUNT) - 1;
    low = n & lowMask;
    high = n >> ASTC6x6_COLOR_BIT_COUNT;
}

void GetBlockMinMax(in float3 Block[PIXEL_COUNT_2D], out float3 OutMin, out float3 OutMax)
{
    OutMin = Block[0];
    OutMax = Block[0];

    for (int i = 1; i < PIXEL_COUNT_2D; ++i)
    {
        OutMin = min(OutMin, Block[i]);
        OutMax = max(OutMax, Block[i]);
    }
}

uint4 CompressASTCBlock(inout float3 Block[PIXEL_COUNT_2D])
{
    float3 MinColor, MaxColor;

    GetBlockMinMax(Block, MinColor, MaxColor);

#if _COMPRESS_ASTC5x5
    #define ColorEndPointRange 63  // RGB666
#elif _COMPRESS_ASTC6x6
    #define ColorEndPointRange 79  // 4 Bits + 1 Quint(2^4*5)
#else
    #define ColorEndPointRange 255 // RGB888
#endif

#if _COMPRESS_ASTC6x6
    // 6x6由于有quint，需要特殊处理: https://registry.khronos.org/DataFormat/specs/1.3/dataformat.1.3.html#astc-endpoint-unquantization
    uint3 MinColorUint = (uint3)round(saturate(MinColor) * 255);
    uint3 MaxColorUint = (uint3)round(saturate(MaxColor) * 255);
    MinColorUint = uint3(color_quant_table[MinColorUint.x], color_quant_table[MinColorUint.y], color_quant_table[MinColorUint.z]);
    MaxColorUint = uint3(color_quant_table[MaxColorUint.x], color_quant_table[MaxColorUint.y], color_quant_table[MaxColorUint.z]);

    MinColor = round(saturate(MinColor) * ColorEndPointRange) / ColorEndPointRange;
    MaxColor = round(saturate(MaxColor) * ColorEndPointRange) / ColorEndPointRange;
#else
    uint3 MinColorUint = (uint3)round(saturate(MinColor) * ColorEndPointRange);
    uint3 MaxColorUint = (uint3)round(saturate(MaxColor) * ColorEndPointRange);
    MinColor = (float3)MinColorUint / ColorEndPointRange;
    MaxColor = (float3)MaxColorUint / ColorEndPointRange;
#endif

    // Encode blockmode & color
#if _COMPRESS_ASTC4x4
    // ASTC 4x4
    uint4 PackedBlock = uint4(0x00010053, 0, 0, 0); // Init BlockMode
    PackedBlock.x |= (MinColorUint.r << 17); // [17, 25)
    
    PackedBlock.x |= (MaxColorUint.r << 25); // [25, 33)
    PackedBlock.y |= (MaxColorUint.r >> 7);
    
    PackedBlock.y |= (MinColorUint.g << 1);  // [33, 41)
    PackedBlock.y |= (MaxColorUint.g << 9);  // [41, 49)
    PackedBlock.y |= (MinColorUint.b << 17); // [49, 57)
    
    PackedBlock.y |= (MaxColorUint.b << 25); // [57, 65)
    PackedBlock.z |= (MaxColorUint.b >> 7);
#elif _COMPRESS_ASTC5x5
    // ASTC 5x5
    uint4 PackedBlock = uint4(0x000100F3, 0, 0, 0); // Init BlockMode
    uint3 ShiftedMin = MinColorUint.rgb << uint3(17, 29, 9);
    uint3 ShiftedMax = MaxColorUint.rgb << uint3(23, 3, 15);
    
    PackedBlock.x |= ShiftedMin.r | ShiftedMin.g | ShiftedMax.r;
    PackedBlock.y |= MinColorUint.g >> 3; // [32, 35)
    PackedBlock.y |= ShiftedMax.g | ShiftedMin.b | ShiftedMax.b;
#elif _COMPRESS_ASTC6x6
    // ASTC 6x6
    uint4 PackedBlock = uint4(0x00010108, 0, 0, 0); // Init BlockMode
    uint3 q0, q1;
    uint3 m0, m1;
    
    ASTC6x6_SplitHighLow(uint3(MinColorUint.r, MaxColorUint.r, MinColorUint.g), q0, m0);
    ASTC6x6_SplitHighLow(uint3(MaxColorUint.g, MinColorUint.b, MaxColorUint.b), q1, m1);
    
    uint packhigh0 = (uint)floor(integer_from_quints[q0.z * 25 + q0.y * 5 + q0.x]);
    uint packhigh1 = (uint)floor(integer_from_quints[q1.z * 25 + q1.y * 5 + q1.x]);
    
    uint3 sm0 = m0.xyz << uint3(17, 24, 30);
    uint3 sm1 = m1.xyz << uint3(4, 11, 17);
    uint3 sh0 = ((packhigh0 >> uint3(0, 3, 5)) & uint3(7, 3, 3)) << uint3(21, 28, 2);
    uint3 sh1 = ((packhigh1 >> uint3(0, 3, 5)) & uint3(7, 3, 3)) << uint3(8, 15, 21);
    
    uint2 tmp2 = sm0.xy | sh0.xy;
    PackedBlock.x |= tmp2.x | tmp2.y | sm0.z;
    
    PackedBlock.y |= m0.z >> 2;
    PackedBlock.y |= sh0.z;
    
    uint3 tmp3 = sm1 | sh1;
    PackedBlock.y |= tmp3.x | tmp3.y | tmp3.z;
#endif
    
    // Project onto max->min color vector and segment into range [0, SCALE_RANGE]
    float3 Range = MaxColor - MinColor;
    float Scale = SCALE_RANGE_F / max(1e-5, dot(Range, Range)); // 正常的最小值是(1/255.0)^2，约等于1.53e-5
    float3 ScaledRange = Range * Scale;
    float Bias = (dot(MinColor, MinColor) - dot(MaxColor, MinColor)) * Scale;
    [unroll]
    for (int i = 0; i < PIXEL_COUNT_2D; ++i)
    {
        // Compute the distance index for this element
        uint Index = clamp(round(dot(Block[i], ScaledRange) + Bias), 0, SCALE_RANGE);

        Block[i] = MinColor + (Index / SCALE_RANGE_F) * Range;
        
        uint bitRevIndex = REV_BITS[Index];
    #if _COMPRESS_ASTC6x6 // 6x6是2 bits weight per pixel
        if (i < 16)
            PackedBlock.w |= (bitRevIndex << (30 - i * 2));
        else if (i < 32)
            PackedBlock.z |= (bitRevIndex << (62 - i * 2));
        else 
            PackedBlock.y |= (bitRevIndex << (94 - i * 2)); // 32 -> 30, 33 -> 28, 34 -> 26, 35 -> 24
    #else // 4x4和5x5是3 bits weight per pixel
        if (i < 10)
        {
            // [96 + 2, 128) 
            PackedBlock.w |= (bitRevIndex << (29 - i * 3));
        }
        else if (i == 10)
        {
            // [95, 96 + 2)
            PackedBlock.w |= (bitRevIndex >> 1);
            PackedBlock.z |= (bitRevIndex << 31);
        }
        else if (i < 21)
        {
            // [64+1, 64+28+3)
            PackedBlock.z |= (bitRevIndex << (28 - (i - 11) * 3));
        }
        else if (i == 21)
        {
            // [62, 65)
            PackedBlock.z |= (bitRevIndex >> 2);
            PackedBlock.y |= (bitRevIndex << 30);
        }
        else
        {
            // [53, 62) -> [32 + 21, 32 + 27 + 3)
            PackedBlock.y |= (bitRevIndex << (27 - (i - 22) * 3));
        }
    #endif
    }

    return PackedBlock;
}
