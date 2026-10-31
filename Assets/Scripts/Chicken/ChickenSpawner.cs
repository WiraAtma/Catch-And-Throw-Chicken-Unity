using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Memunculkan ayam di posisi acak dalam sebuah area. Setelah muncul, ayam menyebar sendiri lewat ChickenWander.
/// Letakkan GameObject ini di tengah arena (setinggi permukaan lantai), atur Area Size sesuai ukuran arena.
/// </summary>
public class ChickenSpawner : MonoBehaviour
{
    [Header("Chicken")]
    [Tooltip("Kosongkan untuk memakai ayam placeholder sementara.")]
    [SerializeField] private GameObject chickenPrefab;
    [Min(0)]
    [SerializeField] private int chickenCount = 10;

    [Header("Spawn Area (pusat = posisi object ini, rotasi tidak dipakai)")]
    [SerializeField] private Vector2 areaSize = new Vector2(20f, 20f); // X dan Z (meter)
    [SerializeField] private float spawnHeight = 1f;       // tinggi di atas object ini; ayam jatuh ke lantai
    [SerializeField] private float minSpacing = 1.5f;      // jarak minimum antar ayam saat spawn
    [SerializeField] private float clearanceRadius = 0.5f; // area bebas di sekitar titik spawn (hindari dinding)
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private int maxAttempts = 30;         // percobaan cari titik per ayam

    [Header("Options")]
    [SerializeField] private bool spawnOnStart = true;

    private readonly List<GameObject> chickens = new List<GameObject>();
    private Transform container;

    public int AliveCount => chickens.Count;

    private void Start()
    {
        if (spawnOnStart) Spawn();
    }

    [ContextMenu("Respawn Chickens")]
    public void Respawn()
    {
        Clear();
        Spawn();
    }

    public void Spawn()
    {
        if (container == null) container = new GameObject("Chickens").transform;

        var usedPositions = new List<Vector3>();
        for (int i = 0; i < chickenCount; i++)
        {
            if (!TryFindSpawnPoint(usedPositions, out Vector3 pos))
            {
                Debug.LogWarning($"ChickenSpawner: hanya berhasil spawn {i} dari {chickenCount} ayam. " +
                                 "Perbesar Area Size atau kecilkan Min Spacing.", this);
                break;
            }
            usedPositions.Add(pos);
            chickens.Add(CreateChicken(pos));
        }
    }

    public void Clear()
    {
        foreach (var c in chickens)
            if (c != null) Destroy(c);
        chickens.Clear();
    }

    private bool TryFindSpawnPoint(List<Vector3> used, out Vector3 result)
    {
        Vector2 half = areaSize * 0.5f;
        float minSqr = minSpacing * minSpacing;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 p = transform.position + new Vector3(
                Random.Range(-half.x, half.x),
                spawnHeight,
                Random.Range(-half.y, half.y));

            // Jangan di dalam dinding / objek lain (termasuk player)
            if (Physics.CheckSphere(p, clearanceRadius, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            // Jangan terlalu dekat dengan ayam lain
            bool tooClose = false;
            foreach (var u in used)
            {
                Vector3 d = u - p; d.y = 0f;
                if (d.sqrMagnitude < minSqr) { tooClose = true; break; }
            }
            if (tooClose) continue;

            result = p;
            return true;
        }

        result = default;
        return false;
    }

    private GameObject CreateChicken(Vector3 position)
    {
        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        GameObject go = chickenPrefab != null
            ? Instantiate(chickenPrefab, position, rot)
            : CreatePlaceholder(position, rot);

        go.transform.SetParent(container, true);

        // Prefab ayam cukup berisi model + Collider; perilaku ditambahkan otomatis
        if (!go.TryGetComponent<ChickenWander>(out _))
            go.AddComponent<ChickenWander>();

        return go;
    }

    // Ayam sementara (kapsul putih + "paruh" oranye di depan) supaya bisa langsung dites
    private GameObject CreatePlaceholder(Vector3 position, Quaternion rotation)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Chicken (placeholder)";
        body.transform.SetPositionAndRotation(position, rotation);
        body.transform.localScale = new Vector3(0.5f, 0.4f, 0.5f);
        body.GetComponent<Renderer>().material.color = Color.white;

        GameObject beak = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(beak.GetComponent<Collider>()); // langsung hilang, jangan sampai ikut terdaftar di ChickenWander
        beak.name = "Beak";
        beak.transform.SetParent(body.transform, false);
        beak.transform.localPosition = new Vector3(0f, 0.3f, 0.55f);
        beak.transform.localScale = new Vector3(0.4f, 0.3f, 0.6f);
        beak.GetComponent<Renderer>().material.color = new Color(1f, 0.55f, 0f);

        return body;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0f, 1f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * spawnHeight, new Vector3(areaSize.x, 0.1f, areaSize.y));
    }
}