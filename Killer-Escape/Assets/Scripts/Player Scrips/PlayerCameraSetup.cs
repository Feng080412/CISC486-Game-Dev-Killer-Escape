using Unity.Netcode;
using UnityEngine;

public class PlayerCameraSetup : NetworkBehaviour
{
    public GameObject cameraHolderPrefab; // assign CameraHolder prefab

    private GameObject cameraInstance;

    public Transform camPos; // where the camera should follow (e.g., head)

    public override void OnNetworkSpawn()
    {
        Debug.Log("PlayerCameraSetup OnNetworkSpawn running. IsOwner=" + IsOwner);
        if (!IsOwner) return;

        // Instantiate camera for this client only
        cameraInstance = Instantiate(cameraHolderPrefab);

        // MoveCam will follow this transform
        MoveCam moveCam = cameraInstance.GetComponent<MoveCam>();
        if (moveCam != null)
            moveCam.camPos = camPos;

        // Enable the FirstPersonCam script only for local player
        FirstPersonCam fpsCam = cameraInstance.GetComponentInChildren<FirstPersonCam>();
        if (fpsCam != null)
            fpsCam.enabled = true;
            fpsCam.orientation = this.transform.Find("Orientation"); 

        // Parent to player for organization (optional)
        cameraInstance.transform.SetParent(transform);
    }

    public override void OnNetworkDespawn()
    {
        if (cameraInstance != null)
            Destroy(cameraInstance);
    }
}
