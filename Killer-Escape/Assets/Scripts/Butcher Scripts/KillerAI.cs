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

    private float currentAnimSpeed = 0f;
    public float speedSmoothTime = 0.1f;
    private float speedVelocity;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        //dealing with foot hover
        agent.baseOffset = -0.2f;

        if (waypoints.Length > 0)
        {
            agent.SetDestination(waypoints[currentWaypointIndex].position);
        }

        animator.applyRootMotion = false;
    }

    // Update is called once per frame
    void Update()
    {
        PatrolLogic();
        DetectPlayer();
        UpdateAnimatorSpeed();
    }

    void PatrolLogic()
    {
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

    void UpdateAnimatorSpeed()
    {
        float targetSpeed = agent.velocity.magnitude / agent.speed;

    // Smooth the animation speed to avoid flicker
    currentAnimSpeed = Mathf.SmoothDamp(currentAnimSpeed, targetSpeed, ref speedVelocity, speedSmoothTime);

    // Clamp in case agent.speed is 0
    currentAnimSpeed = Mathf.Clamp01(currentAnimSpeed);

    animator.SetFloat("speed", currentAnimSpeed);
    }
}
