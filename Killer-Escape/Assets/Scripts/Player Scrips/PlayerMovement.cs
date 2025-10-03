using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed;
    public float sprintMult;
    public float crouchMult;

    public float groundDrag;

    public float jumpForce;
    public float jumpCool;
    public float airMult;

    private float maxSpeed;

    bool jumpPossible;
    bool sprinting;
    bool crouched;

    [Header("Keybinds")]
    public KeyCode jumpKey = KeyCode.Space; 
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;


    [Header("Ground Check")]
    private float playerHeight;
    public LayerMask whatisGround;
    bool grounded;
    public Transform orientation;
    

    [Header("Crouch Debug")]
    public float crouchHeight = 1.0f;
    public float standingHeight = 2.0f;
    public float cameraCrouchChange = -0.5f;
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private Transform camPos;
    

    

    float horizontalInput;
    float verticalInput;

    Vector3 moveDirection;

    Rigidbody rb;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        jumpPossible = true;
        standingHeight = capsule.height;
        playerHeight = standingHeight;
        maxSpeed = moveSpeed;
        
        
    }

    // Update is called once per frame
    void Update()
    {
        GroundCheck();
        GetInput();
        SpeedControl();

        CrouchCheck();

        //handle drag
        rb.linearDamping = grounded ? groundDrag : 0f;

    }

    void GroundCheck()
    {
        //check if grounded
        Vector3 capsuleBottom = capsule.bounds.center - Vector3.up * (capsule.height / 2f - 0.1f);
        Vector3 capsuleTop = capsule.bounds.center + Vector3.up * (capsule.height / 2f - 0.1f);
        grounded = Physics.CheckCapsule(capsuleBottom, capsuleTop, capsule.radius * 0.9f, whatisGround);
    }

    void FixedUpdate()
    {
        MovePlayer();

    }

    private void GetInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");
        sprinting = Input.GetKey(sprintKey);
        crouched = Input.GetKey(crouchKey);


        if (Input.GetKey(jumpKey) && jumpPossible && grounded)
        {
            jumpPossible = false;

            Jump();

            Invoke(nameof(ResetJump), jumpCool);
        }

    }

    private void CrouchCheck()
    {
        if (crouched)
        {
            playerHeight = crouchHeight;
            capsule.height = crouchHeight;
        
        }
        else
        {
            //check to see if uncrouch possible
            Vector3 standBottom = capsule.transform.position + Vector3.up * capsule.radius;
            Vector3 standTop = capsule.transform.position + Vector3.up * (standingHeight - capsule.radius);
            

            bool blocked = Physics.CheckCapsule(standBottom, standTop, capsule.radius * 0.9f, whatisGround);
            

            if (!blocked)
            {
                playerHeight = standingHeight;
                capsule.height = standingHeight;
            }
            else
            {
                crouched=true;
            }

        }
    }

    private void MovePlayer()
    {
        //NOTE: in scene playermaterial stops from sticking to walls
        
        moveDirection = (orientation.forward * verticalInput + orientation.right * horizontalInput).normalized;
        moveDirection.y = 0f;



        //add velocity depending on current states (airborne, sprinting, crouching, etc)
        rb.AddForce(moveDirection * maxSpeed * 10f * (grounded ? 1f : airMult), ForceMode.Force);

        
    }

    private void SpeedControl()
    {
        
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        
        if (crouched && grounded) maxSpeed = moveSpeed*crouchMult;
        else if (sprinting && grounded) maxSpeed = moveSpeed*sprintMult;
        else if (grounded) maxSpeed = moveSpeed;

        //limit velocity if needed
        if (flatVel.magnitude > maxSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * maxSpeed;
            rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
        }
    }

    private void Jump()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }
    private void ResetJump()
    {
        jumpPossible = true;
    }
}
