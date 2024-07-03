using UnityEngine;
using DaggerfallWorkshop.Game;

public class LightningFlashListener : MonoBehaviour
{
    public static LightningFlashListener Instance { get; private set; }

    private LightningFlash lightningFlash;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SetLightningFlash(LightningFlash flash)
    {
        lightningFlash = flash;
    }

    public void StartListening()
    {
        AmbientEffectsPlayer.OnPlayEffect += HandleOnPlayEffect;
    }

    public void StopListening()
    {
        AmbientEffectsPlayer.OnPlayEffect -= HandleOnPlayEffect;
    }

    private void HandleOnPlayEffect(AmbientEffectsPlayer.AmbientEffectsEventArgs args)
    {
        if (lightningFlash == null)
        {
            Debug.LogError("LightningFlashListener: LightningFlash is not assigned.");
            return;
        }

        // Trigger the lightning flash for any ambient noise
        Debug.Log("BLB: Triggering lightning flash.");
        lightningFlash.StartFlash();
    }
}

