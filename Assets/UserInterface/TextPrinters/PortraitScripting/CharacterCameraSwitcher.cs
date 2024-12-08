using UnityEngine;

public class CharacterCameraSwitcher : MonoBehaviour
{
    public RenderTexture charPortraitTemp; // RenderTexture to display the character portrait
    public Camera defaultCamera; // Default fallback camera

    private Camera currentCamera; // Tracks the currently active camera

    void Start()
    {
        // Initialize the default camera
        if (defaultCamera != null && charPortraitTemp != null)
        {
            currentCamera = defaultCamera;
            AssignRenderTexture(defaultCamera);
        }
    }

    public void SwitchCamera(Camera newCamera)
    {
        if (newCamera == null || newCamera == currentCamera) return;

        // Remove render texture from the current camera
        if (currentCamera != null)
            currentCamera.targetTexture = null;

        // Assign the render texture to the new camera
        currentCamera = newCamera;
        AssignRenderTexture(newCamera);
    }

    private void AssignRenderTexture(Camera camera)
    {
        if (camera != null && charPortraitTemp != null)
        {
            camera.targetTexture = charPortraitTemp;
        }
    }
}
