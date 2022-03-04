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

    private static string skyboxMaterialName = "BLBSkyboxMaterial";
    private Material skyboxMat; //Reference to the skybox material so we can change properties

    private Camera playerCam;   //Reference to player cam to manage clear settings
    private GameObject dfSky;   //Reference to classic Daggerfall sky object so we can disable it
    private Light dfSunlight;   //Reference to the SunRig's main light to change the sun color

    private WorldTime worldTime; //Reference to Daggerfall Unity's time management object to detect changed TimeScale

    private DayParts currentDayPart = DayParts.None; //Keeps track of the current part of the day

    private float moonOrbitSpeed = 0.001f / 12; //Default moon orbit speed in realtime
    private float cloudSpeed = 0.005f / 12; //Default sloud speed in realtime
    private float starsTwinkleSpeed = 0.001f / 12; //Default star twinkle speed in realtime
    private float originalTimeScale;    //Original time scale when game started (not used atm)
    private float currentTimeScale; //Keeps track of the current timescale to update speeds if it changes

    [Invoke(StateManager.StateTypes.Start, 0)]
    public static void Init(InitParams initParams)
    {
        Mod = initParams.Mod;  // Get mod     
        Instance = new GameObject("BLBSkybox").AddComponent<BLBSkybox>(); // Add script to the scene.

        UnityEngine.RenderSettings.fogColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        UnityEngine.RenderSettings.fogEndDistance = 2048f;

        WeatherManager.OnWeatherChange += Instance.OnWeatherChange; //Register event for weather changes

        PlayerEnterExit.OnTransitionInterior += Instance.InteriorTransitionEvent; //interior transition
        PlayerEnterExit.OnTransitionDungeonInterior += Instance.InteriorTransitionEvent; //dungeon interior transition
        PlayerEnterExit.OnTransitionExterior += Instance.ExteriorTransitionEvent; //exterior transition
        PlayerEnterExit.OnTransitionDungeonExterior += Instance.ExteriorTransitionEvent; //dungeon exterior transition

        //Instance.setCloudAttributes();
        Instance.setCloudTypes();
        Instance.setLunarPhases();
        Instance.getVanillaFogSettings();
        //Instance.setFogSettings();

        //Load the skybox material and set a random wind direction (which is always the same when the game starts)
        Instance.skyboxMat = Mod.GetAsset<Material>("Materials/" + skyboxMaterialName) as Material;
        Instance.skyboxMat.SetFloat("_CloudDirection", Instance.getWindDirection());

        //Store a reference to the SkyRig game object and disable it
        Instance.dfSky = GameManager.Instance.SkyRig.gameObject;
        Instance.dfSky.SetActive(false);

        //Store a reference to the SunRig's main light
        Instance.dfSunlight = GameObject.Find("SunLight").GetComponent("Light") as Light;

        //Store a reference to the player camera
        Instance.playerCam = GameObject.FindGameObjectWithTag("MainCamera").GetComponent("Camera") as Camera;
        CameraClearManager ccm = Instance.playerCam.GetComponent<CameraClearManager>();
        ccm.cameraClearExterior = CameraClearFlags.Skybox;
        
        Instance.ToggleSkybox(true);
        Instance.SetExposure(WeatherType.Sunny);
        //Store a reference to the game's time management object
        Instance.worldTime = DaggerfallUnity.Instance.WorldTime;

        //Store the original timescale and set the currentTimeScale to it
        Instance.originalTimeScale = Instance.worldTime.TimeScale;
        Instance.currentTimeScale = Instance.originalTimeScale;

        //Set the correct speeds for cloud movement
        Instance.updateSpeeds();

        Debug.Log("BLB Skybox has been set up");
    }

    void Awake ()
    {
        Mod.IsReady = true;
        //I need to do more stuff in here
        Debug.Log("blb-skybox awakened");
    }

    private Color fogDayColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
    private float fogDayDistance = 2048f;
    private Color fogNightColor = new Color(0.1f, 0.1f, 0.1f, 0.125f);
    private float fogNightDistance = 4096f;

    //Counter to limit Update() calls to once per 5 seconds
    private float deltaTime = 0.0f;
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
        deltaTime = 0.0f;
        //Get the current time of day
        int hour = worldTime.Now.Hour;
        int minutes = worldTime.Now.Minute;
        //Determine part of the day
        //The skyboxLerpDuration calculation in each day part is partial, minutes are substracted afterwards
        //currentDayPart is changed to prevent the lerp from firing again
        if (isHourDayPart(hour, DayParts.Night) && currentDayPart != DayParts.Night) {
            //0:00 - 05:00
            if(currentLunarPhase != worldTime.Now.MassarLunarPhase) {
                ChangeLunarPhases();
            }

            atmosphereStartValue = 0.64f;
            atmosphereEndValue = 0.75f;
            doSkyboxLerp = true;

            //This is the time of day that the lerp is supposed to end minus the current hour of the day
            skyboxLerpDuration = (4 - hour); 

            currentDayPart = DayParts.Night;
        } else if (isHourDayPart(hour, DayParts.Dawn) && currentDayPart != DayParts.Dawn) {
            //05:00 - 07:00

            UnityEngine.RenderSettings.fogColor = fogDayColor;
            UnityEngine.RenderSettings.fogEndDistance = fogDayDistance;
            OnDawn();
            atmosphereStartValue = 0.75f;
            atmosphereEndValue = 0.64f;
            doSkyboxLerp = true;

            //This is the time of day that the lerp is supposed to end minus the current hour of the day
            skyboxLerpDuration = (9 - hour);

            currentDayPart = DayParts.Dawn;
        } else if (isHourDayPart(hour, DayParts.Morning) && currentDayPart != DayParts.Morning) {
            //07:00 - 12:00
            if(currentLunarPhase != worldTime.Now.MassarLunarPhase) {
                ChangeLunarPhases();
            }
            currentDayPart = DayParts.Morning;
        } else if (isHourDayPart(hour, DayParts.Midday) && currentDayPart != DayParts.Midday) {
            //12:00 - 17:00
            atmosphereStartValue = 0.64f;
            atmosphereEndValue = 0.75f;
            doSkyboxLerp = true;

            //This is the time of day that the lerp is supposed to end minus the current hour of the day
            skyboxLerpDuration = (16 - hour);

            currentDayPart = DayParts.Midday;
        } else if (isHourDayPart(hour, DayParts.Dusk) && currentDayPart != DayParts.Dusk) {
            //17:00 - 19:00
            OnDusk();

            atmosphereStartValue = 0.75f;
            atmosphereEndValue = 0.64f;
            doSkyboxLerp = true;

            //This is the time of day that the lerp is supposed to end minus the current hour of the day
            skyboxLerpDuration = (21 - hour);

            currentDayPart = DayParts.Dusk;
        } else if (isHourDayPart(hour, DayParts.Evening) && currentDayPart != DayParts.Evening) {
            //20:00 - 0:00
            UnityEngine.RenderSettings.fogColor = fogNightColor;
            UnityEngine.RenderSettings.fogEndDistance = fogNightDistance;
            currentDayPart = DayParts.Evening;
        }
        if(skyboxLerpRunning == false && pendingWeather == true) {
            ApplyPendingWeatherSettings();
        }
        if (doSkyboxLerp) {
            //Calculate the final lerp duration by substracting the minutes of the hour divided by DFU's MinutesPerHour value
            skyboxLerpDuration = calculateScaledLerpDuration(skyboxLerpDuration - (minutes / DaggerfallDateTime.MinutesPerHour));
            //Stop any running skybox lerp and start over
            StopCoroutine("SkyboxLerp");
            skyboxLerpRunning = true;
            StartCoroutine("SkyboxLerp");
            //We only do this once per day part
            doSkyboxLerp = false;
        }
    }

    public void ToggleSkybox(bool activate) {
        if(activate) {
            //Set skybox material and sun for the Unity renderer and change the clearFlags setting on the player camera
            UnityEngine.RenderSettings.skybox = Instance.skyboxMat;
            UnityEngine.RenderSettings.sun = Instance.dfSunlight;
            Instance.playerCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
        } else {
            UnityEngine.RenderSettings.skybox = null;
            UnityEngine.RenderSettings.sun = null;
            Instance.playerCam.clearFlags = UnityEngine.CameraClearFlags.Depth;
        }
    }

    private void updateSpeeds() {
        //Updates speeds when TimeScale has been changed
        skyboxMat.SetFloat("_CloudSpeed", Instance.cloudSpeed * Instance.currentTimeScale);
        skyboxMat.SetFloat("_MoonOrbitSpeed", Instance.moonOrbitSpeed * Instance.currentTimeScale);
        skyboxMat.SetFloat("_SecundaOrbitSpeed", Instance.moonOrbitSpeed * Instance.currentTimeScale);
        skyboxMat.SetFloat("_TwinkleSpeed", Instance.starsTwinkleSpeed * Instance.currentTimeScale);
    }

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
        } else if(dayPart == DayParts.Dusk) {
            sunStartColor = getSunColor(18);
            sunEndColor = getSunColor(2);
        }
        sunLerpDuration = calculateScaledLerpDuration(2);
        StopCoroutine("SunLerp");
        sunLerpRunning = true;
        StartCoroutine("SunLerp");
    }

    //Calculates the duration of the lerp in seconds scaled to the TimeScale to have proper sun rise / set
    private float calculateScaledLerpDuration(float targetDurationHours) {
        float lerpTime = (DaggerfallDateTime.SecondsPerHour / worldTime.TimeScale) * targetDurationHours;
        return Mathf.Max(1.0f, lerpTime);
    }

    private bool doSkyboxLerp = false;  //Indicates if SkyboxLerp should be started
    private bool skyboxLerpRunning = false; //Indicates if SkyboxLerp is running
    float skyboxLerpDuration = 8.0f;    //Skybox Lerp duration in seconds
    float atmosphereStartValue = 1.0f;  //Start value for the atmosphere thickness lerp
    float atmosphereEndValue = 1.0f; //End value for the atmosphere thickness lerp
    IEnumerator SkyboxLerp()
    {
        //Animates the atmospheric thickness and the cloud color
        float timeElapsed = 0;
        while (timeElapsed < skyboxLerpDuration)
        {
            skyboxMat.SetFloat("_AtmosphereThickness", Mathf.Lerp(atmosphereStartValue, atmosphereEndValue, timeElapsed / skyboxLerpDuration));
            //skyboxMat.SetColor("_CloudColor", Color.Lerp(cloudStartColor, cloudEndColor, timeElapsed / skyboxLerpDuration));
            timeElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        skyboxMat.SetFloat("_AtmosphereThickness", atmosphereEndValue);
        //skyboxMat.SetColor("_CloudColor", cloudEndColor);
        skyboxLerpRunning = false;
    }

    //Get cloud color for the time of day - need work or different approach?
    private Color getCloudColor(int hour) {
        Color color = new Color(0.0f, 0.0f, 0.0f, 1f);
        //Night
        if(hour >= 0 && hour < 5) {
            color.r = 0.05f;
            color.g = 0.05f;
            color.b = 0.05f;
        //Dawn - 04:00 - 8:00
        } else if(hour >= 5 && hour < 7) {
            color.r = 0.384f;
            color.g = 0.128f;
            color.b = 0.128f;
        //Morning
        } else if(hour >= 7 && hour < 12) {
            color.r = 0.5f;
            color.g = 0.5f;
            color.b = 0.5f;
        //Midday
        } else if(hour >= 12 && hour < 17) {
            color.r = 0.8f;
            color.g = 0.8f;
            color.b = 0.8f;
        //Dusk
        } else if(hour >= 17 && hour < 20) {
            color.r = 0.384f;
            color.g = 0.128f;
            color.b = 0.128f;
        //Evening
        } else if(hour >= 20) {
            color.r = 0.05f;
            color.g = 0.05f;
            color.b = 0.05f;
        }
        return color;
    }

    private bool doSunLerp = false; //Indicates if SunLerp should be started
    private bool sunLerpRunning = false; //Indicates if SunLerp is running
    float sunLerpDuration = 8.0f; //Sun lerp duration in seconds
    Color sunStartColor = new Color(0.0f, 0.0f, 0.0f, 1.0f); //Start value for sun color lerp
    Color sunEndColor = new Color(0.0f, 0.0f, 0.0f, 1.0f); //End value for the sun color lerp
    IEnumerator SunLerp()
    {
        //Animated the sun color
        float timeElapsed = 0;
        while (timeElapsed < sunLerpDuration)
        {
            dfSunlight.color = Color.Lerp(sunStartColor, sunEndColor, timeElapsed / sunLerpDuration);
            timeElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        dfSunlight.color = sunEndColor;
        sunLerpRunning = false;
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

    //Get the sun color for the time of day, needs a lot of tweaking
    private Color getSunColor(int hour) {
        Color color = new Color(1.0f, 1.0f, 1.0f, 1f);
        //Night
        if(hour >= 0 && hour < 5) {
            color.r = 0.5f;
            color.g = 0.5f;
            color.b = 0.5f;
        //Dawn
        } else if(hour >= 5 && hour < 8) {
            color.r = 0.69f;
            color.g = 0.26f;
            color.b = 0.13f;
        //Morning
        } else if(hour >= 8 && hour < 12) {
            color.r = 0.75f;
            color.g = 1.0f;
            color.b = 0.59f;
        //Midday
        } else if(hour >= 12 && hour < 16) {
            //Sun color from frame 1-22
            color.r = 1.0f;     //255
            color.g = 0.95f;     //255
            color.b = 1.0f;   //239            
        //Dusk
        } else if(hour >= 16 && hour < 20) {
            color.r = 0.69f;
            color.g = 0.26f;
            color.b = 0.13f;
        //Evening
        } else if(hour >= 20) {
            //Sun color from frame 1-5
            color.r = 0.25f;
            color.g = 0.25f;
            color.b = 0.25f;
        }
        return color;
    }

    private bool pendingWeather = false; //Indicates if a weather change is pending - need to figure out best way to handle this during a sun / cloud color lerp
    private float pendingWindDirection; //The new wind direction
    private CloudTypeStruct pendingCloudTop; //The new settings for the cloud top layer
    private CloudTypeStruct pendingCloud; //The new settings for the cloud layer
    private CloudColorStruct pendingCloudColors; //The new settings for cloud colors (both layers)
    private WeatherType currentWeather = WeatherType.Sunny; //Keeps track of the current weather to detect a change
    private void OnWeatherChange(WeatherType weather) {
        //Only change weather if it's not the same weather as the current weather
        if(weather != currentWeather) {
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
            
            //Halt any skybox lerp as we need access to the material settings
            //Force the atmosphere thickness and cloud color to their end values
            if(skyboxLerpRunning == true) {
                StopCoroutine("SkyboxLerp");
                skyboxMat.SetFloat("_AtmosphereThickness", atmosphereEndValue);
                //skyboxMat.SetColor("_CloudColor", cloudEndColor);
            }
            //Apply the pending weather settings
            ApplyPendingWeatherSettings();
            /*if(skyboxLerpRunning == false) {
                ApplyPendingWeatherSettings();
            } else {
                pendingWeather = true;
            }*/
        }
    }

    private void ApplyPendingWeatherSettings() {
        //Change cloud colors
        Debug.Log("Cloud color: " + pendingCloudColors.CloudColor.ToString());
        Debug.Log("Cloud top color: " + pendingCloudColors.CloudTopColor.ToString());

        skyboxMat.SetColor("_CloudColor", pendingCloudColors.CloudColor);
        skyboxMat.SetColor("_CloudTopColor", pendingCloudColors.CloudTopColor);

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

    //Adjusts exposure and fog of the skybox according to Daggerfall Unity's weather sun light scales
    private void SetExposure(WeatherType weather) {
        Color sunColor = getWeatherSunColor(weather);
        WeatherManager wm = GameManager.Instance.WeatherManager;
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
            fogDistance = fogSettings.endDistance * 0.33f;
        } else {
            fogDistance = 1.25f / fogSettings.density;
        }
        fogDayDistance = fogDistance;
        fogNightDistance = fogDayDistance * 2;

        if(sunLerpRunning) { StopCoroutine("SunLerp"); }
        if(skyboxLerpRunning) { 
            StopCoroutine("SkyboxLerp"); 
            skyboxMat.SetFloat("_AtmosphereThickness", atmosphereEndValue);
        }
        
        CloudColorStruct cloudColor = CloudColors[weather];
        UnityEngine.RenderSettings.fogEndDistance = fogSettings.endDistance;
        
        dfSunlight.color = sunColor;

        skyboxMat.SetColor("Ground", cloudColor.CloudTopColor);
        skyboxMat.SetFloat("_Exposure", exposure);
        skyboxMat.SetFloat("_FogDistance", fogDistance);
    }

    //Stores cloud colors used for lerping
    Color cloudStartColor = new Color(1.0f, 1.0f, 1.0f);
    Color cloudEndColor = new Color(0.1f, 0.1f, 0.1f);

    //TODO: needs to rethink this and tie them to weather types instead
    //Available cloud types
    private enum CloudTypes {
        Default,
        Clear,
        ThinStretch,
        ThinStretch2,
        PuffySmall,
        PuffyBig
    }

    //Defines the settings needed for the skybox material to get a specific type of cloud
    struct CloudTypeStruct {

        public CloudTypeStruct(float tilingX, float tilingY, float alphaThreshold, float alphaMax, float normalEffect, float bending, float blendScale, float opacity) {
            TilingX = tilingX;
            TilingY = tilingY;
            AlphaThreshold = alphaThreshold;
            AlphaMax = alphaMax;
            NormalEffect = normalEffect;
            Bending = bending;
            BlendScale = blendScale;
            Opacity = opacity;
        }
        public float TilingX;
        public float TilingY;
        public float AlphaThreshold;
        public float AlphaMax;
        public float NormalEffect;
        public float Bending;
        public float BlendScale;
        public float Opacity;
    }

    //Defines the colors for both cloud layers
    struct CloudColorStruct {
        public CloudColorStruct(Color cloudColor, Color cloudTopColor) {
            CloudColor = cloudColor;
            CloudTopColor = cloudTopColor;
        }
        public Color CloudColor;
        public Color CloudTopColor;
    }

    //Dictionaries to store cloud settings
    private Dictionary<WeatherType, CloudTypeStruct> Clouds;
    private Dictionary<WeatherType, CloudTypeStruct> CloudsTop;
    private Dictionary<WeatherType, CloudColorStruct> CloudColors;
    
    //Set up the available cloud types
    private void setCloudTypes() {
        CloudsTop = new Dictionary<WeatherType, CloudTypeStruct>();
        CloudsTop.Add(WeatherType.Sunny,     new CloudTypeStruct(0.25f, 0.25f, 1.0f, 1.0f,  0.65f, 1.0f, 0.0f, 0.0f));
        CloudsTop.Add(WeatherType.Cloudy,   new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.32f,  0.65f, 1.0f, 0.0f, 1.0f));
        CloudsTop.Add(WeatherType.Overcast, new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.08f,  0.65f, 1.0f, 0.0f, 1.0f));
        CloudsTop.Add(WeatherType.Fog,      new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.01f,  0.65f, 1.0f, 0.0f, 1.0f));
        CloudsTop.Add(WeatherType.Rain,     new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.6f,   0.65f, 1.0f, 0.0f, 1.0f));
        CloudsTop.Add(WeatherType.Thunder,  new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.04f,  0.65f, 1.0f, 0.0f, 1.0f));
        CloudsTop.Add(WeatherType.Snow,     new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.02f,  0.65f, 1.0f, 0.0f, 1.0f));

        Clouds = new Dictionary<WeatherType, CloudTypeStruct>();
        Clouds.Add(WeatherType.Sunny,        new CloudTypeStruct(0.25f, 0.25f, 1.0f, 1.0f,  0.65f, 0.25f, 0.25f, 0.0f));
        Clouds.Add(WeatherType.Cloudy,      new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.32f,  0.65f, 0.25f, 0.25f, 1.0f));
        Clouds.Add(WeatherType.Overcast,    new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.16f,  0.65f, 0.25f, 0.25f, 1.0f));
        Clouds.Add(WeatherType.Fog,         new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.32f,  0.65f, 0.25f, 0.25f, 1.0f));
        Clouds.Add(WeatherType.Rain,        new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.16f,  0.65f, 0.25f, 0.25f, 1.0f));
        Clouds.Add(WeatherType.Thunder,     new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.24f,  0.65f, 0.25f, 0.25f, 1.0f));
        Clouds.Add(WeatherType.Snow,        new CloudTypeStruct(0.25f, 0.25f, 0.0f, 0.4f,   0.65f, 0.25f, 0.25f, 1.0f));

        Color cloudTopColor;
        Color cloudColor;
        CloudColorStruct cloudColors;

        CloudColors = new Dictionary<WeatherType, CloudColorStruct>();

        cloudTopColor = new Color(0.45f, 0.45f, 0.45f);
        cloudColor = new Color(0.8f, 0.8f, 0.8f);
        cloudColors = new CloudColorStruct(cloudColor, cloudTopColor);
        CloudColors.Add(WeatherType.Sunny, cloudColors);
        CloudColors.Add(WeatherType.Cloudy, cloudColors);

        cloudTopColor = new Color(0.3f, 0.3f, 0.3f);
        cloudColor = new Color(0.5f, 0.5f, 0.5f);
        cloudColors = new CloudColorStruct(cloudColor, cloudTopColor);
        CloudColors.Add(WeatherType.Overcast, cloudColors);

        cloudTopColor = new Color(0.3f, 0.3f, 0.3f);
        cloudColor = new Color(0.6f, 0.6f, 0.6f);
        cloudColors = new CloudColorStruct(cloudColor, cloudTopColor);
        CloudColors.Add(WeatherType.Fog, cloudColors);
        
        cloudTopColor = new Color(0.3f, 0.3f, 0.3f);
        cloudColor = new Color(0.6f, 0.6f, 0.6f);
        cloudColors = new CloudColorStruct(cloudColor, cloudTopColor);
        CloudColors.Add(WeatherType.Rain, cloudColors);

        cloudTopColor = new Color(0.2f, 0.2f, 0.2f);
        cloudColor = new Color(0.4f, 0.4f, 0.4f);
        cloudColors = new CloudColorStruct(cloudColor, cloudTopColor);
        CloudColors.Add(WeatherType.Thunder, cloudColors);

        cloudTopColor = new Color(0.05f, 0.05f, 0.05f);
        cloudColor = new Color(0.25f, 0.25f, 0.25f);
        cloudColors = new CloudColorStruct(cloudColor, cloudTopColor);
        CloudColors.Add(WeatherType.Snow, cloudColors);
    }

    //Returns a random wind direction
    private float getWindDirection() {
        return Random.Range(0f, 360f);
    }

    //The different day parts we can identify in isHourDayPart
    private enum DayParts {
        None,
        Dawn, //04:00 - 08:00
        Morning, //08:00 - 12:00
        Midday, //12:00 - 16:00
        Dusk, //16:00 - 20:00
        Evening, //20:00 - 0:00
        Night,  //0:00 - 04:00
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
        FogSettings.Add(WeatherType.Sunny, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 4096f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Cloudy, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 3072f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Overcast, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 2560f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Fog, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 128f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Rain, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 2048f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Thunder, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1536f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Snow, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 768f, excludeSkybox = true});

        //Overwrite the default fog settings
        WeatherManager wm = GameManager.Instance.WeatherManager;
        wm.SunnyFogSettings = FogSettings[WeatherType.Sunny];
        wm.OvercastFogSettings = FogSettings[WeatherType.Overcast];
        wm.HeavyFogSettings = FogSettings[WeatherType.Fog];
        wm.RainyFogSettings = FogSettings[WeatherType.Rain];
        wm.SnowyFogSettings = FogSettings[WeatherType.Snow];
        Debug.Log("Applied new fog settings to WeatherManager");
    }

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

}