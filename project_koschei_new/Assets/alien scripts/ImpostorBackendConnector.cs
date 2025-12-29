using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles communication between Unity and FastAPI backend for impostor spawning/despawning
/// NOW: Continuously updates impostor with current group position
/// </summary>
public class ImpostorBackendConnector : NetworkBehaviour
{
    [Header("Backend Settings")]
    public string backendUrl = "http://127.0.0.1:8000";
    public float checkSpawnInterval = 5f;
    public float groupUpdateInterval = 2f; // NEW: Update impostor's target position

    [Header("References")]
    public ImpostorAlienSpawner spawner;
    public GroupSyncManager groupSyncManager;
    public PlayerGroupManager playerGroupManager; // NEW: Reference to PlayerGroupManager

    [Header("Debug")]
    public bool showDebugLogs = true;

    private bool isCheckingSpawn = false;
    private string currentTargetGroupId = null;
    private string currentDisguiseAs = null;

    void Start()
    {
        if (spawner == null)
            spawner = FindObjectOfType<ImpostorAlienSpawner>();

        if (groupSyncManager == null)
            groupSyncManager = FindObjectOfType<GroupSyncManager>();

        if (playerGroupManager == null)
            playerGroupManager = FindObjectOfType<PlayerGroupManager>();

        if (spawner != null)
            spawner.backendConnector = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            StartCoroutine(CheckSpawnRoutine());
            StartCoroutine(UpdateImpostorTargetRoutine()); // NEW: Dynamic group tracking
        }
    }

    /// <summary>
    /// NEW: Continuously update impostor with current group position
    /// </summary>
    IEnumerator UpdateImpostorTargetRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(groupUpdateInterval);

            // Only update if impostor is active and we have a target group
            if (currentTargetGroupId != null && spawner != null)
            {
                NetworkObject impostor = spawner.GetCurrentImpostor();

                if (impostor != null && impostor.IsSpawned)
                {
                    // Get current group position from PlayerGroupManager
                    Vector3 currentGroupCenter = GetCurrentGroupCenter(currentTargetGroupId);

                    if (currentGroupCenter != Vector3.zero)
                    {
                        // Update impostor AI with new target position
                        ImpostorPlayerAI ai = impostor.GetComponent<ImpostorPlayerAI>();
                        if (ai != null)
                        {
                            string[] groupMembers = GetCurrentGroupMembers(currentTargetGroupId);
                            ai.UpdateTargetGroupPosition(currentGroupCenter, groupMembers);

                            if (showDebugLogs)
                                Debug.Log($"[ImpostorBackend] 📍 Updated impostor target: {currentGroupCenter:F1}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get current center position of target group using PlayerGroupManager
    /// </summary>
    Vector3 GetCurrentGroupCenter(string groupId)
    {
        if (playerGroupManager == null)
            return Vector3.zero;

        // Get all active groups
        var activeGroups = playerGroupManager.GetActiveGroups();

        // Find the group with matching groupId
        foreach (var group in activeGroups)
        {
            if (group.groupId == groupId)
            {
                return group.centerPosition;
            }
        }

        if (showDebugLogs)
            Debug.LogWarning($"[ImpostorBackend] Group '{groupId}' not found in active groups");

        return Vector3.zero;
    }

    /// <summary>
    /// Get current members of target group using PlayerGroupManager
    /// </summary>
    string[] GetCurrentGroupMembers(string groupId)
    {
        if (playerGroupManager == null)
            return new string[0];

        // Get all active groups
        var activeGroups = playerGroupManager.GetActiveGroups();

        // Find the group with matching groupId
        foreach (var group in activeGroups)
        {
            if (group.groupId == groupId)
            {
                return group.playerIds.ToArray();
            }
        }

        return new string[0];
    }

    IEnumerator CheckSpawnRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkSpawnInterval);

            if (!isCheckingSpawn && spawner != null)
            {
                StartCoroutine(CheckSpawnWithBackend());
            }
        }
    }

    IEnumerator CheckSpawnWithBackend()
    {
        isCheckingSpawn = true;

        string url = $"{backendUrl}/impostor/check_spawn";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                SpawnCheckResponse response = JsonUtility.FromJson<SpawnCheckResponse>(json);

                // Handle despawn signal
                if (response.should_despawn)
                {
                    if (showDebugLogs)
                        Debug.Log($"[ImpostorBackend] 🛑 Backend says: DESPAWN (reason: {response.reason})");

                    spawner.DespawnCurrentImpostor();
                    currentTargetGroupId = null;
                    currentDisguiseAs = null;
                }
                // Handle spawn signal
                else if (response.should_spawn && !string.IsNullOrEmpty(response.disguise_as))
                {
                    if (showDebugLogs)
                    {
                        Debug.Log($"[ImpostorBackend] ═══════════════════════════════════");
                        Debug.Log($"[ImpostorBackend] 🎯 Backend says: SPAWN IMPOSTOR");
                        Debug.Log($"[ImpostorBackend] Target Group: {response.target_group_id}");
                        Debug.Log($"[ImpostorBackend] Disguise As: {response.disguise_as}");
                        Debug.Log($"[ImpostorBackend] Position: {response.target_group_position[0]}, {response.target_group_position[1]}, {response.target_group_position[2]}");
                        Debug.Log($"[ImpostorBackend] ═══════════════════════════════════");
                    }

                    Vector3 groupCenter = new Vector3(
                        response.target_group_position[0],
                        response.target_group_position[1],
                        response.target_group_position[2]
                    );

                    spawner.SpawnImpostorForGroup(
                        response.target_group_id,
                        response.target_group_members,
                        groupCenter,
                        response.disguise_as
                    );

                    currentTargetGroupId = response.target_group_id;
                    currentDisguiseAs = response.disguise_as;
                }
            }
            else
            {
                Debug.LogWarning($"[ImpostorBackend] Failed to check spawn: {request.error}");
            }
        }

        isCheckingSpawn = false;
    }

    /// <summary>
    /// Called by spawner after impostor spawns successfully
    /// </summary>
    public void OnImpostorSpawned(NetworkObject impostorNetObj, string targetGroupId, string disguiseAs)
    {
        if (showDebugLogs)
            Debug.Log($"[ImpostorBackend] ✅ Impostor spawned, activating backend...");

        currentTargetGroupId = targetGroupId;
        currentDisguiseAs = disguiseAs;

        StartCoroutine(ActivateImpostorBackend(targetGroupId, disguiseAs));
    }

    IEnumerator ActivateImpostorBackend(string targetGroupId, string disguiseAs)
    {
        string url = $"{backendUrl}/impostor/activate";

        ActivateRequest data = new ActivateRequest
        {
            target_player_id = disguiseAs,
            target_group_id = targetGroupId,
            engagement_rate = 0.4f
        };

        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (showDebugLogs)
                    Debug.Log($"[ImpostorBackend] ✅ Backend impostor activated as {disguiseAs}");
            }
            else
            {
                Debug.LogError($"[ImpostorBackend] Failed to activate backend: {request.error}");
            }
        }
    }

    /// <summary>
    /// Notify backend when impostor despawns
    /// </summary>
    public void NotifyImpostorDespawned()
    {
        if (showDebugLogs)
            Debug.Log($"[ImpostorBackend] 🗑️ Notifying backend of despawn...");

        currentTargetGroupId = null;
        currentDisguiseAs = null;

        StartCoroutine(DeactivateImpostorBackend());
    }

    IEnumerator DeactivateImpostorBackend()
    {
        string url = $"{backendUrl}/impostor/deactivate";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (showDebugLogs)
                    Debug.Log($"[ImpostorBackend] ✅ Backend impostor deactivated");
            }
            else
            {
                Debug.LogWarning($"[ImpostorBackend] Failed to deactivate backend: {request.error}");
            }
        }
    }

    [System.Serializable]
    public class SpawnCheckResponse
    {
        public bool should_spawn;
        public bool should_despawn;
        public string reason;
        public string target_group_id;
        public float[] target_group_position;
        public string[] target_group_members;
        public string disguise_as;
        public float engagement_rate;
        public float conversation_duration;
    }

    [System.Serializable]
    public class ActivateRequest
    {
        public string target_player_id;
        public string target_group_id;
        public float engagement_rate;
    }
}
