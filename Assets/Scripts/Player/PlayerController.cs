using UnityEngine;
using UnityEngine.InputSystem; // New Input System

/// <summary>
/// Third-person player controller ala Fall Guys.
/// - WASD  : gerak relatif terhadap arah kamera
/// - Space : lompat (coyote time + jump buffer)
/// Ground check memakai titik kontak collision (tidak bergantung layer / scale).
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 30f;
    [SerializeField] private float groundAcceleration = 80f;  // makin besar = makin responsif
    [SerializeField] private float groundDeceleration = 100f; // makin besar = berhenti makin cepat (anti licin)
    [Range(0.1f, 1f)]
    [SerializeField] private float airSpeedMultiplier = 0.6f; // kecepatan maju di udara = 60% kecepatan jalan
    [SerializeField] private float airAcceleration = 30f;
    [SerializeField] private float airDeceleration = 10f;     // kecil = momentum terjaga di udara
    [SerializeField] private float turnSpeed = 720f;          // derajat/detik

    [Header("Jump")]
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("Ground")]
    [Range(0f, 1f)]
    [SerializeField] private float minGroundNormalY = 0.6f;   // 0.6 ≈ kemiringan maks ~53°

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private Transform cam;

    private PhysicsMaterial groundMat;  // ada friction -> tidak licin di tanah
    private PhysicsMaterial airMat;     // tanpa friction -> tidak nempel di dinding
    private bool usingGroundMat;

    private Vector2 moveInput;
    private bool groundContact;         // diisi callback collision, dibaca & di-reset di FixedUpdate
    private bool isGrounded;
    private float lastGroundedTime = -10f;
    private float lastJumpPressedTime = -10f;
    private float lastJumpTime = -10f;

    public bool IsGrounded => isGrounded;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.sleepThreshold = 0f; // PENTING: rigidbody yang "tidur" tidak mengirim OnCollisionStay

        groundMat = new PhysicsMaterial("PlayerGround")
        {
            dynamicFriction = 0.6f, staticFriction = 0.6f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        airMat = new PhysicsMaterial("PlayerAir")
        {
            dynamicFriction = 0f, staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        capsule.sharedMaterial = airMat;
    }

    private void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : transform;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        moveInput = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

        if (kb.spaceKey.wasPressedThisFrame)
            lastJumpPressedTime = Time.time;
    }

    private void FixedUpdate()
    {
        // --- Status tanah (hasil callback collision dari step fisika sebelumnya) ---
        isGrounded = groundContact && Time.time - lastJumpTime > 0.15f; // abaikan sesaat setelah lompat
        groundContact = false;
        if (isGrounded) lastGroundedTime = Time.time;
        SetFriction(isGrounded);

        // --- Arah gerak relatif kamera ---
        Vector3 camForward = cam.forward; camForward.y = 0f; camForward.Normalize();
        Vector3 camRight = cam.right;     camRight.y = 0f;   camRight.Normalize();
        Vector3 moveDir = camForward * moveInput.y + camRight * moveInput.x;
        bool hasInput = moveDir.sqrMagnitude > 0.001f;

        // --- Horizontal ---
        Vector3 vel = rb.linearVelocity;
        Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
        float accel = hasInput
            ? (isGrounded ? groundAcceleration : airAcceleration)
            : (isGrounded ? groundDeceleration : airDeceleration);
        float targetSpeed = isGrounded ? moveSpeed : moveSpeed * airSpeedMultiplier;
        horizontal = Vector3.MoveTowards(horizontal, moveDir * targetSpeed, accel * Time.fixedDeltaTime);
        vel.x = horizontal.x;
        vel.z = horizontal.z;

        // --- Jump ---
        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;
        if (canJump && wantsJump)
        {
            vel.y = Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight);
            vel.x *= airSpeedMultiplier; // momentum maju saat lepas landas ikut berkurang
            vel.z *= airSpeedMultiplier;
            lastJumpPressedTime = -10f;
            lastGroundedTime = -10f;
            lastJumpTime = Time.time;
            isGrounded = false;
            SetFriction(false);
        }

        if (vel.y < 0f)
            vel += Vector3.up * Physics.gravity.y * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime;

        rb.linearVelocity = vel;

        // --- Rotasi badan ---
        if (hasInput)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeed * Time.fixedDeltaTime));
        }
    }

    private void OnCollisionEnter(Collision c) => EvaluateContacts(c);
    private void OnCollisionStay(Collision c) => EvaluateContacts(c);

    private void EvaluateContacts(Collision c)
    {
        for (int i = 0; i < c.contactCount; i++)
        {
            // normal.y ≈ 1 berarti permukaan datar di bawah kaki
            if (c.GetContact(i).normal.y >= minGroundNormalY)
            {
                groundContact = true;
                return;
            }
        }
    }

    private void SetFriction(bool grounded)
    {
        if (grounded == usingGroundMat) return;
        usingGroundMat = grounded;
        capsule.sharedMaterial = grounded ? groundMat : airMat;
    }

    private void OnGUI()
    {
        if (!showDebug) return;
        GUI.Label(new Rect(10, 10, 400, 25), $"Grounded: {isGrounded}");
    }
}