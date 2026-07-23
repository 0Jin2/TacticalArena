using UnityEngine;

public class DestroySelfOnAnimationEnd : MonoBehaviour
{
    // 애니메이션에서 마지막 프레임에 Animation Event로 이 메서드 호출
    public void DestroySelf()
    {
        Destroy(gameObject);
    }
}