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
    [Tooltip("Auto-resume patrol after this time if animation event fails")]
    public float slashAnimationTimeout = 3f;

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

    // Animation tracking
    private Coroutine _slashTimeoutCoroutine;

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
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("[KillerAI] No waypoints assigned. Killer will stand still in Patrol.", this);
        }
        else
        {
            // Check for null waypoints on awake
            int nullCount = 0;
            foreach (var wp in waypoints)
            {
                if (wp == null) nullCount++;
            }
            if (nullCount > 0)
            {
                Debug.LogWarning($"[KillerAI] Found {nullCount} null waypoints in array. They will be skipped during patrol.", this);
            }
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

        if (alivePlayers.Count == 0) 
        {
            // No players alive, continue patrolling
            if (!_isAttacking && !_isPlacingTrap)
            {
                Patrol();
            }
            return;
        }

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

        if (nearestPlayer == null) 
        {
            // No valid player found, continue patrolling
            if (!_isAttacking && !_isPlacingTrap)
            {
                Patrol();
            }
            return;
        }

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
        if (_isPlacingTrap || _isAttacking) return;

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

        Debug.Log("[KillerAI] Starting attack sequence");

        // Stop all NavMesh movement and clear velocity
        _agent.isStopped = true;
        _agent.ResetPath();
        _agent.velocity = Vector3.zero;
        _agent.updateRotation = false;

        // Snap in front of player & face them for the slash
        PositionInFrontOfPlayer();
        FacePlayer();

        // Kill the player and remove from alive list
        if (NetworkManager.Singleton.IsServer && player != null)
        {
            NetworkObjectReference playerRef = new NetworkObjectReference(player.GetComponent<NetworkObject>());
            var playerState = player.GetComponent<PlayerState>();
            if (playerState != null)
            {
                playerState.KillPlayerServerRpc();
                alivePlayers.Remove(player); 
                
                // Stop camera coroutine if the killed player was the one we're tracking
                if (_camLockCR != null)
                {
                    StopCoroutine(_camLockCR);
                    _camLockCR = null;
                }
            }
        }

        // Only start camera lock if we have a valid camera
        if (lockCameraDuringSlash && _camLockCR == null && Camera.main != null)
            _camLockCR = StartCoroutine(LockCameraOnKiller());
            
        // Trigger slash animation
        SetAnim(patrol: false, chase: false, slash: true);

        // Start timeout coroutine in case animation event fails
        if (_slashTimeoutCoroutine != null)
            StopCoroutine(_slashTimeoutCoroutine);
        _slashTimeoutCoroutine = StartCoroutine(SlashTimeoutRoutine());

        Debug.Log("[KillerAI] DEATH SLASH - Waiting for animation to complete");
    }

    // ---------------------------
    // Helpers
    // ---------------------------

    private void GoToNextWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0) 
        {
            Debug.LogWarning("[KillerAI] No waypoints available for patrol");
            return;
        }

        // Find a valid waypoint (skip null ones)
        int attempts = 0;
        Transform wp = null;
        
        while (attempts < waypoints.Length)
        {
            wp = waypoints[_currentWaypointIndex];
            if (wp != null)
            {
                break; // Found a valid waypoint
            }
            
            Debug.LogWarning($"[KillerAI] Waypoint {_currentWaypointIndex} is null, skipping to next");
            _currentWaypointIndex = (_currentWaypointIndex + 1) % waypoints.Length;
            attempts++;
        }

        if (wp != null)
        {
            _agent.destination = wp.position;
            _agent.isStopped = false; // Ensure agent is not stopped
            
            Debug.Log($"[KillerAI] Setting patrol destination to waypoint {_currentWaypointIndex} at {wp.position}");
            
            // Only advance to next waypoint if we found a valid one
            _currentWaypointIndex = (_currentWaypointIndex + 1) % waypoints.Length;
        }
        else
        {
            Debug.LogError("[KillerAI] No valid waypoints found in the entire array!");
        }
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
        
        Debug.Log($"[KillerAI] Animator - Patrol: {patrol}, Chase: {chase}, Slash: {slash}");
    }

    // ---------------------------
    // Animation Timeout Failsafe
    // ---------------------------
    private IEnumerator SlashTimeoutRoutine()
    {
        Debug.Log($"[KillerAI] Slash timeout started - will auto-resume in {slashAnimationTimeout} seconds");
        
        yield return new WaitForSeconds(slashAnimationTimeout);
        
        if (_isAttacking)
        {
            Debug.LogWarning("[KillerAI] Slash animation timeout! Animation event may be missing. Forcing patrol resume.");
            OnSlashAnimationComplete();
        }
        
        _slashTimeoutCoroutine = null;
    }

    // ---------------------------
    // Camera Lock Coroutine (Fixed)
    // ---------------------------
    private IEnumerator LockCameraOnKiller()
    {
        var cam = Camera.main;
        
        // Check if camera is null or destroyed before starting
        if (!cam || cam == null) 
        { 
            _camLockCR = null; 
            yield break; 
        }

        // SNAP on the first frame so target is exactly centered immediately
        Vector3 targetPos = (cameraLookTarget ? cameraLookTarget.position : transform.position) + cameraAimOffset;
        Vector3 to = targetPos - cam.transform.position;
        if (to.sqrMagnitude > 1e-6f)
            cam.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);

        yield return null; // first-frame snap done

        // Smoothly keep it centered while attacking
        while (_isAttacking && !_isPlayerDead && cam != null)
        {
            // Additional safety check - camera might be destroyed during this frame
            if (cam == null) 
            {
                _camLockCR = null;
                yield break;
            }

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

    // ---------------------------
    // Animation Events / Game Over
    // ---------------------------

    /// <summary>
    /// Called by the attack animation event at the end of the slash.
    /// </summary>
    public void OnSlashAnimationComplete()
    {
        Debug.Log("[KillerAI] Slash animation complete - Resuming patrol");
        
        // Stop timeout coroutine
        if (_slashTimeoutCoroutine != null)
        {
            StopCoroutine(_slashTimeoutCoroutine);
            _slashTimeoutCoroutine = null;
        }

        // Stop camera coroutine
        if (_camLockCR != null)
        {
            StopCoroutine(_camLockCR);
            _camLockCR = null;
        }

        // Don't kill the player again here - it's already done in AttackPlayer()
        // Just remove the player reference if it exists
        if (player != null)
        {
            // Make sure player is removed from alive list
            if (alivePlayers.Contains(player))
                alivePlayers.Remove(player);
            player = null;
        }

        // Stop slash animation
        SetAnim(patrol: true, chase: false, slash: false);

        // Reset killer state
        _isAttacking = false;
        _agent.isStopped = false;
        _agent.updateRotation = true;

        // Resume patrolling - force a new waypoint
        GoToNextWaypoint();
        
        Debug.Log("[KillerAI] Patrol resumed after slash");
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