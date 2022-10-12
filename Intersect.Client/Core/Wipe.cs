﻿using Intersect.Client.General;
using Intersect.Utilities;
using System;

namespace Intersect.Client.Core
{

    public static class Wipe
    {
        private const float STANDARD_FADE_RATE = 500f;
        private const float FAST_FADE_RATE = 500f;

        public enum FadeType
        {

            None = 0,

            In = 1,

            Out = 2,

        }

        private static FadeType sCurrentAction;

        private static FadeType CurrentAction
        {
            get
            {
                return sCurrentAction;
            }
            set {
                sCurrentAction = value;
                if (sCurrentAction == FadeType.None && CompleteCallback != null)
                {
                    CompleteCallback();
                }
            }
        }

        private static float sFadeAmt;

        private static float sInvertFadeAmt = Graphics.CurrentView.Width / 2;

        private static float sFadeRate = STANDARD_FADE_RATE;

        private static long sLastUpdate;

        private static bool sAlertServerWhenFaded;

        private static Action CompleteCallback;

        public static void FadeIn(bool fast = false, Action callback = null)
        {
            sFadeRate = fast ? FAST_FADE_RATE : STANDARD_FADE_RATE;

            CurrentAction = FadeType.In;
            sFadeAmt = Graphics.CurrentView.Width / 2;
            sInvertFadeAmt = 0f;
            sLastUpdate = Timing.Global.Milliseconds;
            CompleteCallback = callback;
        }

        public static void FadeOut(bool alertServerWhenFaded = false, bool fast = false, Action callback = null)
        {
            sFadeRate = fast ? FAST_FADE_RATE : STANDARD_FADE_RATE;
            
            CurrentAction = FadeType.Out;
            sFadeAmt = 0f;
            sInvertFadeAmt = Graphics.CurrentView.Width / 2;
            sLastUpdate = Timing.Global.Milliseconds;
            sAlertServerWhenFaded = alertServerWhenFaded;
            CompleteCallback = callback;
        }

        public static bool DoneFading()
        {
            return CurrentAction == FadeType.None;
        }

        public static float GetFade(bool inverted = false)
        {
            float maxWidth = Graphics.CurrentView.Width / 2;
            int transitionNum = 8;

            var fade = sFadeAmt;
            if (inverted) fade = sInvertFadeAmt;
            for (int i = transitionNum; i >= 1; i--)
            {
                if (fade >= maxWidth / transitionNum * i)
                {
                    return maxWidth / transitionNum * i;
                }
            }

            return 0f;
        }

        public static void Update()
        {
            float maxWidth = Graphics.CurrentView.Width / 2;
            var amountChange = (Timing.Global.Milliseconds - sLastUpdate) / sFadeRate * maxWidth;

            if (CurrentAction == FadeType.In)
            {
                sFadeAmt -= amountChange;
                sInvertFadeAmt += amountChange;

                if (sFadeAmt <= 0f && sInvertFadeAmt >= maxWidth)
                {
                    if (Globals.WaitFade)
                    {
                        Networking.PacketSender.SendFadeFinishPacket();
                    }
                    CurrentAction = FadeType.None;
                    sFadeAmt = 0f;
                    sInvertFadeAmt = maxWidth;
                }
            }
            else if (CurrentAction == FadeType.Out)
            {
                sFadeAmt += amountChange;
                sInvertFadeAmt -= amountChange;

                if (sFadeAmt >= maxWidth && sInvertFadeAmt <= 0f)
                {
                    CurrentAction = FadeType.None;
                    if (Globals.WaitFade)
                    {
                        Networking.PacketSender.SendFadeFinishPacket();
                    }
                    if (sAlertServerWhenFaded)
                    {
                        Networking.PacketSender.SendMapTransitionReady(Globals.futureWarpMapId, Globals.futureWarpX, Globals.futureWarpY, Globals.futureWarpDir, Globals.futureWarpInstanceType);
                    }

                    sAlertServerWhenFaded = false;
                    sFadeAmt = maxWidth;

                    sInvertFadeAmt = 0f;
                }
            }
            
            sLastUpdate = Timing.Global.Milliseconds;
        }

    }

}
