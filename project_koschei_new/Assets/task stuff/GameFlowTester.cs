using UnityEngine;
using Unity.Netcode;

public class GameFlowTester : MonoBehaviour
{
    void Update()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Debug.Log("[TESTER] Pressing 1 -- CompleteIntro");
            TaskManager.Instance.CompleteIntro();
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            Debug.Log("[TESTER] Pressing 2 -- CompleteBriefing");
            TaskManager.Instance.CompleteBriefing();
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            Debug.Log("[TESTER] Pressing 3 -- CompleteReturnBriefing");
            TaskManager.Instance.CompleteReturnBriefing();
        }
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            Debug.Log("[TESTER] Pressing 4 -- OnBossDefeated");
            TaskManager.Instance.OnBossDefeated();
        }
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            Debug.Log("[TESTER] Pressing 5 -- Simulating player death");
            GameManager.Instance.RegisterPlayerDeath(0);
        }
    }
}
