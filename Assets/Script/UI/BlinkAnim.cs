using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class BlinkAnim : MonoBehaviour
{
    private TextMeshProUGUI textMeshPro; // TextMeshProUGUI를 위한 변수
    public float fadeSpeed = 1.0f; // 알파값 변화 속도
    private float time;

    void Start()
    {
        // TextMeshProUGUI 컴포넌트 가져오기
        textMeshPro = GetComponent<TextMeshProUGUI>();
        textMeshPro.color = new Color(1, 1, 1, 1);
    }

    void Update()
    {
        float alpha = (Mathf.Sin((time * fadeSpeed) + Mathf.PI / 2) + 1) / 2; // 초기값을 1로 설정하기 위해 PI/2를 더함
        textMeshPro.color = new Color(1, 1, 1, alpha);

        // 시간 경과
        time += Time.deltaTime;
    }
}