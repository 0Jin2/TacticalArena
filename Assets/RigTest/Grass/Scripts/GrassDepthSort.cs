using UnityEngine;

// 벨트스크롤용 앞뒤 정렬. 대상보다 아래(앞)에 있는 잔디는 캐릭터를 가리고, 위(뒤)에 있는 잔디는 캐릭터 뒤로 간다.
public class GrassDepthSort : MonoBehaviour
{
    [SerializeField] private Transform target;
    [Tooltip("대상 기준점에서 실제 발끝 맨 밑 픽셀까지의 높이 (유닛). 지금 캐릭터는 3픽셀")]
    [SerializeField] private float footOffset = 3f / 64f;
    [Tooltip("캐릭터보다 앞에 있을 때 정렬 순서 (캐릭터 파츠 0~9보다 커야 함)")]
    [SerializeField] private int frontOrder = 20;
    [Tooltip("캐릭터보다 뒤에 있을 때 정렬 순서")]
    [SerializeField] private int backOrder = -1;

    private SpriteRenderer[] tufts;

    void Awake()
    {
        tufts = GetComponentsInChildren<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 잔디 피벗은 맨 밑 줄에 있으므로, 발끝이 그 줄까지 내려와야 캐릭터가 앞으로 나온다
        float y = target.position.y + footOffset;
        foreach (SpriteRenderer tuft in tufts)
            tuft.sortingOrder = tuft.transform.position.y < y ? frontOrder : backOrder;
    }
}
