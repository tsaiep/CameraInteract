float4 GetBufferVal(StructuredBuffer<float4> bf, float3 spPt, float4 properties)
{
    float3 p = float3(0,100,0);
    float minPt = 1000;
    //float st = 0;
    for (int i = 0; i < properties.w; i++)
    {
        float3 ip = bf[i].xyz;
        float pDist = length(ip - spPt);
        if (pDist < minPt)
        {
            p = ip;
            minPt = pDist;
        }
    }
    
    float s = saturate(1-minPt/properties.x);
    
    return float4(p, s);
}