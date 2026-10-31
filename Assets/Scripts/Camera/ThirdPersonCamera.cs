using UnityEngine;
using UnityEngine.InputSystem; // New Input System

/// <summary>
/// Kamera third-person ala Fall Guys (tanpa Cinemachine).
/// - Gerak mouse   : putar kamera 360° (yaw) + atas/bawah (pitch, dibatasi)
/// - Scroll mouse  : zoom in / zoom out
/// - Esc           : lepas kursor | Klik kiri : kunci kursor lagi
/// - Kamera tidak menembus dinding (SphereCast collision)
/// Pasang di Main Camera, lalu drag Player ke field Target.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.2f, 0f); // titik yang "dilihat" kamera

    [Header("Rotation")]
    [SerializeField] private float sensitivity = 0.15f;  // derajat per pixel mouse
    [SerializeField] private float minPitch = -20f;      // batas lihat ke atas (kamera turun)
    [SerializeField] private float maxPitch = 70f;       // batas lihat ke bawah (kamera naik)
    [SerializeField] private float startPitch = 20f;
    [SerializeField] private bool invertY = false;

    [Header("Zoom")]
    [SerializeField] private float startDistance = 6f;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 15f;
    [SerializeField] private float zoomStep = 1f;         // jarak per "klik" scroll
    [SerializeField] private float zoomSmoothTime = 0.08f;

    [Header("Collision")]
    [SerializeField] private LayerMask collisionMask = ~0;
    [SerializeField] private float collisionRadius = 0.25f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnStart = true;

    private float yaw;
    private float pitch;
    private float targetDistance;
    private float currentDistance;
    private float zoomVelocity;
    private readonly RaycastHit[] hits = new RaycastHit[8];

    private void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = startPitch;
        targetDistance = currentDistance = startDistance;

        if (lockCursorOnStart) SetCursorLocked(true);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);
    }

    private void LateUpdate() // kamera diupdate SETELAH player bergerak -> tidak jitter
    {
        if (target == null) return;

        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            // --- Orbit ---
            Vector2 delta = mouse.delta.ReadValue(); // sudah per-frame, JANGAN dikali Time.deltaTime
            yaw += delta.x * sensitivity;
            pitch += (invertY ? delta.y : -delta.y) * sensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            // --- Zoom ---
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }

        currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref zoomVelocity, zoomSmoothTime);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + targetOffset;
        Vector3 backDir = rotation * Vector3.back; // arah dari pivot ke posisi kamera

        // --- Collision: dekatkan kamera kalau ada tembok di belakang ---
        float distance = currentDistance;
        int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, backDir, hits, currentDistance,
                                               collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(target)) continue; // abaikan collider player sendiri
            if (hits[i].distance < distance) distance = hits[i].distance;
        }

        transform.SetPositionAndRotation(pivot + backDir * distance, rotation);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}