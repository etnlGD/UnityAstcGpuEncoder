# Unity ASTC GPU Encoder
Encode astc texture in pixel shader.

## Support Formats
- Texture format: ASTC_4x4, ASTC_5x5, ASTC_6x6.
- Color format: RGB only, no Alpha support.

## Encoded Texture Detail
1. ASTC_4x4: 2 R8G8B8 color endpoints. 4x4 weight grid, 3 bits weight per pixel
2. ASTC_5x5: 2 R6G6B6 color endpoints. 5x5 weight grid, 3 bits weight per pixel
3. ASTC_6x6: 2 RGB color endpoints, 4 bits & 1 quint per channel, i.e. QUANT80. 6x6 weight grid, 2 bits weight per pixel

## Comparison
The quality of the compressed texture in the ASTC_4x4 and ASTC_5x5 formats is slightly superior to that of the bc1 texture encoded by UnrealEngine’s GPU encoder, and significantly better than the output from UnrealEngine’s etc2 encoder.

ASTC_5x5 is typically your best choice, as it uses 36% less memory than ASTC_4x4, and the difference between the two is almost imperceptible. However, the compression quality of ASTC_6x6 is noticeably poorer than the other two, so use 6x6 with caution.
