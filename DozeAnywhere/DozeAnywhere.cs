using System.Reflection;
using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using UnityEngine;

namespace DozeAnywhere
{
    public class DozeAnywhere : ModBehaviour
    {
        public static DozeAnywhere Instance;

        private bool _isSleeping = false;
        private float _fastForwardStart;
        private bool _isFastForwarding = false;

        private int _ignoreWakeFramesUntil = -1;

        private ScreenPrompt _wakePrompt;
        private InputMode _previousInputMode;
        private NotificationData _cannotDozeNotif;

        public void Awake()
        {
            Instance = this;
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), "Tonecas.DozeAnywhere");

        }

        public void Start()
        {
            Locator.GetPromptManager().AddScreenPrompt(_wakePrompt);
            ModHelper.Console.WriteLine("[DozeAnywhere] Loaded!", MessageType.Success);

            // Ignore early wakeup messages for the first 30 frames
            _ignoreWakeFramesUntil = Time.frameCount + 30;

            GlobalMessenger.AddListener("WakeUp", new Callback(OnWakeEvent));
        }

        [HarmonyPatch]
        public class HarmonyPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(DeathManager), nameof(DeathManager.KillPlayer))]
            public static void DeathManager_KillPlayer_Prefix()
            {
                DozeAnywhere ModInstance = DozeAnywhere.Instance;
                if (ModInstance._isFastForwarding)
                {
                    ModInstance.ModHelper.Console.WriteLine("Died while dozing. Stop fastforwarding");
                    ModInstance.StopDozingOff(true);
                }
            }
        }

        
        [HarmonyPatch(typeof(PauseMenuManager), "OnActivateMenu")]
        public class PauseMenu_OpenPatch
        {
            static void Prefix()
            {
                // This runs *before* the pause menu action
                DozeAnywhere.Instance.SaveCurrentInputMode();
            }
        }

        public void SaveCurrentInputMode()
        {
            _previousInputMode = OWInput.GetInputMode();
        }

        public override void SetupPauseMenu(IPauseMenuManager pauseMenu)
        {
            base.SetupPauseMenu(pauseMenu);

            pauseMenu.MakeSimpleButton(UITextLibrary.GetString(UITextType.CampfireDozeOff).ToUpper(), 3, true).OnSubmitAction += () =>
            {
                Locator.GetSceneMenuManager().pauseMenu.OnSkipToNextTimeLoop();
                DozeOff();
            };
        }


        private void Update()
        {
            if (!_isSleeping)
                return;

            // Start fast-forwarding after 3 seconds
            if (!_isFastForwarding && Time.timeSinceLevelLoad > _fastForwardStart)
                StartFastForwarding();

            if (_isFastForwarding)
            {
                _wakePrompt.SetVisibility(OWInput.IsInputMode(InputMode.None) && Time.timeSinceLevelLoad - _fastForwardStart > 3f);
                // Player wakes up by pressing any interact
                if (OWInput.IsNewlyPressed(InputLibrary.interact, InputMode.All))
                {
                    StopDozingOff(false);
                    return;
                }

                // Stop if near loop end (value equal to campfire's)
                if (TimeLoop.GetSecondsRemaining() < 85f)
                {
                    StopDozingOff(false);
                    return;
                }

                // Smoothly ramp up time scale to 10x
                if (!OWTime.IsPaused())
                {
                    float multiplier = Mathf.MoveTowards(
                        OWTime.GetTimeScale(),
                        10f,
                        2f * Time.unscaledDeltaTime
                    );
                    OWTime.SetTimeScale(multiplier);
                }
            }
        }

        private void DozeOff()
        {
            if (!CanDozeOff())
            {
                ModHelper.Console.WriteLine("[DozeAnywhere] Cannot doze off now.");
                if (_cannotDozeNotif == null) _cannotDozeNotif = new NotificationData(NotificationTarget.Player, "Cannot doze at the moment", 3f, true);
                NotificationManager.SharedInstance.PostNotification(_cannotDozeNotif, false);
                OWInput.ChangeInputMode(_previousInputMode);
                return;
            }

            ModHelper.Console.WriteLine("[DozeAnywhere] Player is dozing off...");
            StartDozingOff();
        }

        private bool CanDozeOff()
        {
            return TimeLoop.IsTimeFlowing() && TimeLoop.GetSecondsRemaining() > 85f;
            return TimeLoop.IsTimeFlowing() && TimeLoop.GetSecondsRemaining() > 85f && LoadManager.GetCurrentScene() != OWScene.EyeOfTheUniverse;
        }

        private void StartDozingOff()
        {
            if (_isSleeping)
                return;

            _isSleeping = true;

            if (_wakePrompt == null)
            {
                _wakePrompt = new ScreenPrompt(InputLibrary.interact, UITextLibrary.GetString(UITextType.WakeUpPrompt), 0, ScreenPrompt.DisplayState.Normal, false);
            }

            Locator.GetPromptManager().AddScreenPrompt(_wakePrompt, PromptPosition.Center, false);
            _wakePrompt.SetVisibility(false);

            // Unequip tools
            Locator.GetToolModeSwapper().UnequipTool();

            // Close eyes
            var camEffect = Locator.GetPlayerCamera().GetComponent<PlayerCameraEffectController>();
            camEffect.CloseEyes(3f);

            // Audio effects
            Locator.GetAudioMixer().MixSleepAtCampfire(3f);
            Locator.GetPlayerAudioController().OnStartSleepingAtCampfire(false);

            // Flashlight safety check
            var flashlight = Locator.GetFlashlight();
            if (flashlight != null)
                flashlight.TurnOff(false);

            // Disable player control
            OWInput.ChangeInputMode(InputMode.None);

            // Fast forward starts in 3 seconds (Add customizable value later?)
            _fastForwardStart = Time.timeSinceLevelLoad + 3f;
        }

        private void StartFastForwarding()
        {
            _isFastForwarding = true;

            Locator.GetPlayerCamera().enabled = false;

            // Lower max delta time for stability
            OWTime.SetMaxDeltaTime(0.03333333f);

            GlobalMessenger.FireEvent("StartFastForward");
        }

        private void StopFastForwarding()
        {
            _isFastForwarding = false;

            OWTime.SetTimeScale(1f);
            OWTime.SetMaxDeltaTime(0.0666667f);

            GlobalMessenger.FireEvent("EndFastForward");
        }

        public void StopDozingOff(bool suddenDamage)
        {
            if (!_isSleeping)
                return;

            _isSleeping = false;

            // Stop fast forward
            if (_isFastForwarding)
            {
                StopFastForwarding();
            }
            _wakePrompt.SetVisibility(false);
            Locator.GetPromptManager().RemoveScreenPrompt(this._wakePrompt);

            Locator.GetPlayerCamera().enabled = true;

            // Open Eyes
            Locator.GetPlayerCamera().GetComponent<PlayerCameraEffectController>()
                .OpenEyes(1f, false);

            // Audio cleanup
            Locator.GetAudioMixer().UnmixSleepAtCampfire(3f);
            Locator.GetPlayerAudioController().OnStopSleepingAtCampfire(true, suddenDamage);

            // Controls back to normal
            OWInput.ChangeInputMode(_previousInputMode);
        }


        private void OnWakeEvent()
        {
            // Ignore wakeup events that happen right after scene load
            if (Time.frameCount < _ignoreWakeFramesUntil)
                return;

            StopDozingOff(false);
        }


    }
}
