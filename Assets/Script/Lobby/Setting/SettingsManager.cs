using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BackEnd;
using UnityEngine.Audio;

public class SettingsManager : MonoBehaviour
{
    [Header("오디오 믹서")]
    public AudioMixer audioMixer; // 반드시 할당, 할당 안하면 볼륨 적용 무시

    [Header("FPS")]
    public Toggle toggleFPS_30;
    public Toggle toggleFPS_60;
    public Toggle toggleFPS_120;

    [Header("볼륨 슬라이더")]
    public Slider sliderBGM;
    public Slider sliderSFX;
    public Slider sliderMaster;

    [Header("저장 버튼")]
    public Button saveButton;

    // 플레이어프랩스 키
    private const string PREF_FPS = "SETTINGS_FPS";
    private const string PREF_BGM = "SETTINGS_BGM";
    private const string PREF_SFX = "SETTINGS_SFX";
    private const string PREF_MASTER = "SETTINGS_MASTER";

    private const string SETTINGS_TABLE = "Settings";  // BackEnd 설정 테이블 이름
    private string settingsInDate = "";

    void Awake()
    {
        // 저장 버튼 안전 바인딩
        if (saveButton != null)
            saveButton.onClick.AddListener(OnSaveButtonClicked);

        // ✅ 슬라이더 값 바뀔 때마다 실시간 적용
        if (sliderBGM != null) sliderBGM.onValueChanged.AddListener(val => ApplySettings());
        if (sliderSFX != null) sliderSFX.onValueChanged.AddListener(val => ApplySettings());
        if (sliderMaster != null) sliderMaster.onValueChanged.AddListener(val => ApplySettings());

        // ✅ FPS 토글도 실시간 적용
        if (toggleFPS_30 != null) toggleFPS_30.onValueChanged.AddListener(val => { if (val) ApplySettings(); });
        if (toggleFPS_60 != null) toggleFPS_60.onValueChanged.AddListener(val => { if (val) ApplySettings(); });
        if (toggleFPS_120 != null) toggleFPS_120.onValueChanged.AddListener(val => { if (val) ApplySettings(); });

        // 첫 실행(혹은 씬 전환) 시 저장된 값을 우선 불러와 적용
        LoadSettingsFromPlayerPrefs();
        ApplySettings();
    }

    void Start()
    {
        // 서버에서 불러오기가 성공하면 그 값으로 갱신 (없으면 유지)
        LoadSettingsFromServer();
    }

    /// <summary>
    /// 1. 기기에서 저장된 설정 불러오기
    /// </summary>
    void LoadSettingsFromPlayerPrefs()
    {
        int fps = PlayerPrefs.GetInt(PREF_FPS, 60);
        float bgm = PlayerPrefs.GetFloat(PREF_BGM, 1.0f);
        float sfx = PlayerPrefs.GetFloat(PREF_SFX, 1.0f);
        float master = PlayerPrefs.GetFloat(PREF_MASTER, 1.0f);

        // FPS UI
        if (toggleFPS_30 != null) toggleFPS_30.isOn = (fps == 30);
        if (toggleFPS_60 != null) toggleFPS_60.isOn = (fps == 60);
        if (toggleFPS_120 != null) toggleFPS_120.isOn = (fps == 120);

        // 볼륨 슬라이더 UI
        if (sliderBGM != null) sliderBGM.value = bgm;
        if (sliderSFX != null) sliderSFX.value = sfx;
        if (sliderMaster != null) sliderMaster.value = master;
    }

    /// <summary>
    /// 2. 서버에서 설정 불러오기, 성공시 UI/로컬 모두 갱신
    /// </summary>
    void LoadSettingsFromServer()
    {
        Backend.GameData.GetMyData(SETTINGS_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess() && result.FlattenRows().Count > 0)
            {
                var row = result.FlattenRows()[0];
                settingsInDate = row["inDate"].ToString();

                // FPS
                int fpsValue = row.ContainsKey("fps") ? int.Parse(row["fps"].ToString()) : 60;
                SetToggleOn(toggleFPS_30, fpsValue == 30);
                SetToggleOn(toggleFPS_60, fpsValue == 60);
                SetToggleOn(toggleFPS_120, fpsValue == 120);

                // 볼륨
                float bgm = row.ContainsKey("bgmVolume") ? float.Parse(row["bgmVolume"].ToString()) : 1.0f;
                float sfx = row.ContainsKey("sfxVolume") ? float.Parse(row["sfxVolume"].ToString()) : 1.0f;
                float master = row.ContainsKey("masterVolume") ? float.Parse(row["masterVolume"].ToString()) : 1.0f;
                SetSliderValue(sliderBGM, bgm);
                SetSliderValue(sliderSFX, sfx);
                SetSliderValue(sliderMaster, master);

                // 불러온 값으로 로컬에도 저장, 실제 적용
                SaveSettingsToPlayerPrefs(fpsValue, bgm, sfx, master);
                ApplySettings();
            }
            else
            {
                // 서버에 데이터 없으면 로컬값만 사용, 서버에 새로 등록
                InsertDefaultSettingsToServer();
            }
        });
    }

    void InsertDefaultSettingsToServer()
    {
        Param param = new Param();
        param.Add("fps", GetSelectedFPS());
        param.Add("bgmVolume", sliderBGM != null ? sliderBGM.value : 1.0f);
        param.Add("sfxVolume", sliderSFX != null ? sliderSFX.value : 1.0f);
        param.Add("masterVolume", sliderMaster != null ? sliderMaster.value : 1.0f);

        Backend.GameData.Insert(SETTINGS_TABLE, param, result =>
        {
            if (result.IsSuccess())
            {
                settingsInDate = result.GetInDate();
            }
        });
    }

    /// <summary>
    /// 3. Save 버튼 클릭 시 서버+로컬 저장
    /// </summary>
    void OnSaveButtonClicked()
    {
        int fps = GetSelectedFPS();
        float bgm = sliderBGM != null ? sliderBGM.value : 1.0f;
        float sfx = sliderSFX != null ? sliderSFX.value : 1.0f;
        float master = sliderMaster != null ? sliderMaster.value : 1.0f;

        // 1) PlayerPrefs 저장
        SaveSettingsToPlayerPrefs(fps, bgm, sfx, master);

        // 2) 서버 저장
        Param param = new Param();
        param.Add("fps", fps);
        param.Add("bgmVolume", bgm);
        param.Add("sfxVolume", sfx);
        param.Add("masterVolume", master);

        string ownerInDate = Backend.UserInDate;
        if (!string.IsNullOrEmpty(settingsInDate))
        {
            Backend.GameData.UpdateV2(SETTINGS_TABLE, settingsInDate, ownerInDate, param);
        }
        else
        {
            Backend.GameData.Insert(SETTINGS_TABLE, param, result => {
                if (result.IsSuccess()) settingsInDate = result.GetInDate();
            });
        }

        // 3) 즉시 적용
        ApplySettings();
    }

    /// <summary>
    /// 4. 실제 게임 적용
    /// </summary>
    void ApplySettings()
    {
        int fps = GetSelectedFPS();
        float bgm = sliderBGM != null ? sliderBGM.value : 1.0f;
        float sfx = sliderSFX != null ? sliderSFX.value : 1.0f;
        float master = sliderMaster != null ? sliderMaster.value : 1.0f;

        // FPS 적용
        Application.targetFrameRate = fps;

        // 오디오 믹서 볼륨 적용
        // 슬라이더 0~1 → dB로 변환(-80~0)
        SetMixerVolume("BGMVolume", bgm);
        SetMixerVolume("SFXVolume", sfx);
        SetMixerVolume("MasterVolume", master);
    }

    void SetMixerVolume(string param, float value)
    {
        if (audioMixer != null)
        {
            float dB = Mathf.Log10(Mathf.Clamp(value, 0.0001f, 1.0f)) * 20f;
            audioMixer.SetFloat(param, dB);
        }
    }

    int GetSelectedFPS()
    {
        if (toggleFPS_30 != null && toggleFPS_30.isOn) return 30;
        if (toggleFPS_60 != null && toggleFPS_60.isOn) return 60;
        if (toggleFPS_120 != null && toggleFPS_120.isOn) return 120;
        return 60;
    }

    void SaveSettingsToPlayerPrefs(int fps, float bgm, float sfx, float master)
    {
        PlayerPrefs.SetInt(PREF_FPS, fps);
        PlayerPrefs.SetFloat(PREF_BGM, bgm);
        PlayerPrefs.SetFloat(PREF_SFX, sfx);
        PlayerPrefs.SetFloat(PREF_MASTER, master);
        PlayerPrefs.Save();
    }

    // Null-safe helper
    void SetToggleOn(Toggle toggle, bool on)
    {
        if (toggle != null) toggle.isOn = on;
    }
    void SetSliderValue(Slider slider, float value)
    {
        if (slider != null) slider.value = value;
    }
}
