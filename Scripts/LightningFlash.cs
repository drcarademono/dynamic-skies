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
        StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        Debug.Log("LightningFlash: Flash started.");

        // Fade in
        float elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            float alpha = Mathf.Lerp(0f, 1f, elapsedTime / fadeDuration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        flashImage.color = new Color(1f, 1f, 1f, 1f); // Ensure fully opaque

        yield return new WaitForSeconds(flashDuration);

        // Fade out
        elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            float alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeDuration);
            flashImage.color = new Color(1f, 1f, 1f, alpha);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        flashImage.color = new Color(1f, 1f, 1f, 0f); // Ensure fully transparent

        Debug.Log("LightningFlash: Flash ended. Current color: " + flashImage.color);
    }
}

