using System;
using UnityEngine;

[Serializable]
public struct BLBStarsSetting
{
    [NonSerialized]
    public Texture2D StarsTexture;
    public string StarsTextureFile;
    public float StarsTilingX;
    public float StarsTilingY;
    public float StarsOffsetX;
    public float StarsOffsetY;
    public float StarBending;
    public float StarBrightness;
    [NonSerialized]
    public Texture2D TwinkleTexture;
    public string TwinkleTextureFile;
    public float TwinkleTilingX;
    public float TwinkleTilingY;
    public float TwinkleOffsetX;
    public float TwinkleOffsetY;
    public float TwinkleBoost;
    public float TwinkleSpeed;
}