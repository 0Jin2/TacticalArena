using UnityEngine;

public class UICharacterIdleAnimator : MonoBehaviour
{
    private Animation anim;

    private void Awake()
    {
        anim = GetComponent<Animation>();

        if (anim == null)
        {
            Debug.LogWarning("❌ Animation 컴포넌트를 찾을 수 없습니다.");
            return;
        }

        if (anim.clip == null)
        {
            Debug.LogWarning("❌ Animation 컴포넌트에 등록된 클립이 없습니다.");
            return;
        }

        anim.Play();  // 첫 번째 클립 자동 재생 (Animation.clip 사용)
    }
}