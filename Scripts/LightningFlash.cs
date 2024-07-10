using UnityEngine;
using System.Collections;

public class LightningFlash : MonoBehaviour
{
    public Light lightningLight; // Light object to simulate the lightning flash
    public float flashDuration = 0.1f;

    private Transform playerTransform;

    private void Start()
    {
        // Find the PlayerAdvanced object
        GameObject playerObject = GameObject.Find("PlayerAdvanced");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
        }
        else
        {
            Debug.LogError("LightningFlash: PlayerAdvanced object not found.");
        }

        if (lightningLight == null)
        {
            // Create a new light object if one is not assigned
            GameObject lightGameObject = new GameObject("LightningLight");
            lightningLight = lightGameObject.AddComponent<Light>();
            lightningLight.type = LightType.Point;
            lightningLight.intensity = 0; // Start with the light off
        }
    }

    public void StartFlash()
    {
        if (lightningLight == null)
        {
            Debug.LogError("LightningFlash: Lightning light is not assigned.");
            return;
        }

        if (playerTransform == null)
        {
            Debug.LogError("LightningFlash: Player transform is not assigned.");
            return;
        }

        // 50% chance to start the flash
        if (Random.value > 0.5f)
        {
            // 33% chance to have a double flash
            if (Random.value < 0.33f)
            {
                StartCoroutine(DoubleFlashRoutine());
            }
            else
            {
                StartCoroutine(FlashRoutine(flashDuration));
            }
        }
        else
        {
            //Debug.Log("LightningFlash: Flash skipped.");
        }
    }

    private IEnumerator FlashRoutine(float duration)
    {
        yield return FlashOnce(duration);
    }

    private IEnumerator DoubleFlashRoutine()
    {
        float halfDuration = flashDuration / 2f;
        yield return FlashOnce(halfDuration);
        yield return new WaitForSeconds(0.1f); // Short delay between flashes
        yield return FlashOnce(halfDuration);
    }

    private IEnumerator FlashOnce(float duration)
    {
        //Debug.Log("LightningFlash: Flash started.");

        // Randomize light intensity and position above the player
        lightningLight.intensity = Random.Range(0.5f, 1.5f);
        lightningLight.range = Random.Range(500f, 1000f);
        lightningLight.transform.position = playerTransform.position + new Vector3(
            Random.Range(-10f, 10f),
            Random.Range(20f, 40f),
            Random.Range(-10f, 10f)
        );

        // Turn on the light
        lightningLight.enabled = true;

        // Wait for the flash duration
        yield return new WaitForSeconds(duration);

        // Turn off the light
        lightningLight.enabled = false;

        //Debug.Log("LightningFlash: Flash ended.");
    }
}

