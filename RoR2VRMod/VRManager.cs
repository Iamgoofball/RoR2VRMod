﻿using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Events;

namespace VRMod
{
    internal static class VRManager
    {
        private static GameObject instance;
        public static void Init()
        {
            instance = new GameObject("VRManager");

            instance.AddComponent<MPEventSystemProvider>().fallBackToMainEventSystem = true;

            InputResponse inputResponse = instance.AddComponent<InputResponse>();
            inputResponse.inputActionNames = new string[] { "RecenterHMD" };
            inputResponse.onPress = new UnityEvent();
            inputResponse.onPress.AddListener(Recenter);

            Object.DontDestroyOnLoad(instance);
        }

        private static void Recenter()
        {
            if (!Run.instance || PauseManager.isPaused)
                UnityEngine.XR.InputTracking.Recenter();
        }
    }
}
