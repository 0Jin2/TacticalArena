using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// AnimationEventDestroy : 이펙트 애니메이션 마지막 프레임의 Animation Event 에서 DestroySelf() 를 호출하면
//                         이 오브젝트를 지운다. (기존 DestroySelfOnAnimationEnd 와 같은 역할/같은 함수 이름)
// ─────────────────────────────────────────────────────────────────────────────

public class AnimationEventDestroy : MonoBehaviour
{
    public void DestroySelf()
    {
        Destroy(gameObject);
    }
}
