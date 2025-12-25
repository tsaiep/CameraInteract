float4 GetBufferVal(StructuredBuffer<float4> bf, float3 pos, int count, float3 properties)
{
    float3 f = float3(0,0,0);
    float st = 0;
    for (int i = 0; i<count; i++)
    {
        float3 p = bf[i].xyz;
        float3 v = pos-p;
        float s = pow(saturate(1-length(v)/properties.x),1);
        float3 nv = normalize(v);

        float fd = 0.1;
        float tfIn = pow((properties.y-bf[i].w)/fd,0.5);
        float tfOut = saturate(1-(properties.y-bf[i].w-fd)/properties.z);
        tfOut = tfOut*tfOut*(3-2*tfOut);
        float tf = tfIn*tfOut;
        
        float3 fv = nv*s*tf;

        st += s*tf;
        f += fv;	
    }
    return float4(f,st);
}

//float3 GetBufferVal(StructuredBuffer<float4> bf, float3 pos, int count, float radius, float totalTime, float fadeOutTime)