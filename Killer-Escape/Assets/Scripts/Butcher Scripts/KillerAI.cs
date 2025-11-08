using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// KillerAI
/// - Patrols through waypoints
/// - Detects and chases the player within detectionRange
/// - Attacks when within attackRange
/// - On slash animation event, marks player dead and triggers Game Over
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class KillerAI : MonoBehaviour
{
    // ---------------------------
    // Inspector (Gameplay Tuning)
    // ---------------------------

    [Header("Scene References")]
    [Tooltip("Waypoints the killer will patrol in order.")]
    public Transform[] waypoints;

    [Tooltip("Reference to the player transform.")]
    public Transform player;

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
        if (player == null)
        {
            Debug.LogError("[KillerAI] Player reference is missing.", this);
        }
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
        if (_isPlayerDead || _isAttacking)
            return;

        if (player == null) // Nothing to do without a player reference
            return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

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
        DisableAllInputs();
        // >>> NEW: keep the camera aimed at the killer while attacking
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
    private void DisableAllInputs()
    {
        Debug.Log("=== ATTEMPTING TO DISABLE INPUTS ===");

        // 1) Disable a known player movement script if present
        if (player != null)
        {
            var playerMovement = player.GetComponent<PlayerMovement>();
            if (playerMovement != null)
            {
                playerMovement.enabled = false;
                Debug.Log("✓ PlayerMovement disabled on: " + playerMovement.gameObject.name);
            }
            else
            {
                Debug.Log("✗ PlayerMovement not found on player object");
            }
        }

        // 2) Camera scripts on Main Camera or children
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            DisableIfPresentOn<FirstPersonCam>(mainCam.transform, "FirstPersonCam");
            DisableIfPresentOn<MoveCam>(mainCam.transform, "MoveCam");
        }
        else
        {
            Debug.Log("✗ Main camera not found");
        }

        // 3) Nuclear option: disable all such scripts scene-wide
        DisableAllOfType<FirstPersonCam>("FirstPersonCam");
        DisableAllOfType<MoveCam>("MoveCam");

        // 4) Clear input axes now (legacy input system)
        Input.ResetInputAxes();
        Debug.Log("=== INPUT DISABLE COMPLETE ===");

        // Ensure continuous blocking while attacking/dead
        if (_blockInputsCR == null)
            _blockInputsCR = StartCoroutine(BlockInputsContinuously());
    }

    private void DisableIfPresentOn<T>(Transform root, string label) where T : Behaviour
    {
        var comp = root.GetComponent<T>() ?? root.GetComponentInChildren<T>(true);
        if (comp != null && comp.enabled)
        {
            comp.enabled = false;
            Debug.Log($"✓ {label} disabled on: {comp.gameObject.name}");
        }
        else
        {
            Debug.Log($"✗ {label} not found/enabled on camera hierarchy");
        }
    }

    private void DisableAllOfType<T>(string label) where T : Behaviour
    {
        var all = FindObjectsByType<T>(FindObjectsSortMode.None);
        foreach (var c in all)
        {
            if (c.enabled)
            {
                c.enabled = false;
                Debug.Log($"✓ {label} disabled via FindObjectsByType: {c.gameObject.name}");
            }
        }
    }

    private IEnumerator BlockInputsContinuously()
    {
        // Keep inputs neutralized while the kill sequence is active or after death
        while (_isAttacking || _isPlayerDead)
        {
            // (Legacy input system) Clear buffered inputs; prevents new deltas from accumulating
            Input.ResetInputAxes();

            // Double-check player & camera scripts remain off
            if (player != null)
            {
                var pm = player.GetComponent<PlayerMovement>();
                if (pm != null && pm.enabled) pm.enabled = false;
            }

            var allFpc = FindObjectsByType<FirstPersonCam>(FindObjectsSortMode.None);
            foreach (var cam in allFpc) if (cam.enabled) cam.enabled = false;

            var allMove = FindObjectsByType<MoveCam>(FindObjectsSortMode.None);
            foreach (var mv in allMove) if (mv.enabled) mv.enabled = false;

            yield return null; // each frame
        }

        // End of lockdown, release handle
        _blockInputsCR = null;
    }

    // ---------------------------
    // Animation Events / Game Over
    // ---------------------------

    /// <summary>
    /// Called by the attack animation event at the end of the slash.
    /// </summary>
    public void OnSlashAnimationComplete()
    {
        if (_isPlayerDead) return;

        Debug.Log("Slash animation complete - GAME OVER");

        _isPlayerDead = true;
        _isAttacking = false;

        SetAnim(patrol: false, chase: false, slash: false);

        _agent.isStopped = true;
        _agent.updateRotation = true;  // ensure agent can rotate again later
        // Inputs remain disabled due to the coroutine while _isPlayerDead is true
        TriggerGameOver();
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

            if (!_isAttacking && !_isChasing && Random.value < trapChance)
            {
                yield return StartCoroutine(PlaceTrapRoutine());
            }
        }
    }
    private IEnumerator PlaceTrapRoutine()
    {
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
        Instantiate(trapPrefab, trapSpawn.position, trapSpawn.rotation);

        // Reset animation
        _anim.SetBool(HashIsPlacingTrap, false);

        // Resume patrol
        _isPlacingTrap = false;
        _agent.isStopped = false;
        
    }
}
