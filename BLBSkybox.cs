using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
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

    public static bool firstInit = true;

    #region General properties
    private static string skyboxMaterialName = "BLBSkyboxMaterial";
    private Mod presetMod = null;
    private Material skyboxMat; //Reference to the skybox material so we can change properties
    private Camera playerCam;   //Reference to player cam to manage clear settings
    private PlayerAmbientLight playerAmbientLight;
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
    //Snow settings
    public static Material SnowMaterial;
    public static float minParticleSize = 0.002f;
    public static float maxParticleSize = 0.0025f;
    public static int maxParticles = 17500;

    #endregion

    [Invoke(StateManager.StateTypes.Start, 0)]
    public static void Init(InitParams initParams)
    {
        Mod = initParams.Mod;  // Get mod     
        Instance = new GameObject("BLBSkybox").AddComponent<BLBSkybox>(); // Add script to the scene.

        Instance.presetMod = Mod;
        Debug.Log("BLB Skybox - set mod instance as default preset, looking for preset mods");
        Instance.FindPresetMod();

        Instance.SubscribeToEvents();
        //Prepare the cloud types, lunar phases and fog settings. 
        Instance.loadAllSkyboxSettings();
        Instance.loadFogSettings();
        Instance.InitSnow();

        Instance.setLunarPhases();

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
        Instance.playerAmbientLight = GameObject.FindGameObjectWithTag("Player").GetComponent("PlayerAmbientLight") as PlayerAmbientLight;
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
            GameObject goCam = GameObject.Find("stackedCamera");
            if(goCam) {
                Instance.stackedCam = goCam.GetComponent<Camera>();
            }
        }

        //Instance.SetPalettizationMaterial();

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
            forceWeatherUpdate = true;
            OnWeatherChange(currentWeather);
            if(currentLunarPhase != worldTime.Now.MassarLunarPhase) {
                ChangeLunarPhases();
            }
        } else if (isHourDayPart(hour, DayParts.Dawn) && currentDayPart != DayParts.Dawn) {
            //05:00 - 07:00
            setFogColor(true);
            currentDayPart = DayParts.Dawn;
            OnDawn();
        } else if (isHourDayPart(hour, DayParts.Morning) && currentDayPart != DayParts.Morning) {
            //07:00 - 12:00
            currentDayPart = DayParts.Morning;
            setFogColor(true);
            if(currentLunarPhase != worldTime.Now.MassarLunarPhase) {
                ChangeLunarPhases();
            }
            forceWeatherUpdate = true;
            OnWeatherChange(currentWeather);
        } else if (isHourDayPart(hour, DayParts.Midday) && currentDayPart != DayParts.Midday) {
            //12:00 - 17:00
            currentDayPart = DayParts.Midday;
            setFogColor(true);
            forceWeatherUpdate = true;
            OnWeatherChange(currentWeather);
        } else if (isHourDayPart(hour, DayParts.Dusk) && currentDayPart != DayParts.Dusk) {
            //17:00 - 19:00
            currentDayPart = DayParts.Dusk;
            setFogColor(true);
            OnDusk();
        } else if (isHourDayPart(hour, DayParts.Evening) && currentDayPart != DayParts.Evening) {
            //20:00 - 0:00
            currentDayPart = DayParts.Evening;
            setFogColor(false);
            forceWeatherUpdate = true;
            OnWeatherChange(currentWeather);
        }
        //ApplyPendingWeatherSettings();
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
            } else if(stackedCam != null) {
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
        } else if(dayPart == DayParts.Dusk) {
            sunStartColor = getSunColor(18);
            sunEndColor = getSunColor(2);
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
            //dfSunlight.color = Color.Lerp(sunStartColor, sunEndColor, timeElapsed / sunFogLerpDuration);
            //UnityEngine.RenderSettings.fogColor = Color.Lerp(fogStartColor, fogEndColor, timeElapsed / sunFogLerpDuration);
            timeElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        //UnityEngine.RenderSettings.fogColor = fogEndColor;
        //dfSunlight.color = sunEndColor;
        sunFogLerpRunning = false;
    }
    //Calculates the duration of the lerp in seconds scaled to the TimeScale to have proper sun rise / set
    private float calculateScaledLerpDuration(float targetDurationHours) {
        float lerpTime = (DaggerfallDateTime.SecondsPerHour / worldTime.TimeScale) * targetDurationHours;
        return Mathf.Max(1.0f, lerpTime);
    }
    #endregion

    #region Weather
    //Returns the day or night index depending on currentDayPart value
    private int getWeatherIndex() {
        int index = 0;
        if(currentDayPart == DayParts.Evening || currentDayPart == DayParts.Night) {
            index = 1;
        }
        return index;
    }
    private bool pendingWeather = false; //Indicates if a weather change is pending - need to figure out best way to handle this during a sun / cloud color lerp
    private WeatherType pendingWeatherType;
    private float pendingWindDirection; //The new wind direction
    private BLBSkyboxSetting pendingSkyboxSettings; //The new settings for the skybox

    private WeatherType currentWeather = WeatherType.Sunny; //Keeps track of the current weather to detect a change
    private void OnWeatherChange(WeatherType weather) {
        //Only change weather if it's not the same weather as the current weather
        if(weather != currentWeather || forceWeatherUpdate == true) {
            forceWeatherUpdate = false;
            pendingWeatherType = weather;

            int index = getWeatherIndex();//TODO: Get correct index based on time

            pendingWindDirection = getWindDirection(); //Get new random wind direction
            pendingSkyboxSettings = SkyboxSettings[pendingWeatherType][index];
            
            //Apply the pending weather settings
            ApplyPendingWeatherSettings();
        }
    }

    private void ApplyPendingWeatherSettings() {
        //if(pendingWeather == true) {
            BLBSkybox.ApplySkyboxSettings(pendingSkyboxSettings, currentWeather == pendingWeatherType, false);
            SetFogDistance(pendingWeatherType);
            currentWeather = pendingWeatherType;
            pendingWeather = false;
        //}
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

    private void FindPresetMod()
    {
        string[] presetNames = new string[]{
            "SkyboxSunny",
            "SkyboxCloudy",
            "SkyboxOvercast",
            "SkyboxFog",
            "SkyboxRain",
            "SkyboxThunder",
            "SkyboxSnow",
        };
        List<string> names = null;
        foreach(Mod mod in ModManager.Instance.EnumerateModsReverse())
        {
            //Skip disabled mods and this mod
            if(!mod.Enabled || mod.GUID == Mod.GUID) {
                continue;
            }
            if (mod.FindAssetNames(ref names, "SkyboxPresets", ".json") != 0)
            {
                if(names.Count >= presetNames.Length) {
                    presetMod = mod;
                    Debug.Log("BLB Skybox - Found preset mod: " + presetMod.Title);
                }
                break;
            }
        }
        return;
    }

    #region Skybox settings
    private float cloudSpeed = 0.001f / 12; //Default cloud speed in realtime (timescale = 1)    
    //Dictionaries to store skybox settings
    private Dictionary<WeatherType, BLBSkyboxSetting[]> SkyboxSettings;

    private void loadAllSkyboxSettings() {
        SkyboxSettings = new Dictionary<WeatherType, BLBSkyboxSetting[]>();

        string data = presetMod.GetAsset<TextAsset>("SkyboxSunny.json", false).text;
        loadSkyboxSettings(WeatherType.Sunny, data, 0);

        TextAsset night = presetMod.GetAsset<TextAsset>("SkyboxSunnyNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Sunny, data, 1);
        }

        data = presetMod.GetAsset<TextAsset>("SkyboxCloudy.json", false).text;
        loadSkyboxSettings(WeatherType.Cloudy, data, 0);

        night = presetMod.GetAsset<TextAsset>("SkyboxCloudyNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Cloudy, data, 1);
        }

        data = presetMod.GetAsset<TextAsset>("SkyboxOvercast.json", false).text;
        loadSkyboxSettings(WeatherType.Overcast, data, 0);

        night = presetMod.GetAsset<TextAsset>("SkyboxOvercastNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Overcast, data, 1);
        }

        data = presetMod.GetAsset<TextAsset>("SkyboxFog.json", false).text;
        loadSkyboxSettings(WeatherType.Fog, data, 0);

        night = presetMod.GetAsset<TextAsset>("SkyboxFogNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Fog, data, 1);
        }

        data = presetMod.GetAsset<TextAsset>("SkyboxRain.json", false).text;
        loadSkyboxSettings(WeatherType.Rain, data, 0);

        night = presetMod.GetAsset<TextAsset>("SkyboxRainNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Rain, data, 1);
        }

        data = presetMod.GetAsset<TextAsset>("SkyboxThunder.json", false).text;
        loadSkyboxSettings(WeatherType.Thunder, data, 0);

        night = presetMod.GetAsset<TextAsset>("SkyboxThunderNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Thunder, data, 1);
        }

        data = presetMod.GetAsset<TextAsset>("SkyboxSnow.json", false).text;
        loadSkyboxSettings(WeatherType.Snow, data, 0);

        night = presetMod.GetAsset<TextAsset>("SkyboxSnowNight.json", false);
        if(night) {
            data = night.text;
            loadSkyboxSettings(WeatherType.Snow, data, 1);
        }
    }
    private void loadSkyboxSettings(WeatherType weatherType, string data, int index = 0) {
        BLBSkyboxSetting skyboxSetting = BLBSkybox.ProcessSkyboxSetting(data);
        if(!SkyboxSettings.ContainsKey(weatherType)) {
            SkyboxSettings.Add(weatherType, new BLBSkyboxSetting[2]);
        }
        SkyboxSettings[weatherType][index] = skyboxSetting;
        //Set the day weather as default, night settings are loaded afterwards with index 1 so they will overwrite if present
        if(index == 0) {
            SkyboxSettings[weatherType][1] = skyboxSetting;
        }
    }
    #endregion

    #region Moons
    private float moonOrbitSpeed = 0.00024f / 12; //Default moon orbit speed in realtime
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
    private float starsTwinkleSpeed = 0.024f / 12; //Default star twinkle speed in realtime (timescale = 1)
    #endregion

    #region Fog
    private void setFogColor(bool day) {
        BLBSkyboxSetting[] skyboxSetting = SkyboxSettings[currentWeather];
        string fogDayColor = skyboxSetting[getWeatherIndex()].FogDayColor;
        string fogNightColor = skyboxSetting[getWeatherIndex()].FogNightColor;

        Color tmpColor;
        if(ColorUtility.TryParseHtmlString("#" + fogDayColor, out tmpColor)) {
            skyboxMat.SetColor("_FogDayColor", tmpColor);
            if(day) {
                UnityEngine.RenderSettings.fogColor = tmpColor;
            }
        }
        if(ColorUtility.TryParseHtmlString("#" + fogNightColor, out tmpColor)) {
            skyboxMat.SetColor("_FogNightColor", tmpColor);
            if(!day) {
                UnityEngine.RenderSettings.fogColor = tmpColor;
            }
        }
    }
    private void SetFogDistance(WeatherType weather) {
        BLBSkyboxSetting[] skyboxSetting = SkyboxSettings[weather];
        float fogDistance = skyboxSetting[getWeatherIndex()].FogDistance;
        WeatherManager.FogSettings fogSettings = FogSettings[weather];
        UnityEngine.RenderSettings.fogEndDistance = fogSettings.endDistance;
        skyboxMat.SetFloat("_FogDistance", fogDistance);
    }
    private Dictionary<WeatherType, WeatherManager.FogSettings> FogSettings;
    private void loadFogSettings() {
        string[] fogTypes = new string[]{"FogSunny","FogOvercast","FogHeavyFog","FogRainy","FogSnowy"};
        WeatherManager wm = GameManager.Instance.WeatherManager;
        string data;
        for(int i = 0; i < fogTypes.Length; i++) {
            data = presetMod.GetAsset<TextAsset>(fogTypes[i] + ".json", false).text;
            ProcessFogSetting(wm, fogTypes[i], data);
        }
        FogSettings = new Dictionary<WeatherType, WeatherManager.FogSettings>();
        FogSettings.Add(WeatherType.Sunny, wm.SunnyFogSettings);
        FogSettings.Add(WeatherType.Cloudy, wm.SunnyFogSettings);
        FogSettings.Add(WeatherType.Overcast, wm.OvercastFogSettings);
        FogSettings.Add(WeatherType.Fog, wm.HeavyFogSettings);
        FogSettings.Add(WeatherType.Rain, wm.RainyFogSettings);
        FogSettings.Add(WeatherType.Thunder, wm.RainyFogSettings);
        FogSettings.Add(WeatherType.Snow, wm.SnowyFogSettings);
    }

    private static bool ProcessFogSetting(WeatherManager wm, string name, string data) {
        WeatherManager.FogSettings newSettings = new WeatherManager.FogSettings();
        BLBFogSetting fogSetting = JsonUtility.FromJson<BLBFogSetting>(data);
        newSettings.fogMode = (FogMode) fogSetting.FogModeInt;
        newSettings.density = fogSetting.Density;
        newSettings.startDistance = fogSetting.StartDistance;
        newSettings.endDistance = fogSetting.EndDistance;
        newSettings.excludeSkybox = fogSetting.ExcludeSkybox;
        bool success = true;
        switch(name) {
            case "FogSunny":
                wm.SunnyFogSettings = newSettings;
                break;
            case "FogOvercast":
                wm.OvercastFogSettings = newSettings;
                break;
            case "FogHeavyFog":
                wm.HeavyFogSettings = newSettings;
                break;
            case "FogRainy":
                wm.RainyFogSettings = newSettings;
                break;
            case "FogSnowy":
                wm.SnowyFogSettings = newSettings;
                break;
            default:
                success = false;
                break;
        }
        return success;
    }

    private void getVanillaFogSettings() {
        WeatherManager wm = GameManager.Instance.WeatherManager;
        wm.SunnyFogSettings.excludeSkybox = true;
        wm.OvercastFogSettings.excludeSkybox = true;
        wm.HeavyFogSettings.excludeSkybox = false;
        wm.RainyFogSettings.excludeSkybox = true;
        wm.SnowyFogSettings.excludeSkybox = true;
        
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
        FogSettings.Add(WeatherType.Sunny, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1920f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Cloudy, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1600f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Overcast, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1440f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Fog, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 64f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Rain, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 1200f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Thunder, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 960f, excludeSkybox = true});
        FogSettings.Add(WeatherType.Snow, new WeatherManager.FogSettings {fogMode = FogMode.Linear, density = 0.0f, startDistance = 0f, endDistance = 720f, excludeSkybox = true});

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

    #region Snow
    private void InitSnow() {
        var settings = Mod.GetSettings();

        int minPS = settings.GetValue<int>("SnowSize","minParticleSize");
        int maxPS = settings.GetValue<int>("SnowSize","maxParticleSize");
        int maxP = settings.GetValue<int>("MaxParticles", "MaxParticles");
        
        minParticleSize = (minPS / 100000f);
        maxParticleSize = (maxPS / 100000f);
        maxParticles = (int) (maxP * 1000);

        Debug.Log("Min particle size: " + minParticleSize.ToString());
        Debug.Log("Max particle size: " + maxParticleSize.ToString());
        Debug.Log("Max particles: " + maxParticles.ToString());

        SnowMaterial = Mod.GetAsset<Material>("Materials/BLBSnowMaterial") as Material;
        GameObject playerAdvanced = GameObject.Find("PlayerAdvanced");
        if(playerAdvanced != null) {
            //Debug.Log("BLB: PlayerAdvanced found");
            GameObject smoothFollower = playerAdvanced.transform.Find("SmoothFollower").gameObject;
            if(smoothFollower != null) {
                //Debug.Log("BLB: SmoothFollower found");
                GameObject snowParticles = smoothFollower.transform.Find("Snow_Particles").gameObject;
                if(snowParticles != null) {
                    //Debug.Log("BLB: SnowParticles found");
                    ParticleSystem ps = snowParticles.GetComponent<ParticleSystem>();
                    if(ps != null) {
                        //Debug.Log("BLB: Particle system found");
                        ParticleSystem.MainModule main = ps.main;
                        main.startRotation = 0;

                        ParticleSystem.RotationOverLifetimeModule psROL = ps.rotationOverLifetime;
                        psROL.enabled = false;

                        ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                        psr.material = SnowMaterial;
                        psr.minParticleSize = minParticleSize;
                        psr.maxParticleSize = maxParticleSize;
                    }
                }
            }
        }
        Debug.Log("blb-pixel-snow initialized");
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

    /*
    private bool skyboxLerpRunning = false;
    IEnumerator SkyboxLerp()
    {
        //Animated the sun color
        float timeElapsed = 0;
        while (timeElapsed < sunFogLerpDuration)
        {
            //dfSunlight.color = Color.Lerp(sunStartColor, sunEndColor, timeElapsed / sunFogLerpDuration);
            UnityEngine.RenderSettings.fogColor = Color.Lerp(fogStartColor, fogEndColor, timeElapsed / sunFogLerpDuration);
            timeElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        UnityEngine.RenderSettings.fogColor = fogEndColor;
        //dfSunlight.color = sunEndColor;
        skyboxLerpRunning = false;
    }
    */
    #region Settings management

    private static bool ApplySkyboxSettings(BLBSkyboxSetting skyboxSetting, bool refreshTextures = true, bool updateMoons = true) {
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
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.AmbientColor, out tmpColor)) {
            #if !UNITY_EDITOR
                //Instance.playerAmbientLight.ExteriorNightAmbientLight = tmpColor;
            #endif
        }
        //UnityEngine.RenderSettings.ambientIntensity = skyboxSetting.AmbientIntensity;

        skyboxMat.SetFloat("_Exposure", skyboxSetting.Exposure);
        skyboxMat.SetFloat("_NightStartHeight", skyboxSetting.NightStartHeight);
        skyboxMat.SetFloat("_NightEndHeight", skyboxSetting.NightEndHeight);
        skyboxMat.SetFloat("_SkyFadeStart", skyboxSetting.SkyFadeStart);
        skyboxMat.SetFloat("_SkyFadeEnd", skyboxSetting.SkyEndStart);

        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.FogDayColor, out tmpColor)) {
            skyboxMat.SetColor("_FogDayColor", tmpColor);
        }
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.FogNightColor, out tmpColor)) {
            skyboxMat.SetColor("_FogNightColor", tmpColor);
        }
        skyboxMat.SetFloat("_FogDistance", skyboxSetting.FogDistance);

        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.TopClouds.DayColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudTopColor", tmpColor);
        }
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.TopClouds.NightColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudTopNightColor", tmpColor);
        }
        skyboxMat.SetFloat("_CloudTopAlphaCutoff", skyboxSetting.TopClouds.AlphaTreshold);
        skyboxMat.SetFloat("_CloudTopAlphaMax", skyboxSetting.TopClouds.AlphaMax);
        skyboxMat.SetFloat("_CloudTopColorBoost", skyboxSetting.TopClouds.ColorBoost);
        skyboxMat.SetFloat("_CloudTopOpacity", skyboxSetting.TopClouds.Opacity);
        skyboxMat.SetFloat("_CloudTopNormalEffect", skyboxSetting.TopClouds.NormalEffect);
        skyboxMat.SetFloat("_CloudTopBending", skyboxSetting.TopClouds.Bending);

        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.BottomClouds.DayColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudColor", tmpColor);
        }
        if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.BottomClouds.NightColor, out tmpColor)) {
            skyboxMat.SetColor("_CloudNightColor", tmpColor);
        }
        skyboxMat.SetFloat("_CloudAlphaCutoff", skyboxSetting.BottomClouds.AlphaTreshold);
        skyboxMat.SetFloat("_CloudAlphaMax", skyboxSetting.BottomClouds.AlphaMax);
        skyboxMat.SetFloat("_CloudColorBoost", skyboxSetting.BottomClouds.ColorBoost);
        skyboxMat.SetFloat("_CloudNormalEffect", skyboxSetting.BottomClouds.NormalEffect);
        skyboxMat.SetFloat("_CloudOpacity", skyboxSetting.BottomClouds.Opacity);
        skyboxMat.SetFloat("_CloudBending", skyboxSetting.BottomClouds.Bending);
        
        if(refreshTextures || firstInit) {
            firstInit = false;
            skyboxMat.SetTexture("_CloudTopDiffuse", skyboxSetting.TopClouds.CloudsTexture);
            skyboxMat.SetTexture("_CloudTopNormal", skyboxSetting.TopClouds.CloudsNormalTexture);
            skyboxMat.SetTextureScale("_CloudTopDiffuse", new Vector2(skyboxSetting.TopClouds.TilingX, skyboxSetting.TopClouds.TilingY));
            skyboxMat.SetTextureOffset("_CloudTopDiffuse", new Vector2(skyboxSetting.TopClouds.OffsetX, skyboxSetting.TopClouds.OffsetY));            

            skyboxMat.SetTexture("_CloudDiffuse", skyboxSetting.BottomClouds.CloudsTexture);
            skyboxMat.SetTexture("_CloudNormal", skyboxSetting.BottomClouds.CloudsNormalTexture);
            skyboxMat.SetTextureScale("_CloudDiffuse", new Vector2(skyboxSetting.BottomClouds.TilingX, skyboxSetting.BottomClouds.TilingY));
            skyboxMat.SetTextureOffset("_CloudDiffuse", new Vector2(skyboxSetting.BottomClouds.OffsetX, skyboxSetting.BottomClouds.OffsetY));

            skyboxMat.SetTexture("_StarTex", skyboxSetting.Stars.StarsTexture);
            skyboxMat.SetTextureScale("_StarTex", new Vector2(skyboxSetting.Stars.StarsTilingX, skyboxSetting.Stars.StarsTilingY));
            skyboxMat.SetTextureOffset("_StarTex", new Vector2(skyboxSetting.Stars.StarsOffsetX, skyboxSetting.Stars.StarsOffsetY));    

            skyboxMat.SetTexture("_StarTwinkleTex", skyboxSetting.Stars.StarsTwinkleTexture);
            skyboxMat.SetTextureScale("_StarTwinkleTex", new Vector2(skyboxSetting.Stars.StarsTilingX, skyboxSetting.Stars.StarsTilingY));
            skyboxMat.SetTextureOffset("_StarTwinkleTex", new Vector2(skyboxSetting.Stars.StarsOffsetX, skyboxSetting.Stars.StarsOffsetY));    

            skyboxMat.SetTexture("_TwinkleTex", skyboxSetting.Stars.TwinkleTexture);
            skyboxMat.SetTextureScale("_TwinkleTex", new Vector2(skyboxSetting.Stars.TwinkleTilingX, skyboxSetting.Stars.TwinkleTilingY));
            skyboxMat.SetTextureOffset("_TwinkleTex", new Vector2(skyboxSetting.Stars.StarsOffsetX, skyboxSetting.Stars.StarsOffsetY));

            skyboxMat.SetTexture("_MoonTex", skyboxSetting.Masser.MoonTexture);
            skyboxMat.SetTextureScale("_MoonTex", new Vector2(skyboxSetting.Masser.TilingX, skyboxSetting.Masser.TilingY));
            skyboxMat.SetTextureOffset("_MoonTex", new Vector2(skyboxSetting.Masser.OffsetX, skyboxSetting.Masser.OffsetY));

            skyboxMat.SetTexture("_SecundaTex", skyboxSetting.Secunda.MoonTexture);
            skyboxMat.SetTextureScale("_SecundaTex", new Vector2(skyboxSetting.Secunda.TilingX, skyboxSetting.Secunda.TilingY));
            skyboxMat.SetTextureOffset("_SecundaTex", new Vector2(skyboxSetting.Secunda.OffsetX, skyboxSetting.Secunda.OffsetY));
        }

        skyboxMat.SetFloat("_StarBending", skyboxSetting.Stars.StarBending);

        //skyboxMat.SetFloat("_StarBrightness", skyboxSetting.Stars.StarBrightness);
        skyboxMat.SetFloat("_TwinkleBoost", skyboxSetting.Stars.TwinkleBoost);
        skyboxMat.SetFloat("_TwinkleSpeed", skyboxSetting.Stars.TwinkleSpeed);

        if(updateMoons) {
            if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.Masser.MoonColor, out tmpColor)) {
                skyboxMat.SetColor("_MoonColor", tmpColor);
            }
            skyboxMat.SetFloat("_MoonMinSize", skyboxSetting.Masser.MinSize);
            skyboxMat.SetFloat("_MoonMaxSize", skyboxSetting.Masser.MaxSize);
            skyboxMat.SetVector("_MoonOrbitAngle", skyboxSetting.Masser.OrbitAngle);
            skyboxMat.SetFloat("_MoonOrbitOffset", skyboxSetting.Masser.OrbitOffset);
            skyboxMat.SetFloat("_MoonOrbitSpeed", skyboxSetting.Masser.OrbitSpeed);
            skyboxMat.SetFloat("_MoonSemiMinAxis", skyboxSetting.Masser.SemiMinAxis);
            skyboxMat.SetFloat("_MoonSemiMajAxis", skyboxSetting.Masser.SemiMajAxis);
            skyboxMat.SetFloat("_MoonPhaseOption", skyboxSetting.Masser.AutoPhase);
            skyboxMat.SetVector("_MoonPhase", skyboxSetting.Masser.Phase);
            skyboxMat.SetFloat("_MoonSpinOption", skyboxSetting.Masser.Spin);
            skyboxMat.SetVector("_MasserTidalAngle", skyboxSetting.Masser.TidalAngle);
            skyboxMat.SetVector("_MoonSpinSpeed", skyboxSetting.Masser.SpinSpeed);

            if(ColorUtility.TryParseHtmlString("#" + skyboxSetting.Secunda.MoonColor, out tmpColor)) {
                skyboxMat.SetColor("_SecundaColor", tmpColor);
            }

            skyboxMat.SetFloat("_SecundaMinSize", skyboxSetting.Secunda.MinSize);
            skyboxMat.SetFloat("_SecundaMaxSize", skyboxSetting.Secunda.MaxSize);
            skyboxMat.SetVector("_SecundaOrbitAngle", skyboxSetting.Secunda.OrbitAngle);
            skyboxMat.SetFloat("_SecundaOrbitOffset", skyboxSetting.Secunda.OrbitOffset);
            skyboxMat.SetFloat("_SecundaOrbitSpeed", skyboxSetting.Secunda.OrbitSpeed);
            skyboxMat.SetFloat("_SecundaSemiMinAxis", skyboxSetting.Secunda.SemiMinAxis);
            skyboxMat.SetFloat("_SecundaSemiMajAxis", skyboxSetting.Secunda.SemiMajAxis);
            skyboxMat.SetFloat("_SecundaPhaseOption", skyboxSetting.Secunda.AutoPhase);
            skyboxMat.SetVector("_SecundaPhase", skyboxSetting.Secunda.Phase);
            skyboxMat.SetFloat("_SecundaSpinOption", skyboxSetting.Secunda.Spin);
            skyboxMat.SetVector("_SecundaTidalAngle", skyboxSetting.Secunda.TidalAngle);
            skyboxMat.SetVector("_SecundaSpinSpeed", skyboxSetting.Secunda.SpinSpeed);
        }

        return true;
    }

    private static BLBSkyboxSetting ProcessSkyboxSetting(string data) {
        BLBSkyboxSetting skyboxSetting = JsonUtility.FromJson<BLBSkyboxSetting>(data);
        skyboxSetting.TopClouds = JsonUtility.FromJson<BLBCloudsSetting>(skyboxSetting.TopCloudsFlat);
        Debug.Log("Tried to read top clouds settings");
        skyboxSetting.BottomClouds = JsonUtility.FromJson<BLBCloudsSetting>(skyboxSetting.BottomCloudsFlat);
        Debug.Log("Tried to read bottom clouds settings");
        skyboxSetting.Stars = JsonUtility.FromJson<BLBStarsSetting>(skyboxSetting.StarsFlat);
        Debug.Log("Tried to read stars settings");
        skyboxSetting.Masser = JsonUtility.FromJson<BLBMoonSetting>(skyboxSetting.MasserFlat);
        Debug.Log("Tried to read masser settings");
        skyboxSetting.Secunda = JsonUtility.FromJson<BLBMoonSetting>(skyboxSetting.SecundaFlat);
        Debug.Log("Tried to read secunda settings");

        Texture2D TopCloudsTexture;
        Texture2D TopCloudsNormalTexture;
        Texture2D BottomCloudsTexture;
        Texture2D BottomCloudsNormalTexture;
        Texture2D StarsTexture;
        Texture2D StarsTwinkleTexture;
        Texture2D TwinkleTexture;
        Texture2D MoonTexture;
        Texture2D SecundaTexture;
        
        string[] guids;
        #if UNITY_EDITOR
            guids = AssetDatabase.FindAssets(skyboxSetting.TopClouds.CloudsTextureFile);
            TopCloudsTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.TopClouds.CloudsNormalTextureFile);
            TopCloudsNormalTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.BottomClouds.CloudsTextureFile);
            BottomCloudsTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.BottomClouds.CloudsNormalTextureFile);
            BottomCloudsNormalTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.Stars.StarsTextureFile);
            StarsTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.Stars.StarsTwinkleTextureFile);
            StarsTwinkleTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.Stars.TwinkleTextureFile);
            TwinkleTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.Masser.MoonTextureFile);
            MoonTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));

            guids = AssetDatabase.FindAssets(skyboxSetting.Secunda.MoonTextureFile);
            SecundaTexture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));
        #else
            TopCloudsTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.TopClouds.CloudsTextureFile);
            TopCloudsNormalTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.TopClouds.CloudsNormalTextureFile);
            
            BottomCloudsTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.BottomClouds.CloudsTextureFile);
            BottomCloudsNormalTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.BottomClouds.CloudsNormalTextureFile);

            StarsTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.Stars.StarsTextureFile);
            StarsTwinkleTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.Stars.StarsTwinkleTextureFile);
            TwinkleTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.Stars.TwinkleTextureFile);

            MoonTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.Masser.MoonTextureFile);
            SecundaTexture = Instance.presetMod.GetAsset<Texture2D>(skyboxSetting.Secunda.MoonTextureFile);
        #endif
        
        skyboxSetting.TopClouds.CloudsTexture = TopCloudsTexture;
        skyboxSetting.TopClouds.CloudsNormalTexture = TopCloudsNormalTexture;

        skyboxSetting.BottomClouds.CloudsTexture = BottomCloudsTexture;
        skyboxSetting.BottomClouds.CloudsNormalTexture = BottomCloudsNormalTexture;

        skyboxSetting.Stars.StarsTexture = StarsTexture;
        skyboxSetting.Stars.StarsTwinkleTexture = StarsTwinkleTexture;
        skyboxSetting.Stars.TwinkleTexture = TwinkleTexture;

        skyboxSetting.Masser.MoonTexture = MoonTexture;
        skyboxSetting.Secunda.MoonTexture = SecundaTexture;

        return skyboxSetting;
    }

    #if UNITY_EDITOR
    [MenuItem("BLB/Import skybox settings")]
    static void ImportSkyboxSettings()
    {
        string path = EditorUtility.OpenFilePanel("Choose skybox settings to import", "", "json");
        string data = File.ReadAllText(path);

        BLBSkyboxSetting skyboxSetting = ProcessSkyboxSetting(data);
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
        BLBCloudsSetting TopClouds = new BLBCloudsSetting();
        BLBCloudsSetting BottomClouds = new BLBCloudsSetting();
        BLBStarsSetting Stars = new BLBStarsSetting();

        skyboxSetting.SunSize = skyboxMat.GetFloat("_SunSize");
        skyboxSetting.SunSizeConvergence = skyboxMat.GetInt("_SunSizeConvergence");
        skyboxSetting.AtmosphereThickness = skyboxMat.GetFloat("_AtmosphereThickness");
        skyboxSetting.SkyTint = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_SkyTint"));
        skyboxSetting.GroundColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_GroundColor"));
        skyboxSetting.AmbientColor = ColorUtility.ToHtmlStringRGBA(UnityEngine.RenderSettings.ambientLight);
        skyboxSetting.AmbientIntensity = UnityEngine.RenderSettings.ambientIntensity;
        skyboxSetting.Exposure = skyboxMat.GetFloat("_Exposure");
        skyboxSetting.NightStartHeight = skyboxMat.GetFloat("_NightStartHeight");
        skyboxSetting.NightEndHeight = skyboxMat.GetFloat("_NightEndHeight");
        skyboxSetting.SkyFadeStart = skyboxMat.GetFloat("_SkyFadeStart");
        skyboxSetting.SkyEndStart = skyboxMat.GetFloat("_SkyFadeEnd");
        skyboxSetting.FogDayColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_FogDayColor"));
        skyboxSetting.FogNightColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_FogNightColor"));
        skyboxSetting.FogDistance = skyboxMat.GetFloat("_FogDistance");
        
        TopClouds.CloudsTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_CloudTopDiffuse")));
        TopClouds.CloudsNormalTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_CloudTopNormal")));
        TopClouds.TilingX = skyboxMat.GetTextureScale("_CloudTopDiffuse").x;
        TopClouds.TilingY = skyboxMat.GetTextureScale("_CloudTopDiffuse").y;
        TopClouds.OffsetX = skyboxMat.GetTextureOffset("_CloudTopDiffuse").x;
        TopClouds.OffsetY = skyboxMat.GetTextureOffset("_CloudTopDiffuse").y;
        TopClouds.DayColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudTopColor"));
        TopClouds.NightColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudTopNightColor"));
        TopClouds.AlphaTreshold = skyboxMat.GetFloat("_CloudTopAlphaCutoff");
        TopClouds.AlphaMax = skyboxMat.GetFloat("_CloudTopAlphaMax");
        TopClouds.ColorBoost = skyboxMat.GetFloat("_CloudTopColorBoost");
        TopClouds.NormalEffect = skyboxMat.GetFloat("_CloudTopNormalEffect");
        TopClouds.Opacity = skyboxMat.GetFloat("_CloudTopOpacity");
        TopClouds.Bending = skyboxMat.GetFloat("_CloudTopBending");

        skyboxSetting.TopCloudsFlat = JsonUtility.ToJson(TopClouds);

        BottomClouds.CloudsTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_CloudDiffuse")));
        BottomClouds.CloudsNormalTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_CloudNormal")));
        BottomClouds.TilingX = skyboxMat.GetTextureScale("_CloudDiffuse").x;
        BottomClouds.TilingY = skyboxMat.GetTextureScale("_CloudDiffuse").y;
        BottomClouds.OffsetX = skyboxMat.GetTextureOffset("_CloudDiffuse").x;
        BottomClouds.OffsetY = skyboxMat.GetTextureOffset("_CloudDiffuse").y;
        BottomClouds.DayColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudColor"));
        BottomClouds.NightColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_CloudNightColor"));
        BottomClouds.AlphaTreshold = skyboxMat.GetFloat("_CloudAlphaCutoff");
        BottomClouds.AlphaMax = skyboxMat.GetFloat("_CloudAlphaMax");
        BottomClouds.ColorBoost = skyboxMat.GetFloat("_CloudColorBoost");
        BottomClouds.NormalEffect = skyboxMat.GetFloat("_CloudNormalEffect");
        BottomClouds.Opacity = skyboxMat.GetFloat("_CloudOpacity");
        BottomClouds.Bending = skyboxMat.GetFloat("_CloudBending");

        skyboxSetting.BottomCloudsFlat = JsonUtility.ToJson(BottomClouds);

        Stars.StarsTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_StarTex")));
        Stars.StarsTilingX = skyboxMat.GetTextureScale("_StarTex").x;
        Stars.StarsTilingY = skyboxMat.GetTextureScale("_StarTex").y;
        Stars.StarsOffsetX = skyboxMat.GetTextureOffset("_StarTex").x;
        Stars.StarsOffsetY = skyboxMat.GetTextureOffset("_StarTex").y;
        Stars.StarBending = skyboxMat.GetFloat("_StarBending");
        //Stars.StarBrightness = skyboxMat.GetFloat("_StarBrightness");

        Stars.StarsTwinkleTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_StarTwinkleTex")));

        Stars.TwinkleTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_TwinkleTex")));
        Stars.TwinkleTilingX = skyboxMat.GetTextureScale("_TwinkleTex").x;
        Stars.TwinkleTilingY = skyboxMat.GetTextureScale("_TwinkleTex").y;
        Stars.TwinkleOffsetX = skyboxMat.GetTextureOffset("_TwinkleTex").x;
        Stars.TwinkleOffsetY = skyboxMat.GetTextureOffset("_TwinkleTex").y;
        Stars.TwinkleBoost = skyboxMat.GetFloat("_TwinkleBoost");
        Stars.TwinkleSpeed = skyboxMat.GetFloat("_TwinkleSpeed");

        skyboxSetting.StarsFlat = JsonUtility.ToJson(Stars);

        BLBMoonSetting Masser = new BLBMoonSetting();
        Masser.MoonColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_MoonColor"));
        Masser.MoonTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_MoonTex")));
        Masser.MinSize = skyboxMat.GetFloat("_MoonMinSize");
        Masser.MaxSize = skyboxMat.GetFloat("_MoonMaxSize");
        Masser.OrbitAngle = skyboxMat.GetVector("_MoonOrbitAngle");
        Masser.OrbitOffset = skyboxMat.GetFloat("_MoonOrbitOffset");
        Masser.OrbitSpeed = skyboxMat.GetFloat("_MoonOrbitSpeed");
        Masser.SemiMinAxis = skyboxMat.GetFloat("_MoonSemiMinAxis");
        Masser.SemiMajAxis = skyboxMat.GetFloat("_MoonSemiMajAxis");
        Masser.AutoPhase = skyboxMat.GetFloat("_MoonPhaseOption");
        Masser.Phase = skyboxMat.GetVector("_MoonPhase");
        Masser.Spin = skyboxMat.GetFloat("_MoonSpinOption");
        Masser.TidalAngle = skyboxMat.GetVector("_MoonTidalAngle");
        Masser.SpinSpeed = skyboxMat.GetVector("_MoonSpinSpeed");

        skyboxSetting.MasserFlat = JsonUtility.ToJson(Masser);

        BLBMoonSetting Secunda = new BLBMoonSetting();
        Secunda.MoonColor = ColorUtility.ToHtmlStringRGBA(skyboxMat.GetColor("_SecundaColor"));
        Secunda.MoonTextureFile = Path.GetFileNameWithoutExtension(UnityEditor.AssetDatabase.GetAssetPath(skyboxMat.GetTexture("_SecundaTex")));
        Secunda.MinSize = skyboxMat.GetFloat("_SecundaMinSize");
        Secunda.MaxSize = skyboxMat.GetFloat("_SecundaMaxSize");
        Secunda.OrbitAngle = skyboxMat.GetVector("_SecundaOrbitAngle");
        Secunda.OrbitOffset = skyboxMat.GetFloat("_SecundaOrbitOffset");
        Secunda.OrbitSpeed = skyboxMat.GetFloat("_SecundaOrbitSpeed");
        Secunda.SemiMinAxis = skyboxMat.GetFloat("_SecundaSemiMinAxis");
        Secunda.SemiMajAxis = skyboxMat.GetFloat("_SecundaSemiMajAxis");
        Secunda.AutoPhase = skyboxMat.GetFloat("_SecundaPhaseOption");
        Secunda.Phase = skyboxMat.GetVector("_SecundaPhase");
        Secunda.Spin = skyboxMat.GetFloat("_SecundaSpinOption");
        Secunda.TidalAngle = skyboxMat.GetVector("_SecundaTidalAngle");
        Secunda.SpinSpeed = skyboxMat.GetVector("_SecundaSpinSpeed");

        skyboxSetting.SecundaFlat = JsonUtility.ToJson(Secunda);

        string data = JsonUtility.ToJson(skyboxSetting, true);
        File.WriteAllText(path, data);

        EditorUtility.DisplayDialog("Success","Skybox settings have been saved to: \n" + path, "Ok","");
    }
    [MenuItem("BLB/Export skybox settings", true)]
    static bool ValidateExportSkyboxSettings()
    {
        return UnityEngine.RenderSettings.skybox != null;
    }

    [MenuItem("BLB/Import fog settings")]
    static void ImportFogSettings()
    {
        string path = EditorUtility.OpenFilePanel("Choose fog settings to import", "", "json");
        string name = Path.GetFileNameWithoutExtension(path);
        string data = File.ReadAllText(path);
        WeatherManager wm = GetWeatherManager();
        bool success = ProcessFogSetting(wm, name, data);
        if(success) {
            EditorUtility.DisplayDialog("Success",name + " settings have been imported successfully.", "Ok","");
        } else {
            EditorUtility.DisplayDialog("Failure",name + " settings have not been imported successfully.", "Ok","");
        }
    }
    [MenuItem("BLB/Export fog settings")]
    static void ExportFogSettings()
    {
        WeatherManager wm = GetWeatherManager();
        BLBFogSetting fogSetting;
        string data, path, savePath;
        if(wm != null) {
            path = EditorUtility.SaveFolderPanel("Choose save folder", "", "");//SaveFilePanel("Choose save folder", "", "", "json");
            fogSetting = CreateFogSetting(wm.SunnyFogSettings);
            data = JsonUtility.ToJson(fogSetting, true);
            savePath = Path.Combine(path, "FogSunny.json");
            File.WriteAllText(savePath, data);
            //savePath = Path.Combine(path, "FogCloudy.json");
            //File.WriteAllText(savePath, data);

            fogSetting = CreateFogSetting(wm.OvercastFogSettings);
            data = JsonUtility.ToJson(fogSetting, true);
            savePath = Path.Combine(path, "FogOvercast.json");
            File.WriteAllText(savePath, data);

            fogSetting = CreateFogSetting(wm.HeavyFogSettings);
            data = JsonUtility.ToJson(fogSetting, true);
            savePath = Path.Combine(path, "FogHeavyFog.json");
            File.WriteAllText(savePath, data);

            fogSetting = CreateFogSetting(wm.RainyFogSettings);
            data = JsonUtility.ToJson(fogSetting, true);
            savePath = Path.Combine(path, "FogRainy.json");
            File.WriteAllText(savePath, data);

            fogSetting = CreateFogSetting(wm.SnowyFogSettings);
            data = JsonUtility.ToJson(fogSetting, true);
            savePath = Path.Combine(path, "FogSnowy.json");
            File.WriteAllText(savePath, data);

            EditorUtility.DisplayDialog("Success","Fog settings have been saved to: \n" + path, "Ok","");
        }
        
    }

    static WeatherManager GetWeatherManager() {
        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject[] gos = scene.GetRootGameObjects();
        WeatherManager wm = null;
        for(int i = 0; i < gos.Length; i++) {
            if(gos[i].name == "WeatherManager") {
                wm = gos[i].GetComponent<WeatherManager>();
                break;
            }
        }
        return wm;
    }

    static BLBFogSetting CreateFogSetting(WeatherManager.FogSettings setting) {
        BLBFogSetting fogSetting = new BLBFogSetting();
        fogSetting.FogMode = setting.fogMode;
        fogSetting.FogModeInt = (int) setting.fogMode;
        fogSetting.Density = setting.density;
        fogSetting.StartDistance = setting.startDistance;
        fogSetting.EndDistance = setting.endDistance;
        fogSetting.ExcludeSkybox = setting.excludeSkybox;
        return fogSetting;
    }

    #endif

    #endregion

    #region VGA Color palette

    private void SetPalettizationMaterial()
    {
        int lutShift = DaggerfallUnity.Settings.PalettizationLUTShift;
        int size = 256 >> lutShift;
        //InitLut(lutShift, size);
        //skyboxMat.SetTexture("_Lut", lut);
    }

    static Color32[] vgaPalette = new Color32[]{
        new Color32( 0, 0, 0, 255),
        new Color32( 131, 161, 74, 255),
        new Color32( 172, 121, 123, 255),
        new Color32( 106, 145, 65, 255),
        new Color32( 90, 129, 90, 255),
        new Color32( 131, 186, 189, 255),
        new Color32( 106, 153, 222, 255),
        new Color32( 222, 178, 180, 255),
        new Color32( 205, 157, 156, 255),
        new Color32( 156, 133, 131, 255),
        new Color32( 156, 145, 115, 255),
        new Color32( 156, 202, 205, 255),
        new Color32( 172, 93, 74, 255),
        new Color32( 213, 198, 139, 255),
        new Color32( 213, 153, 74, 255),
        new Color32( 189, 206, 123, 255),
        new Color32( 246, 210, 139, 255),
        new Color32( 164, 85, 57, 255),
        new Color32( 156, 105, 106, 255),
        new Color32( 57, 40, 32, 255),
        new Color32( 230, 153, 65, 255),
        new Color32( 139, 161, 222, 255),
        new Color32( 148, 121, 106, 255),
        new Color32( 180, 72, 32, 255),
        new Color32( 115, 60, 57, 255),
        new Color32( 180, 190, 197, 255),
        new Color32( 197, 206, 222, 255),
        new Color32( 74, 32, 24, 255),
        new Color32( 230, 210, 131, 255),
        new Color32( 213, 210, 189, 255),
        new Color32( 123, 68, 57, 255),
        new Color32( 230, 226, 205, 255),
        new Color32( 156, 68, 32, 255),
        new Color32( 180, 141, 90, 255),
        new Color32( 189, 210, 238, 255),
        new Color32( 172, 125, 65, 255),
        new Color32( 156, 165, 189, 255),
        new Color32( 98, 40, 24, 255),
        new Color32( 82, 40, 24, 255),
        new Color32( 156, 44, 16, 255),
        new Color32( 213, 174, 131, 255),
        new Color32( 82, 85, 82, 255),
        new Color32( 172, 186, 238, 255),
        new Color32( 205, 234, 255, 255),
        new Color32( 148, 113, 98, 255),
        new Color32( 213, 194, 139, 255),
        new Color32( 230, 178, 90, 255),
        new Color32( 115, 89, 65, 255),
        new Color32( 164, 170, 139, 255),
        new Color32( 164, 97, 57, 255),
        new Color32( 197, 101, 49, 255),
        new Color32( 74, 76, 74, 255),
        new Color32( 32, 4, 0, 255),
        new Color32( 164, 149, 123, 255),
        new Color32( 205, 210, 197, 255),
        new Color32( 148, 93, 57, 255),
        new Color32( 172, 60, 24, 255),
        new Color32( 148, 89, 41, 255),
        new Color32( 246, 246, 164, 255),
        new Color32( 139, 137, 139, 255),
        new Color32( 189, 64, 16, 255),
        new Color32( 123, 68, 41, 255),
        new Color32( 255, 250, 189, 255),
        new Color32( 106, 109, 106, 255),
        new Color32( 222, 137, 57, 255),
        new Color32( 148, 76, 41, 255),
        new Color32( 123, 80, 57, 255),
        new Color32( 255, 194, 82, 255),
        new Color32( 189, 101, 57, 255),
        new Color32( 164, 109, 74, 255),
        new Color32( 139, 145, 148, 255),
        new Color32( 98, 56, 41, 255),
        new Color32( 230, 246, 238, 255),
        new Color32( 156, 170, 230, 255),
        new Color32( 255, 226, 115, 255),
        new Color32( 246, 234, 139, 255),
        new Color32( 246, 202, 98, 255),
        new Color32( 180, 182, 189, 255),
        new Color32( 238, 222, 148, 255),
        new Color32( 131, 121, 106, 255),
        new Color32( 164, 174, 222, 255),
        new Color32( 172, 178, 197, 255),
        new Color32( 230, 190, 82, 255),
        new Color32( 123, 97, 82, 255),
        new Color32( 189, 76, 24, 255),
        new Color32( 197, 93, 32, 255),
        new Color32( 131, 56, 24, 255),
        new Color32( 74, 56, 65, 255),
        new Color32( 172, 186, 222, 255),
        new Color32( 246, 214, 98, 255),
        new Color32( 106, 170, 172, 255),
        new Color32( 123, 121, 139, 255),
        new Color32( 164, 157, 148, 255),
        new Color32( 197, 194, 189, 255),
        new Color32( 164, 137, 98, 255),
        new Color32( 164, 178, 222, 255),
        new Color32( 148, 52, 24, 255),
        new Color32( 74, 4, 0, 255),
        new Color32( 49, 4, 0, 255),
        new Color32( 74, 60, 32, 255),
        new Color32( 115, 28, 16, 255),
        new Color32( 222, 214, 189, 255),
        new Color32( 197, 133, 82, 255),
        new Color32( 139, 64, 41, 255),
        new Color32( 131, 93, 90, 255),
        new Color32( 148, 186, 246, 255),
        new Color32( 205, 226, 246, 255),
        new Color32( 148, 137, 98, 255),
        new Color32( 139, 105, 65, 255),
        new Color32( 156, 64, 24, 255),
        new Color32( 139, 93, 90, 255),
        new Color32( 205, 105, 32, 255),
        new Color32( 222, 238, 255, 255),
        new Color32( 139, 121, 90, 255),
        new Color32( 172, 85, 41, 255),
        new Color32( 246, 250, 205, 255),
        new Color32( 148, 149, 139, 255),
        new Color32( 164, 161, 148, 255),
        new Color32( 238, 238, 230, 255),
        new Color32( 139, 89, 57, 255),
        new Color32( 164, 165, 156, 255),
        new Color32( 57, 32, 16, 255),
        new Color32( 205, 129, 65, 255),
        new Color32( 98, 68, 82, 255),
        new Color32( 115, 117, 115, 255),
        new Color32( 156, 117, 65, 255),
        new Color32( 148, 117, 98, 255),
        new Color32( 156, 174, 213, 255),
        new Color32( 148, 117, 82, 255),
        new Color32( 82, 52, 41, 255),
        new Color32( 164, 129, 65, 255),
        new Color32( 156, 182, 98, 255),
        new Color32( 123, 117, 98, 255),
        new Color32( 205, 149, 82, 255),
        new Color32( 164, 174, 205, 255),
        new Color32( 238, 182, 82, 255),
        new Color32( 131, 76, 57, 255),
        new Color32( 106, 24, 16, 255),
        new Color32( 98, 16, 8, 255),
        new Color32( 123, 109, 82, 255),
        new Color32( 139, 80, 57, 255),
        new Color32( 197, 202, 172, 255),
        new Color32( 139, 137, 106, 255),
        new Color32( 205, 218, 238, 255),
        new Color32( 172, 153, 90, 255),
        new Color32( 205, 210, 222, 255),
        new Color32( 180, 198, 222, 255),
        new Color32( 74, 20, 16, 255),
        new Color32( 189, 222, 246, 255),
        new Color32( 189, 194, 172, 255),
        new Color32( 238, 234, 180, 255),
        new Color32( 131, 32, 16, 255),
        new Color32( 213, 182, 98, 255),
        new Color32( 189, 170, 115, 255),
        new Color32( 41, 12, 8, 255),
        new Color32( 180, 109, 41, 255),
        new Color32( 98, 64, 57, 255),
        new Color32( 106, 32, 16, 255),
        new Color32( 213, 133, 57, 255),
        new Color32( 148, 109, 65, 255),
        new Color32( 197, 129, 65, 255),
        new Color32( 205, 165, 115, 255),
        new Color32( 222, 230, 222, 255),
        new Color32( 123, 44, 24, 255),
        new Color32( 222, 218, 164, 255),
        new Color32( 57, 20, 16, 255),
        new Color32( 123, 157, 115, 255),
        new Color32( 180, 89, 41, 255),
        new Color32( 222, 174, 106, 255),
        new Color32( 106, 64, 57, 255),
        new Color32( 57, 24, 16, 255),
        new Color32( 213, 222, 230, 255),
        new Color32( 148, 105, 90, 255),
        new Color32( 106, 76, 65, 255),
        new Color32( 238, 165, 57, 255),
        new Color32( 246, 230, 131, 255),
        new Color32( 82, 137, 205, 255),
        new Color32( 123, 97, 65, 255),
        new Color32( 74, 12, 8, 255),
        new Color32( 197, 145, 98, 255),
        new Color32( 205, 222, 255, 255),
        new Color32( 180, 198, 238, 255),
        new Color32( 222, 230, 238, 255),
        new Color32( 246, 226, 139, 255),
        new Color32( 180, 165, 139, 255),
        new Color32( 189, 174, 148, 255),
        new Color32( 172, 174, 164, 255),
        new Color32( 213, 190, 115, 255),
        new Color32( 246, 222, 156, 255),
        new Color32( 148, 44, 16, 255),
        new Color32( 189, 121, 74, 255),
        new Color32( 148, 149, 180, 255),
        new Color32( 189, 137, 139, 255),
        new Color32( 213, 202, 164, 255),
        new Color32( 139, 153, 205, 255),
        new Color32( 139, 149, 172, 255),
        new Color32( 90, 56, 57, 255),
        new Color32( 180, 80, 41, 255),
        new Color32( 148, 165, 205, 255),
        new Color32( 115, 64, 41, 255),
        new Color32( 172, 149, 115, 255),
        new Color32( 238, 198, 197, 255),
        new Color32( 213, 186, 131, 255),
        new Color32( 189, 214, 255, 255),
        new Color32( 180, 202, 255, 255),
        new Color32( 156, 182, 255, 255),
        new Color32( 98, 48, 41, 255),
        new Color32( 98, 97, 98, 255),
        new Color32( 82, 44, 41, 255),
        new Color32( 148, 161, 213, 255),
        new Color32( 115, 16, 8, 255),
        new Color32( 180, 178, 156, 255),
        new Color32( 180, 182, 164, 255),
        new Color32( 156, 109, 57, 255),
        new Color32( 246, 214, 172, 255),
        new Color32( 197, 186, 131, 255),
        new Color32( 98, 76, 57, 255),
        new Color32( 238, 238, 213, 255),
        new Color32( 197, 202, 197, 255),
        new Color32( 57, 12, 8, 255),
        new Color32( 197, 186, 148, 255),
        new Color32( 222, 202, 131, 255),
        new Color32( 156, 157, 172, 255),
        new Color32( 213, 206, 172, 255),
        new Color32( 255, 250, 148, 255),
        new Color32( 205, 113, 49, 255),
        new Color32( 246, 174, 74, 255),
        new Color32( 115, 56, 41, 255),
        new Color32( 148, 153, 197, 255),
        new Color32( 164, 161, 164, 255),
        new Color32( 139, 44, 24, 255),
        new Color32( 115, 105, 90, 255),
        new Color32( 255, 222, 106, 255),
        new Color32( 139, 133, 123, 255),
        new Color32( 255, 242, 148, 255),
        new Color32( 90, 20, 16, 255),
        new Color32( 131, 133, 131, 255),
        new Color32( 189, 149, 74, 255),
        new Color32( 90, 56, 41, 255),
        new Color32( 222, 218, 205, 255),
        new Color32( 148, 174, 255, 255),
        new Color32( 180, 137, 98, 255),
        new Color32( 238, 206, 115, 255),
        new Color32( 230, 255, 255, 255),
        new Color32( 139, 149, 164, 255),
        new Color32( 180, 161, 123, 255),
        new Color32( 98, 72, 57, 255),
        new Color32( 172, 105, 65, 255),
        new Color32( 172, 161, 98, 255),
        new Color32( 222, 117, 41, 255),
        new Color32( 222, 129, 41, 255),
        new Color32( 197, 153, 106, 255),
        new Color32( 180, 174, 131, 255),
        new Color32( 148, 145, 115, 255),
        new Color32( 197, 165, 82, 255),
        new Color32( 156, 161, 172, 255)
        /*new Color32( 0, 0, 170, 255),
        new Color32( 0, 170, 0, 255),
        new Color32( 0, 170, 170, 255),
        new Color32( 170, 0, 0, 255),
        new Color32( 170, 0, 170, 255),
        new Color32( 170, 85, 0, 255),
        new Color32( 170, 170, 170, 255),
        new Color32( 85, 85, 85, 255),
        new Color32( 85, 85, 255, 255),
        new Color32( 85, 255, 85, 255),
        new Color32( 85, 255, 255, 255),
        new Color32( 255, 85, 85, 255),
        new Color32( 255, 85, 255, 255),
        new Color32( 255, 255, 85, 255),
        new Color32( 255, 255, 255, 255),
        new Color32( 16, 16, 16, 255),
        new Color32( 32, 32, 32, 255),
        new Color32( 53, 53, 53, 255),
        new Color32( 69, 69, 69, 255),
        new Color32( 85, 85, 85, 255),
        new Color32( 101, 101, 101, 255),
        new Color32( 117, 117, 117, 255),
        new Color32( 138, 138, 138, 255),
        new Color32( 154, 154, 154, 255),
        new Color32( 170, 170, 170, 255),
        new Color32( 186, 186, 186, 255),
        new Color32( 202, 202, 202, 255),
        new Color32( 223, 223, 223, 255),
        new Color32( 239, 239, 239, 255),
        new Color32( 255, 255, 255, 255),
        new Color32( 0, 0, 255, 255),
        new Color32( 65, 0, 255, 255),
        new Color32( 130, 0, 255, 255),
        new Color32( 190, 0, 255, 255),
        new Color32( 255, 0, 255, 255),
        new Color32( 255, 0, 190, 255),
        new Color32( 255, 0, 130, 255),
        new Color32( 255, 0, 65, 255),
        new Color32( 255, 0, 0, 255),
        new Color32( 255, 65, 0, 255),
        new Color32( 255, 130, 0, 255),
        new Color32( 255, 190, 0, 255),
        new Color32( 255, 255, 0, 255),
        new Color32( 190, 255, 0, 255),
        new Color32( 130, 255, 0, 255),
        new Color32( 65, 255, 0, 255),
        new Color32( 0, 255, 0, 255),
        new Color32( 0, 255, 65, 255),
        new Color32( 0, 255, 130, 255),
        new Color32( 0, 255, 190, 255),
        new Color32( 0, 255, 255, 255),
        new Color32( 0, 190, 255, 255),
        new Color32( 0, 130, 255, 255),
        new Color32( 0, 65, 255, 255),
        new Color32( 130, 130, 255, 255),
        new Color32( 158, 130, 255, 255),
        new Color32( 190, 130, 255, 255),
        new Color32( 223, 130, 255, 255),
        new Color32( 255, 130, 255, 255),
        new Color32( 255, 130, 223, 255),
        new Color32( 255, 130, 190, 255),
        new Color32( 255, 130, 158, 255),
        new Color32( 255, 130, 130, 255),
        new Color32( 255, 158, 130, 255),
        new Color32( 255, 190, 130, 255),
        new Color32( 255, 223, 130, 255),
        new Color32( 255, 255, 130, 255),
        new Color32( 223, 255, 130, 255),
        new Color32( 190, 255, 130, 255),
        new Color32( 158, 255, 130, 255),
        new Color32( 130, 255, 130, 255),
        new Color32( 130, 255, 158, 255),
        new Color32( 130, 255, 190, 255),
        new Color32( 130, 255, 223, 255),
        new Color32( 130, 255, 255, 255),
        new Color32( 130, 223, 255, 255),
        new Color32( 130, 190, 255, 255),
        new Color32( 130, 158, 255, 255),
        new Color32( 186, 186, 255, 255),
        new Color32( 202, 186, 255, 255),
        new Color32( 223, 186, 255, 255),
        new Color32( 239, 186, 255, 255),
        new Color32( 255, 186, 255, 255),
        new Color32( 255, 186, 239, 255),
        new Color32( 255, 186, 223, 255),
        new Color32( 255, 186, 202, 255),
        new Color32( 255, 186, 186, 255),
        new Color32( 255, 202, 186, 255),
        new Color32( 255, 223, 186, 255),
        new Color32( 255, 239, 186, 255),
        new Color32( 255, 255, 186, 255),
        new Color32( 239, 255, 186, 255),
        new Color32( 223, 255, 186, 255),
        new Color32( 202, 255, 186, 255),
        new Color32( 186, 255, 186, 255),
        new Color32( 186, 255, 202, 255),
        new Color32( 186, 255, 223, 255),
        new Color32( 186, 255, 239, 255),
        new Color32( 186, 255, 255, 255),
        new Color32( 186, 239, 255, 255),
        new Color32( 186, 223, 255, 255),
        new Color32( 186, 202, 255, 255),
        new Color32( 0, 0, 113, 255),
        new Color32( 28, 0, 113, 255),
        new Color32( 57, 0, 113, 255),
        new Color32( 85, 0, 113, 255),
        new Color32( 113, 0, 113, 255),
        new Color32( 113, 0, 85, 255),
        new Color32( 113, 0, 57, 255),
        new Color32( 113, 0, 28, 255),
        new Color32( 113, 0, 0, 255),
        new Color32( 113, 28, 0, 255),
        new Color32( 113, 57, 0, 255),
        new Color32( 113, 85, 0, 255),
        new Color32( 113, 113, 0, 255),
        new Color32( 85, 113, 0, 255),
        new Color32( 57, 113, 0, 255),
        new Color32( 28, 113, 0, 255),
        new Color32( 0, 113, 0, 255),
        new Color32( 0, 113, 28, 255),
        new Color32( 0, 113, 57, 255),
        new Color32( 0, 113, 85, 255),
        new Color32( 0, 113, 113, 255),
        new Color32( 0, 85, 113, 255),
        new Color32( 0, 57, 113, 255),
        new Color32( 0, 28, 113, 255),
        new Color32( 57, 57, 113, 255),
        new Color32( 69, 57, 113, 255),
        new Color32( 85, 57, 113, 255),
        new Color32( 97, 57, 113, 255),
        new Color32( 113, 57, 113, 255),
        new Color32( 113, 57, 97, 255),
        new Color32( 113, 57, 85, 255),
        new Color32( 113, 57, 69, 255),
        new Color32( 113, 57, 57, 255),
        new Color32( 113, 69, 57, 255),
        new Color32( 113, 85, 57, 255),
        new Color32( 113, 97, 57, 255),
        new Color32( 113, 113, 57, 255),
        new Color32( 97, 113, 57, 255),
        new Color32( 85, 113, 57, 255),
        new Color32( 69, 113, 57, 255),
        new Color32( 57, 113, 57, 255),
        new Color32( 57, 113, 69, 255),
        new Color32( 57, 113, 85, 255),
        new Color32( 57, 113, 97, 255),
        new Color32( 57, 113, 113, 255),
        new Color32( 57, 97, 113, 255),
        new Color32( 57, 85, 113, 255),
        new Color32( 57, 69, 113, 255),
        new Color32( 81, 81, 113, 255),
        new Color32( 89, 81, 113, 255),
        new Color32( 97, 81, 113, 255),
        new Color32( 105, 81, 113, 255),
        new Color32( 113, 81, 113, 255),
        new Color32( 113, 81, 105, 255),
        new Color32( 113, 81, 97, 255),
        new Color32( 113, 81, 89, 255),
        new Color32( 113, 81, 81, 255),
        new Color32( 113, 89, 81, 255),
        new Color32( 113, 97, 81, 255),
        new Color32( 113, 105, 81, 255),
        new Color32( 113, 113, 81, 255),
        new Color32( 105, 113, 81, 255),
        new Color32( 97, 113, 81, 255),
        new Color32( 89, 113, 81, 255),
        new Color32( 81, 113, 81, 255),
        new Color32( 81, 113, 89, 255),
        new Color32( 81, 113, 97, 255),
        new Color32( 81, 113, 105, 255),
        new Color32( 81, 113, 113, 255),
        new Color32( 81, 105, 113, 255),
        new Color32( 81, 97, 113, 255),
        new Color32( 81, 89, 113, 255),
        new Color32( 0, 0, 65, 255),
        new Color32( 16, 0, 65, 255),
        new Color32( 32, 0, 65, 255),
        new Color32( 49, 0, 65, 255),
        new Color32( 65, 0, 65, 255),
        new Color32( 65, 0, 49, 255),
        new Color32( 65, 0, 32, 255),
        new Color32( 65, 0, 16, 255),
        new Color32( 65, 0, 0, 255),
        new Color32( 65, 16, 0, 255),
        new Color32( 65, 32, 0, 255),
        new Color32( 65, 49, 0, 255),
        new Color32( 65, 65, 0, 255),
        new Color32( 49, 65, 0, 255),
        new Color32( 32, 65, 0, 255),
        new Color32( 16, 65, 0, 255),
        new Color32( 0, 65, 0, 255),
        new Color32( 0, 65, 16, 255),
        new Color32( 0, 65, 32, 255),
        new Color32( 0, 65, 49, 255),
        new Color32( 0, 65, 65, 255),
        new Color32( 0, 49, 65, 255),
        new Color32( 0, 32, 65, 255),
        new Color32( 0, 16, 65, 255),
        new Color32( 32, 32, 65, 255),
        new Color32( 40, 32, 65, 255),
        new Color32( 49, 32, 65, 255),
        new Color32( 57, 32, 65, 255),
        new Color32( 65, 32, 65, 255),
        new Color32( 65, 32, 57, 255),
        new Color32( 65, 32, 49, 255),
        new Color32( 65, 32, 40, 255),
        new Color32( 65, 32, 32, 255),
        new Color32( 65, 40, 32, 255),
        new Color32( 65, 49, 32, 255),
        new Color32( 65, 57, 32, 255),
        new Color32( 65, 65, 32, 255),
        new Color32( 57, 65, 32, 255),
        new Color32( 49, 65, 32, 255),
        new Color32( 40, 65, 32, 255),
        new Color32( 32, 65, 32, 255),
        new Color32( 32, 65, 40, 255),
        new Color32( 32, 65, 49, 255),
        new Color32( 32, 65, 57, 255),
        new Color32( 32, 65, 65, 255),
        new Color32( 32, 57, 65, 255),
        new Color32( 32, 49, 65, 255),
        new Color32( 32, 40, 65, 255),
        new Color32( 45, 45, 65, 255),
        new Color32( 49, 45, 65, 255),
        new Color32( 53, 45, 65, 255),
        new Color32( 61, 45, 65, 255),
        new Color32( 65, 45, 65, 255),
        new Color32( 65, 45, 61, 255),
        new Color32( 65, 45, 53, 255),
        new Color32( 65, 45, 49, 255),
        new Color32( 65, 45, 45, 255),
        new Color32( 65, 49, 45, 255),
        new Color32( 65, 53, 45, 255),
        new Color32( 65, 61, 45, 255),
        new Color32( 65, 65, 45, 255),
        new Color32( 61, 65, 45, 255),
        new Color32( 53, 65, 45, 255),
        new Color32( 49, 65, 45, 255),
        new Color32( 45, 65, 45, 255),
        new Color32( 45, 65, 49, 255),
        new Color32( 45, 65, 53, 255),
        new Color32( 45, 65, 61, 255),
        new Color32( 45, 65, 65, 255),
        new Color32( 45, 61, 65, 255),
        new Color32( 45, 53, 65, 255),
        new Color32( 45, 49, 65, 255)*/
    };

    Texture3D lut = null;

    private void InitLut(int lutShift, int size)
    {
        if (lut)
            return;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        BLBFastColorPalette.IPalette palette = BLBFastColorPalette.BuildPalette(vgaPalette);
        watch.Stop();
        Debug.Log("Time spent building skybox palette = " + watch.ElapsedMilliseconds + "ms");

        var watch2 = System.Diagnostics.Stopwatch.StartNew();
        lut = new Texture3D(size, size, size, TextureFormat.RGBA32, false);
        lut.filterMode = FilterMode.Point;
        lut.wrapMode = TextureWrapMode.Clamp;

        Color32[] colors = new Color32[size * size * size];
        Color32 targetColor = new Color32();
        targetColor.a = 255;
        int colorsIndex = 0;
        Color32 color;
        for (int b = 0; b < size; b++)
        {
            targetColor.b = (byte)((b << lutShift));
            for (int g = 0; g < size; g++)
            {
                targetColor.g = (byte)((g << lutShift));
                for (int r = 0; r < size; r++)
                {
                    targetColor.r = (byte)((r << lutShift));
                    palette.GetNearestColor(targetColor, out color);
                    colors[colorsIndex++] = color;
                }
            }
        }
        watch2.Stop();
        Debug.Log("Time spent filling skybox LUT = " + watch2.ElapsedMilliseconds + "ms");
        var watch3 = System.Diagnostics.Stopwatch.StartNew();
        lut.SetPixels32(colors);
        lut.Apply();
        watch3.Stop();
        Debug.Log("Time spent transferring skybox LUT = " + watch3.ElapsedMilliseconds + "ms");
    }

    #endregion

}