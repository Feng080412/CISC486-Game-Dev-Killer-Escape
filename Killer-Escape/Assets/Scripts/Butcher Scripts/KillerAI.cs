using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

/// <summary>
/// KillerAI
/// - Patrols through waypoints
/// - Detects and chases the player within detectionRange
/// - Attacks when within attackRange
/// - On slash animation event, marks player dead and triggers Game Over
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class KillerAI : NetworkBehaviour
{
    // ---------------------------
    // Inspector (Gameplay Tuning)
    // ---------------------------

    [Header("Scene References")]
    [Tooltip("Waypoints the killer will patrol in order.")]
    public Transform[] waypoints;

    [Tooltip("Reference to the player transform/transforms.")]
    private List<Transform> alivePlayers = new List<Transform>();
    //current targer
    private Transform player;

    [Header("Ranges & Speeds")]
    [Min(0f)] public float detectionRange = 10f;
    [Min(0f)] public float attackRange = 2f;
    [Min(0f)] public float chaseSpeed = 5f;
    [Min(0f)] public float patrolSpeed = 2f;

    [Header("Attack")]
    [Tooltip("Where the killer teleports relative to player forward at attack start.")]
    [Min(0f)] public float slashDistance = 1.5f;

    [Header("Trap Debug")]
    public float checkInterval = 5f;
    public float trapChance = 0.3f;
    public Transform trapSpawn;
    public GameObject trapPrefab;

    // ---------------------------
    // Private State
    // ---------------------------

    private int _currentWaypointIndex = 0;

    private NavMeshAgent _agent;
    private Animator _anim;

    private bool _isChasing = false;
    private bool _isAttacking = false;
    private bool _isPlayerDead = false;
    private bool _isPlacingTrap = false;

    // --- Camera lock during slash ---
    [SerializeField] private bool lockCameraDuringSlash = true;  // turn on/off in Inspector
    [SerializeField] private float cameraTurnSpeed = 180f;       // deg/sec smoothing
    [SerializeField] private float  cameraSnapFirstFrame = 1f;   // 1 = snap instantly, 0 = no snap
    [SerializeField] private float  cameraMinDistance = 1.2f;    // prevent too-close framing
    [SerializeField] private Vector3 cameraAimOffset = new Vector3(0f, 0.12f, 0f); // up a bit
    [SerializeField] private Transform cameraLookTarget;          // assign killer head/chest; falls back automatically

    private Coroutine _camLockCR;


    private Coroutine _blockInputsCR; // handle to input-block coroutine

    // Animator parameter hashes (faster & less typo-prone)
    private static readonly int HashIsPatrolling = Animator.StringToHash("isPatrolling");
    private static readonly int HashIsChasing    = Animator.StringToHash("isChasing");
    private static readonly int HashIsSlashing = Animator.StringToHash("isSlashing");
    private static readonly int HashIsPlacingTrap   = Animator.StringToHash("isPlacingTrap");

    // ---------------------------
    // Unity Lifecycle
    // ---------------------------

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim  = GetComponent<Animator>();

        // Soft guardrails 
        // if (player == null)
        // {
        //     Debug.LogError("[KillerAI] Player reference is missing.", this);
        // }
        // ABOVE COMMENTED BECAUSE OF NETCODE
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("[KillerAI] No waypoints assigned. Killer will stand still in Patrol.", this);
        }
    }

    private void Start()
    {
        // Start patrolling immediately
        _agent.speed = patrolSpeed;
        GoToNextWaypoint();
        SetAnim(patrol: true, chase: false, slash: false);

        if (cameraLookTarget == null)
        {
            var a = GetComponent<Animator>();
            if (a != null && a.isHuman)
                cameraLookTarget = a.GetBoneTransform(HumanBodyBones.Head);
            if (cameraLookTarget == null) cameraLookTarget = transform; // fallback
        }
        
        // Start Trap Routine (will check every __ seconds and place a trap if it passes and patroling)
        StartCoroutine(TrapOpportunityRoutine());
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)  return; // Only server controls AI

        UpdatePlayersList();

        if (alivePlayers.Count == 0) return;

        if (_isAttacking)
            return;

        Transform nearestPlayer = null;
        float nearestDist = Mathf.Infinity;

        foreach (var p in alivePlayers)
        {
            float dist = Vector3.Distance(transform.position, p.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestPlayer = p;
            }
        }

        if (nearestPlayer == null) return;

        float distanceToPlayer = nearestDist;
        player = nearestPlayer;

        // Attack takes precedence
        if (distanceToPlayer <= attackRange)
        {
            AttackPlayer();
            return;
        }

        // Chase logic
        if (distanceToPlayer <= detectionRange)
        {
            if (!_isChasing) StartChase();
            ChasePlayer(); // keep updating destination
            return;
        }

        // Patrol as fallback
        if (_isChasing) StopChase();
        Patrol();
    }

    // ---------------------------
    // High-level Behaviors
    // ---------------------------

    private void UpdatePlayersList()
    {
        alivePlayers.Clear();
        foreach (var netObj in FindObjectsOfType<NetworkObject>())
        {
            if (netObj.IsPlayerObject)
            {
                var playerState = netObj.GetComponent<PlayerState>();
                if (playerState != null && !playerState.isDead.Value)
                    alivePlayers.Add(netObj.transform);
            }
        }
    }
    private void Patrol()
    {
        if (_isPlacingTrap) return;

        _agent.isStopped = false;
        _agent.speed = patrolSpeed;

        SetAnim(patrol: true, chase: false, slash: false);

        if (waypoints == null || waypoints.Length == 0)
            return;

        // Advance when close to current target
        if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
        {
            GoToNextWaypoint();
        }
    }

    private void StartChase()
    {
        _isChasing = true;
        _agent.isStopped = false;
        _agent.speed = chaseSpeed;

        SetAnim(patrol: false, chase: true, slash: false);
    }

    private void StopChase()
    {
        _isChasing = false;
        _agent.speed = patrolSpeed;

        SetAnim(patrol: true, chase: false, slash: false);

        // Resume patrol pathing
        GoToNextWaypoint();
    }

    private void ChasePlayer()
    {
        if (player == null) return;
        _agent.isStopped = false;
        _agent.destination = player.position;
    }

    private void AttackPlayer()
    {
        if (_isAttacking || _isPlayerDead) return;

        _isAttacking = true;
        _isChasing = false;

        // Stop all NavMesh movement and clear velocity
        _agent.isStopped = true;
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;

        _agent.updateRotation = false;

        // Snap in front of player & face them for the slash
        PositionInFrontOfPlayer();
        FacePlayer();

        // Disable all player inputs/camera control
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkObjectReference playerRef = new NetworkObjectReference(player.GetComponent<NetworkObject>());
            var playerState = player.GetComponent<PlayerState>();
            if (playerState != null)
            {
                playerState.KillPlayerServerRpc();
                alivePlayers.Remove(player); 
            }
        }

        if (lockCameraDuringSlash && _camLockCR == null)
            _camLockCR = StartCoroutine(LockCameraOnKiller());
        // Trigger slash animation
        SetAnim(patrol: false, chase: false, slash: true);

        Debug.Log("[KillerAI] DEATH SLASH - Inputs disabled");
    }

    // ---------------------------
    // Helpers
    // ---------------------------

    private void GoToNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) return;

        Transform wp = waypoints[_currentWaypointIndex];
        if (wp != null)
        {
            _agent.destination = wp.position;
        }
        _currentWaypointIndex = (_currentWaypointIndex + 1) % waypoints.Length;
    }

    private void PositionInFrontOfPlayer()
    {
        if (player == null) return;

        Vector3 forward = player.forward;
        Vector3 target = player.position + forward * slashDistance;
        target.y = transform.position.y; // keep killer on its current ground Y
        transform.position = target;
    }

    private void FacePlayer()
    {
        if (player == null) return;

        Vector3 dir = (player.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        }
    }
    


    /// <summary>
    /// Sets animator parameters in one place.
    /// </summary>
    private void SetAnim(bool patrol, bool chase, bool slash)
    {
        _anim.SetBool(HashIsPatrolling, patrol);
        _anim.SetBool(HashIsChasing, chase);
        _anim.SetBool(HashIsSlashing, slash);
    }

    // ---------------------------
    // Input Lockdown
    // ---------------------------
    private IEnumerator LockCameraOnKiller()
    {
        var cam = Camera.main;
        if (!cam) { _camLockCR = null; yield break; }

        // SNAP on the first frame so target is exactly centered immediately
        Vector3 targetPos = (cameraLookTarget ? cameraLookTarget.position : transform.position) + cameraAimOffset;
        Vector3 to = targetPos - cam.transform.position;
        if (to.sqrMagnitude > 1e-6f)
            cam.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);

        yield return null; // first-frame snap done

        // Smoothly keep it centered while attacking
        while (_isAttacking && !_isPlayerDead)
        {
            // Maintain a minimum distance so the model doesn't overshoot frame
            float dist = Vector3.Distance(cam.transform.position, targetPos);
            if (dist < cameraMinDistance)
            {
                Vector3 back = (cam.transform.position - targetPos).normalized;
                cam.transform.position = targetPos + back * cameraMinDistance;
            }

            targetPos = (cameraLookTarget ? cameraLookTarget.position : transform.position) + cameraAimOffset;
            to = targetPos - cam.transform.position;

            if (to.sqrMagnitude > 1e-6f)
            {
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                cam.transform.rotation = Quaternion.RotateTowards(
                    cam.transform.rotation, want, cameraTurnSpeed * Time.deltaTime);
            }

            yield return null;
        }

        _camLockCR = null;
    }



    /// Disables known movement/camera scripts and continually clears input axes.
    /// Keeps doing so while attacking or after player death.
    [ServerRpc]
    private void OnPlayerHitServerRpc(NetworkObjectReference playerRef)
    {
        // Server tells that specific client to disable their input
        DisablePlayerInputClientRpc(playerRef);
    }
    [ClientRpc]
    private void DisablePlayerInputClientRpc(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out var netObj)) return;

        if (netObj.IsOwner)
        {
            // Disable movement
            var movement = netObj.GetComponent<PlayerMovement>();
            if (movement != null) movement.Stun(999f); //effectively a kill

            // Disable camera on the same player prefab
            var cam = netObj.GetComponentInChildren<FirstPersonCam>();
            if (cam != null) cam.enabled = false;
        }
       
    }

    // private void DisableIfPresentOn<T>(Transform root, string label) where T : Behaviour
    // {
    //     var comp = root.GetComponent<T>() ?? root.GetComponentInChildren<T>(true);
    //     if (comp != null && comp.enabled)
    //     {
    //         comp.enabled = false;
    //         Debug.Log($"✓ {label} disabled on: {comp.gameObject.name}");
    //     }
    //     else
    //     {
    //         Debug.Log($"✗ {label} not found/enabled on camera hierarchy");
    //     }
    // }

    // private void DisableAllOfType<T>(string label) where T : Behaviour
    // {
    //     var all = FindObjectsByType<T>(FindObjectsSortMode.None);
    //     foreach (var c in all)
    //     {
    //         if (c.enabled)
    //         {
    //             c.enabled = false;
    //             Debug.Log($"✓ {label} disabled via FindObjectsByType: {c.gameObject.name}");
    //         }
    //     }
    // }


    // ---------------------------
    // Animation Events / Game Over
    // ---------------------------

    /// <summary>
    /// Called by the attack animation event at the end of the slash.
    /// </summary>
    public void OnSlashAnimationComplete()
    {
        if (player == null) return;

        // Kill the player
        var playerState = player.GetComponent<PlayerState>();
        if (playerState != null)
            playerState.KillPlayerServerRpc();

        // Remove player from AI's alive list immediately
        alivePlayers.Remove(player);
        player = null;

        // Stop slash animation
        SetAnim(patrol:false, chase:false, slash:false);

        // Reset killer state
        _isAttacking = false;
        _agent.isStopped = false;
        _agent.updateRotation = true;

        // Resume patroling
        GoToNextWaypoint();
    }

    private void TriggerGameOver()
    {
        Debug.Log("GAME OVER - Player has been killed");
        // TODO: Hook up UI / sound / scene transitions here
        // Example:
        // GameOverUI.Instance?.ShowGameOver();
    }

    IEnumerator TrapOpportunityRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (!IsServer) continue;

            if (!_isAttacking && !_isChasing && Random.value < trapChance)
            {
                yield return StartCoroutine(PlaceTrapRoutine());
            }
        }
    }
    private IEnumerator PlaceTrapRoutine()
    {
        if (!IsServer)
            yield break; // immediately exit if this is a client

        if (trapPrefab == null || trapSpawn == null)
            yield break;

        // Trigger placing animation
        _anim.SetBool(HashIsPlacingTrap, true);

        _isPlacingTrap = true;
        _agent.isStopped = true;

        // Wait for animation to finish
        float animLength = 1f;
        yield return new WaitForSeconds(animLength);

        // Spawn trap at the spawn transform
        var trapInstance = Instantiate(trapPrefab, trapSpawn.position, trapSpawn.rotation);
        var netObj = trapInstance.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn(); // Sync with all clients
        }

        // Reset animation
        _anim.SetBool(HashIsPlacingTrap, false);

        // Resume patrol
        _isPlacingTrap = false;
        _agent.isStopped = false;
        
    }
}
