﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Kanro.MaidTranslate.Hook;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kanro.MaidTranslate
{
    [BepInPlugin(GUID: "YotogiSubtitles", Name: "Kanro.YotogiSubtitles", Version:"5.0")]
    public class YotogiSubtitles : BaseUnityPlugin
    {
        public YotogiSubtitles()
        {
            SubtitleConfig = new SubtitleConfig(Config);

            Logger.Log(LogLevel.Debug, $"Subtitles {(SubtitleConfig.EnableSubtitle ? "Enabled" : "Disabled")}.");
            Logger.Log(LogLevel.Debug, $"Max subtitles {SubtitleConfig.MaxSubtitle }.");
        }

        private bool isInVR;

        private SubtitleConfig SubtitleConfig { get; }

        private Scene CurrentScene { get; set; }

        private GUIStyle BoxStyle { get; set; }

        private Dictionary<string, string> VoiceCache { get; } = new Dictionary<string, string>();

        public Dictionary<string, AudioSource> PlayingAudioSource { get; } = new Dictionary<string, AudioSource>();

        private LinkedList<string> PlayingAudio { get; } = new LinkedList<string>();

        private HashSet<string> StoppedAudio { get; } = new HashSet<string>();

        private Rect _lastArea;

        private float _firstCommandUnitY;

        private void OnPlaySound(object sender, EventArgs e)
        {
            try
            {
                if (!SubtitleConfig.EnableSubtitle)
                {
                    return;
                }

                if (CurrentScene.name != "SceneYotogi")
                {
                    return;
                }

                var audioManager = sender as AudioSourceMgr;

                if (audioManager == null)
                {
                    return;
                }

                if (audioManager.SoundType != AudioSourceMgr.Type.VoiceHeroine)
                {
                    return;
                }

                lock (PlayingAudioSource)
                {
                    Logger.Log(LogLevel.Debug, $"Now playing {audioManager.FileName}");
                    PlayingAudioSource[audioManager.FileName] = audioManager.audiosource;
                    if (PlayingAudio.Contains(audioManager.FileName))
                    {
                        PlayingAudio.Remove(audioManager.FileName);
                    }
                    PlayingAudio.AddFirst(audioManager.FileName);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex);
                throw;
            }
        }

        private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            CurrentScene = arg0;
            lock (PlayingAudioSource)
            {
                VoiceCache.Clear();
                PlayingAudioSource.Clear();
                StoppedAudio.Clear();
            }
        }

        public void Start()
        {
            try
            {
                HookCenter.YotogiKagHitRet += OnYotogiKagHitRet;
                HookCenter.PlaySound += OnPlaySound;
                SceneManager.sceneLoaded += OnSceneLoaded;
                isInVR = Environment.GetCommandLineArgs().Any(s => s.ToLower().Contains("/vr"));
                Logger.Log(LogLevel.Debug, $"Initialized");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex);
                throw;
            }
        }

        private void OnYotogiKagHitRet(object sender, YotogiKagHitRetEventArgs e)
        {
            try
            {
                if (!SubtitleConfig.EnableSubtitle)
                {
                    return;
                }

                if (CurrentScene.name != "SceneYotogi")
                {
                    return;
                }

                var text = string.IsNullOrEmpty(e.Translation) ? e.Text : e.Translation;
                if (string.IsNullOrEmpty(text)) return;

                VoiceCache[e.Voice] = text;
                Logger.Log(LogLevel.Debug, $"Text ({text}) for voice '{e.Voice}' loaded.");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex);
                throw;
            }
        }

        private void OnDestroy()
        {
            HookCenter.YotogiKagHitRet -= OnYotogiKagHitRet;
            HookCenter.PlaySound -= OnPlaySound;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnKeyShort()
        {
            if (Event.current.type != EventType.KeyUp) return;
            if (!Event.current.alt) return;

            switch (Event.current.keyCode)
            {
                case KeyCode.F1:
                    SubtitleConfig.Reload();
                    Logger.Log(LogLevel.Info, $"Config reloaded.");
                    Logger.Log(LogLevel.Info, $"Subtitles {(SubtitleConfig.EnableSubtitle ? "Enabled" : "Disabled")}.");
                    Logger.Log(LogLevel.Info, $"Max subtitles {SubtitleConfig.MaxSubtitle }.");
                    break;
                case KeyCode.F2:
                    SubtitleConfig.EnableSubtitle = !SubtitleConfig.EnableSubtitle;
                    Logger.Log(LogLevel.Info, $"Subtitles {(SubtitleConfig.EnableSubtitle ? "Enabled" : "Disabled")}.");
                    break;
                case KeyCode.Keypad1:
                    lock (PlayingAudioSource)
                    {
                        var index = 0;
                        foreach (var audioFileName in PlayingAudio)
                        {
                            var audioSource = PlayingAudioSource[audioFileName];
                            Logger.Log(LogLevel.Debug, $"{index} : Now playing {audioFileName}({audioSource.isPlaying})");
                            index++;
                        }
                    }
                    break;
            }
        }

        public void OnGUI()
        {
            try
            {
                OnKeyShort();

                if (!SubtitleConfig.EnableSubtitle) return;

                if (BoxStyle == null)
                {
                    BoxStyle = new GUIStyle(GUI.skin.box)
                    {
                        wordWrap = true,
                        fontSize = 20
                    };
                }

                if (CurrentScene.name != "SceneYotogi") return;

                var commandUnit = GameObject.Find("CommandUnit");
                if (commandUnit != null)
                {
                    var commandRects = new List<Rect>();

                    foreach (Transform obj in commandUnit.transform)
                    {
                        if (!obj.name.StartsWith("cm:"))
                        {
                            continue;
                        }

                        var component = obj.GetComponent<BoxCollider>();
                        if (component == null)
                        {
                            continue;
                        }
                        var vector = component.transform.TransformPoint(component.center);
                        vector = UICamera.currentCamera.WorldToScreenPoint(vector);
                        var x = component.size.x;
                        var y = component.size.y;

                        var rect = new Rect(vector.x - x / 2f, Screen.height - vector.y - y / 2f, x, y);
                        commandRects.Add(rect);
                    }

                    commandRects.Sort((rect1, rect2) => rect1.yMin.CompareTo(rect2.yMin));

                    if (commandRects.Count > 0)
                    {
                        var last = commandRects.Last();
                        _lastArea.x = last.xMin;
                        _lastArea.y = last.yMax + last.height;
                        _lastArea.width = last.width;
                        _lastArea.height = 1080;
                        _firstCommandUnitY = commandRects.First().yMin;
                    }
                    else
                    {
                        _lastArea.y = _firstCommandUnitY;
                    }
                }

                GUILayout.BeginArea(_lastArea);
                lock (PlayingAudioSource)
                {
                    var rendered = 0;
                    foreach (var audioFileName in PlayingAudio)
                    {
                        if (!PlayingAudioSource.TryGetValue(audioFileName, out var audioSource))
                        {
                            continue;
                        }

                        if (!audioSource.isPlaying)
                        {
                            StoppedAudio.Add(audioFileName);
                            continue;
                        }

                        if (!VoiceCache.ContainsKey(audioFileName)) continue;

                        if (SubtitleConfig.MaxSubtitle > 0 && rendered >= SubtitleConfig.MaxSubtitle) continue;
                        
                        GUILayout.Box(VoiceCache[audioFileName], BoxStyle);
                        rendered++;
                    }

                    foreach (var audioSource in StoppedAudio)
                    {
                        Logger.Log(LogLevel.Debug, $"{audioSource} Stopped.");
                        PlayingAudioSource.Remove(audioSource);
                    }
                    StoppedAudio.Clear();
                }
                GUILayout.EndArea();
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex);
                throw;
            }
        }
    }
}
