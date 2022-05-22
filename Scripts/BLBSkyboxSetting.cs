using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct BLBSkyboxSetting
{
    public float SunSize;
    public int SunSizeConvergence;
    public float AtmosphereThickness;
    public string SkyTint;
    public string GroundColor;
    public float Exposure;
    public float NightStartHeight;
    public float NightEndHeight;
    public float SkyFadeStart;
    public float SkyEndStart;
    public float FogDistance;

    public string topCloudsFlat;
    public string bottomCloudsFlat;
    [NonSerialized]
    public BLBCloudsSetting topClouds;
    [NonSerialized]
    public BLBCloudsSetting bottomClouds;

}
