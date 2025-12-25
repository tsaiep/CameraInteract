using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Random = UnityEngine.Random;

public class SetGraphicBufferAndSendVFXEvent : MonoBehaviour
{
    private const int Stride = 16;    // Vector4 (4 * 4 bytes)

    [Header("VFX Settings")]
    [SerializeField] private int capacity = 1024;           //一次生成粒子的系統的上限
    //[SerializeField] private bool sendEvent = true;         //是否傳送Event
    //[SerializeField] private int perEventSpawnAmount = 1;   //若使用Event，一次生成的粒子數量

    [Serializable]
    private class VFXBinding
    {
        [Header("VFX Property Names")]
        public VisualEffect vfx;
        public ExposedProperty spawnPointsBufferProperty = "SpawnPoints";     //綁定Graphic Buffer的Property的名稱
        public ExposedProperty spawnPointsCountProperty = "SpawnPointsCount"; // 綁定Buffer長度與一次生成粒子的系統的上限的Property的名稱
        public ExposedProperty groundSizeProperty = "GroundSize";             //地面長寬的Property的名稱

        [Header("VFX Event & Attribute Names")]
        public string spawnEventName = "SpawnOnce";                 //綁定start Event名稱
        public string spawnCountAttributeName = "OnceSpawnCount";   //綁定Event一次生成的粒子數量Attribute名稱
        public string spawnBufferIdAttributeName = "SpawnBufferId"; //綁定當次Event對應的Buffer ID的Attribute名稱

        public bool sendEvent = true;          // 新增：是否傳送Event
        public int perEventSpawnAmount = 1;    // 新增：一次生成粒子數

        [NonSerialized] public int spawnEventId;
        [NonSerialized] public VFXEventAttribute spawnEventAttribute;
    }

    [Header("VFX Bindings")]
    [SerializeField] private List<VFXBinding> vfxBindings = new List<VFXBinding>();

    // ───────────────── Ground Size ─────────────────
    [Header("Spawn Range (World)")]
    [SerializeField] private Vector2 worldXRange = new Vector2(-3f, 3f);
    [SerializeField] private Vector2 worldYRange = new Vector2(-3f, 3f);
    [SerializeField] private float worldZ = 0f;
    private Vector2 groundSizeWorld;

    // ───────────────── 緩衝區與索引 ─────────────────
    private GraphicsBuffer buffer;    // GPU 緩衝（固定容量）
    private Vector4[] data;           // CPU 鏡像資料
    private int writeIndex = 0;       // 下一個要寫入的索引（循環）

    // ───────────────── 初始化 / 綁定 ─────────────────
    private void OnEnable()
    {
        // 安全容量
        capacity = Mathf.Max(1, capacity);

        // 初始化 VFX 事件
        InitVFXEvent();

        // 建立 CPU / GPU Buffer
        data = new Vector4[capacity];
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Stride);

        // 計算地面範圍（給 VFX 使用）
        groundSizeWorld = new Vector2(
            math.abs(worldXRange.x) + math.abs(worldXRange.y),
            math.abs(worldYRange.x) + math.abs(worldYRange.y)
        );

        BindBufferAndStaticProperties();
        InitializeBufferData();
    }

    private void OnDisable()
    {
        ReleaseBuffer();
    }

    private void OnDestroy()
    {
        ReleaseBuffer();
    }

    // 初始化 event ID & eventAttribute，並做基本檢查
    private void InitVFXEvent()
    {
        if (vfxBindings == null) return;

        for (int i = 0; i < vfxBindings.Count; i++)
        {
            var b = vfxBindings[i];
            if (b == null || b.vfx == null)
            {
                if (b != null)
                {
                    b.spawnEventAttribute = null;
                    b.spawnEventId = 0;
                }
                continue;
            }

            if (string.IsNullOrEmpty(b.spawnEventName))
            {
                Debug.LogWarning("[SetGraphicBufferAndSendVFXEvent] spawnEventName is null");
                b.spawnEventId = 0;
            }
            else
            {
                b.spawnEventId = Shader.PropertyToID(b.spawnEventName);
            }

            b.spawnEventAttribute = b.vfx.CreateVFXEventAttribute();

#if UNITY_EDITOR
            // 只在 Editor 模式做一次型別檢查，避免打錯字沒發現
            if (!string.IsNullOrEmpty(b.spawnCountAttributeName) &&
                !b.spawnEventAttribute.HasInt(b.spawnCountAttributeName))
            {
                Debug.LogWarning(
                    $"[SetGraphicBufferAndSendVFXEvent] VFXEventAttribute 中沒有 int 屬性 \"{b.spawnCountAttributeName}\"，"
                );
            }

            if (!string.IsNullOrEmpty(b.spawnBufferIdAttributeName) &&
                !b.spawnEventAttribute.HasInt(b.spawnBufferIdAttributeName))
            {
                Debug.LogWarning(
                    $"[SetGraphicBufferAndSendVFXEvent] VFXEventAttribute 中沒有 int 屬性 \"{b.spawnBufferIdAttributeName}\"，"
                );
            }
#endif
        }
    }

    // ───────────────── 遊戲迴圈：輸入更新粒子 ─────────────────
    private void LateUpdate()
    {
        if (buffer == null || data == null) return;

        // 空白鍵：寫入隨機位置
        if (Input.GetKeyDown(KeyCode.Space))
        {
            int index = GetNextIndex();
            data[index] = new Vector4(
                Random.Range(worldXRange.x, worldXRange.y),
                Random.Range(worldYRange.x, worldXRange.y),
                worldZ,
                Time.time
            );

            buffer.SetData(data, index, index, 1);
            SendVFXEvent(index);
        }

        // 滑鼠左鍵：螢幕座標 → 世界範圍 → 寫入 + 送出事件
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 worldPos = ScreenToWorldInFixedRange(Input.mousePosition);

            int index = GetNextIndex();
            data[index] = new Vector4(worldPos.x, worldPos.y, worldZ, Time.time);

            buffer.SetData(data, index, index, 1);
            SendVFXEvent(index);
        }
    }

    // ───────────────── Buffer 操作 ─────────────────
    private int GetNextIndex()
    {
        int index = writeIndex % capacity;
        writeIndex++;
        Debug.Log($"Spawn ID: {index} at {Time.time}");
        return index;
    }

    private void InitializeBufferData()
    {
        // 初始資料：沿 X 排一條線
        for (int i = 0; i < capacity; i++)
        {
            data[i] = new Vector4(
                worldXRange.x + i * 0.25f,
                worldYRange.x - 5.5f,
                worldZ,
                0f
            );
        }

        buffer.SetData(data);
    }

    private void ReleaseBuffer()
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    // ───────────────── VFX Binding ─────────────────
    private void BindBufferAndStaticProperties()
    {
        if (vfxBindings == null) return;

        for (int i = 0; i < vfxBindings.Count; i++)
        {
            var b = vfxBindings[i];
            if (b == null || b.vfx == null) continue;

            if (b.vfx.HasGraphicsBuffer(b.spawnPointsBufferProperty))
                b.vfx.SetGraphicsBuffer(b.spawnPointsBufferProperty, buffer);
            else
                Debug.LogWarning($"[SetGraphicBufferAndSendVFXEvent] VFX 中沒有 GraphicsBuffer 屬性 \"{b.spawnPointsBufferProperty}\"。");

            if (b.vfx.HasInt(b.spawnPointsCountProperty))
                b.vfx.SetInt(b.spawnPointsCountProperty, capacity);
            else
                Debug.LogWarning($"[SetGraphicBufferAndSendVFXEvent] VFX 中沒有 Int 屬性 \"{b.spawnPointsCountProperty}\"。");

            if (b.vfx.HasVector2(b.groundSizeProperty))
                b.vfx.SetVector2(b.groundSizeProperty, groundSizeWorld);
        }
    }

    // ───────────────── Mouse to World Transform ─────────────────
    private Vector3 ScreenToWorldInFixedRange(Vector3 mousePos)
    {
        float u = Screen.width > 0 ? Mathf.Clamp01(mousePos.x / Screen.width) : 0f;
        float v = Screen.height > 0 ? Mathf.Clamp01(mousePos.y / Screen.height) : 0f;

        float x = Mathf.Lerp(worldXRange.x, worldXRange.y, u);
        float y = Mathf.Lerp(worldYRange.x, worldYRange.y, v);

        return new Vector3(x, y, worldZ);
    }

    // ───────────────── VFX Event ─────────────────
    public void SendVFXEvent(int index)
    {
        if (vfxBindings == null) return;

        for (int i = 0; i < vfxBindings.Count; i++)
        {
            var b = vfxBindings[i];
            if (b == null || b.vfx == null || b.spawnEventAttribute == null)
                continue;

            if (!b.sendEvent) // 新增：每個 VFX 可獨立決定是否送 Event
                continue;

            int finalCount = b.perEventSpawnAmount; // 新增：每個 VFX 可獨立設定數量

            // 檢查並寫入 spawnCount
            if (!string.IsNullOrEmpty(b.spawnCountAttributeName))
            {
                if (b.spawnEventAttribute.HasInt(b.spawnCountAttributeName))
                {
                    b.spawnEventAttribute.SetInt(b.spawnCountAttributeName, finalCount);
                }
                else
                {
                    Debug.LogWarning(
                        $"[SetGraphicBufferAndSendVFXEvent] 送 event 時找不到 int attribute \"{b.spawnCountAttributeName}\"。"
                    );
                }
            }

            // 檢查並寫入 SpawnBufferId
            if (!string.IsNullOrEmpty(b.spawnBufferIdAttributeName))
            {
                if (b.spawnEventAttribute.HasInt(b.spawnBufferIdAttributeName))
                {
                    b.spawnEventAttribute.SetInt(b.spawnBufferIdAttributeName, index);
                }
                else
                {
                    Debug.LogWarning(
                        $"[SetGraphicBufferAndSendVFXEvent] 送 event 時找不到 int attribute \"{b.spawnBufferIdAttributeName}\"。"
                    );
                }
            }

            // 事件名稱為空就不要送，避免送出奇怪的 default event
            if (b.spawnEventId == 0 && string.IsNullOrEmpty(b.spawnEventName))
                continue;

            b.vfx.SendEvent(b.spawnEventId, b.spawnEventAttribute);
        }
    }
}
