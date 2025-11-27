using UnityEngine;
using Unity.Netcode;

public class BearTrap : NetworkBehaviour
{
    public Animator animator;
    public float destroyDelay = 1.5f;
    public float stunDuration = 2f;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        // Look for NetworkObject on self or parent
        NetworkObject playerNetObj = other.GetComponentInParent<NetworkObject>();
        if (playerNetObj != null && other.CompareTag("Player"))
        {
            ActivateTrapServerRpc(new NetworkObjectReference(playerNetObj));
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ActivateTrapServerRpc(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out NetworkObject playerObj)) return;

        // Apply stun to the player
        var playerState = playerObj.GetComponent<PlayerState>();
        if (playerState != null)
        {
            playerState.StunPlayerServerRpc(stunDuration);
        }

        // Play animation for all clients
        PlayTrapAnimationClientRpc();

        // Destroy trap after delay
        Invoke(nameof(DestroySelf), destroyDelay);
    }

    [ClientRpc]
    private void PlayTrapAnimationClientRpc()
    {
        if (animator != null)
        {
            // Ensure this trigger exists in your Animator
            animator.SetTrigger("Snap");
        }
    }

    private void DestroySelf()
    {
        if (IsServer)
        {
            Destroy(gameObject);
        }
    }
}
