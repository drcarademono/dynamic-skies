using System;

[Serializable]
public struct BLBSkyboxSetting
{
    public float SunSize;
    public int SunSizeConvergence;
    public float AtmosphereLerpDuration;
    public float AtmosphereNormalThickness;
    public float AtmosphereDawnDuskThickness;
    public float AtmosphereLerp;
    public string SkyTint;
    public string GroundColor;
    public string AmbientColor;
    public float AmbientIntensity;
    public float Exposure;
    public float NightStartHeight;
    public float NightEndHeight;
    public float SkyFadeStart;
    public float SkyEndStart;
    public float stepSize;
    public string FogDayColor;
    public string FogNightColor;
    public float FogDistance;
    public float CloudFadeHeight;

    public string TopCloudsFlat;
    public string BottomCloudsFlat;
    public string StarsFlat;
    public string MasserFlat;
    public string SecundaFlat;
    [NonSerialized]
    public BLBCloudsSetting TopClouds;
    [NonSerialized]
    public BLBCloudsSetting BottomClouds;
    [NonSerialized]
    public BLBStarsSetting Stars;
    [NonSerialized]
    public BLBMoonSetting Masser;
    [NonSerialized]
    public BLBMoonSetting Secunda;
}
