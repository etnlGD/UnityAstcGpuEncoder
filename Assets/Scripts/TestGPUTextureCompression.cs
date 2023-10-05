using ASTCEncoder;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class TestGPUTextureCompression : MonoBehaviour
{
    public Shader m_CompressShader;
    public Texture m_SourceTexture;

    private GPUTextureCompressor m_TextureCompressor;
    private Texture m_TargetTexture;
    private bool m_SRGB = true;
    private int m_SelectFormat = 2;
    private int m_EncodeCountPerFrame = 1;

    private void Awake()
    {
        m_TextureCompressor = GetComponent<GPUTextureCompressor>();
    }

    public void Start()
    {
        if (m_SelectFormat >= 1 && m_SelectFormat <= 3)
        {
            var blockSize = m_SelectFormat == 1 ? ASTC_BLOCKSIZE.ASTC_4x4 : m_SelectFormat == 2 ? ASTC_BLOCKSIZE.ASTC_5x5 : ASTC_BLOCKSIZE.ASTC_6x6;
            m_TextureCompressor.ReInit(m_CompressShader, m_SourceTexture.width, m_SourceTexture.height, blockSize);

            DestroyImmediate(m_TargetTexture);
            m_TargetTexture = m_TextureCompressor.CreateOutputTexture(1, 1, m_SRGB, GraphicsFormat.R8G8B8A8_SRGB);
            m_TargetTexture.filterMode = FilterMode.Point;
        }
        else
        {
            DestroyImmediate(m_TargetTexture);
            m_TargetTexture = null;
        }
        
        GetComponent<MeshRenderer>().material.mainTexture = m_TargetTexture != null ? m_TargetTexture : m_SourceTexture;
    }

    private void OnGUI()
    {
        int newFormat = GUILayout.SelectionGrid(
            m_SelectFormat, 
            new []{ "Original", "ASTC 4x4", "ASTC 5x5", "ASTC 6x6" }, 2, 
            new GUIStyle(GUI.skin.button) { fontSize = 50 });
        
        GUILayout.Space(50);
        m_EncodeCountPerFrame = (int)GUILayout.HorizontalSlider(m_EncodeCountPerFrame, 1, 1000);
        GUILayout.Label($"{m_EncodeCountPerFrame}", new GUIStyle(GUI.skin.label) { fontSize = 50 });
        
        if (m_SelectFormat != newFormat)
        {
            m_SelectFormat = newFormat;
            Start();
        }
    }

    private void Update()
    {
        if (!(m_TargetTexture != null && m_SelectFormat >= 1 && m_SelectFormat <= 3))
            return;

        CommandBuffer cmd = CommandBufferPool.Get("GPU Texture Compress");
        for (int i = 0; i < m_EncodeCountPerFrame; i++)
            m_TextureCompressor.CompressTexture(cmd, m_SourceTexture, m_TargetTexture, 0, 0, m_SRGB);
        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    private void OnDestroy()
    {
        DestroyImmediate(m_TargetTexture);
    }
}
