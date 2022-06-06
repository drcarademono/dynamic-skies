using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct BLBCloudsSetting
{
    [NonSerialized]
    public Texture2D cloudsTexture;
    public string cloudsTextureFile;
    [NonSerialized]
    public Texture2D cloudsNormalTexture;
    public string cloudsNormalTextureFile;
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
    public float Bending;
}
