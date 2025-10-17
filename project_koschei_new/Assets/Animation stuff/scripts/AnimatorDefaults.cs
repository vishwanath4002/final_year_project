using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AnimatorDefaults : MonoBehaviour
{
    public float startSpeed = 0f;
    public bool startIsGrounded = true;

    Animator anim;
    void Awake()
    {
        anim = GetComponent<Animator>();
        anim.SetFloat("Speed", startSpeed);
        anim.SetBool("IsGrounded", startIsGrounded);
        anim.SetBool("CanMove", true);
        anim.SetFloat("AimBlend", 0f);
    }
}
