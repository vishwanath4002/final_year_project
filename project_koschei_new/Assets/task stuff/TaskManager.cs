using UnityEngine;

public class TaskManager : MonoBehaviour
{
    [SerializeField] ScavengerRaidTask scavengerRaidTask;

    void Start()
    {
        // Remove this line once tested -- replace with your actual trigger
        StartScavengerRaid();
    }

    public void StartScavengerRaid()
    {
        scavengerRaidTask.OnTaskCompleted += OnRaidWon;
        scavengerRaidTask.OnTaskFailed += OnRaidLost;
        scavengerRaidTask.StartTask();
    }

    void OnRaidWon()
    {
        Debug.Log("All aliens defeated -- Scientist is safe!");
        // Add your next task or reward logic here
    }

    void OnRaidLost()
    {
        Debug.Log("Scientist was killed -- Task failed!");
        // Add your fail state logic here
    }
}
