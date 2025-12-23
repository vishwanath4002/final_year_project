using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;

/// <summary>
/// Handles communication between Unity and the FastAPI backend for impostor activation
/// Attach this to your ImpostorAlienSpawner GameObject
/// </summary>
public class ImpostorBackendConnector : NetworkBehaviour
{
    [Header("Backend Settings")]
    [Tooltip("Backend URL - leave empty to auto-detect from NetworkManager")]
    public string backendUrlOverride;
    public int backendPort = 8000;
    
    [Header("References")]
    public PlayerGroupManager groupManager;
    public ImpostorAlienSpawner spawner;
    
    private string ResolvedBackendUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(backendUrlOverride))
                return backendUrlOverride;
            
            return NetworkHostAddressHelper.GetChatApiUrlFromNetworkManager(backendPort, "");
        }
    }
    
    /// <summary>
    /// Activate impostor in backend when spawning near a group
    /// Call this from ImpostorAlienSpawner after choosing target group
    /// </summary>
    public void NotifyImpostorSpawned(PlayerGroup targetGroup)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ImpostorBackend] NotifyImpostorSpawned should only be called on server");
            return;
        }
        
        if (targetGroup == null)
        {
            Debug.LogWarning("[ImpostorBackend] Cannot activate impostor - no target group");
            return;
        }
        
        StartCoroutine(ActivateImpostorInBackend(targetGroup.groupId));
    }
    
    /// <summary>
    /// Notify backend that impostor has been deactivated/despawned
    /// </summary>
    public void NotifyImpostorDespawned()
    {
        if (!IsServer) return;
        
        StartCoroutine(DeactivateImpostorInBackend());
    }
    
    private IEnumerator ActivateImpostorInBackend(string targetGroupId)
    {
        string url = ResolvedBackendUrl;
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("[ImpostorBackend] Could not resolve backend URL");
            yield break;
        }
        
        // Ensure HTTP for local development
        if (url.StartsWith("https://127.0.0.1") || url.StartsWith("https://localhost"))
        {
            url = url.Replace("https://", "http://");
        }
        
        string activateUrl = url.TrimEnd('/') + "/impostor/activate";
        
        // Build request payload
        var payload = new ImpostorActivatePayload
        {
            target_group_id = targetGroupId,
            engagement_rate = 0.4f
        };
        
        string json = JsonUtility.ToJson(payload);
        
        Debug.Log($"[ImpostorBackend] 🚀 Activating impostor for group '{targetGroupId}'");
        Debug.Log($"[ImpostorBackend] POST {activateUrl}");
        
        using (UnityWebRequest req = new UnityWebRequest(activateUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            
            yield return req.SendWebRequest();
            
            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    ImpostorActivateResponse response = JsonUtility.FromJson<ImpostorActivateResponse>(req.downloadHandler.text);
                    
                    if (response.success)
                    {
                        Debug.Log($"[ImpostorBackend] ✅ Impostor activated successfully!");
                        Debug.Log($"[ImpostorBackend]    Disguised as: {response.disguised_as}");
                        Debug.Log($"[ImpostorBackend]    Target group: {response.target_group_id}");
                        Debug.Log($"[ImpostorBackend]    Group members: {string.Join(", ", response.target_group_members ?? new string[0])}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ImpostorBackend] ⚠️ Backend activation failed: {response.message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ImpostorBackend] Failed to parse response: {ex.Message}");
                    Debug.LogError($"[ImpostorBackend] Raw response: {req.downloadHandler.text}");
                }
            }
            else
            {
                Debug.LogError($"[ImpostorBackend] ❌ Activation request failed: {req.error}");
                Debug.LogError($"[ImpostorBackend] Is the backend server running at {activateUrl}?");
            }
        }
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
        
        Debug.Log($"[ImpostorBackend] 🛑 Deactivating impostor");
        
        using (UnityWebRequest req = new UnityWebRequest(deactivateUrl, "POST"))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            
            yield return req.SendWebRequest();
            
            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[ImpostorBackend] ✅ Impostor deactivated");
            }
            else
            {
                Debug.LogWarning($"[ImpostorBackend] Failed to deactivate: {req.error}");
            }
        }
    }
}

[Serializable]
public class ImpostorActivatePayload
{
    public string target_group_id;
    public float engagement_rate;
}

[Serializable]
public class ImpostorActivateResponse
{
    public bool success;
    public string message;
    public string disguised_as;
    public string target_group_id;
    public string[] target_group_members;
    public float engagement_rate;
}