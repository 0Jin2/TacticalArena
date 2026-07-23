using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraResolution : MonoBehaviour
{
    void Awake()
    {
        // 실행되자마자 카메라 배경을 검정색으로 고정
        Camera camera = GetComponent<Camera>();
        camera.backgroundColor = Color.black;
        camera.clearFlags = CameraClearFlags.SolidColor;
    }

    void Start()
    {
        Camera camera = GetComponent<Camera>();
        Rect rect = camera.rect;
        float scaleheight = ((float)Screen.width / Screen.height) / ((float)16 / 9);
        float scalewidth = 1f / scaleheight;

        // 위아래 공백 (세로로 긴 기기)
        if (scaleheight < 1)
        {
            rect.height = scaleheight;
            rect.y = (1f - scaleheight) / 2f;
        }
        // 좌우 공백 (가로로 넓은 기기)
        else
        {
            rect.width = scalewidth;
            rect.x = (1f - scalewidth) / 2f;
        }
        camera.rect = rect;
    }

    void OnApplicationPause(bool pause)
    {
        if (!pause) // 앱이 다시 켜질 때
        {
            Camera camera = GetComponent<Camera>();
            camera.backgroundColor = Color.black;
            camera.clearFlags = CameraClearFlags.SolidColor;
        }
    }
}
