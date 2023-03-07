﻿using Intersect.Client.Core;
using Intersect.Client.Core.Controls;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Gwen;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.Framework.Gwen.Control.EventArguments;
using Intersect.Client.General;
using Intersect.Client.Localization;
using Intersect.Client.Networking;
using Intersect.Configuration;
using Intersect.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Intersect.Client.Interface.Game
{

    public class EventResponse
    {
        public string Response { get; set; }
        public byte Index { get; set; }
    }

    public class EventWindow : Base
    {
        private readonly long TypewriterSkipInteractDelay = ClientConfiguration.Instance.TypewriterResponseDelay;

        private ScrollControl mEventDialogArea;

        private ScrollControl mEventDialogAreaNoFace;

        private RichLabel mEventDialogLabel;

        private RichLabel mEventDialogLabelNoFace;

        private Label mEventDialogLabelNoFaceTemplate;

        private Label mEventDialogLabelTemplate;

        //Window Controls
        private ImagePanel mEventDialogWindow;

        private ImagePanel mEventFace;

        private Button mEventResponse1;

        private Button mEventResponse2;

        private Button mEventResponse3;

        private Button mEventResponse4;

        private Label mSpeakerLabel;

        private List<EventResponse> Responses;

        private bool Typewriting = false;

        private Typewriter Writer;

        private bool HasSpeaker { get; set; } = false;

        //Init
        public EventWindow(Canvas gameCanvas)
        {
            //Event Dialog Window
            mEventDialogWindow = new ImagePanel(gameCanvas, "EventDialogueWindow");
            mEventDialogWindow.Hide();
            mEventDialogWindow.Clicked += Window_Clicked;
            Interface.InputBlockingElements.Add(mEventDialogWindow);

            mEventFace = new ImagePanel(mEventDialogWindow, "EventFacePanel");

            mEventDialogArea = new ScrollControl(mEventDialogWindow, "EventDialogArea");
            mEventDialogLabelTemplate = new Label(mEventDialogArea, "EventDialogLabel");
            mEventDialogLabel = new RichLabel(mEventDialogArea);

            mEventDialogAreaNoFace = new ScrollControl(mEventDialogWindow, "EventDialogAreaNoFace");
            mEventDialogLabelNoFaceTemplate = new Label(mEventDialogAreaNoFace, "EventDialogLabel");
            mEventDialogLabelNoFace = new RichLabel(mEventDialogAreaNoFace);

            mSpeakerLabel = new Label(mEventDialogWindow, "SpeakerLabel");

            mEventResponse1 = new Button(mEventDialogWindow, "EventResponse1");
            mEventResponse1.Clicked += EventResponse1_Clicked;

            mEventResponse2 = new Button(mEventDialogWindow, "EventResponse2");
            mEventResponse2.Clicked += EventResponse2_Clicked;

            mEventResponse3 = new Button(mEventDialogWindow, "EventResponse3");
            mEventResponse3.Clicked += EventResponse3_Clicked;

            mEventResponse4 = new Button(mEventDialogWindow, "EventResponse4");
            mEventResponse4.Clicked += EventResponse4_Clicked;

            Responses = new List<EventResponse>();

            Writer = new Typewriter();
        }

        private void Window_Clicked(Base sender, ClickedEventArgs arguments)
        {
            SkipTypewriting();
        }

        public void SetResponses(string response1, string response2, string response3, string response4)
        {
            var responses = new List<string> {
                            response1, response2,
                            response3, response4
                        };

            Responses.Clear();
            byte idx = 1;
            foreach (var response in responses)
            {
                var eventResponse = new EventResponse();
                eventResponse.Response = response;
                eventResponse.Index = idx;
                Responses.Add(eventResponse);
                idx++;
            }

            Responses = Responses.Where(response => !string.IsNullOrEmpty(response.Response)).ToList();
        }

        //Update
        public void Update()
        {
            if (mEventDialogWindow.IsHidden)
            {
                Interface.InputBlockingElements.Remove(this);
            }
            else
            {
                if (!Interface.InputBlockingElements.Contains(this))
                {
                    Interface.InputBlockingElements.Add(this);
                }
            }

            if (Globals.EventDialogs.Count > 0)
            {
                if (mEventDialogWindow.IsHidden)
                {
                    base.Show();
                    mEventDialogWindow.Show();
                    mEventDialogWindow.MakeModal();
                    mEventDialogArea.ScrollToTop();
                    mEventDialogWindow.BringToFront();
                    var faceTex = Globals.ContentManager.GetTexture(
                        GameContentManager.TextureType.Face, Globals.EventDialogs[0].Face
                    );
                    if (faceTex == null)
                    {
                        // Check for item texture if we can't find a face
                        faceTex = Globals.ContentManager.GetTexture(
                            GameContentManager.TextureType.Item, Globals.EventDialogs[0].Face
                        );
                    }

                    var responseCount = 0;
                    var maxResponse = 1;

                    responseCount = Responses.Count;
                    maxResponse = responseCount == 0 ? 1 : responseCount;

                    mEventResponse1.Name = "";
                    mEventResponse2.Name = "";
                    mEventResponse3.Name = "";
                    mEventResponse4.Name = "";

                    Typewriting = ClientConfiguration.Instance.EnableTypewriting && Globals.Database.TypewriterText;

                    // Determine whether or not this event is a "dialog", with a speaker, to display the event differently.
                    HasSpeaker = false;
                    var prompt = Globals.EventDialogs[0].Prompt;
                    var splitPrompt = prompt.Split(new string[] { ":\r\n" }, StringSplitOptions.None).ToList();
                    if (splitPrompt.Count > 1 && splitPrompt[0].Split(' ').Length <= 4 && splitPrompt[0][0] != '"')
                    {
                        HasSpeaker = true;
                        mSpeakerLabel.Show();
                        mSpeakerLabel.Text = $"{splitPrompt[0].ToUpper()}:";
                        prompt = string.Concat(splitPrompt.Skip(1));
                    }
                    else
                    {
                        mSpeakerLabel.Hide();
                    }

                    switch (maxResponse)
                    {
                        case 1:
                            if (HasSpeaker)
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_1ResponseSpeaker";
                            }
                            else
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_1Response";
                            }
                            
                            mEventResponse1.Name = "Response1Button";

                            break;
                        case 2:
                            if (HasSpeaker)
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_2ResponsesSpeaker";
                            }
                            else
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_2Responses";
                            }
                            
                            mEventResponse1.Name = "Response1Button";
                            mEventResponse2.Name = "Response2Button";

                            break;
                        case 3:
                            if (HasSpeaker)
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_3ResponsesSpeaker";
                            }
                            else
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_3Responses";
                            }
                            
                            mEventResponse1.Name = "Response1Button";
                            mEventResponse2.Name = "Response2Button";
                            mEventResponse3.Name = "Response3Button";

                            break;
                        case 4:
                            if (HasSpeaker)
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_4ResponsesSpeaker";
                            }
                            else
                            {
                                mEventDialogWindow.Name = "EventDialogWindow_4Responses";
                            }
                            
                            mEventResponse1.Name = "Response1Button";
                            mEventResponse2.Name = "Response2Button";
                            mEventResponse3.Name = "Response3Button";
                            mEventResponse4.Name = "Response4Button";

                            break;
                    }

                    mEventDialogWindow.LoadJsonUi(
                        GameContentManager.UI.InGame, Graphics.Renderer.GetResolutionString()
                    );

                    if (faceTex != null)
                    {
                        mEventFace.Show();
                        mEventFace.Texture = faceTex;
                        mEventDialogArea.Show();
                        mEventDialogAreaNoFace.Hide();
                    }
                    else
                    {
                        mEventFace.Hide();
                        mEventDialogArea.Hide();
                        mEventDialogAreaNoFace.Show();
                    }

                    if (responseCount == 0)
                    {
                        mEventResponse1.Show();
                        mEventResponse1.SetText(Strings.EventWindow.Continue);
                        mEventResponse2.Hide();
                        mEventResponse3.Hide();
                        mEventResponse4.Hide();
                    }
                    else
                    {
                        ShowHideResponse(mEventResponse1, 0);
                        ShowHideResponse(mEventResponse2, 1);
                        ShowHideResponse(mEventResponse3, 2);
                        ShowHideResponse(mEventResponse4, 3);
                    }

                    mEventDialogWindow.SetSize(
                        mEventDialogWindow.Texture.GetWidth(), mEventDialogWindow.Texture.GetHeight()
                    );

                    if (faceTex != null)
                    {
                        ShowText(mEventDialogArea, mEventDialogLabel, mEventDialogLabelTemplate, prompt);
                    }
                    else
                    {
                        ShowText(mEventDialogAreaNoFace, mEventDialogLabelNoFace, mEventDialogLabelNoFaceTemplate, prompt);
                    }
                }
                else if (Typewriting)
                {
                    var voiceIdx = Randomization.Next(0, ClientConfiguration.Instance.TypewriterSounds.Count);

                    Writer.Write(ClientConfiguration.Instance.TypewriterSounds.ElementAtOrDefault(voiceIdx));
                    if (Writer.Done)
                    {
                        mEventResponse1.Show();
                        ShowHideResponse(mEventResponse2, 1);
                        ShowHideResponse(mEventResponse3, 2);
                        ShowHideResponse(mEventResponse4, 3);

                        mEventResponse1.IsDisabled = Timing.Global.Milliseconds - Writer.DoneAt < TypewriterSkipInteractDelay;
                        mEventResponse2.IsDisabled = Timing.Global.Milliseconds - Writer.DoneAt < TypewriterSkipInteractDelay;
                        mEventResponse3.IsDisabled = Timing.Global.Milliseconds - Writer.DoneAt < TypewriterSkipInteractDelay;
                        mEventResponse4.IsDisabled = Timing.Global.Milliseconds - Writer.DoneAt < TypewriterSkipInteractDelay;
                    }
                }
            }
        }

        public void ShowText(ScrollControl dialogArea, RichLabel label, Label labelTemplate, string prompt)
        {
            label.ClearText();
            label.Width = dialogArea.Width - dialogArea.GetVerticalScrollBar().Width;

            label.AddText(prompt, labelTemplate);

            label.SizeToChildren(false, true);
            if (Typewriting)
            {
                // Do this _after_ sizing so we have lines broken up
                Writer.Initialize(label.Labels);
                mEventResponse1.Hide();
                mEventResponse2.Hide();
                mEventResponse3.Hide();
                mEventResponse4.Hide();
            }
            mEventDialogAreaNoFace.ScrollToTop();
        }

        public void ShowHideResponse(Button responseButton, int idx)
        {
            if (Responses.Count >= idx + 1 && !string.IsNullOrEmpty(Responses[idx].Response))
            {
                responseButton.Show();
                responseButton.SetText(Responses[idx].Response);
                responseButton.UserData = Responses[idx].Index;
            }
            else
            {
                responseButton.Hide();
            }
        }

        //Input Handlers
        void EventResponse4_Clicked(Base sender, ClickedEventArgs arguments)
        {
            var ed = Globals.EventDialogs[0];
            if (ed.ResponseSent != 0)
            {
                return;
            }

            PacketSender.SendEventResponse(Responses[3].Index, ed);
            mEventDialogWindow.RemoveModal();
            mEventDialogWindow.IsHidden = true;
            ed.ResponseSent = 1;
            base.Hide();
        }

        void EventResponse3_Clicked(Base sender, ClickedEventArgs arguments)
        {
            var ed = Globals.EventDialogs[0];
            if (ed.ResponseSent != 0)
            {
                return;
            }

            PacketSender.SendEventResponse(Responses[2].Index, ed);
            mEventDialogWindow.RemoveModal();
            mEventDialogWindow.IsHidden = true;
            ed.ResponseSent = 1;
            base.Hide();
        }

        void EventResponse2_Clicked(Base sender, ClickedEventArgs arguments)
        {
            var ed = Globals.EventDialogs[0];
            if (ed.ResponseSent != 0)
            {
                return;
            }

            PacketSender.SendEventResponse(Responses[1].Index, ed);
            mEventDialogWindow.RemoveModal();
            mEventDialogWindow.IsHidden = true;
            ed.ResponseSent = 1;
            base.Hide();
        }

        public void EventResponse1_Clicked(Base sender, ClickedEventArgs arguments)
        {
            var ed = Globals.EventDialogs[0];
            if (ed.ResponseSent != 0)
            {
                return;
            }

            if (Responses.Count > 0)
            {
                PacketSender.SendEventResponse(Responses[0].Index, ed);
            }
            else
            {
                PacketSender.SendEventResponse(0, ed);
            }
            mEventDialogWindow.RemoveModal();
            mEventDialogWindow.IsHidden = true;
            ed.ResponseSent = 1;
            base.Hide();
        }

        public void SkipTypewriting()
        {
            if (Writer?.Done ?? true)
            {
                return;
            }

            Writer.End();
        }
    }

    sealed class Typewriter
    {
        readonly long TypingSpeed = ClientConfiguration.Instance.TypewriterLetterSpeed;
        readonly long FullStopSpeed = ClientConfiguration.Instance.TypewriterFullstopSpeed;
        readonly long PartialStopSpeed = ClientConfiguration.Instance.TypewriterPartialstopSpeed;

        private List<Label> Labels { get; set; }
        private string[] Lines { get; set; }
        private Label CurrentLabel => Labels.ElementAtOrDefault(LineIdx);
        private string CurrentLine => Lines.ElementAtOrDefault(LineIdx);
        private int LineIdx { get; set; }
        private int CharIdx { get; set; }
        private char? LastChar { get; set; }
        private long LastUpdateTime;
        private long LastPauseTime { get; set; }

        public bool Done { get; private set; }

        public long DoneAt { get; set; }

        public void CarriageReturn()
        {
            LineIdx++;
            if (LineIdx >= Lines.Length)
            {
                End();
                return;
            }
            CharIdx = 0;
            LastChar = null;
        }

        public void Initialize(List<Label> labels)
        {
            LastUpdateTime = Timing.Global.Milliseconds;
            Labels = labels;
            Lines = Labels.Select(l => l.Text).ToArray();
            Labels.ForEach(l => l.SetText("")); // Clear the text from the labels. The writer is the captain now
            LineIdx = 0;
            CharIdx = 0;
            LastChar = null;
            Done = false;
        }

        public void Write(string voice)
        {
            if (Done)
            {
                return;
            }

            if (string.IsNullOrEmpty(CurrentLine) || CurrentLabel == default)
            {
                End();
                return;
            }

            if (Timing.Global.Milliseconds < LastUpdateTime)
            {
                return;
            }

            if (!string.IsNullOrEmpty(voice) && CharIdx % ClientConfiguration.Instance.TypewriterSoundFrequency == 0)
            {
                Audio.StopAllGameSoundsOf("typewriter.wav");
                Audio.StopAllGameSoundsOf("typewriter_high.wav");
                Audio.AddGameSound(voice ?? string.Empty, false);
            }

            CharIdx++;
            if (CharIdx >= CurrentLine.Length)
            {
                CurrentLabel?.SetText(CurrentLine);
                CarriageReturn();
                return;
            }

            LastChar = CurrentLine[CharIdx - 1];

            var written = CurrentLine?.Substring(0, CharIdx) ?? string.Empty;
            CurrentLabel?.SetText(written);

            LastUpdateTime = Timing.Global.Milliseconds + GetTypingSpeed();
        }

        private long GetTypingSpeed()
        {
            if (LastChar == null)
            {
                return TypingSpeed;
            }

            var currentChar = CurrentLine.ElementAtOrDefault(CharIdx);
            if ((LastChar == '.' || LastChar == '?' || LastChar == '!' || LastChar == ':') && currentChar != LastChar)
            {
                return FullStopSpeed;
            }
            if ((LastChar == '-' || LastChar == ',' || LastChar == '-') && currentChar != LastChar)
            {
                return PartialStopSpeed;
            }

            return TypingSpeed;
        }

        public void End()
        {
            if (Done || (Lines?.Length ?? 0) == 0)
            {
                return;
            }

            for (var i = 0; i < Lines.Length; i++)
            {
                if (i >= Labels.Count)
                {
                    continue;
                }

                Labels[i].SetText(Lines[i]);
            }

            Done = true;
            DoneAt = Timing.Global.Milliseconds;
        }
    }
}
