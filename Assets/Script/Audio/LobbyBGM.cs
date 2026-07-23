using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class LobbyBGM : MonoBehaviour
{
    private static LobbyBGM instance;
    private AudioSource audioSource;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string name = scene.name;

        if (name == "LoginPage" || name == "LobbyPage")
        {
            if (!audioSource.isPlaying)
            {
                audioSource.time = 0f; // 새로 들어올 땐 항상 처음부터
                audioSource.Play();
            }
        }
        else
        {
            if (audioSource.isPlaying)
                audioSource.Stop();
        }
    }
}
