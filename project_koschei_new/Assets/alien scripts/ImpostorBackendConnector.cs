using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

/// <summary>
/// UPDATED: Now polls backend for spawn/despawn commands
/// Backend controls WHEN impostor spawns based on groups and timing
/// </summary>
public class ImpostorBackendConnector : NetworkBehaviour
{
    [Header("Backend Settings")]
    [Tooltip("Backend URL - leave empty to auto-detect from NetworkManager")]
    public string backendUrlOverride;
    public int backendPort = 8000;

    [Header("Polling Settings")]
    public float pollInterval = 2f; // Check backend every 2 seconds

    [Header("References")]
    public PlayerGroupManager groupManager;
    public ImpostorAlienSpawner spawner;
    public ImpostorPlayerAI impostorAI; // Reference to impostor AI (set after spawn)

    [Header("Debug")]
    public bool showDebugLogs = true;

    private float lastPollTime = 0f;
    private bool isPolling = false;

    private string ResolvedBackendUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(backendUrlOverride))
                return backendUrlOverride;
            return NetworkHostAddressHelper.GetChatApiUrlFromNetworkManager(backendPort, "");
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            isPolling = true;
            if (showDebugLogs)
                Debug.Log("[ImpostorConnector] 🔄 Started polling backend");
        }
    }

    void Update()
    {
        if (!IsServer || !isPolling) return;

        // Poll backend regularly
        if (Time.time - lastPollTime >= pollInterval)
        {
            lastPollTime = Time.time;
            StartCoroutine(PollBackendForSpawnCommand());
        }
    }

    /// <summary>
    /// Poll backend to check if impostor should spawn or despawn
    /// </summary>
    private IEnumerator PollBackendForSpawnCommand()
    {
        string url = ResolvedBackendUrl;
        if (string.IsNullOrEmpty(url))
        {
            yield break;
        }

        // Ensure HTTP for local development
        if (url.StartsWith("https://127.0.0.1") || url.StartsWith("https://localhost"))
        {
            url = url.Replace("https://", "http://");
        }

        string checkUrl = url.TrimEnd('/') + "/impostor/check_spawn";

        using (UnityWebRequest req = UnityWebRequest.Get(checkUrl))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    SpawnCheckResponse response = JsonUtility.FromJson<SpawnCheckResponse>(req.downloadHandler.text);

                    // Check if backend wants us to DESPAWN
                    if (response.should_despawn)
                    {
                        if (showDebugLogs)
                            Debug.Log($"[ImpostorConnector] 🛑 Backend says: DESPAWN impostor (reason: {response.reason})");

                        HandleDespawnCommand();
                    }
                    // Check if backend wants us to SPAWN
                    else if (response.should_spawn)
                    {
                        if (showDebugLogs)
                        {
                            Debug.Log($"[ImpostorConnector] 🎯 Backend says: SPAWN impostor");
                            Debug.Log($"  Target group: {response.target_group_id}");
                            Debug.Log($"  Disguise as: {response.disguise_as}");
                        }

                        HandleSpawnCommand(response);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ImpostorConnector] Failed to parse spawn check: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Handle spawn command from backend
    /// </summary>
    private void HandleSpawnCommand(SpawnCheckResponse spawnData)
    {
        if (spawner == null)
        {
            Debug.LogError("[ImpostorConnector] No spawner reference!");
            return;
        }

        // Parse target group position
        Vector3 targetPosition = Vector3.zero;
        if (spawnData.target_group_position != null && spawnData.target_group_position.Length >= 3)
        {
            targetPosition = new Vector3(
                spawnData.target_group_position[0],
                spawnData.target_group_position[1],
                spawnData.target_group_position[2]
            );
        }

        // Tell spawner to spawn at this target group
        spawner.SpawnImpostorForGroup(
            spawnData.target_group_id,
            spawnData.target_group_members,
            targetPosition,
            spawnData.disguise_as
        );

        // Backend will be notified via spawner's NotifyImpostorSpawned call
    }

    /// <summary>
    /// Handle despawn command from backend
    /// </summary>
    private void HandleDespawnCommand()
    {
        if (spawner == null)
        {
            Debug.LogWarning("[ImpostorConnector] No spawner reference for despawn");
            return;
        }

        // Get impostor AI reference if we don't have it
        if (impostorAI == null && spawner.GetCurrentImpostor() != null)
        {
            impostorAI = spawner.GetCurrentImpostor().GetComponent<ImpostorPlayerAI>();
        }

        // Tell impostor AI to leave area (walk away)
        if (impostorAI != null)
        {
            if (showDebugLogs)
                Debug.Log("[ImpostorConnector] 👋 Telling impostor to walk away");

            impostorAI.LeaveArea();

            // Despawn after a delay to let it walk away
            StartCoroutine(DespawnAfterDelay(5f));
        }
        else
        {
            // No AI found, just despawn immediately
            spawner.DespawnCurrentImpostor();
        }
    }

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (spawner != null)
        {
            if (showDebugLogs)
                Debug.Log("[ImpostorConnector] 🗑️ Despawning impostor after walk-away");

            spawner.DespawnCurrentImpostor();
        }
    }

    /// <summary>
    /// Called by spawner after impostor spawns successfully
    /// </summary>
    public void OnImpostorSpawned(NetworkObject impostorObject, string targetGroupId, string disguiseAs)
    {
        // Store AI reference
        impostorAI = impostorObject.GetComponent<ImpostorPlayerAI>();

        // Notify backend that impostor is now active
        StartCoroutine(ActivateImpostorInBackend(targetGroupId, disguiseAs));
    }

    private IEnumerator ActivateImpostorInBackend(string targetGroupId, string disguiseAs)
    {
        string url = ResolvedBackendUrl;
        if (string.IsNullOrEmpty(url))
        {
            yield break;
        }

        if (url.StartsWith("https://127.0.0.1") || url.StartsWith("https://localhost"))
        {
            url = url.Replace("https://", "http://");
        }

        string activateUrl = url.TrimEnd('/') + "/impostor/activate";

        var payload = new ImpostorActivatePayload
        {
            target_group_id = targetGroupId,
            target_player_id = disguiseAs,
            engagement_rate = 0.4f
        };

        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest req = new UnityWebRequest(activateUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                if (showDebugLogs)
                    Debug.Log($"[ImpostorConnector] ✅ Backend confirmed impostor activation");
            }
            else
            {
                Debug.LogError($"[ImpostorConnector] ❌ Backend activation failed: {req.error}");
            }
        }
    }

    /// <summary>
    /// Called when impostor is despawned
    /// </summary>
    public void NotifyImpostorDespawned()
    {
        if (!IsServer) return;

        impostorAI = null;
        StartCoroutine(DeactivateImpostorInBackend());
    }

    private IEnumerator DeactivateImpostorInBackend()
    {
        string url = ResolvedBackendUrl;
        if (string.IsNullOrEmpty(url))
        {
            yield break;
        }

        if (url.StartsWith("https://127.0.0.1") || url.StartsWith("https://localhost"))
        {
            url = url.Replace("https://", "http://");
        }

        string deactivateUrl = url.TrimEnd('/') + "/impostor/deactivate";

        using (UnityWebRequest req = new UnityWebRequest(deactivateUrl, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                if (showDebugLogs)
                    Debug.Log($"[ImpostorConnector] ✅ Backend confirmed impostor deactivation");
            }
        }
    }

    [Serializable]
    public class SpawnCheckResponse
    {
        public bool should_spawn;
        public bool should_despawn;
        public string target_group_id;
        public float[] target_group_position;
        public string[] target_group_members;
        public string disguise_as;
        public float engagement_rate;
        public string reason;
        public bool impostor_active;
        public float next_spawn_in;
    }

    [Serializable]
    public class ImpostorActivatePayload
    {
        public string target_group_id;
        public string target_player_id;
        public float engagement_rate;
    }
}