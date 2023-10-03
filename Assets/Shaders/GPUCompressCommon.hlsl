#ifndef GPU_COMPRESS_COMMON_HLSL
#define GPU_COMPRESS_COMMON_HLSL

#if _COMPRESS_ASTC5x5
    #define PIXEL_COUNT_1D 5
    #define PIXEL_COUNT_2D 25
#elif _COMPRESS_ASTC6x6
    #define PIXEL_COUNT_1D 6
    #define PIXEL_COUNT_2D 36
#else
    #define PIXEL_COUNT_1D 4
    #define PIXEL_COUNT_2D 16
#endif

#endif
