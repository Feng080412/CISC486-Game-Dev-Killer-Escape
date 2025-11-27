using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerState : NetworkBehaviour
{
    public PlayerMovement movement;

    public NetworkVariable<bool> isStunned = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

    private float pendingStunDuration = 0f;

    private void Awake()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
    }

    public override void OnNetworkSpawn()
    {
        isStunned.OnValueChanged += OnStunChanged;
        isDead.OnValueChanged += OnDeathChanged;
    }

    private void OnStunChanged(bool previous, bool current)
    {
         if (movement != null)
        {
            // If current is true, use the duration already stored somewhere
            // We'll store the duration temporarily in PlayerState
            if (current && pendingStunDuration > 0f)
            {
                movement.Stun(pendingStunDuration);
                pendingStunDuration = 0f; // reset
            }
        }
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


    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StunPlayerServerRpc(float duration)
    {
        pendingStunDuration = duration;
        isStunned.Value = true;
        StartCoroutine(ResetStun(duration));
    }

    private System.Collections.IEnumerator ResetStun(float duration)
    {
        yield return new WaitForSeconds(duration);
        isStunned.Value = false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void KillPlayerServerRpc()
    {
        isDead.Value = true;
        
        
    }
}
