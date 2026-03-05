using UnityEngine;

/// <summary>
/// Receives OnFootstep animation events from Walk_N and Run_N clips
/// and plays a random footstep sound. Add to the root of ImpostorPlayer prefab.
/// Assign footstep clips and an AudioSource in the Inspector.
/// </summary>
public class FootstepReceiver : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] footstepClips;

    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.5f;

    [Tooltip("Random pitch variance around 1.0 to stop footsteps sounding identical")]
    [SerializeField] private float pitchVariance = 0.1f;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponentInChildren<AudioSource>();
    }

    // Called by animation events on Walk_N and Run_N clips
    private void OnFootstep(AnimationEvent animationEvent)
    {
        if (audioSource == null || footstepClips == null || footstepClips.Length == 0)
            return;

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        audioSource.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
        audioSource.PlayOneShot(clip, volume);
    }
}
