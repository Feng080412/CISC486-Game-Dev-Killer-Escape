using UnityEngine;
using UnityEngine.AI;

public class KillerAI : MonoBehaviour
{


    public Transform[] waypoints;
    private int currentWaypointIndex = 0;

    public Transform player;
    public float detectionRange = 10f;

    private NavMeshAgent agent;
    private Animator animator;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (waypoints.Length > 0)
        {
            agent.SetDestination(waypoints[currentWaypointIndex].position);
        }
    }

    // Update is called once per frame
    void Update()
    {
        PatrolLogic();
        DetectPlayer();
    }

    void PatrolLogic()
    {
        animator.SetBool("isPatrolling", true);
        animator.SetFloat("speed", agent.velocity.magnitude);

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            // Move to next waypoint
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            agent.SetDestination(waypoints[currentWaypointIndex].position);
        }
    }

    void DetectPlayer()
    {
        float dist = Vector3.Distance(transform.position, player.position);
        animator.SetFloat("distanceToPlayer", dist);

        if (dist < detectionRange)
        {
            animator.SetBool("playerInSight", true);
            animator.SetBool("isPatrolling", false);
        }
        else
        {
            animator.SetBool("playerInSight", false);
        }
    }
}
