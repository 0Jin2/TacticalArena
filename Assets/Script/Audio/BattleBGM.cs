using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BattleBGM : MonoBehaviour
{
    private static BattleBGM instance;
    private AudioSource audioSource;

    [Header("클립 지정")]
    public AudioClip characterSelectClip;
    public AudioClip battleClip;
    public AudioClip resultClip;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = true;
    }

    private void Start()
    {
        PlayCharacterSelect();
    }

    public static BattleBGM Instance => instance;

    public void PlayCharacterSelect()
    {
        PlayClip(characterSelectClip, true);
    }

    public void PlayBattle()
    {
        PlayClip(battleClip, true);
    }

    public void PlayResult()
    {
        PlayClip(resultClip, true);
    }

    private void PlayClip(AudioClip clip, bool loop)
    {
        if (clip == null) return;
        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.loop = loop;
        audioSource.time = 0f;
        audioSource.Play();
    }
}
