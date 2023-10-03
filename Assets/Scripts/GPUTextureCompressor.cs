using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ASTCEncoder
{
    public enum ASTC_BLOCKSIZE
    {
        ASTC_4x4,
        ASTC_5x5,
        ASTC_6x6,
    }

    public class GPUTextureCompressor : MonoBehaviour
    {
        // 安卓模拟器可能不支持GPU压缩ASTC，检测到模拟器的话应该关闭GPU压缩
        public static bool EnableCompress { get; set; } = true;

    #if UNITY_EDITOR
        // 在编辑器中仍然使用ASTC压缩。由于PC平台不支持ASTC，所以需要在压缩的同时会将压缩结果解压到uav中
        // 这个选项的目的是方便在编辑器中预览压缩后的效果
        private static bool s_DecompressInEditor = true;
    #endif
    
        // 指定ASTC的块大小，只有当UseASTC()返回true时才有效
        public ASTC_BLOCKSIZE ASTCBlockSize { get; private set; }

        private Material m_CompressMaterial;
        private int m_TextureWidth, m_TextureHeight;

        private RenderTexture m_IntermediateTexture;
        private RenderTexture m_DecompressTexture;
        private RenderTargetIdentifier m_IntermediateTextureId;
        private Mesh m_FullScreenMesh;

        private static int k_SourceTextureId = Shader.PropertyToID("_CompressSourceTexture");
        private static int k_SourceTextureMipLevelId = Shader.PropertyToID("_CompressSourceTexture_MipLevel");
        private static int k_ResultId = Shader.PropertyToID("_Result");
        private static int k_DestRectId = Shader.PropertyToID("_DestRect");
        private static readonly int k_IntegerFromQuintsId = Shader.PropertyToID("integer_from_quints");
        private static readonly int k_ColorQuantTableId = Shader.PropertyToID("color_quant_table");

        public static bool DecompressAstc()
        {
#if UNITY_EDITOR
            return s_DecompressInEditor;
#else
            return false;
#endif
        }

        private void OnEnable()
        {
            // DomainReload之后恢复无法序列化的数据
            if (m_IntermediateTexture != null)
                m_IntermediateTextureId = m_IntermediateTexture;
        }

        public void ReInit(Shader compressShader, int srcWidth, int srcHeight, ASTC_BLOCKSIZE astcBlockSize)
        {
            m_TextureWidth = srcWidth;
            m_TextureHeight = srcHeight;
            RecreateMaterial(compressShader, ASTCBlockSize, astcBlockSize);
            ASTCBlockSize = astcBlockSize;
            
            if (m_IntermediateTexture)
                DestroyImmediate(m_IntermediateTexture);
            
            int blockSize = CompressBlockSize;
            Debug.Assert(srcWidth % blockSize == 0 && srcHeight % blockSize == 0);
            
            m_IntermediateTexture = new RenderTexture(
                m_TextureWidth / blockSize, m_TextureHeight / blockSize, 0, 
                GraphicsFormat.R32G32B32A32_UInt, 1);
            m_IntermediateTexture.hideFlags = HideFlags.HideAndDontSave;
            m_IntermediateTexture.name = "GPU Compressor Intermediate Texture";
            m_IntermediateTexture.Create();
            m_IntermediateTextureId = m_IntermediateTexture;
            m_CompressMaterial.SetTexture(k_ResultId, m_IntermediateTexture);

            if (DecompressAstc())
            {
                if (m_DecompressTexture)
                    DestroyImmediate(m_DecompressTexture);
                
                m_DecompressTexture = new RenderTexture(m_TextureWidth, m_TextureHeight, 0, GraphicsFormat.R8G8B8A8_UNorm, 1);
                m_DecompressTexture.hideFlags = HideFlags.HideAndDontSave;
                m_DecompressTexture.name = "GPU Compressor Decompress Texture";
                m_DecompressTexture.enableRandomWrite = true;
                m_DecompressTexture.Create();
            }
            
            if (!m_FullScreenMesh)
            {
                m_FullScreenMesh = new Mesh();
                m_FullScreenMesh.hideFlags = HideFlags.HideAndDontSave;
                m_FullScreenMesh.vertices = new []
                {
                    new Vector3(-1, -1, 0),
                    new Vector3(-1, 3, 0),
                    new Vector3(3, -1, 0),
                };
                m_FullScreenMesh.triangles = new [] { 0, 1, 2 }; 
                m_FullScreenMesh.RecalculateBounds();
            }
        }

        private int CompressBlockSize
        {
            get
            {
                switch (ASTCBlockSize)
                {
                    case ASTC_BLOCKSIZE.ASTC_4x4: return 4;
                    case ASTC_BLOCKSIZE.ASTC_5x5: return 5;
                    case ASTC_BLOCKSIZE.ASTC_6x6: return 6;
                    default: throw new System.ArgumentException("Invalid ASTC block size");
                }                    
            }
        }

        private void RecreateMaterial(Shader compressShader, ASTC_BLOCKSIZE prevBlocksize, ASTC_BLOCKSIZE blocksize)
        {
            if (m_CompressMaterial != null && m_CompressMaterial.shader == compressShader)
            {
                if (prevBlocksize != blocksize)
                {
                    // 仍然需要重新创建材质
                    DestroyImmediate(m_CompressMaterial);
                    m_CompressMaterial = null;
                }
                else
                {
                    return;
                }
            }
            
            m_CompressMaterial = new Material(compressShader);
            m_CompressMaterial.hideFlags = HideFlags.HideAndDontSave;
            
            if (blocksize == ASTC_BLOCKSIZE.ASTC_5x5)
            {
                m_CompressMaterial.EnableKeyword("_COMPRESS_ASTC5x5");
                m_CompressMaterial.DisableKeyword("_COMPRESS_ASTC4x4");
                m_CompressMaterial.DisableKeyword("_COMPRESS_ASTC6x6");
            }
            else if (blocksize == ASTC_BLOCKSIZE.ASTC_4x4)
            {
                m_CompressMaterial.EnableKeyword("_COMPRESS_ASTC4x4");
                m_CompressMaterial.DisableKeyword("_COMPRESS_ASTC5x5");
                m_CompressMaterial.DisableKeyword("_COMPRESS_ASTC6x6");
            }
            else
            {
                m_CompressMaterial.EnableKeyword("_COMPRESS_ASTC6x6");
                m_CompressMaterial.DisableKeyword("_COMPRESS_ASTC4x4");
                m_CompressMaterial.DisableKeyword("_COMPRESS_ASTC5x5");
                
                var quintsLookup = new float[]
                {
                    0,1,2,3,4, 			8,9,10,11,12, 			16,17,18,19,20,			24,25,26,27,28, 		5,13,21,29,6,
                    32,33,34,35,36, 	40,41,42,43,44, 		48,49,50,51,52, 		56,57,58,59,60, 		37,45,53,61,14,	
                    64,65,66,67,68, 	72,73,74,75,76, 		80,81,82,83,84, 		88,89,90,91,92, 		69,77,85,93,22,
                    96,97,98,99,100, 	104,105,106,107,108,	112,113,114,115,116,	120,121,122,123,124, 	101,109,117,125,30,	
                    102,103,70,71,38, 	110,111,78,79,46, 		118,119,86,87,54, 		126,127,94,95,62, 		39,47,55,63,31
                };
                for (int i = 0; i < quintsLookup.Length; i++)
                    quintsLookup[i] += 0.5f;
                    
                var colorQuantTable = new float[]
                {
                    0, 0, 16, 16, 16, 32, 32, 32, 48, 48, 48, 48, 64, 64, 64, 2,
                    2, 2, 18, 18, 18, 34, 34, 34, 50, 50, 50, 50, 66, 66, 66, 4,
                    4, 4, 20, 20, 20, 36, 36, 36, 36, 52, 52, 52, 68, 68, 68, 6,
                    6, 6, 22, 22, 22, 38, 38, 38, 38, 54, 54, 54, 70, 70, 70, 8,
                    8, 8, 24, 24, 24, 24, 40, 40, 40, 56, 56, 56, 72, 72, 72, 10,
                    10, 10, 26, 26, 26, 26, 42, 42, 42, 58, 58, 58, 74, 74, 74, 12,
                    12, 12, 12, 28, 28, 28, 44, 44, 44, 60, 60, 60, 76, 76, 76, 14,
                    14, 14, 14, 30, 30, 30, 46, 46, 46, 62, 62, 62, 78, 78, 78, 78,
                    79, 79, 79, 79, 63, 63, 63, 47, 47, 47, 31, 31, 31, 15, 15, 15,
                    15, 77, 77, 77, 61, 61, 61, 45, 45, 45, 29, 29, 29, 13, 13, 13,
                    13, 75, 75, 75, 59, 59, 59, 43, 43, 43, 27, 27, 27, 27, 11, 11,
                    11, 73, 73, 73, 57, 57, 57, 41, 41, 41, 25, 25, 25, 25, 9, 9,
                    9, 71, 71, 71, 55, 55, 55, 39, 39, 39, 39, 23, 23, 23, 7, 7,
                    7, 69, 69, 69, 53, 53, 53, 37, 37, 37, 37, 21, 21, 21, 5, 5,
                    5, 67, 67, 67, 51, 51, 51, 51, 35, 35, 35, 19, 19, 19, 3, 3,
                    3, 65, 65, 65, 49, 49, 49, 49, 33, 33, 33, 17, 17, 17, 1, 1,
                };
                for (int i = 0; i < colorQuantTable.Length; i++)
                    colorQuantTable[i] += 0.5f;
                m_CompressMaterial.SetFloatArray(k_IntegerFromQuintsId, quintsLookup);
                m_CompressMaterial.SetFloatArray(k_ColorQuantTableId, colorQuantTable);
            }
            
            if (DecompressAstc())
                m_CompressMaterial.EnableKeyword("_DECOMPRESS_RGB");
            else
                m_CompressMaterial.DisableKeyword("_DECOMPRESS_RGB");
        }

        public int TextureWidth => m_TextureWidth;

        public int TextureHeight => m_TextureHeight;

        private void OnDestroy()
        {
            DestroyImmediate(m_DecompressTexture);
            m_DecompressTexture = null;

            DestroyImmediate(m_IntermediateTexture);
            m_IntermediateTexture = null;
            m_IntermediateTextureId = BuiltinRenderTextureType.None;
            
            DestroyImmediate(m_FullScreenMesh);
            m_FullScreenMesh = null;
            
            DestroyImmediate(m_CompressMaterial);
            m_CompressMaterial = null;
        }

        public Texture CreateOutputTexture(int mipCount, int sliceCount, bool srgb, GraphicsFormat noCompressFallback = GraphicsFormat.R8G8B8A8_UNorm)
        {
            var format = ASTCBlockSize == ASTC_BLOCKSIZE.ASTC_4x4 ? TextureFormat.ASTC_4x4 :
                ASTCBlockSize == ASTC_BLOCKSIZE.ASTC_5x5 ? TextureFormat.ASTC_5x5 : TextureFormat.ASTC_6x6;
            
            Debug.Assert(m_TextureWidth % (CompressBlockSize * (1 << (mipCount - 1))) == 0);
            Debug.Assert(m_TextureHeight % (CompressBlockSize * (1 << (mipCount - 1))) == 0);

            var gfxFormat = GraphicsFormatUtility.GetGraphicsFormat(format, srgb);
            if (DecompressAstc() || !EnableCompress)
                gfxFormat = noCompressFallback;

            Texture output;
            if (sliceCount == 1)
            {
                var flags = (mipCount != 1) ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
                output = new Texture2D(m_TextureWidth, m_TextureHeight, gfxFormat, mipCount, flags);
                ((Texture2D)output).Apply(false, true); // 让贴图变成不可读，以卸载内存只保留显存
            }
            else
            {
                // 不创建TextureArray的cpu side内存，直接通过copy texture提交贴图数据
                var flags = (mipCount != 1) ? TextureCreationFlags.MipChain : TextureCreationFlags.None;

                output = new Texture2DArray(m_TextureWidth, m_TextureHeight, sliceCount, gfxFormat, flags, mipCount);
                ((Texture2DArray)output).Apply(false, true); // 让贴图变成不可读，以卸载内存只保留显存
            }
            output.filterMode = FilterMode.Trilinear;
            output.wrapMode = TextureWrapMode.Clamp;
            Debug.Assert(!output.isReadable);
            return output;
        }

        public void CompressTexture(CommandBuffer cmd, RenderTargetIdentifier sourceTexture, RenderTargetIdentifier targetTexture, int dstElement, int mipLevel, bool srgb = false)
        {
            if (!EnableCompress)
            {
                cmd.CopyTexture(
                    sourceTexture, 0, mipLevel, 0, 0,
                    m_TextureWidth >> mipLevel, m_TextureHeight >> mipLevel,
                    targetTexture, dstElement, mipLevel, 0, 0);
                return;
            }

            cmd.SetRenderTarget(m_IntermediateTextureId);
            int rtWidth = m_IntermediateTexture.width >> mipLevel, rtHeight = m_IntermediateTexture.height >> mipLevel;
            cmd.SetViewport(new Rect(0, 0, rtWidth, rtHeight));
            
            if (DecompressAstc())
                cmd.SetRandomWriteTarget(1, m_DecompressTexture);
            
            if (QualitySettings.activeColorSpace == ColorSpace.Linear && srgb)
                cmd.EnableShaderKeyword("_GPU_COMPRESS_SRGB");
            else
                cmd.DisableShaderKeyword("_GPU_COMPRESS_SRGB");
            
            int destWidth = m_TextureWidth >> mipLevel, destHeight = m_TextureHeight >> mipLevel;
            cmd.SetGlobalVector(k_DestRectId, new Vector4(destWidth, destHeight, 1.0f / destWidth, 1.0f / destHeight));
            cmd.SetGlobalTexture(k_SourceTextureId, sourceTexture);
            cmd.SetGlobalInt(k_SourceTextureMipLevelId, mipLevel);
            
            cmd.BeginSample("Compress");
            cmd.DrawMesh(m_FullScreenMesh, Matrix4x4.identity, m_CompressMaterial, 0, 0);
            cmd.EndSample("Compress");
            
            cmd.SetRenderTarget(BuiltinRenderTextureType.None);

            if (!DecompressAstc())
            {
                cmd.BeginSample("CopyTexture");
                cmd.CopyTexture(
                    m_IntermediateTextureId, 0, 0, 0, 0, rtWidth, rtHeight,
                    targetTexture, dstElement,mipLevel,0,0);
                cmd.EndSample("CopyTexture");
            }
            else
            {
                var blockSize = CompressBlockSize;
                cmd.CopyTexture(
                    m_DecompressTexture, 0, 0, 0, 0, 
                    rtWidth * blockSize, rtHeight * blockSize,
                    targetTexture, dstElement,mipLevel,0,0);
            }
        }

    }
}
