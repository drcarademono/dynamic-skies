using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;   //required for modding features
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Weather;

public class BLBSkybox : MonoBehaviour
{
    public static Mod Mod {
        get;
        private set;
    }
    public static BLBSkybox Instance { get; private set; }

    #region General properties
    private static string skyboxMaterialName = "BLBSkyboxMaterial";
    private Material skyboxMat; //Reference to the skybox material so we can change properties
    private Camera playerCam;   //Reference to player cam to manage clear settings
    private GameObject dfSky;   //Reference to classic Daggerfall sky object so we can disable it
    private Light dfSunlight;   //Reference to the SunRig's main light to change the sun color
    private WorldTime worldTime; //Reference to Daggerfall Unity's time management object to detect changed TimeScale
    private DayParts currentDayPart = DayParts.None; //Keeps track of the current part of the day
    private float originalTimeScale;    //Original time scale when game started (not used atm)
    private float currentTimeScale; //Keeps track of the current timescale to update speeds if it changes
    private bool forceWeatherUpdate = false;
    #endregion

    #region Material Settings
    private float sunSize = 0.04f;
    private float sunConvergence = 2f;
    private float atmosphere = 0.75f;
    private Color skyTint = new Color(0.5294118f, 0.8078431f, 0.9215686f, 1.0f);
    private float nightStartHeight = 0.01f;
    private float nightEndHeight = -0.01f;
    private float skyFadeStart = -0.01f;
    private float skyFadeEnd = -0.04f;
    #endregion

    [Invoke(StateManager.StateTypes.Start, 0)]
    public static void Init(InitParams initParams)
    {
        Mod = initParams.Mod;  // Get mod     
        Instance = new GameObject("BLBSkybox").AddComponent<BLBSkybox>(); // Add script to the scene.

        //Set up some default settings for the Unity renderer
        UnityEngine.RenderSettings.fogColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        UnityEngine.RenderSettings.fogEndDistance = 2048f;

        //Subscribe to DFU events
        WeatherManager.OnWeatherChange += Instance.OnWeatherChange; //Register event for weather changes
        PlayerEnterExit.OnTransitionInterior += Instance.InteriorTransitionEvent; //interior transition
        PlayerEnterExit.OnTransitionDungeonInterior += Instance.InteriorTransitionEvent; //dungeon interior transition
        PlayerEnterExit.OnTransitionExterior += Instance.ExteriorTransitionEvent; //exterior transition
        PlayerEnterExit.OnTransitionDungeonExterior += Instance.ExteriorTransitionEvent; //dungeon exterior transition

        //Prepare the cloud types, lunar phases and fog settings
        Instance.setCloudTypes();
        Instance.setLunarPhases();
        Instance.getVanillaFogSettings();
        //Instance.setFogSettings(); //Overwrites vanilla fog with linear fog settings

        //Load the skybox material
        Instance.skyboxMat = Mod.GetAsset<Material>("Materials/" + skyboxMaterialName) as Material;

        //Store a reference to the SkyRig game object
        Instance.dfSky = GameManager.Instance.SkyRig.gameObject;
        //Store a reference to the game's time management object
        Instance.worldTime = DaggerfallUnity.Instance.WorldTime;
        //Store the original timescale and set the currentTimeScale to it
        Instance.originalTimeScale = Instance.worldTime.TimeScale;
        Instance.currentTimeScale = Instance.originalTimeScale;
        //Store a reference to the SunRig's main light
        Instance.dfSunlight = GameObject.Find("SunLight").GetComponent("Light") as Light;
        //Store a reference to the player camera
        Instance.playerCam = GameObject.FindGameObjectWithTag("MainCamera").GetComponent("Camera") as Camera;
        Instance.wm = GameManager.Instance.WeatherManager;

        //Set wind direction on material
        Instance.skyboxMat.SetFloat("_AtmosphereThickness", Instance.atmosphere);
        Instance.skyboxMat.SetColor("_SkyTint", Instance.skyTint);
        Instance.skyboxMat.SetFloat("_SunSize", Instance.sunSize);
        Instance.skyboxMat.SetFloat("_SunSizeConvergence", Instance.sunConvergence);
        Instance.skyboxMat.SetFloat("_SkyFadeStart", Instance.skyFadeStart);
        Instance.skyboxMat.SetFloat("_SkyFadeEnd", Instance.skyFadeEnd);
        Instance.skyboxMat.SetFloat("_NightStartHeight", Instance.nightStartHeight);
        Instance.skyboxMat.SetFloat("_NightEndHeight", Instance.nightEndHeight);
        Instance.skyboxMat.SetFloat("_CloudDirection", Instance.getWindDirection());

        //Disable the vanilla sky
        Instance.dfSky.SetActive(false);

        //Change the clear flags in the camera clear manager, otherwise it would unset the skybox in its Update method after an exterior transition
        CameraClearManager ccm = Instance.playerCam.GetComponent<CameraClearManager>();
        ccm.cameraClearExterior = CameraClearFlags.Skybox;

        //Turn on the skybox
        Instance.ToggleSkybox(true);

        UnityEngine.RenderSettings.fogColor = Instance.fogDayColor;
        
        Instance.currentWeather = WeatherType.None;
        Instance.forceWeatherUpdate = true;
        Instance.OnWeatherChange(Instance.currentWeather);
        Instance.SetExposure(Instance.currentWeather);

        Instance.updateSpeeds();

        Debug.Log("BLB Skybox has been set up");
    }

    void Awake ()
    {
        Mod.IsReady = true;
        Debug.Log("blb-skybox awakened");
    }

    private float deltaTime = 0.0f; //Counter to limit Update() calls to once per 5 seconds
    public void Update()
    {
        //When the timescale is altered, adjust the cloud speeds accordingly or they would move in slow-mo
        //Would really like an OnTimeScaleChange event for this but it works
        if(worldTime.TimeScale != currentTimeScale) {
            currentTimeScale = worldTime.TimeScale;
            updateSpeeds();
        }
        deltaTime += Time.unscaledDeltaTime;
        if(deltaTime < 5.0f) {
            return;
        }
        //Reset deltaTime when 5 seconds have elapsed
        deltaTime -= 5.0f;
        //Get the current time of day
        int hour = worldTime.Now.Hour;
        int minutes = worldTime.Now.Minute;
        //Determine part of the day
        //The skyboxLerpDuration calculation in each day part is partial, minutes are substracted after these if statements
        //currentDayPart is set to prevent the lerp from firing again
        if (isHourDayPart(hour, DayParts.Night) && currentDayPart != DayParts.Night) {
            //0:00 - 05:00
            currentDayPart = DayParts.Night;
            setFogColor(false);
            if(currentLunarPhase != worldTime.Now.MassarLunarPhase) {
                ChangeLunarPhases();
            }
        } else if (isHourDayPart(hour, DayParts.Dawn) && currentDayPart != DayParts.Dawn) {
            //05:00 - 07:00
            currentDayPart = DayParts.Dawn;
            OnDawn();
        } else if (isHourDayPart(hour, DayParts.Morning) && currentDayPart != DayParts.Morning) {
            //07:00 - 12:00
            currentDayPart = DayParts.Morning;
            setFogColor(true);
            if(currentLunarPhase != worldTime.Now.MassarLunarPhase) {
                ChangeLunarPhases();
            }
        } else if (isHourDayPart(hour, DayParts.Midday) && currentDayPart != DayParts.Midday) {
            //12:00 - 17:00
            currentDayPart = DayParts.Midday;
            setFogColor(true);
        } else if (isHourDayPart(hour, DayParts.Dusk) && currentDayPart != DayParts.Dusk) {
            //17:00 - 19:00
            currentDayPart = DayParts.Dusk;
            OnDusk();
        } else if (isHourDayPart(hour, DayParts.Evening) && currentDayPart != DayParts.Evening) {
            //20:00 - 0:00
            currentDayPart = DayParts.Evening;
            setFogColor(false);
        }
        ApplyPendingWeatherSettings();
    }

    #region Helper methods
    public void ToggleSkybox(bool activate) {
        //Change skybox material and sun assignment for the Unity renderer and change the clearFlags setting on the player camera
        if(activate) {
            UnityEngine.RenderSettings.skybox = skyboxMat;
            UnityEngine.RenderSettings.sun = dfSunlight;
            playerCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
        } else {
            UnityEngine.RenderSettings.skybox = null;
            UnityEngine.RenderSettings.sun = null;
            playerCam.clearFlags = UnityEngine.CameraClearFlags.Depth;
        }
    }

    private void updateSpeeds() {
        //Updates speeds when TimeScale has been changed by multiplying the realtime speed with the currentTimeScale
        skyboxMat.SetFloat("_CloudSpeed", cloudSpeed * currentTimeScale);
        skyboxMat.SetFloat("_MoonOrbitSpeed", moonOrbitSpeed * currentTimeScale);
        skyboxMat.SetFloat("_SecundaOrbitSpeed", moonOrbitSpeed * currentTimeScale);
        skyboxMat.SetFloat("_TwinkleSpeed", starsTwinkleSpeed * currentTimeScale);
    }
    #endregion

    #region Day / Night transition
    //Start the sun and cloud lerp for dawn
    private void OnDawn() {
        HandleDawnDusk(DayParts.Dawn);
    }

    private void OnMidday() {
        //Not used atm
    }

    //Start the sun and cloud lerp for dusk
    private void OnDusk() {
        HandleDawnDusk(DayParts.Dusk);
    }

    //Handles starting the lerp for the sun and skybox
    private void HandleDawnDusk(DayParts dayPart) {
        if(dayPart == DayParts.Dawn) {
            sunStartColor = getSunColor(4);
            sunEndColor = getSunColor(12);
            fogStartColor = fogNightColor;
            fogEndColor = fogDayColor;
        } else if(dayPart == DayParts.Dusk) {
            sunStartColor = getSunColor(18);
            sunEndColor = getSunColor(2);
            fogStartColor = fogDayColor;
            fogEndColor = fogNightColor;
        }
        sunFogLerpDuration = calculateScaledLerpDuration(2);
        StopCoroutine("SunFogLerp");
        sunFogLerpRunning = true;
        StartCoroutine("SunFogLerp");
    }

        //The different day parts we can identify in isHourDayPart
    private enum DayParts {
        None,
        Dawn,
        Morning,
        Midday,
        Dusk,
        Evening,
        Night
    }
    //Determines if an hour of the day falls in a certain day part
    private bool isHourDayPart(int hour, DayParts dayPart) {
        //00:00 - 04:00
        if ((hour >= 0 && hour < 5) && dayPart == DayParts.Night) {
            return true;
        //04:00 - 08:00
        } else if (hour >= 5 && hour < 7 && dayPart == DayParts.Dawn) {
            return true;
        //08:00 - 12:00
        } else if (hour >= 7 && hour < 12 && dayPart == DayParts.Morning) {
            return true;
        //12:00 - 16:00
        } else if (hour >= 12 && hour < 17 && dayPart == DayParts.Midday) {
            return true;
        //17:00 - 19:00
        } else if (hour >= 17 && hour < 20 && dayPart == DayParts.Dusk) {
            return true;
        //20:00 - 23:00
        } else if (hour >= 20 && dayPart == DayParts.Evening) {
            return true;
        }
        return false;
    }
    #endregion

    #region Sun / Fog lerp
    private bool sunFogLerpRunning = false; //Indicates if SunLerp is running
    float sunFogLerpDuration; //Sun lerp duration in seconds
    Color sunStartColor; //Start value for sun color lerp
    Color sunEndColor; //End value for the sun color lerp

    IEnumerator SunFogLerp()
    {
        //Animated the sun color
        float timeElapsed = 0;
        while (timeElapsed < sunFogLerpDuration)
        {
            dfSunlight.color = Color.Lerp(sunStartColor, sunEndColor, timeElapsed / sunFogLerpDuration);
            UnityEngine.RenderSettings.fogColor = Color.Lerp(fogStartColor, fogEndColor, timeElapsed / sunFogLerpDuration);
            timeElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        UnityEngine.RenderSettings.fogColor = fogEndColor;
        dfSunlight.color = sunEndColor;
        sunFogLerpRunning = false;
    }
    //Calculates the duration of the lerp in seconds scaled to the TimeScale to have proper sun rise / set
    private float calculateScaledLerpDuration(float targetDurationHours) {
        float lerpTime = (DaggerfallDateTime.SecondsPerHour / worldTime.TimeScale) * targetDurationHours;
        return Mathf.Max(1.0f, lerpTime);
    }
    #endregion

    #region Weather
    private bool pendingWeather = false; //Indicates if a weather change is pending - need to figure out best way to handle this during a sun / cloud color lerp
    private float pendingWindDirection; //The new wind direction
    private CloudTypeStruct pendingCloudTop; //The new settings for the cloud top layer
    private CloudTypeStruct pendingCloud; //The new settings for the cloud layer
    private CloudColorStruct pendingCloudColors; //The new settings for cloud colors (both layers)
    private WeatherType currentWeather = WeatherType.Sunny; //Keeps track of the current weather to detect a change
    private void OnWeatherChange(WeatherType weather) {
        //Only change weather if it's not the same weather as the current weather
        if(weather != currentWeather || forceWeatherUpdate == true) {
            forceWeatherUpdate = false;
            currentWeather = weather;

            pendingWindDirection = getWindDirection(); //Get new random wind direction

            CloudTypeStruct cloudTop = CloudsTop[weather];   //Get cloud type for top layer
            CloudTypeStruct cloud = Clouds[weather];
            CloudColorStruct cloudColors = CloudColors[weather]; //Get cloud colors for both layers

            //TODO: Add several cloud variations based on weather type

            //Store the new cloud settings
            pendingCloud = cloud;
            pendingCloudTop = cloudTop;
            pendingCloudColors = cloudColors;
            
            //Apply the pending weather settings
            ApplyPendingWeatherSettings();
        }
    }

    private void ApplyPendingWeatherSettings() {
        //Change cloud colors
        skyboxMat.SetColor("_CloudColor", pendingCloudColors.CloudColor);
        skyboxMat.SetColor("_CloudNightColor", pendingCloudColors.CloudNightColor);
        skyboxMat.SetColor("_CloudTopColor", pendingCloudColors.CloudTopColor);
        skyboxMat.SetColor("_CloudTopNightColor", pendingCloudColors.CloudTopNightColor);

        //Change settings for cloud top layer
        skyboxMat.SetTextureScale("_CloudTopDiffuse", new Vector2(pendingCloudTop.TilingX, pendingCloudTop.TilingY));
        skyboxMat.SetFloat("_CloudTopAlphaCutoff", pendingCloudTop.AlphaThreshold);
        skyboxMat.SetFloat("_CloudTopAlphaMax", pendingCloudTop.AlphaMax);
        skyboxMat.SetFloat("_CloudTopNormalEffect", pendingCloudTop.NormalEffect);
        skyboxMat.SetFloat("_CloudTopBending", pendingCloudTop.Bending);
        skyboxMat.SetFloat("_CloudTopOpacity", pendingCloudTop.Opacity);

        //Change settings for cloud layer
        skyboxMat.SetFloat("_CloudDirection", pendingWindDirection);
        skyboxMat.SetTextureScale("_CloudDiffuse", new Vector2(pendingCloud.TilingX, pendingCloud.TilingY));
        skyboxMat.SetFloat("_CloudAlphaCutoff", pendingCloud.AlphaThreshold);
        skyboxMat.SetFloat("_CloudAlphaMax", pendingCloud.AlphaMax);
        skyboxMat.SetFloat("_CloudNormalEffect", pendingCloud.NormalEffect);
        skyboxMat.SetFloat("_CloudBending", pendingCloud.Bending);
        skyboxMat.SetFloat("_CloudBlendScale", pendingCloud.BlendScale);
        skyboxMat.SetFloat("_CloudOpacity", pendingCloud.Opacity);

        //Change exposure to match Daggerfall Unity's sunlight reduction - might be a better way
        SetExposure(currentWeather);
        pendingWeather = false;
    }

    private WeatherManager wm;
    //Adjusts exposure and fog of the skybox according to Daggerfall Unity's weather sun light scales
    private void SetExposure(WeatherType weather) {
        
        float exposure = 1.0f;
        if(weather == WeatherType.Cloudy) {
            exposure = 0.9f;
        } else if(weather == WeatherType.Overcast) {
            exposure = wm.OvercastSunlightScale;
        } else if(weather == WeatherType.Fog) {
            exposure = wm.OvercastSunlightScale;
        } else if(weather == WeatherType.Rain) {
            exposure = wm.RainSunlightScale;
        } else if(weather == WeatherType.Thunder) {
            exposure = wm.StormSunlightScale;
        } else if(weather == WeatherType.Snow) {
            exposure = wm.SnowSunlightScale;
        }

        float fogDistance;
        WeatherManager.FogSettings fogSettings = FogSettings[weather];
        if(fogSettings.fogMode == FogMode.Linear) {
            fogDistance = fogSettings.endDistance * 0.25f;
        } else {
            fogDistance = 1f / fogSettings.density;
        }
        fogDayDistance = fogDistance;
        fogNightDistance = fogDayDistance;
        
        UnityEngine.RenderSettings.fogEndDistance = fogSettings.endDistance;

        skyboxMat.SetFloat("_Exposure", exposure * 1.25f);
        skyboxMat.SetFloat("_FogDistance", fogDistance);
    }

    //Returns a random wind direction
    private float getWindDirection() {
        return Random.Range(0f, 360f);
    }

    private Color getWeatherSunColor(WeatherType weather) {
        Color sunColor;
        switch(weather) {
            case WeatherType.Overcast:
                sunColor = new Color(0.6f, 0.6f, 0.6f, 1.0f);
                break;
            case WeatherType.Fog:
                sunColor = new Color(0.4f, 0.4f, 0.4f, 1.0f);
                break;
            case WeatherType.Rain:
                sunColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
                break;
            case WeatherType.Thunder:
                sunColor = new Color(0.45f, 0.45f, 0.45f, 1.0f);
                break;
            case WeatherType.Snow:
                sunColor = new Color(0.375f, 0.375f, 0.375f, 1.0f);
                break;
            default:
                sunColor = new Color(1.0f, 1.0f, 0.9843137f, 1.0f);
                break;
        }
        return sunColor;
    }
    #endregion

    #region Sun
    //Get the sun color for the time of day, needs a lot of tweaking
    private Color getSunColor(int hour) {
        Color color = new Color(1.0f, 1.0f, 1.0f, 1f);
        //Night
        if(hour >= 0 && hour < 5) {
            color.r = 0.8705882f;
            color.g = 0.3803922f;
            color.b = 0.1607843f;
        //Dawn
        } else if(hour >= 5 && hour < 8) {
            color.r = 1f;
            color.g = 0.7607843f;
            color.b = 0.3215686f;
        //Morning
        } else if(hour >= 8 && hour < 12) {
            color.r = 0.9647059f;
            color.g = 0.9803922f;
            color.b = 0.8039216f;
        //Midday
        } else if(hour >= 12 && hour < 16) {
            color.r = 0.9647059f;
            color.g = 0.9803922f;
            color.b = 0.8039216f;          
        //Dusk
        } else if(hour >= 16 && hour < 20) {
            color.r = 0.8705882f;
            color.g = 0.3803922f;
            color.b = 0.1607843f;
        //Evening
        } else if(hour >= 20) {
            color.r = 0.8705882f;
            color.g = 0.3803922f;
            color.b = 0.1607843f;
        }
        return color;
    }
    #endregion

    #region Clouds
    private float cloudSpeed = 0.001f / 12; //Default sloud speed in realtime (timescale = 1)
    //TODO: needs to rethink this and tie them to weather types instead
    //Defines the settings needed for the skybox material to get a specific type of cloud
    struct CloudTypeStruct {
        public CloudTypeStruct(float tilingX, float tilingY, float alphaThreshold, float alphaMax, float colorBoost, float normalEffect, float bending, float blendScale, float opacity) {
            TilingX = tilingX;
            TilingY = tilingY;
            AlphaThreshold = alphaThreshold;
            AlphaMax = alphaMax;
            ColorBoost = colorBoost;
            NormalEffect = normalEffect;
            Bending = bending;
            BlendScale = blendScale;
            Opacity = opacity;
        }
        public float TilingX;
        public float TilingY;
        public float AlphaThreshold;
        public float AlphaMax;
        public float ColorBoost;
        public float NormalEffect;
        public float Bending;
        public float BlendScale;
        public float Opacity;
    }

    //Defines the colors for both cloud layers
    struct CloudColorStruct {
        public CloudColorStruct(Color cloudColor, Color cloudNightColor, Color cloudTopColor, Color cloudTopNightColor) {
            CloudColor = cloudColor;
            CloudNightColor = cloudNightColor;
            CloudTopColor = cloudTopColor;
            CloudTopNightColor = cloudTopNightColor;
        }
        public Color CloudColor;
        public Color CloudNightColor;
        public Color CloudTopColor;
        public Color CloudTopNightColor;
    }

    //Dictionaries to store cloud settings
    private Dictionary<WeatherType, CloudTypeStruct> Clouds;
    private Dictionary<WeatherType, CloudTypeStruct> CloudsTop;
    private Dictionary<WeatherType, CloudColorStruct> CloudColors;
    
    //Set up the available cloud types
    private void setCloudTypes() {
        CloudsTop = new Dictionary<WeatherType, CloudTypeStruct>();
        Clouds = new Dictionary<WeatherType, CloudTypeStruct>();

        CloudsTop.Add(WeatherType.Sunny,    new CloudTypeStruct(0.25f, 0.25f, 0.0f, 1.0f, 0.0f, 0.64f, 0.56f, 0.0f, 0.0f));
        Clouds.Add(WeatherType.Sunny,       new CloudTypeStruct(0.125f, 0.125f, 0.0f, 1.0f, 0.0f, 0.48f, 0.48f, 0.25f, 0.0f));

        CloudsTop.Add(WeatherType.Cloudy,   new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.32f, 0.0f, 0.64f, 0.56f, 0.0f, 1.0f));
        Clouds.Add(WeatherType.Cloudy,      new CloudTypeStruct(0.125f, 0.125f, 0.0f, 0.32f, 0.0f, 0.48f, 0.48f, 0.25f, 1.0f));
        
        CloudsTop.Add(WeatherType.Overcast, new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.024f, 0.0f, 0.64f, 0.56f, 0.0f, 1.0f));
        Clouds.Add(WeatherType.Overcast,    new CloudTypeStruct(0.125f, 0.125f, 0.0f, 0.096f, 0.0f, 0.48f, 0.48f, 0.25f, 1.0f));
        
        CloudsTop.Add(WeatherType.Fog,      new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.000f, 0.0f, 0.64f, 0.56f, 0.0f, 1.0f));
        Clouds.Add(WeatherType.Fog,         new CloudTypeStruct(0.125f, 0.125f, 0.0f, 0.024f, 0.0f, 0.48f, 0.48f, 0.25f, 1.0f));
        
        CloudsTop.Add(WeatherType.Rain,     new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.000f, 0.0f, 0.64f, 0.56f, 0.0f, 1.0f));
        Clouds.Add(WeatherType.Rain,        new CloudTypeStruct(0.125f, 0.125f, 0.0f, 0.048f, 0.0f, 0.48f, 0.48f, 0.25f, 1.0f));

        CloudsTop.Add(WeatherType.Thunder,  new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.000f, 0.0f, 0.64f, 0.56f, 0.0f, 1.0f));
        Clouds.Add(WeatherType.Thunder,     new CloudTypeStruct(0.125f, 0.125f, 0.0f, 0.032f, 0.0f, 0.48f, 0.48f, 0.25f, 1.0f));

        CloudsTop.Add(WeatherType.Snow,     new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.008f, 0.0f, 0.64f,  0.56f, 0.0f, 1.0f));
        Clouds.Add(WeatherType.Snow,        new CloudTypeStruct(0.125f, 0.125f, 0.0f, 0.064f,  0.0f, 0.48f, 0.48f, 0.25f, 1.0f));

        Color cloudColor;
        Color cloudNightColor;
        Color cloudTopColor;
        Color cloudTopNightColor;
        CloudColorStruct cloudColors;

        CloudColors = new Dictionary<WeatherType, CloudColorStruct>();

        cloudTopColor = new Color(0.9056604f, 0.9056604f, 0.9056604f);
        cloudColor = new Color(0.8873585f, 0.8873585f, 0.8873585f);
        cloudTopNightColor = new Color(0.09056604f, 0.09056604f, 0.09056604f);
        cloudNightColor = new Color(0.08873585f, 0.08873585f, 0.08873585f);

        cloudColors = new CloudColorStruct(cloudColor, cloudNightColor, cloudTopColor, cloudTopNightColor);
        CloudColors.Add(WeatherType.Sunny, cloudColors);
        CloudColors.Add(WeatherType.Cloudy, cloudColors);

        cloudTopColor = new Color(0.6415094f, 0.6415094f, 0.6415094f);
        cloudColor = new Color(0.6647169f, 0.6647169f, 0.6647169f);
        cloudTopNightColor = new Color(0.06415094f, 0.06415094f, 0.06415094f);
        cloudNightColor = new Color(0.06647169f, 0.06647169f, 0.06647169f);

        cloudColors = new CloudColorStruct(cloudColor, cloudNightColor, cloudTopColor, cloudTopNightColor);
        CloudColors.Add(WeatherType.Overcast, cloudColors);

        cloudTopColor = new Color(0.5849056f, 0.5849056f, 0.5849056f);
        cloudColor = new Color(0.6037736f, 0.6037736f, 0.6037736f);
        cloudTopNightColor = new Color(0.05849056f, 0.05849056f, 0.05849056f);
        cloudNightColor = new Color(0.06037736f, 0.06037736f, 0.06037736f);

        cloudColors = new CloudColorStruct(cloudColor, cloudNightColor, cloudTopColor, cloudTopNightColor);
        CloudColors.Add(WeatherType.Fog, cloudColors);

        cloudTopColor = new Color(0.509434f, 0.509434f, 0.509434f);
        cloudColor = new Color(0.5283019f, 0.5283019f, 0.5283019f);
        cloudTopNightColor = new Color(0.0509434f, 0.0509434f, 0.0509434f);        
        cloudNightColor = new Color(0.05283019f, 0.05283019f, 0.05283019f);

        cloudColors = new CloudColorStruct(cloudColor, cloudNightColor, cloudTopColor, cloudTopNightColor);
        CloudColors.Add(WeatherType.Rain, cloudColors);

        cloudTopColor = new Color(0.409434f, 0.409434f, 0.409434f);
        cloudColor = new Color(0.4283019f, 0.4283019f, 0.4283019f);
        cloudTopNightColor = new Color(0.0409434f, 0.0409434f, 0.0409434f);
        cloudNightColor = new Color(0.04283019f, 0.04283019f, 0.04283019f);

        cloudColors = new CloudColorStruct(cloudColor, cloudNightColor, cloudTopColor, cloudTopNightColor);
        CloudColors.Add(WeatherType.Thunder, cloudColors);

        cloudTopColor = new Color(0.9224528f, 0.9224528f, 0.9224528f);
        cloudColor = new Color(0.9433962f, 0.9433962f, 0.9433962f);
        cloudTopNightColor = new Color(0.09224528f, 0.09224528f, 0.09224528f);
        cloudNightColor = new Color(0.09433962f, 0.09433962f, 0.09433962f);

        cloudColors = new CloudColorStruct(cloudColor, cloudNightColor, cloudTopColor, cloudTopNightColor);
        CloudColors.Add(WeatherType.Snow, cloudColors);
    }
    #endregion

    #region Moons
    private float moonOrbitSpeed = 0.0005f / 12; //Default moon orbit speed in realtime
    private void ChangeLunarPhases() {
        currentLunarPhase = worldTime.Now.MassarLunarPhase;
        Vector4 lunarPhase = new Vector4(LunarPhaseStates[currentLunarPhase].X, LunarPhaseStates[currentLunarPhase].Y, 0, 0);
        skyboxMat.SetVector("_MoonPhase", lunarPhase);
        skyboxMat.SetVector("_SecundaPhase", lunarPhase);
    }
    private LunarPhases currentLunarPhase = LunarPhases.None; //Reference to the current lunar phase (both moons)
    private bool pendingLunarPhase = false;
    private LunarPhaseCoordinates pendingLunarPhaseCoordinates; //New phase coordinates
    struct LunarPhaseCoordinates {
        public LunarPhaseCoordinates(int x, int y) {
            X = x;
            Y = y;
        }
        public int X;
        public int Y;
    }
    private Dictionary<DaggerfallWorkshop.LunarPhases, LunarPhaseCoordinates> LunarPhaseStates;
    private void setLunarPhases() {
        LunarPhaseStates = new Dictionary<LunarPhases, LunarPhaseCoordinates>();
        LunarPhaseStates.Add(LunarPhases.New, new LunarPhaseCoordinates(180, 0));
        LunarPhaseStates.Add(LunarPhases.OneWax, new LunarPhaseCoordinates(135, 0));
        LunarPhaseStates.Add(LunarPhases.HalfWax, new LunarPhaseCoordinates(90, 0));
        LunarPhaseStates.Add(LunarPhases.ThreeWax, new LunarPhaseCoordinates(45, 0));
        LunarPhaseStates.Add(LunarPhases.Full, new LunarPhaseCoordinates(0, 0));
        LunarPhaseStates.Add(LunarPhases.ThreeWane, new LunarPhaseCoordinates(-45, -45));
        LunarPhaseStates.Add(LunarPhases.HalfWane, new LunarPhaseCoordinates(-90, -45));
        LunarPhaseStates.Add(LunarPhases.OneWane, new LunarPhaseCoordinates(-135, -45));
    }
    #endregion

    #region Stars
    private float starsTwinkleSpeed = 0.001f / 12; //Default star twinkle speed in realtime (timescale = 1)
    #endregion

    #region Fog
    Color fogStartColor;
    Color fogEndColor;
    //Fog colors and view distance for day and night
    private Color fogDayColor = new Color(0.3f, 0.3f, 0.3f, 1.0f);
    private float fogDayDistance = 2048f;
    private Color fogNightColor = new Color(0.05f, 0.05f, 0.05f, 1.0f);
    private float fogNightDistance = 2048f;
    private void setFogColor(bool day) {
        if(day) {
            UnityEngine.RenderSettings.fogColor = fogDayColor; //TODO: probably should be lerped
        } else {
            UnityEngine.RenderSettings.fogColor = fogNightColor; //TODO: probably should be lerped
        }
    }
    private Dictionary<WeatherType, WeatherManager.FogSettings> FogSettings;
    private void getVanillaFogSettings() {
        WeatherManager wm = GameManager.Instance.WeatherManager;
        FogSettings = new Dictionary<WeatherType, WeatherManager.FogSettings>();
        FogSettings.Add(WeatherType.Sunny, wm.SunnyFogSettings);
        FogSettings.Add(WeatherType.Cloudy, wm.SunnyFogSettings);
        FogSettings.Add(WeatherType.Overcast, wm.OvercastFogSettings);
        FogSettings.Add(WeatherType.Fog, wm.HeavyFogSettings);
        FogSettings.Add(WeatherType.Rain, wm.RainyFogSettings);
        FogSettings.Add(WeatherType.Thunder, wm.RainyFogSettings);
        FogSettings.Add(WeatherType.Snow, wm.SnowyFogSettings);
    }
    private void setFogSettings() {
        FogSettings = new Dictionary<WeatherType, WeatherManager.FogSettings>();
        FogSettings.Add(WeatherType.Sunny, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 3072f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Cloudy, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 2560f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Overcast, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 2048f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Fog, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 64f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Rain, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1536f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Thunder, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1024f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Snow, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 512f, excludeSkybox = true});

        //Overwrite the default fog settings
        WeatherManager wm = GameManager.Instance.WeatherManager;
        wm.SunnyFogSettings = FogSettings[WeatherType.Sunny];
        wm.OvercastFogSettings = FogSettings[WeatherType.Overcast];
        wm.HeavyFogSettings = FogSettings[WeatherType.Fog];
        wm.RainyFogSettings = FogSettings[WeatherType.Rain];
        wm.SnowyFogSettings = FogSettings[WeatherType.Snow];
        //Debug.Log("Applied new fog settings to WeatherManager");
    }
    #endregion

    #region Events
    private System.Diagnostics.Stopwatch stopWatch;
    public int TimeInside;

    /// <summary>
    /// Get InteriorTransition & InteriorDungeonTransition events from PlayerEnterExit
    /// </summary>
    /// <param name="args"></param>
    public void InteriorTransitionEvent(PlayerEnterExit.TransitionEventArgs args)      //player went indoors (or dungeon), disable sky objects
    {
        Debug.Log("Deactivating skybox");
        //stopWatch.Reset();
        ToggleSkybox(false);
        //stopWatch.Start();
    }

    /// <summary>
    /// Get ExteriorTransition & DungeonExteriorTransition events from PlayerEnterExit
    /// </summary>
    /// <param name="args"></param>
    public void ExteriorTransitionEvent(PlayerEnterExit.TransitionEventArgs args)   //player transitioned to exterior from indoors or dungeon
    {
        Debug.Log("Activating skybox");
        //stopWatch.Stop();
        //TimeInside = stopWatch.Elapsed.Minutes;
        ToggleSkybox(true);
    }
    #endregion

}