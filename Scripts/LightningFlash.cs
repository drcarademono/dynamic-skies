using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class LightningFlash : MonoBehaviour
{
    public Image flashImage;
    public float flashDuration = 0.1f;
    public float fadeDuration = 0.1f; // Duration to fade in and out

    public void StartFlash()
    {
        if (flashImage == null)
        {
            Debug.LogError("LightningFlash: Flash image is not assigned.");
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
        yield return new WaitForSeconds(0.05f); // Short delay between flashes
        yield return FlashOnce(halfDuration);
    }

    private IEnumerator FlashOnce(float duration)
    {
        //Debug.Log("LightningFlash: Flash started.");

        // Get a random opacity between 0.5 and 1.0
        float targetOpacity = Random.Range(0.25f, 0.75f);

        // Fade in
        float elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            float alpha = Mathf.Lerp(0f, targetOpacity, elapsedTime / fadeDuration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        flashImage.color = new Color(1f, 1f, 1f, targetOpacity); // Ensure fully opaque with target opacity

        yield return new WaitForSeconds(duration);

        // Fade out
        elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            float alpha = Mathf.Lerp(targetOpacity, 0f, elapsedTime / fadeDuration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        flashImage.color = new Color(1f, 1f, 1f, 0f); // Ensure fully transparent

        //Debug.Log("LightningFlash: Flash ended. Current color: " + flashImage.color);
    }
}

