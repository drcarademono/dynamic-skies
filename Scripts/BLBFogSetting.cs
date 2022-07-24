using System;
using UnityEngine;

[Serializable]
public struct BLBFogSetting
{
    [NonSerialized]
    public FogMode FogMode;
    public int FogModeInt;
    public float Density;
    public float StartDistance;
    public float EndDistance;
    public bool ExcludeSkybox;
}