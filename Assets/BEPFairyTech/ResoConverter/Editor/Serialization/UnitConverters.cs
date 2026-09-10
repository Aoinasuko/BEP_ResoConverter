// Copyright (c) 2025 bd_, MIT license. See UPSTREAM-LICENSE.txt.
#nullable enable
using UnityEngine;
using p = nadena.dev.ndmf.proto;

namespace BEPFairyTech.ResoConverter
{
internal static class UnitConverters
{
    public static p::Vector ToRPC(this UnityEngine.Vector2 v) => new() { X = v.x, Y = v.y };
    public static p::Vector ToRPC(this UnityEngine.Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };
    public static p::Vector ToRPC(this UnityEngine.Vector4 v) => new() { X = v.x, Y = v.y, Z = v.z, W = v.w };
    public static p::Quaternion ToRPC(this UnityEngine.Quaternion q) => new() { X = q.x, Y = q.y, Z = q.z, W = q.w };
    
    public static p::Color ToRPC(this Color c) => new() { R = c.r, G = c.g, B = c.b, A = c.a };
}
}
