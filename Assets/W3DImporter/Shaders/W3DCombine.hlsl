#ifndef W3D_COMBINE_INCLUDED
#define W3D_COMBINE_INCLUDED

// Texture stage operations of the E&B client's shader setup (FUN_0093b510).
// Stage 0 combines the texture with the lighting gradient (W3D PriGradient), stage 1
// combines the result with the detail texture (DetailColorFunc / DetailAlphaFunc).

// PriGradient: 0 = disable (texture only), 1 = modulate, 2 = add, 5 = modulate 2x.
float3 W3DApplyPrimaryGradient(float3 tex, float3 gradient, int func)
{
    switch (func)
    {
        case 0: return tex;
        case 2: return saturate(tex + gradient);
        case 5: return saturate(tex * gradient * 2.0);
        default: return tex * gradient;
    }
}

float3 W3DApplyDetailColor(float4 localCol, float4 detailCol, int func)
{
    switch (func)
    {
        case 1: return detailCol.rgb;                                                    // SELECTARG1 (detail)
        case 2: return localCol.rgb * detailCol.rgb;                                     // MODULATE
        case 3: return saturate(localCol.rgb + detailCol.rgb - localCol.rgb * detailCol.rgb); // ADDSMOOTH
        case 4: return saturate(localCol.rgb + detailCol.rgb);                           // ADD
        case 5: return saturate(detailCol.rgb - localCol.rgb);                           // SUBTRACT (detail - current)
        case 6: return saturate(localCol.rgb - detailCol.rgb);                           // SUBTRACT (current - detail)
        case 7: return lerp(localCol.rgb, detailCol.rgb, detailCol.a);                   // BLENDTEXTUREALPHA
        case 8: return lerp(localCol.rgb, detailCol.rgb, localCol.a);                    // BLENDCURRENTALPHA
        case 9: return saturate(localCol.rgb + detailCol.rgb - 0.5);                     // ADDSIGNED
        case 10: return saturate((localCol.rgb + detailCol.rgb - 0.5) * 2.0);            // ADDSIGNED2X
        case 11: return saturate(localCol.rgb * detailCol.rgb * 2.0);                    // MODULATE2X (lightmaps)
        case 12: return saturate(localCol.rgb + localCol.a * detailCol.rgb);             // MODULATEALPHA_ADDCOLOR
        default: return localCol.rgb;
    }
}

float W3DApplyDetailAlpha(float localAlpha, float detailAlpha, int func)
{
    switch (func)
    {
        case 1: return detailAlpha;
        case 2: return saturate(localAlpha * detailAlpha);
        case 3: return saturate(localAlpha + detailAlpha - localAlpha * detailAlpha);
        default: return localAlpha;
    }
}

// Texture coordinates of the client's environment mappers (decoded from client.exe: the mapper
// Apply functions set D3DTSS_TCI_CAMERASPACENORMAL / CAMERASPACEREFLECTIONVECTOR plus a texture
// matrix). kind: 0 = mesh uv, 1 = camera-space reflection (Environment, Grid environment),
// 2 = camera-space normal (Cheap environment, Grid classic environment), 3 = world-space normal
// (WS classic environment), 4 = world-space reflection (WS environment). World-space kinds project
// the vector along the mapper's Axis (0 = X: u,v from y,z; 1 = Y: x,z; 2 = Z: x,y) in W3D axes.
// The result is v * 0.5 + 0.5, or for the grid mappers that range squeezed into one cell of the
// atlas: grid = (cells per row, frames per second, first frame, frame count).
float2 W3DTexGen(float kind, float axis, float4 grid, float2 meshUv,
                 float3 normalVS, float3 reflectVS, float3 normalWS, float3 reflectWS)
{
    int k = (int)floor(kind + 0.5);
    if (k == 0)
        return meshUv;

    float2 v;
    if (k == 1)
        v = reflectVS.xy;
    else if (k == 2)
        v = normalVS.xy;
    else
    {
        float3 w = k == 3 ? normalWS : reflectWS;
        w = float3(-w.x, -w.z, w.y); // Unity world -> W3D world (inverse of W3DCoordinateConverter)
        int a = (int)floor(axis + 0.5);
        v = a == 0 ? w.yz : (a == 1 ? w.xz : w.xy);
    }

    float2 uv = v * 0.5 + 0.5;
    if (grid.x > 1.5)
    {
        float frame = fmod(grid.z + floor(_Time.y * grid.y), max(grid.w, 1.0));
        uv = (uv + float2(fmod(frame, grid.x), floor(frame / grid.x))) / grid.x;
    }
    return uv;
}

#endif
