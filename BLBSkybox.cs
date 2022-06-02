using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEditor;
using System.IO;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;   //required for modding features
using DaggerfallWorkshop.Game.Serialization;
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
    public string jsonPrefix = "";
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
    private Camera stackedCam;
    #endregion

    [Invoke(StateManager.StateTypes.Start, 0)]
    public static void Init(InitParams initParams)
    {
        Mod = initParams.Mod;  // Get mod     
        Instance = new GameObject("BLBSkybox").AddComponent<BLBSkybox>(); // Add script to the scene.

        Instance.SetFogDefaults();

        Instance.SubscribeToEvents();

        //Prepare the cloud types, lunar phases and fog settings
        Instance.loadAllSkyboxSettings();

        //Instance.setCloudTypes();
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

        //Turn on the skybox
        Instance.ToggleSkybox(true);

        UnityEngine.RenderSettings.fogColor = Instance.fogDayColor;
        
        Instance.currentWeather = WeatherType.None;
        Instance.forceWeatherUpdate = true;
        Instance.OnWeatherChange(Instance.currentWeather);
        Instance.SetFogDistance(Instance.currentWeather);

        Instance.updateSpeeds();

        GameObject distantTerrain = GameObject.Find("DistantTerrain");
        if(distantTerrain == null) {
            //Change the clear flags in the camera clear manager, otherwise it would unset the skybox in its Update method after an exterior transition
            CameraClearManager ccm = Instance.playerCam.GetComponent<CameraClearManager>();
            ccm.cameraClearExterior = CameraClearFlags.Skybox;
        } else {
            Debug.Log("BLB: Detected distant terrain");
            GameObject goCam = GameObject.Find("stackedCamera");
            if(goCam) {
                Instance.stackedCam = goCam.GetComponent<Camera>();
                if(Instance.stackedCam) {
                    Debug.Log("Found stacked camera object");
                }
            }
            
        }

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

    void onEnable()
    {
        SaveLoadManager.OnLoad += OnLoadEvent;
    }

    void onDisable()
    {
        SaveLoadManager.OnLoad -= OnLoadEvent;
    }

    void OnLoadEvent(SaveData_v1 saveData)
    {
        if(!GameManager.Instance.PlayerEnterExit.IsPlayerInside) {
            if(stackedCam == null) {
                playerCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            } else {
                stackedCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            }
        }
    }

    //Force skybox flag to prevent Distant Terrain from overriding it again in it's Update function
    void LateUpdate() {
        if(!GameManager.Instance.PlayerEnterExit.IsPlayerInside) {
            if(playerCam.clearFlags != UnityEngine.CameraClearFlags.Skybox && stackedCam == null) {
                playerCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            } else {
                stackedCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            }
        }
    }

    #region Helper methods
    public void ToggleSkybox(bool activate) {
        //Change skybox material and sun assignment for the Unity renderer and change the clearFlags setting on the player camera
        if(activate) {
            UnityEngine.RenderSettings.skybox = skyboxMat;
            UnityEngine.RenderSettings.sun = dfSunlight;
            if(stackedCam == null) {
                playerCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            } else {
                stackedCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            }
        } else {
            UnityEngine.RenderSettings.skybox = null;
            UnityEngine.RenderSettings.sun = null;
            if(stackedCam == null) {
                playerCam.clearFlags = UnityEngine.CameraClearFlags.Depth;
            } else {
                stackedCam.clearFlags = UnityEngine.CameraClearFlags.Skybox;
            }
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
        //StopCoroutine("SunFogLerp");
        //sunFogLerpRunning = true;
        //StartCoroutine("SunFogLerp");
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

    private BLBSkyboxSetting pendingSkyboxSettings; //The new settings for the skybox

    private WeatherType currentWeather = WeatherType.Sunny; //Keeps track of the current weather to detect a change
    private void OnWeatherChange(WeatherType weather) {
        //Only change weather if it's not the same weather as the current weather
        if(weather != currentWeather || forceWeatherUpdate == true) {
            forceWeatherUpdate = false;
            currentWeather = weather;

            pendingWindDirection = getWindDirection(); //Get new random wind direction
            pendingSkyboxSettings = SkyboxSettings[weather];

            Debug.Log("Pending weather = " + weather.ToString());
            Debug.Log("Pending weather settings: " + JsonUtility.ToJson(pendingSkyboxSettings));

            //CloudTypeStruct cloudTop = CloudsTop[weather];   //Get cloud type for top layer
            //CloudTypeStruct cloud = Clouds[weather];
            //CloudColorStruct cloudColors = CloudColors[weather]; //Get cloud colors for both layers

            //TODO: Add several cloud variations based on weather type

            //Store the new cloud settings
            //pendingCloud = cloud;
            //pendingCloudTop = cloudTop;
            //pendingCloudColors = cloudColors;
            
            //Apply the pending weather settings
            ApplyPendingWeatherSettings();
        }
    }

    private void ApplyPendingWeatherSettings() {
        BLBSkybox.ApplySkyboxSettings(pendingSkyboxSettings);
        //Change exposure to match Daggerfall Unity's sunlight reduction - might be a better way
        SetFogDistance(currentWeather);
        pendingWeather = false;
    }

    private WeatherManager wm;
    //Adjusts fog distance of the skybox according to Daggerfall Unity's weather settings
    //Returns a random wind direction
    private float getWindDirection() {
        return Random.Range(0f, 360f);
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

    #region Skybox settings
    private float cloudSpeed = 0.001f / 12; //Default cloud speed in realtime (timescale = 1)    
    //Dictionaries to store skybox settings
    private Dictionary<WeatherType, BLBSkyboxSetting> SkyboxSettings;

    private void loadAllSkyboxSettings() {
        SkyboxSettings = new Dictionary<WeatherType, BLBSkyboxSetting>();

        string data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxSunny.json", false).text;
        loadSkyboxSettings(WeatherType.Sunny, data);

        data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxCloudy.json", false).text;
        loadSkyboxSettings(WeatherType.Cloudy, data);

        data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxOvercast.json", false).text;
        loadSkyboxSettings(WeatherType.Overcast, data);

        data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxFog.json", false).text;
        loadSkyboxSettings(WeatherType.Fog, data);

        data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxRain.json", false).text;
        loadSkyboxSettings(WeatherType.Rain, data);

        data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxThunder.json", false).text;
        loadSkyboxSettings(WeatherType.Thunder, data);

        data = Mod.GetAsset<TextAsset>(jsonPrefix + "SkyboxSnow.json", false).text;
        loadSkyboxSettings(WeatherType.Snow, data);

    }
    private void loadSkyboxSettings(WeatherType weatherType, string data) {
        BLBSkyboxSetting skyboxSetting = JsonUtility.FromJson<BLBSkyboxSetting>(data);

        skyboxSetting.topClouds = JsonUtility.FromJson<BLBCloudsSetting>(skyboxSetting.topCloudsFlat);
        skyboxSetting.bottomClouds = JsonUtility.FromJson<BLBCloudsSetting>(skyboxSetting.bottomCloudsFlat);

        SkyboxSettings.Add(weatherType, skyboxSetting);
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
    private void SetFogDefaults() {
        //Set up some default settings for the Unity renderer
        UnityEngine.RenderSettings.fogColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);
        UnityEngine.RenderSettings.fogEndDistance = 2048f;
    }
    private void setFogColor(bool day) {
        if(day) {
            UnityEngine.RenderSettings.fogColor = fogDayColor; //TODO: probably should be lerped
        } else {
            UnityEngine.RenderSettings.fogColor = fogNightColor; //TODO: probably should be lerped
        }
    }
    private void SetFogDistance(WeatherType weather) {
        
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
        skyboxMat.SetFloat("_FogDistance", fogDistance);
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

    private void SubscribeToEvents() {
        //Subscribe to DFU events
        WeatherManager.OnWeatherChange += OnWeatherChange; //Register event for weather changes
        PlayerEnterExit.OnTransitionInterior += InteriorTransitionEvent; //interior transition
        PlayerEnterExit.OnTransitionDungeonInterior += InteriorTransitionEvent; //dungeon interior transition
        PlayerEnterExit.OnTransitionExterior += ExteriorTransitionEvent; //exterior transition
        PlayerEnterExit.OnTransitionDungeonExterior += ExteriorTransitionEvent; //dungeon exterior transition
    }

    /// <summary>
    /// Get InteriorTransition & InteriorDungeonTransition events from PlayerEnterExit
    /// </summary>
    /// <param name="args"></param>
    public void InteriorTransitionEvent(PlayerEnterExit.TransitionEventArgs args)      //player went indoors (or dungeon), disable sky objects
    {
        Debug.Log("Deactivating skybox");
        ToggleSkybox(false);
    }

    /// <summary>
    /// Get ExteriorTransition & DungeonExteriorTransition events from PlayerEnterExit
    /// </summary>
    /// <param name="args"></param>
    public void ExteriorTransitionEvent(PlayerEnterExit.TransitionEventArgs args)   //player transitioned to exterior from indoors or dungeon
    {
        Debug.Log("Activating skybox");
        ToggleSkybox(true);
    }
    #endregion

    #region Settings management

    static bool ApplySkyboxSettings(BLBSkyboxSetting skyboxSetting) {
        if(UnityEngine.RenderSettings.skybox == null) {
            Debug.Log("BLB: Skybox material not found");
            return false;
        }
        Material skyboxMat = UnityEngine.RenderSettings.skybox;

        skyboxMat.SetFloat("_SunSize", skyboxSetting.SunSize);
        skyboxMat.SetInt("_SunSizeConvergence", skyboxSetting.SunSizeConvergence);
        skyboxMat.SetFloat("_AtmosphereThickness", skyboxSetting.AtmosphereThickness);

        Color tmpColor;
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.SkyTint, out tmpColor)) {
            skyboxMat.SetColor("_SkyTint", tmpColor);
        }
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.GroundColor, out tmpColor)) {
            skyboxMat.SetColor("_GroundColor", tmpColor);
        }
        skyboxMat.SetFloat("_Exposure", skyboxSetting.Exposure);
        skyboxMat.SetFloat("_NightStartHeight", skyboxSetting.NightStartHeight);
        skyboxMat.SetFloat("_NightEndHeight", skyboxSetting.NightEndHeight);
        skyboxMat.SetFloat("_SkyFadeStart", skyboxSetting.SkyFadeStart);
        skyboxMat.SetFloat("_SkyFadeEnd", skyboxSetting.SkyEndStart);
        skyboxMat.SetFloat("_FogDistance", skyboxSetting.FogDistance);

        skyboxMat.SetTextureScale("_CloudTopDiffuse", new Vector2(skyboxSetting.topClouds.TilingX, skyboxSetting.topClouds.TilingY));
        skyboxMat.SetTextureOffset("_CloudTopDiffuse", new Vector2(skyboxSetting.topClouds.OffsetX, skyboxSetting.topClouds.OffsetY));
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.topClouds.DayColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudTopColor", tmpColor);
        }
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.topClouds.NightColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudTopNightColor", tmpColor);
        }
        skyboxMat.SetFloat("_CloudTopAlphaCutoff", skyboxSetting.topClouds.AlphaTreshold);
        skyboxMat.SetFloat("_CloudTopAlphaMax", skyboxSetting.topClouds.AlphaMax);
        skyboxMat.SetFloat("_CloudTopColorBoost", skyboxSetting.topClouds.ColorBoost);
        skyboxMat.SetFloat("_CloudTopOpacity", skyboxSetting.topClouds.Opacity);
        skyboxMat.SetFloat("_CloudTopNormalEffect", skyboxSetting.topClouds.NormalEffect);
        skyboxMat.SetFloat("_CloudTopBending", skyboxSetting.topClouds.Bending);

        skyboxMat.SetTextureScale("_CloudDiffuse", new Vector2(skyboxSetting.bottomClouds.TilingX, skyboxSetting.bottomClouds.TilingY));
        skyboxMat.SetTextureOffset("_CloudDiffuse", new Vector2(skyboxSetting.bottomClouds.OffsetX, skyboxSetting.bottomClouds.OffsetY));
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.bottomClouds.DayColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudColor", tmpColor);
        }
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.bottomClouds.NightColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudNightColor", tmpColor);
        }
        skyboxMat.SetFloat("_CloudAlphaCutoff", skyboxSetting.bottomClouds.AlphaTreshold);
        skyboxMat.SetFloat("_CloudAlphaMax", skyboxSetting.bottomClouds.AlphaMax);
        skyboxMat.SetFloat("_CloudColorBoost", skyboxSetting.bottomClouds.ColorBoost);
        skyboxMat.SetFloat("_CloudNormalEffect", skyboxSetting.bottomClouds.NormalEffect);
        skyboxMat.SetFloat("_CloudOpacity", skyboxSetting.bottomClouds.Opacity);
        skyboxMat.SetFloat("_CloudBending", skyboxSetting.bottomClouds.Bending);
        
        Debug.Log("BLB: Applied skybox settings");

        return true;
    }

    #if UNITY_EDITOR
    [MenuItem("BLB/Import skybox settings")]
    static void ImportSkyboxSettings()
    {
        string path = EditorUtility.OpenFilePanel("Choose skybox settings to import", "", "json");
        string data = File.ReadAllText(path);

        BLBSkyboxSetting skyboxSetting = JsonUtility.FromJson<BLBSkyboxSetting>(data);
        skyboxSetting.topClouds = JsonUtility.FromJson<BLBCloudsSetting>(skyboxSetting.topCloudsFlat);
        skyboxSetting.bottomClouds = JsonUtility.FromJson<BLBCloudsSetting>(skyboxSetting.bottomCloudsFlat);
        if(ApplySkyboxSettings(skyboxSetting)) {
            EditorUtility.DisplayDialog("Success", "The following settings have been imported:\n" + path, "Ok","");
        }

    }

    [MenuItem("BLB/Import skybox settings", true)]
    static bool ValidateImportSkyboxSettings()
    {
        return UnityEngine.RenderSettings.skybox != null;
    }

    [MenuItem("BLB/Export skybox settings")]
    static void ExportSkyboxSettings()
    {
        Material skyboxMat = UnityEngine.RenderSettings.skybox;
        string path = EditorUtility.SaveFilePanel("Choose save folder", "", "", "json");

        BLBSkyboxSetting skyboxSetting = new BLBSkyboxSetting();
        BLBCloudsSetting topClouds = new BLBCloudsSetting();
        BLBCloudsSetting bottomClouds = new BLBCloudsSetting();

        skyboxSetting.SunSize = skyboxMat.GetFloat("_SunSize");
        skyboxSetting.SunSizeConvergence = skyboxMat.GetInt("_SunSizeConvergence");
        skyboxSetting.AtmosphereThickness = skyboxMat.GetFloat("_AtmosphereThickness");
        skyboxSetting.SkyTint = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_SkyTint"));
        skyboxSetting.GroundColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_GroundColor"));
        skyboxSetting.Exposure = skyboxMat.GetFloat("_Exposure");
        skyboxSetting.NightStartHeight = skyboxMat.GetFloat("_NightStartHeight");
        skyboxSetting.NightEndHeight = skyboxMat.GetFloat("_NightEndHeight");
        skyboxSetting.SkyFadeStart = skyboxMat.GetFloat("_SkyFadeStart");
        skyboxSetting.SkyEndStart = skyboxMat.GetFloat("_SkyFadeEnd");
        skyboxSetting.FogDistance = skyboxMat.GetFloat("_FogDistance");
        
        topClouds.TilingX = skyboxMat.GetTextureScale("_CloudTopDiffuse").x;
        topClouds.TilingY = skyboxMat.GetTextureScale("_CloudTopDiffuse").y;
        topClouds.OffsetX = skyboxMat.GetTextureOffset("_CloudTopDiffuse").x;
        topClouds.OffsetY = skyboxMat.GetTextureOffset("_CloudTopDiffuse").y;
        topClouds.DayColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudTopColor"));
        topClouds.NightColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudTopNightColor"));
        topClouds.AlphaTreshold = skyboxMat.GetFloat("_CloudTopAlphaCutoff");
        topClouds.AlphaMax = skyboxMat.GetFloat("_CloudTopAlphaMax");
        topClouds.ColorBoost = skyboxMat.GetFloat("_CloudTopColorBoost");
        topClouds.NormalEffect = skyboxMat.GetFloat("_CloudTopNormalEffect");
        topClouds.Opacity = skyboxMat.GetFloat("_CloudTopOpacity");
        topClouds.Bending = skyboxMat.GetFloat("_CloudTopBending");

        skyboxSetting.topCloudsFlat = JsonUtility.ToJson(topClouds);

        bottomClouds.TilingX = skyboxMat.GetTextureScale("_CloudDiffuse").x;
        bottomClouds.TilingY = skyboxMat.GetTextureScale("_CloudDiffuse").y;
        bottomClouds.OffsetX = skyboxMat.GetTextureOffset("_CloudDiffuse").x;
        bottomClouds.OffsetY = skyboxMat.GetTextureOffset("_CloudDiffuse").y;
        bottomClouds.DayColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudColor"));
        bottomClouds.NightColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudNightColor"));
        bottomClouds.AlphaTreshold = skyboxMat.GetFloat("_CloudAlphaCutoff");
        bottomClouds.AlphaMax = skyboxMat.GetFloat("_CloudAlphaMax");
        bottomClouds.ColorBoost = skyboxMat.GetFloat("_CloudColorBoost");
        bottomClouds.NormalEffect = skyboxMat.GetFloat("_CloudNormalEffect");
        bottomClouds.Opacity = skyboxMat.GetFloat("_CloudOpacity");
        bottomClouds.Bending = skyboxMat.GetFloat("_CloudBending");

        skyboxSetting.bottomCloudsFlat = JsonUtility.ToJson(bottomClouds);

        string data = JsonUtility.ToJson(skyboxSetting, true);
        
        File.WriteAllText(path, data);

        EditorUtility.DisplayDialog("Success","Skybox settings have been saved to: \n" + path, "Ok","");
        
        //File.WriteAllBytes(path, pngData);

    }
    [MenuItem("BLB/Export skybox settings", true)]
    static bool ValidateExportSkyboxSettings()
    {
        return UnityEngine.RenderSettings.skybox != null;
    }

    #endif

    #endregion

}