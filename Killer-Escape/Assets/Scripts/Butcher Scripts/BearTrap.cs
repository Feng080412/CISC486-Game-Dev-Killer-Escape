using UnityEngine;
using System.Collections;

public class BearTrap : MonoBehaviour
{
    public Animator animator;
    public float stunDuration = 2f;
    public float destroyDelay = 1.5f;

    private void OnTriggerEnter(Collider other)
    {

        if (other.CompareTag("Player"))
        {
            animator?.SetTrigger("Snap");
                
            
            PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
            player?.Stun(stunDuration);
                

            StartCoroutine(DestroySelf());
        }
    }
    private IEnumerator DestroySelf()
    {
        yield return new WaitForSeconds(destroyDelay);
        Destroy(gameObject);
    }

}
