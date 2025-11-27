using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerState : NetworkBehaviour
{
    public PlayerMovement movement;

    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);


    private void Awake()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
    }

    public override void OnNetworkSpawn()
    {
        isDead.OnValueChanged += OnDeathChanged;
    }


    private void OnDeathChanged(bool previous, bool current)
    {
        if (current)
        {
            if (movement != null) movement.Stun(999f);

            
            StartCoroutine(HandleDeathRoutine());
            
        }
    }

    private IEnumerator HandleDeathRoutine()
    {
        // Wait for death animation duration
        yield return new WaitForSeconds(2f);

        if (!IsOwner) yield break;

        // Destroy or disable the main camera
        var cam = Camera.main;
        if (cam != null)
        {
            Destroy(cam.gameObject); 
        }
        if (IsServer)
        {
            var netObj = GetComponent<NetworkObject>();
            if (netObj != null) netObj.Despawn();
        }
    }

    [ClientRpc]
    private void HidePlayerClientRpc()
    {
        // Disable mesh, collider, rigidbody for visual clarity
        foreach (var mr in GetComponentsInChildren<MeshRenderer>())
            mr.enabled = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        var rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }
    
    public void StunPlayer(float duration)
    {
        if (!IsServer) return;
        ApplyStunClientRpc(duration);
    }

    [ClientRpc]
    private void ApplyStunClientRpc(float duration)
    {
        movement.Stun(duration);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void KillPlayerServerRpc()
    {
        if (!IsServer) return;

        isDead.Value = true;

        HidePlayerClientRpc();
    }
}
