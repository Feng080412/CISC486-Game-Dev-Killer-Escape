using UnityEngine;
using Unity.Netcode;

public class PlayerState : NetworkBehaviour
{
    public PlayerMovement movement;

    public NetworkVariable<bool> isStunned = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(false);

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
            movement.Stun(current ? 999f : 0f); // Or a stun duration if needed
    }

    private void OnDeathChanged(bool previous, bool current)
    {
        if (current)
        {
            if (movement != null) movement.Stun(999f); // Freeze player
            // optionally disable camera
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cam.enabled = false;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void StunPlayerServerRpc(float duration)
    {
        isStunned.Value = true;
        StartCoroutine(ResetStun(duration));
    }

    private System.Collections.IEnumerator ResetStun(float duration)
    {
        yield return new WaitForSeconds(duration);
        isStunned.Value = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void KillPlayerServerRpc()
    {
        isDead.Value = true;
    }
}
