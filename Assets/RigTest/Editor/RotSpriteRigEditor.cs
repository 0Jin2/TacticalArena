using UnityEditor;
using UnityEngine;

// RotSpriteRig 인스펙터에 '회전본 다시 굽기' 버튼 추가
[CustomEditor(typeof(RotSpriteRig))]
public class RotSpriteRigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("회전본 다시 굽기", GUILayout.Height(28)))
        {
            RotSpriteBaker.Bake(true);
        }
        EditorGUILayout.HelpBox("aseprite 파일을 저장하면 자동으로 다시 구워집니다.\n각도 단위나 관절 위치를 바꿨을 때만 이 버튼을 누르세요.", MessageType.Info);
    }
}
