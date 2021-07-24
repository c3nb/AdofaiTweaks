﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdofaiTweaks.Core;
using AdofaiTweaks.Core.Attributes;
using AdofaiTweaks.Strings;
using AdofaiTweaks.Translation;
using HarmonyLib;
using UnityEngine;
using MelonLoader;

namespace AdofaiTweaks
{
    /// <summary>
    /// The main runner of the AdofaiTweaks mod.
    /// </summary>
    public class AdofaiTweaks : MelonMod
    {
        /// <summary>
        /// Whether the tweak is enabled.
        /// </summary>
        public static bool IsEnabled { get; private set; }

        /// <summary>
        /// The game's release number.
        /// </summary>
        public static readonly int ReleaseNumber = (int)AccessTools.Field(typeof(GCNS), "releaseNumber").GetValue(null);

        /// <summary>
        /// GlobalSettings instance.
        /// </summary>
        [SyncTweakSettings]
        public GlobalSettings GlobalSettings { get; set; }

        private List<Type> allTweakTypes;
        private readonly List<TweakRunner> tweakRunners = new List<TweakRunner>();

        private SettingsSynchronizer synchronizer;

        private bool didStart;

        /// <summary>
        /// Runs the initial setup of AdofaiTweaks.
        /// </summary>
        public override void OnApplicationStart() {
            MelonLogger.Msg("OnApplicationStart");
            allTweakTypes =
                AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => t.GetCustomAttribute<RegisterTweakAttribute>() != null)
                    .OrderBy(t => t.Name)
                    .OrderBy(t => t.GetCustomAttribute<RegisterTweakAttribute>().Priority)
                    .ToList();

            synchronizer = new SettingsSynchronizer();

            synchronizer.Load();

            // Register global settings
            synchronizer.Register(typeof(TweakStrings));
            synchronizer.Register(typeof(TweakRunner));
            synchronizer.Register(this);

#if DEBUG
            // TODO: Show some sort of debug message in the in-game settings.
#endif
            // TODO: Hook up OnSaveGUI and OnHideGUI
        }

        /// <summary>
        /// Handler for MelonLoader's OnSceneWasLoaded event.
        /// </summary>
        /// <param name="buildindex">The build index of the loaded scene.</param>
        /// <param name="sceneName">The name of the scene.</param>
        public override void OnSceneWasLoaded(int buildindex, string sceneName) {
            // OnApplicationStart is run too early for Unity to create
            // GameObjects with usable properties, so run the initial enabling
            // of the mod after the first scene is loaded.
            if (!didStart) {
                didStart = true;
                // TODO: Load whether mod is enabled/disabled from settings
                OnToggle(true);
            }
        }

        /// <summary>
        /// Handler for MelonLoader's OnToggle event.
        /// </summary>
        /// <param name="value">
        /// <c>true</c> if the mod is enabled, or <c>false</c> if the mod is
        /// disabled.
        /// </param>
        /// <returns><c>true</c>.</returns>
        private bool OnToggle(bool value) {
            IsEnabled = value;
            if (value) {
                StartTweaks();
            } else {
                StopTweaks();

                // Save all settings
                synchronizer.Save();
            }
            return true;
        }

        /// <summary>
        /// Starts all tweak runners.
        /// </summary>
        private void StartTweaks() {
            HashSet<string> tweakIds = new HashSet<string>();

            // Create runners for all the tweaks
            foreach (Type tweakType in allTweakTypes) {
                // Verify tweaks do not have conflicting IDs
                RegisterTweakAttribute registerAttribute =
                    tweakType.GetCustomAttribute<RegisterTweakAttribute>();
                if (tweakIds.Contains(registerAttribute.Id)) {
                    throw new InvalidOperationException(
                        "Found conflicting tweaks with the ID '{0}', ");
                }

                // Create the tweak
                ConstructorInfo constructor = tweakType.GetConstructor(new Type[] { });
                Tweak tweak = (Tweak)constructor.Invoke(null);
                TweakSettings settings =
                    synchronizer.GetSettingsForType(registerAttribute.SettingsType);

                // Create and register the runner
                TweakRunner runner = new TweakRunner(tweak, settings);
                tweakRunners.Add(runner);
                synchronizer.Register(runner.Tweak);
                synchronizer.Register(runner.TweakMetadata.PatchesType);
            }

            // Sync patch/tweak references with their respective settings
            synchronizer.Sync();

            // Start all runners
            foreach (TweakRunner runner in tweakRunners) {
                runner.Start();
            }
        }

        /// <summary>
        /// Stops all tweak runners.
        /// </summary>
        private void StopTweaks() {
            // Stop all runners
            foreach (TweakRunner runner in tweakRunners) {
                runner.Stop();
                synchronizer.Unregister(runner.Tweak);
                synchronizer.Unregister(runner.TweakMetadata.PatchesType);
            }

            // Clear out all runners
            tweakRunners.Clear();
        }

        /// <summary>
        /// Handler for MelonLoader's OnGUI event. Displays the language chooser
        /// and every tweak's settings GUI.
        /// </summary>
        public override void OnGUI() {
            // TODO: Don't always show the GUI

            // Set some default GUI settings for better layouts
            if (GlobalSettings.Language.IsSymbolLanguage()) {
                GUI.skin.button.font = TweakAssets.SymbolLangNormalFont;
                GUI.skin.label.font = TweakAssets.SymbolLangNormalFont;
                GUI.skin.textArea.font = TweakAssets.SymbolLangNormalFont;
                GUI.skin.textField.font = TweakAssets.SymbolLangNormalFont;
                GUI.skin.toggle.font = TweakAssets.SymbolLangNormalFont;
                GUI.skin.button.fontSize = 15;
                GUI.skin.label.fontSize = 15;
                GUI.skin.textArea.fontSize = 15;
                GUI.skin.textField.fontSize = 15;
                GUI.skin.toggle.fontSize = 15;
            }
            GUI.skin.toggle = new GUIStyle(GUI.skin.toggle) {
                margin = new RectOffset(0, 4, 6, 6),
            };
            GUI.skin.label.wordWrap = false;

            GUILayout.Space(4);

            // Language chooser
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);
            GUILayout.Label(
                TweakStrings.Get(TranslationKeys.Global.GLOBAL_LANGUAGE),
                new GUIStyle(GUI.skin.label) {
                    fontStyle = GlobalSettings.Language.IsSymbolLanguage()
                        ? FontStyle.Normal
                        : FontStyle.Bold,
                    font = GlobalSettings.Language.IsSymbolLanguage()
                        ? TweakAssets.KoreanBoldFont
                        : null,
                });
            foreach (LanguageEnum language in Enum.GetValues(typeof(LanguageEnum))) {
                string langString =
                    TweakStrings.GetForLanguage(TranslationKeys.Global.LANGUAGE_NAME, language);

                // Set special styles for selected and Korean language
                GUIStyle style = new GUIStyle(GUI.skin.button);
                if (language == GlobalSettings.Language) {
                    if (language.IsSymbolLanguage()) {
                        style.font = TweakAssets.KoreanBoldFont;
                        style.fontSize = 15;
                    } else {
                        style.fontStyle = FontStyle.Bold;
                    }
                } else if (language.IsSymbolLanguage()) {
                    style.font = TweakAssets.SymbolLangNormalFont;
                    style.fontSize = 15;
                }

                bool click = GUILayout.Button(langString, style);
                if (click) {
                    GlobalSettings.Language = language;
                    foreach (TweakRunner runner in tweakRunners) {
                        runner.OnLanguageChange();
                    }
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Show each tweak's GUI
            GUILayout.Space(4);
            foreach (TweakRunner runner in tweakRunners) {
                runner.OnGUI();
            }

            // Reset GUI settings to defaults
            GUI.skin.button.font = null;
            GUI.skin.label.font = null;
            GUI.skin.textArea.font = null;
            GUI.skin.textField.font = null;
            GUI.skin.toggle.font = null;
            GUI.skin.button.fontSize = 0;
            GUI.skin.label.fontSize = 0;
            GUI.skin.textArea.fontSize = 0;
            GUI.skin.textField.fontSize = 0;
            GUI.skin.toggle.fontSize = 0;

#if DEBUG
            GUILayout.Label($"<color=#a7a7a7><i>This build is a debug build.\nGame Version: r{ReleaseNumber}\nBuild Date: {GCNS.buildDate}\nCurrent Scene: {ADOBase.sceneName}</i></color>");
#endif
        }

        /// <summary>
        /// Handler for MelonLoader's OnHideGUI event.
        /// </summary>
        private void OnHideGUI() {
            foreach (TweakRunner runner in tweakRunners) {
                runner.OnHideGUI();
            }
            synchronizer.Save();
        }

        /// <summary>
        /// Handler for MelonLoader's OnSaveGUI event.
        /// </summary>
        private void OnSaveGUI() {
            synchronizer.Save();
        }

        /// <summary>
        /// Handler for MelonLoader's OnUpdate event.
        /// </summary>
        public override void OnUpdate() {
            foreach (TweakRunner runner in tweakRunners) {
                runner.OnUpdate(Time.deltaTime);
            }
        }
    }
}
