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

        public void Awake()
        {
            Instance = this;
            
        }

        public void Start()
        {
            Locator.GetPromptManager().AddScreenPrompt(_wakePrompt);
            ModHelper.Console.WriteLine("[DozeAnywhere] Loaded!", MessageType.Success);

            // Ignore early wakeup messages for the first 30 frames
            _ignoreWakeFramesUntil = Time.frameCount + 30;

            GlobalMessenger.AddListener("WakeUp", new Callback(OnWakeEvent));

            new Harmony("Tonecas.DozeAnywhere").PatchAll(Assembly.GetExecutingAssembly());
        }


        public override void SetupPauseMenu(IPauseMenuManager pauseMenu)
        {
            base.SetupPauseMenu(pauseMenu);

            pauseMenu.MakeSimpleButton("DOZE OFF", 3, true).OnSubmitAction += () =>
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
                if (_wakePrompt == null)
                {
                    _wakePrompt = new ScreenPrompt(InputLibrary.interact, UITextLibrary.GetString(UITextType.WakeUpPrompt), 0, ScreenPrompt.DisplayState.Normal, false);
                    Locator.GetPromptManager().AddScreenPrompt(_wakePrompt, PromptPosition.Center);
                }
                _wakePrompt.SetVisibility(true);
                // Player wakes up by pressing any interact
                if (OWInput.IsNewlyPressed(InputLibrary.interact, InputMode.All))
                {
                    StopDozingOff();
                    return;
                }

                // Stop if near loop end (value equal to campfire's)
                if (TimeLoop.GetSecondsRemaining() < 85f)
                {
                    StopDozingOff();
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
                return;
            }

            ModHelper.Console.WriteLine("[DozeAnywhere] Player is dozing off...");
            StartDozingOff();
        }

        private bool CanDozeOff()
        {
            return TimeLoop.IsTimeFlowing() && TimeLoop.GetSecondsRemaining() > 85f;
        }

        private void StartDozingOff()
        {
            if (_isSleeping)
                return;

            _isSleeping = true;

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

        public void StopDozingOff()
        {
            if (!_isSleeping)
                return;

            _isSleeping = false;

            // Stop fast forward
            if (_isFastForwarding)
            {
                _isFastForwarding = false;
                _wakePrompt.SetVisibility(false);

                Locator.GetPlayerCamera().enabled = true;

                OWTime.SetTimeScale(1f);
                OWTime.SetMaxDeltaTime(0.0666667f);

                GlobalMessenger.FireEvent("EndFastForward");
            }

            // Open Eyes
            Locator.GetPlayerCamera().GetComponent<PlayerCameraEffectController>()
                .OpenEyes(1f, false);

            // Audio cleanup
            Locator.GetAudioMixer().UnmixSleepAtCampfire(3f);
            Locator.GetPlayerAudioController().OnStopSleepingAtCampfire(true, false);

            // Controls back to normal
            OWInput.ChangeInputMode(InputMode.Character);
        }


        private void OnWakeEvent()
        {
            // Ignore wakeup events that happen right after scene load
            if (Time.frameCount < _ignoreWakeFramesUntil)
                return;

            StopDozingOff();
        }


    }
}
