﻿using Intersect.Client.General;
using System;
using static Intersect.Client.Core.Fade;

namespace Intersect.Client.Core
{
    public static class FadeService
    {
        public static bool FadeInstead => (Globals.Database?.FadeTransitions ?? false) || Globals.GameState == GameStates.Intro;

        public static void FadeIn(bool fast = false, Action callback = null)
        {
            if (FadeInstead)
            {
                Fade.FadeIn(fast, callback);
            }
            else
            {
                Wipe.FadeIn(fast, callback);
            }
        }

        public static void SetFade(float amt, bool both = false)
        {
            if (FadeInstead || both)
            {
                Fade.FadeAmt = amt;
                Fade.CurrentAction = FadeType.None;
                if (!both)
                {
                    return;
                }
            }
            float maxWidth = Graphics.CurrentView.Width / 2;
            Wipe.FadeAmt = (maxWidth / 255) * amt;
            Wipe.InvertFadeAmt = maxWidth - Wipe.FadeAmt;
            Wipe.CurrentAction = FadeType.None;
        }

        public static void FadeOut(bool alertServerWhenFaded = false, bool fast = false, Action callback = null)
        {
            if (FadeInstead)
            {
                Fade.FadeOut(alertServerWhenFaded, fast, callback);
            }
            else
            {
                Wipe.FadeOut(alertServerWhenFaded, fast, callback);
            }
        }

        public static bool DoneFading()
        {
            if (FadeInstead)
            {
                return Fade.DoneFading();
            }
            else
            {
                return Wipe.DoneFading();
            }
        }

        public static FadeType FadeType
        {
            get
            {
                if (FadeInstead)
                {
                    return Fade.CurrentAction;
                }
                else
                {
                    return Wipe.CurrentAction;
                }
            }
        }
    }
}
