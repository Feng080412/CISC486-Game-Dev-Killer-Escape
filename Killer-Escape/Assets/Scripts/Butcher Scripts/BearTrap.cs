using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class BearTrap : NetworkBehaviour
{
    public Animator animator;
    public float stunDuration = 2f; // configurable per trap
    public float destroyDelay = 1.5f;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return; // only the server handles logic

        if (other.CompareTag("Player"))
        {
            animator?.SetTrigger("Snap");

            // Attempt to get PlayerState on the hit player
            PlayerState state = other.GetComponentInParent<PlayerState>();
            if (state != null)
            {
                state.StunPlayerServerRpc(stunDuration);
            }

            StartCoroutine(DestroySelf());
        }
    }

    private IEnumerator DestroySelf()
    {
        yield return new WaitForSeconds(destroyDelay);
        if (IsServer)
        {
            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn();
            else
                Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
