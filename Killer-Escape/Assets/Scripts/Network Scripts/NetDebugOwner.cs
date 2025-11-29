using UnityEngine;
using Unity.Netcode;

public class NetDebugOwner : NetworkBehaviour
{
    void Start()
    {
        Debug.Log($"[NetDebugOwner] On Start - IsServer: {IsServer}, IsClient: {IsClient}, IsOwner: {IsOwner}, OwnerClientId: {OwnerClientId}");
    }

    void Update()
    {
        if (Time.frameCount % 300 == 0) // print occasionally
            Debug.Log($"[NetDebugOwner] Update - name:{gameObject.name} IsOwner:{IsOwner} Owner:{OwnerClientId}");
    }
}
