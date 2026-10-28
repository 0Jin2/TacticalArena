using System.Collections.Generic;
using UnityEngine;

// RotSprite로 미리 구운 파츠별 회전 스프라이트 정보 (RotSpriteBaker가 생성)
// 프레임 i = 관절 기준 i * angleStep 도 회전한 모습
public class RotSpriteSet : ScriptableObject
{
    [System.Serializable]
    public class Frame
    {
        public RectInt rect;   // 아틀라스 안 위치 (픽셀)
        public Vector2 pivotPx; // 관절 위치 (rect 왼쪽 아래 기준 픽셀)
    }

    [System.Serializable]
    public class Part
    {
        public string name;
        public List<Frame> frames = new List<Frame>();
    }

    public Texture2D atlas;
    public float pixelsPerUnit = 64f;
    public float angleStep = 15f;
    public List<Part> parts = new List<Part>();

    public int FrameCount => Mathf.RoundToInt(360f / angleStep);

    public Part Find(string partName)
    {
        return parts.Find(p => p.name == partName);
    }
}
