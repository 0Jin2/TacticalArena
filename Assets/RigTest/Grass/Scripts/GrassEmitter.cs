using System.Collections.Generic;
using UnityEngine;

// 잔디를 건드리는 물체. 움직이는 동안 박스 안의 잔디에 속도를 넣는다.
public class GrassEmitter : MonoBehaviour
{
    public static readonly List<GrassEmitter> All = new List<GrassEmitter>();

    [Tooltip("잔디와 닿는 영역 크기 (유닛)")]
    [SerializeField] private Vector2 size = new Vector2(0.22f, 0.25f);
    [Tooltip("영역 중심의 위치 (이 오브젝트 기준)")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 0.12f);
    [Tooltip("이 속도(유닛/초)에서 밀어내는 힘이 최대가 된다")]
    [Min(0.01f)] [SerializeField] private float maxSpeed = 0.5f;
    [Tooltip("최대 속도일 때 잔디에 넣는 속도")]
    [Min(0f)] [SerializeField] private float push = 6f;

    private Vector2 lastPosition;

    public Vector2 Velocity { get; private set; }

    public Rect WorldRect
    {
        get
        {
            Vector2 center = (Vector2)transform.position + offset;
            return new Rect(center - size * 0.5f, size);
        }
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void LateUpdate()
    {
        Vector2 position = transform.position;
        Vector2 worldVelocity = Time.deltaTime > 0f ? (position - lastPosition) / Time.deltaTime : Vector2.zero;
        lastPosition = position;

        Velocity = Vector2.ClampMagnitude(worldVelocity / maxSpeed, 1f) * push;
    }

    void OnDrawGizmosSelected()
    {
        Rect rect = WorldRect;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(rect.center, rect.size);
    }
}
