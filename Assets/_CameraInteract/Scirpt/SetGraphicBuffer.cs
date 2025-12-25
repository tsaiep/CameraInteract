using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;
using System;
using Unity.Mathematics;
using Random = UnityEngine.Random;

/// <summary>
/// 固定容量＋局部更新：不重建 GraphicsBuffer，只在內容變動時做區段上傳。
/// - 空白鍵：寫入隨機位置
/// - 滑鼠左鍵：根據螢幕座標映射到固定世界範圍，寫入下一筆資料
/// </summary>
public class SpawnPointsBufferFixedPartial : MonoBehaviour
{
    //Vector4，4 * 4 bytes = 16 bytes
    private const int Stride = 16;

    [Header("VFX Properties")]
    [SerializeField] private int Capacity = 1024; 
    [SerializeField] private float Lifetime = 25;
    [SerializeField] private float FadeOutDuration = 5;
    [SerializeField] private float ParticleSize = 1.5f;
    private Vector2 GroundSize;
    
    [Header("VFX Binding")]
    [SerializeField] private VisualEffect vfx;
    [SerializeField] private ExposedProperty bufferProp = "SpawnPoints";
    [SerializeField] private ExposedProperty countProp  = "SpawnPointsCount";
    [SerializeField] private ExposedProperty lifetime = "Lifetime";
    [SerializeField] private ExposedProperty fadeOutDuration  = "FadeOutDuration";
    [SerializeField] private ExposedProperty particleSize  = "ParticlesSize";
    [SerializeField] private ExposedProperty groundSize  = "GroundSize";

    [Header("Spawn Range")]
    [SerializeField] private Vector2 worldXRange = new Vector2(-3f, 3f);
    [SerializeField] private Vector2 worldYRange = new Vector2(-3f, 3f);
    [SerializeField] private float worldZ = 0f;

    private GraphicsBuffer buffer;    // GPU 端固定容量緩衝
    private Vector4[] data;           // CPU 端鏡像資料（固定長度）

    private int moveID = 0;           // 下一個要寫入的索引（循環使用）

    void OnEnable()
    {
        // 容量安全檢查
        Capacity = Mathf.Max(1, Capacity);

        // 配置 CPU 鏡像資料與 GPU Buffer（只做一次）
        data = new Vector4[Capacity];
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Capacity, Stride);
        
        //Calculate ground size
        GroundSize = new Vector2(math.abs(worldXRange.x)+math.abs(worldXRange.y), math.abs(worldYRange.x)+math.abs(worldYRange.y));
        
        // 綁定給 VFX，並把元素數量固定告知一次
        if (vfx != null)
        {
            if(vfx.HasGraphicsBuffer(bufferProp))
                vfx.SetGraphicsBuffer(bufferProp, buffer);
            if(vfx.HasInt(countProp))
                vfx.SetInt(countProp, Capacity);
            
            if(vfx.HasFloat(lifetime))
                vfx.SetFloat(lifetime, Lifetime);
            if(vfx.HasFloat(fadeOutDuration))
                vfx.SetFloat(fadeOutDuration, FadeOutDuration);
            if(vfx.HasFloat(particleSize))
                vfx.SetFloat(particleSize, ParticleSize);
            if(vfx.HasVector2(groundSize))    
                vfx.SetVector2(groundSize, GroundSize);
        }

        // 初始資料：沿 X 排一條線（你原本的設定）
        for (int i = 0; i < Capacity; i++)
        {
            data[i] = new Vector4(worldXRange.x + i * ParticleSize * 0.25f, worldYRange.x-5.5f, worldZ, 0);
        }
        buffer.SetData(data);
    }

    void LateUpdate()
    {
        if (Input.GetKeyDown("space"))
        {
            int index = GetNextIndex();
            data[index] = new Vector4(
                Random.Range(worldXRange.x, worldXRange.y),
                Random.Range(worldYRange.x, worldYRange.y),
                worldZ,
                Time.time
            );
            
            buffer.SetData(data, index, index, 1);
        }

       
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 worldPos = ScreenToWorldInFixedRange(Input.mousePosition);

            int index = GetNextIndex();
            data[index] = new Vector4(worldPos.x, worldPos.y, worldZ, Time.time);
            
            buffer.SetData(data, index, index, 1);
        }
    }
    private int GetNextIndex()
    {
        int index = moveID % Capacity;
        moveID++;
        Debug.Log("Spawn ID: "+index +" at "+Time.time);
        return index;
    }
    
    private Vector3 ScreenToWorldInFixedRange(Vector3 mousePos)
    {
        float u = 0;
        float v = 0;

        if (Screen.width > 0)
            u = Mathf.Clamp01(mousePos.x / Screen.width);

        if (Screen.height > 0)
            v = Mathf.Clamp01(mousePos.y / Screen.height);

        float x = Mathf.Lerp(worldXRange.x, worldXRange.y, u);
        float y = Mathf.Lerp(worldYRange.x, worldYRange.y, v);

        return new Vector3(x, y, worldZ);
    }

    void OnDestroy()
    {
        buffer?.Release();
        buffer = null;
    }

    void OnDisable()
    {
        buffer?.Release();
        buffer = null;
    }
}
