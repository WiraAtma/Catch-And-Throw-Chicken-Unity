using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Perilaku ayam: jalan acak, sesekali berhenti, belok saat menabrak dinding,
/// serta bisa DIANGKAT dan DILEMPAR oleh ChickenCarrier.
/// Script ini otomatis dipasang oleh ChickenSpawner.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ChickenWander : MonoBehaviour
{
    public enum State { Wander, Carried, Thrown }

    [Header("Physics (anti dorong)")]
    [Tooltip("Massa ayam saat berjalan. Sangat berat = tidak bisa didorong player. Nilai ini menimpa Mass di Rigidbody.")]
    [SerializeField] private float mass = 1000f;
    [Tooltip("Massa saat dilempar. Ringan supaya tidak melontarkan player kalau bertabrakan.")]
    [SerializeField] private float thrownMass = 1f;

    [Header("Movement")]
    [SerializeField] private Vector2 speedRange = new Vector2(1.5f, 3f);
    [SerializeField] private float acceleration = 20f;
    [SerializeField] private float turnSpeed = 360f;

    [Header("Behaviour")]
    [SerializeField] private Vector2 walkTimeRange = new Vector2(1.5f, 4f);
    [SerializeField] private Vector2 idleTimeRange = new Vector2(0.5f, 2f);
    [Range(0f, 1f)]
    [SerializeField] private float idleChance = 0.3f;

    [Header("Wall Avoidance")]
    [SerializeField] private float wallTurnMaxAngle = 70f;
    [SerializeField] private float wallNormalMaxY = 0.5f;
    [SerializeField] private float turnCooldown = 0.25f;

    [Header("After Throw")]
    [SerializeField] private float landingFriction = 15f;  // perlambatan saat meluncur di tanah (m/s²)
    [SerializeField] private float recoverSpeed = 0.5f;    // di bawah kecepatan ini ayam dianggap sudah berhenti
    [SerializeField] private float maxThrownTime = 6f;     // batas aman agar tidak nyangkut di state Thrown
    [SerializeField] private Vector2 dazedTimeRange = new Vector2(0.5f, 1.2f); // pusing sebentar setelah mendarat

    [Header("Panic After Landing")]
    [SerializeField] private float panicDuration = 3f;
    [SerializeField] private float panicSpeedMultiplier = 2f;

    [Header("In Pen (setelah masuk kandang)")]
    [SerializeField] private float penPadding = 0.35f;  // ayam menjaga jarak ini dari tepi kandang
    [SerializeField] private float penLookAhead = 0.6f; // seberapa jauh ke depan ayam "mengintip" tepi kandang

    private Rigidbody rb;
    private State state = State.Wander;
    private Transform homeParent;

    private Vector3 moveDir;
    private float speed;
    private bool isIdle;
    private float stateTimer;
    private float lastTurnTime = -10f;

    private float thrownTimer;
    private bool groundContact;
    private bool landedOnGround;
    private float panicTimer;

    private ChickenPen pen;

    /// <summary>Semua ayam yang aktif di scene (dipakai ChickenPen, tanpa perlu Find tiap frame).</summary>
    public static readonly List<ChickenWander> All = new List<ChickenWander>();

    public State CurrentState => state;
    public ChickenPen Pen => pen;
    public bool IsPenned => pen != null;
    public bool WasThrown { get; private set; }

    /// <summary>Bisa diangkat / dihitung masuk kandang: sedang berkeliaran dan belum masuk kandang.</summary>
    public bool CanBePickedUp => state == State.Wander && pen == null;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.sleepThreshold = 0f; // rigidbody yang "tidur" tidak mengirim OnCollisionStay

        Collider[] cols = GetComponentsInChildren<Collider>();
        if (cols.Length > 0)
        {
            var noFriction = new PhysicsMaterial("ChickenNoFriction")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (var c in cols) c.sharedMaterial = noFriction;
        }
        else
        {
            Debug.LogWarning($"{name}: tidak ada Collider, ayam tidak akan bisa menabrak dinding.", this);
        }
    }

    private void Start()
    {
        StartWalking(true);
    }

    // ---------------------------------------------------------------- API untuk ChickenCarrier

    public void PickUp(Transform holdPoint)
    {
        if (state != State.Wander) return;

        state = State.Carried;
        homeParent = transform.parent;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;
        SetCollidersEnabled(false); // tidak menabrak / mendorong player saat dibawa

        transform.SetParent(holdPoint, true);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void Throw(Vector3 velocity)
    {
        if (state != State.Carried) return;

        transform.SetParent(homeParent, true);

        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        if (flat.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

        SetCollidersEnabled(true);
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.mass = thrownMass;
        rb.linearVelocity = velocity;

        thrownTimer = 0f;
        groundContact = false;
        landedOnGround = false;
        panicTimer = 0f;
        WasThrown = true;
        state = State.Thrown;
    }

    /// <summary>Dipanggil ChickenPen saat ayam sudah resmi masuk kandang. Ayam tidak bisa diangkat lagi dan tetap di dalam.</summary>
    public void EnterPen(ChickenPen targetPen)
    {
        pen = targetPen;
    }

    // ---------------------------------------------------------------- Update fisika

    private void FixedUpdate()
    {
        switch (state)
        {
            case State.Carried: return;
            case State.Thrown: UpdateThrown(); return;
        }

        if (panicTimer > 0f)
            panicTimer = Mathf.Max(0f, panicTimer - Time.fixedDeltaTime);

        stateTimer -= Time.fixedDeltaTime;
        if (stateTimer <= 0f)
        {
            if (panicTimer > 0f) StartWalking(true);
            else if (!isIdle && Random.value < idleChance) StartIdle(Random.Range(idleTimeRange.x, idleTimeRange.y));
            else StartWalking(true);
        }

        // Sudah di kandang: kalau langkah berikutnya keluar area, belok ke arah tengah kandang
        if (pen != null && !isIdle)
        {
            Vector3 ahead = transform.position + moveDir * penLookAhead;
            if (!pen.Contains(ahead, penPadding))
            {
                Vector3 toCenter = pen.Center - transform.position;
                toCenter.y = 0f;
                if (toCenter.sqrMagnitude > 0.01f)
                    moveDir = Quaternion.AngleAxis(Random.Range(-45f, 45f), Vector3.up) * toCenter.normalized;
            }
        }

        Vector3 vel = rb.linearVelocity;
        Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
        float currentSpeed = panicTimer > 0f ? speed * panicSpeedMultiplier : speed;
        Vector3 target = isIdle ? Vector3.zero : moveDir * currentSpeed;
        horizontal = Vector3.MoveTowards(horizontal, target, acceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(horizontal.x, vel.y, horizontal.z);

        if (!isIdle && moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeed * Time.fixedDeltaTime));
        }
    }

    private void UpdateThrown()
    {
        thrownTimer += Time.fixedDeltaTime;

        bool grounded = groundContact && thrownTimer > 0.15f;
        groundContact = false;

        if (grounded)
        {
            landedOnGround = true;

            // Meluncur di tanah lalu berhenti (karena friction material = 0, kita rem manual)
            Vector3 vel = rb.linearVelocity;
            Vector3 h = new Vector3(vel.x, 0f, vel.z);
            h = Vector3.MoveTowards(h, Vector3.zero, landingFriction * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector3(h.x, vel.y, h.z);

            if (h.magnitude < recoverSpeed) Recover();
        }
        else if (thrownTimer > maxThrownTime)
        {
            Recover();
        }
    }

    private void Recover()
    {
        state = State.Wander;
        rb.mass = mass;
        if (landedOnGround)
        {
            ChickenPen scoringPen = FindFirstObjectByType<ChickenPen>();
            if (scoringPen == null || !scoringPen.ContainsForScoring(transform.position))
            {
                panicTimer = panicDuration;
                StartWalking(true);
                return;
            }
        }

        StartIdle(Random.Range(dazedTimeRange.x, dazedTimeRange.y));
    }

    // ---------------------------------------------------------------- Perilaku jalan

    private void StartWalking(bool pickNewDirection)
    {
        if (pickNewDirection)
            moveDir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

        speed = Random.Range(speedRange.x, speedRange.y);
        stateTimer = Random.Range(walkTimeRange.x, walkTimeRange.y);
        isIdle = false;
    }

    private void StartIdle(float duration)
    {
        isIdle = true;
        stateTimer = duration;
    }

    // ---------------------------------------------------------------- Tabrakan

    private void OnCollisionEnter(Collision c) => HandleCollision(c);
    private void OnCollisionStay(Collision c) => HandleCollision(c);

    private void HandleCollision(Collision c)
    {
        if (state == State.Thrown)
        {
            // Deteksi mendarat: ada permukaan datar di bawah
            for (int i = 0; i < c.contactCount; i++)
            {
                if (c.GetContact(i).normal.y > 0.7f) { groundContact = true; break; }
            }
            return;
        }

        if (state != State.Wander || isIdle) return;
        if (Time.time - lastTurnTime < turnCooldown) return;

        for (int i = 0; i < c.contactCount; i++)
        {
            Vector3 n = c.GetContact(i).normal; // menunjuk menjauh dari permukaan yang ditabrak
            if (n.y > wallNormalMaxY) continue;  // lantai, bukan dinding

            n.y = 0f;
            if (n.sqrMagnitude < 0.001f) continue;
            n.Normalize();

            if (Vector3.Dot(moveDir, n) > 0.3f) continue; // sudah menjauh

            float angle = Random.Range(-wallTurnMaxAngle, wallTurnMaxAngle);
            moveDir = Quaternion.AngleAxis(angle, Vector3.up) * n;
            lastTurnTime = Time.time;
            StartWalking(false);
            return;
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        foreach (var col in GetComponentsInChildren<Collider>(true))
            col.enabled = enabled;
    }
}