using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerAudio : 캐릭터 하나가 쓰는 효과음 재생기
//   발사/점프/재장전/피격/스킬 소리를 모두 여기로 재생한다.
//   AudioMixerGroup 마다 AudioSource 를 따로 만들어, 소리끼리 믹서 설정이 섞이지 않게 한다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerAudio : MonoBehaviour
{
    [Header("재생 설정")]
    [Tooltip("믹서를 지정하지 않은 소리가 사용할 기본 믹서 (비워도 됨)")]
    public AudioMixerGroup defaultMixer;
    [Range(0f, 1f)] public float volume = 1f;

    private AudioSource defaultSource;
    private readonly Dictionary<AudioMixerGroup, AudioSource> mixedSources = new Dictionary<AudioMixerGroup, AudioSource>();

    private void Awake()
    {
        defaultSource = CreateSource(defaultMixer, true);
    }

    private AudioSource CreateSource(AudioMixerGroup mixer, bool reuseExisting)
    {
        AudioSource source = reuseExisting ? GetComponent<AudioSource>() : null;
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        if (mixer != null) source.outputAudioMixerGroup = mixer;
        return source;
    }

    private AudioSource GetSource(AudioMixerGroup mixer)
    {
        if (mixer == null || mixer == defaultMixer)
        {
            if (defaultSource == null) defaultSource = CreateSource(defaultMixer, true);
            return defaultSource;
        }

        AudioSource source;
        if (!mixedSources.TryGetValue(mixer, out source) || source == null)
        {
            source = CreateSource(mixer, false);
            mixedSources[mixer] = source;
        }
        return source;
    }

    /// <summary>효과음 한 번 재생. pitch 를 올리면 짧은 시간 안에 끝나게 할 수 있다. (재장전 소리)</summary>
    public void PlayOneShot(AudioClip clip, AudioMixerGroup mixer = null, float pitch = 1f)
    {
        if (clip == null) return;
        var source = GetSource(mixer);
        source.pitch = Mathf.Max(0.01f, pitch);
        source.PlayOneShot(clip, volume);
    }
}
