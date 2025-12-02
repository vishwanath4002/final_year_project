using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerIdentity : NetworkBehaviour
{
    public static PlayerIdentity Local;   // easy access on each client

    [SerializeField] private string defaultBaseName = "Player";

    // Networked name so all clients see the same label
    public NetworkVariable<FixedString32Bytes> playerName =
        new NetworkVariable<FixedString32Bytes>();

    private void Awake()
    {
        // nothing here yet
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Local = this;

            // If host/server, you can assign names here, or use some lobby system
            if (IsServer)
            {
                // Example: "Player 1", "Player 2" based on ClientId
                playerName.Value = $"{defaultBaseName} {OwnerClientId + 1}";
            }
        }
    }

    public string GetDisplayName()
    {
        if (playerName.Value.IsEmpty)
        {
            return $"{defaultBaseName} {OwnerClientId + 1}";
        }
        return playerName.Value.ToString();
    }
}
