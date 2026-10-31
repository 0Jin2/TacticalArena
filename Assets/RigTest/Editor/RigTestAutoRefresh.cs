using System.IO;
using UnityEditor;
using UnityEngine;

// Aseprite에서 저장 → Unity 창을 클릭하지 않아도 자동으로 다시 임포트하고 RotSprite를 다시 굽는다.
// 플레이 중에도 동작하므로 걷는 모습을 보면서 도트를 고칠 수 있다.
[InitializeOnLoad]
public static class RigTestAutoRefresh
{
    private const string WatchFolder = "Assets/RigTest";
    private const double Debounce = 0.3; // 저장 직후 파일이 아직 쓰이는 중일 수 있어 잠깐 기다림

    private static readonly FileSystemWatcher watcher;
    private static volatile bool changed;
    private static double changedAt;

    static RigTestAutoRefresh()
    {
        if (!Directory.Exists(WatchFolder)) return;

        watcher = new FileSystemWatcher(Path.GetFullPath(WatchFolder), "*.aseprite")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Renamed += OnFileChanged;
        watcher.EnableRaisingEvents = true;

        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += () => watcher.Dispose();
    }

    // 감시 스레드에서 호출됨 → 메인 스레드(Update)에서 처리
    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        changed = true;
    }

    private static void Update()
    {
        if (!changed) return;
        if (changedAt == 0) changedAt = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - changedAt < Debounce) return;

        changed = false;
        changedAt = 0;
        AssetDatabase.Refresh();
    }

    // 캐릭터 aseprite가 다시 임포트되면 RotSprite 자동 굽기
    private class Postprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (path != RotSpriteBaker.CharacterPath) continue;
                EditorApplication.delayCall += () => RotSpriteBaker.Bake(false);
                return;
            }
        }
    }
}
