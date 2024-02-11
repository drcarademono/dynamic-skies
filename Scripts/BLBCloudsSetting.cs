using System;
using UnityEngine;

[Serializable]
public struct BLBCloudsSetting
{
    [NonSerialized]
    public Texture2D CloudsTexture;
    public string CloudsTextureFile;
    [NonSerialized]
    public Texture2D CloudsNormalTexture;
    public string CloudsNormalTextureFile;
    public float TilingX;
    public float TilingY;
    public float OffsetX;
    public float OffsetY;
    public string DayColor;
    public string NightColor;
    public float AlphaTreshold;
    public float AlphaMax;
    public  float ColorBoost;
    public float NormalEffect;
    public float NormalSpeed;
    public float Opacity;
    public float Speed;
    public float Direction;
    public float Bending;
    public float BlendSpeed;
    public float BlendScale;
    public float BlendLB;
    public float BlendUB;
    public float SunColorScale;
    public float SunColorLerpScale;
    public string SunColor;

}
