﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

using static libmpv;
using static mpvnet.Global;
using System.Text.RegularExpressions;

namespace mpvnet
{
    public class CorePlayer
    {
        public static string[] VideoTypes { get; set; } = "mkv mp4 avi mov flv mpg webm wmv ts vob 264 265 asf avc avs dav h264 h265 hevc m2t m2ts m2v m4v mpeg mpv mts vpy y4m".Split(' ');
        public static string[] AudioTypes { get; set; } = "mp3 flac m4a mka mp2 ogg opus aac ac3 dts dtshd dtshr dtsma eac3 mpa mpc thd w64 wav".Split(' ');
        public static string[] ImageTypes { get; set; } = { "jpg", "bmp", "png", "gif", "webp" };
        public static string[] SubtitleTypes { get; } = { "srt", "ass", "idx", "sub", "sup", "ttxt", "txt", "ssa", "smi", "mks" };

        public event Action<mpv_log_level, string> LogMessageAsync; // log-message        MPV_EVENT_LOG_MESSAGE
        public event Action<mpv_end_file_reason> EndFileAsync;      // end-file           MPV_EVENT_END_FILE
        public event Action<string[]> ClientMessageAsync;           // client-message     MPV_EVENT_CLIENT_MESSAGE
        public event Action GetPropertyReplyAsync;                  // get-property-reply MPV_EVENT_GET_PROPERTY_REPLY
        public event Action SetPropertyReplyAsync;                  // set-property-reply MPV_EVENT_SET_PROPERTY_REPLY
        public event Action CommandReplyAsync;                      // command-reply      MPV_EVENT_COMMAND_REPLY
        public event Action StartFileAsync;                         // start-file         MPV_EVENT_START_FILE
        public event Action FileLoadedAsync;                        // file-loaded        MPV_EVENT_FILE_LOADED
        public event Action VideoReconfigAsync;                     // video-reconfig     MPV_EVENT_VIDEO_RECONFIG
        public event Action AudioReconfigAsync;                     // audio-reconfig     MPV_EVENT_AUDIO_RECONFIG
        public event Action SeekAsync;                              // seek               MPV_EVENT_SEEK
        public event Action PlaybackRestartAsync;                   // playback-restart   MPV_EVENT_PLAYBACK_RESTART

        public event Action<mpv_log_level, string>LogMessage; // log-message        MPV_EVENT_LOG_MESSAGE
        public event Action<mpv_end_file_reason> EndFile;     // end-file           MPV_EVENT_END_FILE
        public event Action<string[]> ClientMessage;          // client-message     MPV_EVENT_CLIENT_MESSAGE
        public event Action Shutdown;                         // shutdown           MPV_EVENT_SHUTDOWN
        public event Action GetPropertyReply;                 // get-property-reply MPV_EVENT_GET_PROPERTY_REPLY
        public event Action SetPropertyReply;                 // set-property-reply MPV_EVENT_SET_PROPERTY_REPLY
        public event Action CommandReply;                     // command-reply      MPV_EVENT_COMMAND_REPLY
        public event Action StartFile;                        // start-file         MPV_EVENT_START_FILE
        public event Action FileLoaded;                       // file-loaded        MPV_EVENT_FILE_LOADED
        public event Action VideoReconfig;                    // video-reconfig     MPV_EVENT_VIDEO_RECONFIG
        public event Action AudioReconfig;                    // audio-reconfig     MPV_EVENT_AUDIO_RECONFIG
        public event Action Seek;                             // seek               MPV_EVENT_SEEK
        public event Action PlaybackRestart;                  // playback-restart   MPV_EVENT_PLAYBACK_RESTART

        public event Action Initialized;
        public event Action InitializedAsync;
        public event Action Pause;
        public event Action ShowMenu;
        public event Action<double> WindowScaleMpv;
        public event Action<float> ScaleWindow;
        public event Action<float> WindowScaleNET;
        public event Action<int> PlaylistPosChanged;
        public event Action<int> PlaylistPosChangedAsync;
        public event Action<Size> VideoSizeChanged;
        public event Action<Size> VideoSizeChangedAsync;
        public event Action<string> MoveWindow;

        public Dictionary<string, List<Action>>               PropChangeActions { get; set; } = new Dictionary<string, List<Action>>();
        public Dictionary<string, List<Action<int>>>       IntPropChangeActions { get; set; } = new Dictionary<string, List<Action<int>>>();
        public Dictionary<string, List<Action<bool>>>     BoolPropChangeActions { get; set; } = new Dictionary<string, List<Action<bool>>>();
        public Dictionary<string, List<Action<double>>> DoublePropChangeActions { get; set; } = new Dictionary<string, List<Action<double>>>();
        public Dictionary<string, List<Action<string>>> StringPropChangeActions { get; set; } = new Dictionary<string, List<Action<string>>>();

        public AutoResetEvent ShutdownAutoResetEvent { get; } = new AutoResetEvent(false);
        public AutoResetEvent VideoSizeAutoResetEvent { get; } = new AutoResetEvent(false);
        public DateTime HistoryTime;
        public IntPtr Handle { get; set; }
        public IntPtr NamedHandle { get; set; }
        public List<MediaTrack> MediaTracks { get; set; } = new List<MediaTrack>();
        public List<TimeSpan> BluRayTitles { get; } = new List<TimeSpan>();
        public object MediaTracksLock { get; } = new object();
        public Size VideoSize { get; set; }
        public TimeSpan Duration;

        public string ConfPath { get => ConfigFolder + "mpv.conf"; }
        public string GPUAPI { get; set; } = "auto";
        public string InputConfPath => ConfigFolder + "input.conf";
        public string Path { get; set; } = "";
        public string VO { get; set; } = "gpu";

        public string VID { get; set; } = "";
        public string AID { get; set; } = "";
        public string SID { get; set; } = "";

        public bool Border { get; set; } = true;
        public bool FileEnded { get; set; }
        public bool Fullscreen { get; set; }
        public bool IsQuitNeeded { set; get; } = true;
        public bool KeepaspectWindow { get; set; }
        public bool Paused { get; set; }
        public bool Shown { get; set; }
        public bool SnapWindow { get; set; }
        public bool TaskbarProgress { get; set; } = true;
        public bool WasInitialSizeSet;
        public bool WindowMaximized { get; set; }
        public bool WindowMinimized { get; set; }

        public int Edition { get; set; }
        public int PlaylistPos { get; set; } = -1;
        public int Screen { get; set; } = -1;
        public int VideoRotate { get; set; }

        public float Autofit { get; set; } = 0.6f;
        public float AutofitSmaller { get; set; } = 0.3f;
        public float AutofitLarger { get; set; } = 0.8f;

        public void Init(IntPtr handle)
        {
            ApplyShowMenuFix();

            Handle = mpv_create();

            var events = Enum.GetValues(typeof(mpv_event_id)).Cast<mpv_event_id>();

            foreach (mpv_event_id i in events)
                mpv_request_event(Handle, i, 0);

            mpv_request_log_messages(Handle, "no");

            App.RunTask(() => MainEventLoop());

            if (Handle == IntPtr.Zero)
                throw new Exception("error mpv_create");

            if (App.IsTerminalAttached)
            {
                SetPropertyString("terminal", "yes");
                SetPropertyString("input-terminal", "yes");
            }

            SetPropertyInt("osd-duration", 2000);
            SetPropertyLong("wid", handle.ToInt64());

            SetPropertyBool("input-default-bindings", true);
            SetPropertyBool("input-builtin-bindings", false);

            SetPropertyString("watch-later-options", "mute");
            SetPropertyString("screenshot-directory", "~~desktop/");
            SetPropertyString("osd-playing-msg", "${media-title}");
            SetPropertyString("osc", "yes");
            SetPropertyString("force-window", "yes");
            SetPropertyString("config-dir", ConfigFolder);
            SetPropertyString("config", "yes");

            ProcessCommandLine(true);

            Environment.SetEnvironmentVariable("MPVNET_VERSION", Application.ProductVersion);

            mpv_error err = mpv_initialize(Handle);

            if (err < 0)
                throw new Exception("mpv_initialize error" + BR2 + GetError(err) + BR);

            string idle = GetPropertyString("idle");
            App.Exit = idle == "no" || idle == "once";

            NamedHandle = mpv_create_client(Handle, "mpvnet");

            if (NamedHandle == IntPtr.Zero)
                throw new Exception("mpv_create_client error");

            mpv_request_log_messages(NamedHandle, "terminal-default");

            App.RunTask(() => EventLoop());

            // otherwise shutdown is raised before media files are loaded,
            // this means Lua scripts that use idle might not work correctly
            SetPropertyString("idle", "yes");

            ObservePropertyDouble("window-scale", value => WindowScaleMpv(value));
          
            ObservePropertyString("path", value => {
                if (HistoryTime == DateTime.MinValue)
                {
                    HistoryTime = DateTime.Now;
                    HistoryPath = value;
                }
                Path = value;
            });

            ObservePropertyBool("pause", value => {
                Paused = value;
                Pause();
            });

            ObservePropertyInt("video-rotate", value => {
                VideoRotate = value;
                UpdateVideoSize("dwidth", "dheight");
            });

            ObservePropertyInt("playlist-pos", value => {
                PlaylistPos = value;
                InvokeEvent(PlaylistPosChanged, PlaylistPosChangedAsync, value);

                if (value == -1 && Core.Shown)
                    ShowLogo();
                
                if (value != -1)
                    HideLogo();

                if (FileEnded && value == -1)
                {
                    if (GetPropertyString("keep-open") == "no" && App.Exit)
                        Core.CommandV("quit");
                }
            });

            if (!GetPropertyBool("osd-scale-by-window"))
                App.StartThreshold = 0;

            Initialized?.Invoke();
            InvokeAsync(InitializedAsync);
        }

        public void Destroy()
        {
            mpv_destroy(Handle);
            mpv_destroy(NamedHandle);
        }

        void ApplyShowMenuFix()
        {
            if (App.Settings.ShowMenuFixApplied)
                return;

            if (File.Exists(InputConfPath))
            {
                string content = File.ReadAllText(InputConfPath);

                if (!content.Contains("script-message mpv.net show-menu") &&
                    !content.Contains("script-message-to mpvnet show-menu"))

                    File.WriteAllText(InputConfPath, BR + content.Trim() + BR +
                        "MBTN_Right script-message-to mpvnet show-menu" + BR);
            }

            App.Settings.ShowMenuFixApplied = true;
        }

        void ApplyInputDefaultBindingsFix()
        {
            if (App.Settings.InputDefaultBindingsFixApplied)
                return;

            if (File.Exists(ConfPath))
            {
                string content = File.ReadAllText(ConfPath);

                if (content.Contains("input-default-bindings = no"))
                    File.WriteAllText(ConfPath, content.Replace("input-default-bindings = no", ""));

                if (content.Contains("input-default-bindings=no"))
                    File.WriteAllText(ConfPath, content.Replace("input-default-bindings=no", ""));
            }

            App.Settings.InputDefaultBindingsFixApplied = true;
        }

        public void ProcessProperty(string name, string value)
        {
            switch (name)
            {
                case "autofit":
                    {
                        if (int.TryParse(value.Trim('%'), out int result))
                            Autofit = result / 100f;
                    }
                    break;
                case "autofit-smaller":
                    {
                        if (int.TryParse(value.Trim('%'), out int result))
                            AutofitSmaller = result / 100f;
                    }
                    break;
                case "autofit-larger":
                    {
                        if (int.TryParse(value.Trim('%'), out int result))
                            AutofitLarger = result / 100f;
                    }
                    break;
                case "border": Border = value == "yes"; break;
                case "fs":
                case "fullscreen": Fullscreen = value == "yes"; break;
                case "gpu-api": GPUAPI = value; break;
                case "keepaspect-window": KeepaspectWindow = value == "yes"; break;
                case "screen": Screen = Convert.ToInt32(value); break;
                case "snap-window": SnapWindow = value == "yes"; break;
                case "taskbar-progress": TaskbarProgress = value == "yes"; break;
                case "vo": VO = value; break;
                case "window-maximized": WindowMaximized = value == "yes"; break;
                case "window-minimized": WindowMinimized = value == "yes"; break;
            }

            if (AutofitLarger > 1)
                AutofitLarger = 1;
        }

        bool? _UseNewMsgModel;

        public bool UseNewMsgModel {
            get {
                if (!_UseNewMsgModel.HasValue)
                    _UseNewMsgModel = InputConfContent.Contains("script-message-to mpvnet");
                return _UseNewMsgModel.Value;
            }
        }

        string _InputConfContent;

        public string InputConfContent {
            get {
                if (_InputConfContent == null)
                    _InputConfContent = File.ReadAllText(Core.InputConfPath);
                return _InputConfContent;
            }
        }

        string _ConfigFolder;

        public string ConfigFolder {
            get {
                if (_ConfigFolder == null)
                {
                    _ConfigFolder = Folder.Startup + "portable_config";

                    if (!Directory.Exists(_ConfigFolder))
                        _ConfigFolder = Folder.AppData + "mpv.net";

                    if (!Directory.Exists(_ConfigFolder))
                    {
                        try {
                            using (Process proc = new Process())
                            {
                                proc.StartInfo.UseShellExecute = false;
                                proc.StartInfo.CreateNoWindow = true;
                                proc.StartInfo.FileName = "powershell.exe";
                                proc.StartInfo.Arguments = $@"-Command New-Item -Path '{_ConfigFolder}' -ItemType Directory";
                                proc.Start();
                                proc.WaitForExit();
                            }
                        } catch (Exception) {}

                        if (!Directory.Exists(_ConfigFolder))
                            Directory.CreateDirectory(_ConfigFolder);
                    }

                    _ConfigFolder = _ConfigFolder.AddSep();

                    if (!File.Exists(_ConfigFolder + "input.conf"))
                    {
                        File.WriteAllText(_ConfigFolder + "input.conf", Properties.Resources.input_conf);

                        string scriptOptsPath = _ConfigFolder + "script-opts" + System.IO.Path.DirectorySeparatorChar;

                        if (!Directory.Exists(scriptOptsPath))
                        {
                            Directory.CreateDirectory(scriptOptsPath);
                            File.WriteAllText(scriptOptsPath + "console.conf", BR + "scale=1.5" + BR);
                            string content = BR + "scalewindowed=1.5" + BR + "hidetimeout=2000" + BR +
                                             "idlescreen=no" + BR;
                            File.WriteAllText(scriptOptsPath + "osc.conf", content);
                        }
                    }
                }

                return _ConfigFolder;
            }
        }

        Dictionary<string, string> _Conf;

        public Dictionary<string, string> Conf {
            get {
                if (_Conf == null)
                {
                    ApplyInputDefaultBindingsFix();

                    _Conf = new Dictionary<string, string>();

                    if (File.Exists(ConfPath))
                        foreach (var i in File.ReadAllLines(ConfPath))
                            if (i.Contains("=") && !i.TrimStart().StartsWith("#"))
                            {
                                string key = i.Substring(0, i.IndexOf("=")).Trim();
                                string value = i.Substring(i.IndexOf("=") + 1).Trim();

                                if (key.StartsWith("-"))
                                    key = key.TrimStart('-');

                                if (value.Contains("#") && !value.StartsWith("#") &&
                                    !value.StartsWith("'#") && !value.StartsWith("\"#"))

                                    value = value.Substring(0, value.IndexOf("#")).Trim();

                                _Conf[key] = value;
                            }

                    foreach (var i in _Conf)
                        ProcessProperty(i.Key, i.Value);
                }

                return _Conf;
            }
        }

        public void LoadScripts()
        {
            if (Directory.Exists(ConfigFolder + "scripts-ps"))
                foreach (string file in Directory.GetFiles(ConfigFolder + "scripts-ps", "*.ps1"))
                    App.RunTask(() => InvokePowerShellScript(file));
        }

        public void InvokePowerShellScript(string file)
        {
            PowerShell ps = new PowerShell();
            ps.Variables.Add(new KeyValuePair<string, object>("core", Core));
            ps.Variables.Add(new KeyValuePair<string, object>("window", MainForm.Instance));
            ps.Scripts.Add("Using namespace mpvnet; [Reflection.Assembly]::LoadWithPartialName('mpvnet')" + BR);

            string eventCode = @"
                $eventJob = Register-ObjectEvent -InputObject $mp -EventName Event -Action {
                    foreach ($pair in $mp.EventHandlers)
                    {
                        if ($pair.Key -eq $args[0])
                        {
                            if ($args.Length -gt 1)
                            {
                                $args2 = $args[1]
                            }

                            Invoke-Command -ScriptBlock $pair.Value -ArgumentList $args2
                        }
                    }
                }

                $mp.RedirectStreams($eventJob)
            ";

            string propertyChangedCode = @"
                $propertyChangedJob = Register-ObjectEvent -InputObject $mp -EventName PropertyChanged -Action {
                    foreach ($pair in $mp.PropChangedHandlers)
                    {
                        if ($pair.Key -eq $args[0])
                        {
                            if ($args.Length -gt 1)
                            {
                                $args2 = $args[1]
                            }

                            Invoke-Command -ScriptBlock $pair.Value -ArgumentList $args2
                        }
                    }
                }

                $mp.RedirectStreams($propertyChangedJob)
            ";

            ps.Scripts.Add(eventCode);
            ps.Scripts.Add(propertyChangedCode);
            ps.Scripts.Add(File.ReadAllText(file));
            ps.Module = System.IO.Path.GetFileName(file);
            ps.Print = true;

            lock (PowerShell.References)
                PowerShell.References.Add(ps);

            ps.Invoke();
        }

        void UpdateVideoSize(string w, string h)
        {
            Size size = new Size(GetPropertyInt(w), GetPropertyInt(h));

            if (size.Width == 0 || size.Height == 0)
                return;

            if (VideoRotate == 90 || VideoRotate == 270)
                size = new Size(size.Height, size.Width);

            if (VideoSize != size)
            {
                VideoSize = size;
                InvokeEvent(VideoSizeChanged, VideoSizeChangedAsync, size);
                VideoSizeAutoResetEvent.Set();
            }
        }

        public void MainEventLoop()
        {
            while (true)
                mpv_wait_event(Handle, -1);
        }

        public void EventLoop()
        {
            while (true)
            {
                IntPtr ptr = mpv_wait_event(NamedHandle, -1);
                mpv_event evt = (mpv_event)Marshal.PtrToStructure(ptr, typeof(mpv_event));

                try
                {
                    switch (evt.event_id)
                    {
                        case mpv_event_id.MPV_EVENT_SHUTDOWN:
                            IsQuitNeeded = false;
                            Shutdown?.Invoke();
                            WriteHistory();
                            ShutdownAutoResetEvent.Set();
                            return;
                        case mpv_event_id.MPV_EVENT_LOG_MESSAGE:
                            {
                                var data = (mpv_event_log_message)Marshal.PtrToStructure(evt.data, typeof(mpv_event_log_message));

                                if (data.log_level == mpv_log_level.MPV_LOG_LEVEL_INFO)
                                {
                                    string prefix = ConvertFromUtf8(data.prefix);

                                    if (prefix == "bd")
                                        ProcessBluRayLogMessage(ConvertFromUtf8(data.text));
                                }

                                if (LogMessage != null || LogMessageAsync != null)
                                {
                                    string msg = $"[{ConvertFromUtf8(data.prefix)}] {ConvertFromUtf8(data.text)}";
                                    InvokeAsync(LogMessageAsync, data.log_level, msg);
                                    LogMessage?.Invoke(data.log_level, msg);
                                }
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_CLIENT_MESSAGE:
                            {
                                var data = (mpv_event_client_message)Marshal.PtrToStructure(evt.data, typeof(mpv_event_client_message));
                                string[] args = ConvertFromUtf8Strings(data.args, data.num_args);

                                if (UseNewMsgModel && args[0] != "mpv.net")
                                    App.RunTask(() => Commands.Execute(args[0], args.Skip(1).ToArray()));
                                else if (args.Length > 1 && args[0] == "mpv.net")
                                    App.RunTask(() => Commands.Execute(args[1], args.Skip(2).ToArray()));

                                if (args.Length > 1 && args[0] == "osc-idlescreen")
                                {
                                    if (args[1] == "no")
                                        HideLogo();
                                    else if (args[1] == "yes" && PlaylistPos == -1)
                                        ShowLogo();
                                }

                                InvokeAsync(ClientMessageAsync, args);
                                ClientMessage?.Invoke(args);
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_VIDEO_RECONFIG:
                            UpdateVideoSize("dwidth", "dheight");
                            InvokeEvent(VideoReconfig, VideoReconfigAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_END_FILE:
                            {
                                var data = (mpv_event_end_file)Marshal.PtrToStructure(evt.data, typeof(mpv_event_end_file));
                                var reason = (mpv_end_file_reason)data.reason;
                                InvokeAsync(EndFileAsync, reason);
                                EndFile?.Invoke(reason);
                                FileEnded = true;
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_FILE_LOADED:
                            {
                                if (App.AutoPlay && Paused)
                                    SetPropertyBool("pause", false);

                                App.QuickBookmark = 0;

                                HideLogo();

                                Duration = TimeSpan.FromSeconds(GetPropertyDouble("duration"));

                                if (App.StartSize == "video")
                                    WasInitialSizeSet = false;

                                string path = GetPropertyString("path");

                                if (!VideoTypes.Contains(path.Ext()) || AudioTypes.Contains(path.Ext()))
                                {
                                    UpdateVideoSize("width", "height");
                                    VideoSizeAutoResetEvent.Set();
                                }

                                App.RunTask(new Action(() => UpdateTracks()));
                                App.RunTask(new Action(() => WriteHistory()));

                                InvokeEvent(FileLoaded, FileLoadedAsync);
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_PROPERTY_CHANGE:
                            {
                                var data = (mpv_event_property)Marshal.PtrToStructure(evt.data, typeof(mpv_event_property));

                                if (data.format == mpv_format.MPV_FORMAT_FLAG)
                                {
                                    lock (BoolPropChangeActions)
                                        foreach (var pair in BoolPropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                bool value = Marshal.PtrToStructure<int>(data.data) == 1;

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_STRING)
                                {
                                    lock (StringPropChangeActions)
                                        foreach (var pair in StringPropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                string value = ConvertFromUtf8(Marshal.PtrToStructure<IntPtr>(data.data));

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_INT64)
                                {
                                    lock (IntPropChangeActions)
                                        foreach (var pair in IntPropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                int value = Marshal.PtrToStructure<int>(data.data);

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_NONE)
                                {
                                    lock (PropChangeActions)
                                        foreach (var pair in PropChangeActions)
                                            if (pair.Key == data.name)
                                                foreach (var action in pair.Value)
                                                    action.Invoke();
                                }
                                else if (data.format == mpv_format.MPV_FORMAT_DOUBLE)
                                {
                                    lock (DoublePropChangeActions)
                                        foreach (var pair in DoublePropChangeActions)
                                            if (pair.Key == data.name)
                                            {
                                                double value = Marshal.PtrToStructure<double>(data.data);

                                                foreach (var action in pair.Value)
                                                    action.Invoke(value);
                                            }
                                }
                            }
                            break;
                        case mpv_event_id.MPV_EVENT_GET_PROPERTY_REPLY:
                            InvokeEvent(GetPropertyReply, GetPropertyReplyAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_SET_PROPERTY_REPLY:
                            InvokeEvent(SetPropertyReply, SetPropertyReplyAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_COMMAND_REPLY:
                            InvokeEvent(CommandReply, CommandReplyAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_START_FILE:
                            InvokeEvent(StartFile, StartFileAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_AUDIO_RECONFIG:
                            InvokeEvent(AudioReconfig, AudioReconfigAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_SEEK:
                            InvokeEvent(Seek, SeekAsync);
                            break;
                        case mpv_event_id.MPV_EVENT_PLAYBACK_RESTART:
                            InvokeEvent(PlaybackRestart, PlaybackRestartAsync);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    App.ShowException(ex);
                }
            }
        }

        void ProcessBluRayLogMessage(string msg)
        {
            lock (BluRayTitles)
            {
                if (msg.Contains(" 0 duration: "))
                    BluRayTitles.Clear();

                if (msg.Contains(" duration: "))
                {
                    int start = msg.IndexOf(" duration: ") + 11;
                    BluRayTitles.Add(new TimeSpan(
                        msg.Substring(start, 2).ToInt(),
                        msg.Substring(start + 3, 2).ToInt(),
                        msg.Substring(start + 6, 2).ToInt()));
                }
            }
        }

        public void SetBluRayTitle(int id)
        {
            LoadFiles(new[] { @"bd://" + id }, false, false);
        }

        void InvokeEvent(Action action, Action asyncAction)
        {
            InvokeAsync(asyncAction);
            action?.Invoke();
        }

        void InvokeEvent<T>(Action<T> action, Action<T> asyncAction, T t)
        {
            InvokeAsync(asyncAction, t);
            action?.Invoke(t);
        }

        void InvokeAsync(Action action)
        {
            if (action != null)
            {
                foreach (Action a in action.GetInvocationList())
                {
                    var a2 = a;
                    App.RunTask(a2);
                }
            }
        }

        void InvokeAsync<T>(Action<T> action, T t)
        {
            if (action != null)
            {
                foreach (Action<T> a in action.GetInvocationList())
                {
                    var a2 = a;
                    App.RunTask(() => a2.Invoke(t));
                }
            }
        }

        void InvokeAsync<T1, T2>(Action<T1, T2> action, T1 t1, T2 t2)
        {
            if (action != null)
            {
                foreach (Action<T1, T2> a in action.GetInvocationList())
                {
                    var a2 = a;
                    App.RunTask(() => a2.Invoke(t1, t2));
                }
            }
        }

        public void Command(string command)
        {
            mpv_error err = mpv_command_string(Handle, command);
            if (err < 0)
                HandleError(err, "error executing command: " + command);
        }

        public void CommandV(params string[] args)
        {
            int count = args.Length + 1;
            IntPtr[] pointers = new IntPtr[count];
            IntPtr rootPtr = Marshal.AllocHGlobal(IntPtr.Size * count);

            for (int index = 0; index < args.Length; index++)
            {
                var bytes = GetUtf8Bytes(args[index]);
                IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                pointers[index] = ptr;
            }

            Marshal.Copy(pointers, 0, rootPtr, count);
            mpv_error err = mpv_command(Handle, rootPtr);

            foreach (IntPtr ptr in pointers)
                Marshal.FreeHGlobal(ptr);

            Marshal.FreeHGlobal(rootPtr);
            if (err < 0)
                HandleError(err, "error executing command: " + string.Join("\n", args));
        }

        public string Expand(string value)
        {
            if (value == null)
                return "";

            if (!value.Contains("${"))
                return value;

            string[] args = { "expand-text", value };
            int count = args.Length + 1;
            IntPtr[] pointers = new IntPtr[count];
            IntPtr rootPtr = Marshal.AllocHGlobal(IntPtr.Size * count);

            for (int index = 0; index < args.Length; index++)
            {
                var bytes = GetUtf8Bytes(args[index]);
                IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                pointers[index] = ptr;
            }

            Marshal.Copy(pointers, 0, rootPtr, count);
            IntPtr resultNodePtr = Marshal.AllocHGlobal(16);
            mpv_error err = mpv_command_ret(Handle, rootPtr, resultNodePtr);

            foreach (IntPtr ptr in pointers)
                Marshal.FreeHGlobal(ptr);

            Marshal.FreeHGlobal(rootPtr);

            if (err < 0)
            {
                HandleError(err, "error executing command: " + string.Join("\n", args));
                Marshal.FreeHGlobal(resultNodePtr);
                return "property expansion error";
            }

            mpv_node resultNode = Marshal.PtrToStructure<mpv_node>(resultNodePtr);
            string ret = ConvertFromUtf8(resultNode.str);
            mpv_free_node_contents(resultNodePtr);
            Marshal.FreeHGlobal(resultNodePtr);
            return ret;
        }

        public bool GetPropertyBool(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_FLAG, out IntPtr lpBuffer);
            if (err < 0)
                HandleError(err, "error getting property: " + name);
            return lpBuffer.ToInt32() != 0;
        }

        public void SetPropertyBool(string name, bool value)
        {
            long val = value ? 1 : 0;
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_FLAG, ref val);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public int GetPropertyInt(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_INT64, out IntPtr lpBuffer);
            if (err < 0 && App.DebugMode)
                HandleError(err, "error getting property: " + name);
            return lpBuffer.ToInt32();
        }

        public void SetPropertyInt(string name, int value)
        {
            long val = value;
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_INT64, ref val);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public void SetPropertyLong(string name, long value)
        {
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_INT64, ref value);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public long GetPropertyLong(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_INT64, out IntPtr lpBuffer);
            if (err < 0)
                HandleError(err, "error getting property: " + name);
            return lpBuffer.ToInt64();
        }

        public double GetPropertyDouble(string name, bool handleError = true)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_DOUBLE, out double value);
            if (err < 0 && handleError && App.DebugMode)
                HandleError(err, "error getting property: " + name);
            return value;
        }

        public void SetPropertyDouble(string name, double value)
        {
            double val = value;
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_DOUBLE, ref val);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public string GetPropertyString(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_STRING, out IntPtr lpBuffer);

            if (err == 0)
            {
                string ret = ConvertFromUtf8(lpBuffer);
                mpv_free(lpBuffer);
                return ret;
            }

            if (err < 0 && App.DebugMode)
                HandleError(err, "error getting property: " + name);

            return "";
        }

        public void SetPropertyString(string name, string value)
        {
            byte[] bytes = GetUtf8Bytes(value);
            mpv_error err = mpv_set_property(Handle, GetUtf8Bytes(name), mpv_format.MPV_FORMAT_STRING, ref bytes);
            if (err < 0)
                HandleError(err, $"error setting property: {name} = {value}");
        }

        public string GetPropertyOsdString(string name)
        {
            mpv_error err = mpv_get_property(Handle, GetUtf8Bytes(name),
                mpv_format.MPV_FORMAT_OSD_STRING, out IntPtr lpBuffer);

            if (err == 0)
            {
                string ret = ConvertFromUtf8(lpBuffer);
                mpv_free(lpBuffer);
                return ret;
            }

            if (err < 0)
                HandleError(err, "error getting property: " + name);

            return "";
        }

        public void ObservePropertyInt(string name, Action<int> action)
        {
            lock (IntPropChangeActions)
            {
                if (!IntPropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_INT64);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        IntPropChangeActions[name] = new List<Action<int>>();
                }

                if (IntPropChangeActions.ContainsKey(name))
                    IntPropChangeActions[name].Add(action);
            }
        }

        public void ObservePropertyDouble(string name, Action<double> action)
        {
            lock (DoublePropChangeActions)
            {
                if (!DoublePropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_DOUBLE);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        DoublePropChangeActions[name] = new List<Action<double>>();
                }

                if (DoublePropChangeActions.ContainsKey(name))
                    DoublePropChangeActions[name].Add(action);
            }
        }

        public void ObservePropertyBool(string name, Action<bool> action)
        {
            lock (BoolPropChangeActions)
            {
                if (!BoolPropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_FLAG);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        BoolPropChangeActions[name] = new List<Action<bool>>();
                }

                if (BoolPropChangeActions.ContainsKey(name))
                    BoolPropChangeActions[name].Add(action);
            }
        }

        public void ObservePropertyString(string name, Action<string> action)
        {
            lock (StringPropChangeActions)
            {
                if (!StringPropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_STRING);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        StringPropChangeActions[name] = new List<Action<string>>();
                }

                if (StringPropChangeActions.ContainsKey(name))
                    StringPropChangeActions[name].Add(action);
            }
        }

        public void ObserveProperty(string name, Action action)
        {
            lock (PropChangeActions)
            {
                if (!PropChangeActions.ContainsKey(name))
                {
                    mpv_error err = mpv_observe_property(NamedHandle, 0, name, mpv_format.MPV_FORMAT_NONE);

                    if (err < 0)
                        HandleError(err, "error observing property: " + name);
                    else
                        PropChangeActions[name] = new List<Action>();
                }

                if (PropChangeActions.ContainsKey(name))
                    PropChangeActions[name].Add(action);
            }
        }

        public void HandleError(mpv_error err, string msg)
        {
            Terminal.WriteError(msg);
            Terminal.WriteError(GetError(err));
        }

        public void ProcessCommandLine(bool preInit)
        {
            bool shuffle = false;
            var args = Environment.GetCommandLineArgs().Skip(1);

            string[] preInitProperties = { "input-terminal", "terminal", "input-file", "config",
                "config-dir", "input-conf", "load-scripts", "scripts", "player-operation-mode",
                "idle", "log-file", "msg-color", "dump-stats", "msg-level", "really-quiet" };

            foreach (string i in args)
            {
                string arg = i;

                if (arg.StartsWith("-") && arg.Length > 1)
                {
                    if (!preInit)
                    {
                        if (arg == "--profile=help")
                        {
                            Console.WriteLine(mpvHelp.GetProfiles());
                            continue;
                        }
                        else if (arg == "--vd=help" || arg == "--ad=help")
                        {
                            Console.WriteLine(mpvHelp.GetDecoders());
                            continue;
                        }
                        else if (arg == "--audio-device=help")
                        {
                            Console.WriteLine(GetPropertyOsdString("audio-device-list"));
                            continue;
                        }
                        else if (arg == "--version")
                        {
                            Console.WriteLine(App.Version);
                            continue;
                        }
                        else if (arg == "--input-keylist")
                        {
                            Console.WriteLine(GetPropertyString("input-key-list").Replace(",", BR));
                            continue;
                        }
                        else if (arg.StartsWith("--command="))
                        {
                            Command(arg.Substring(10));
                            continue;
                        }
                    }

                    if (!arg.StartsWith("--"))
                        arg = "-" + arg;

                    if (!arg.Contains("="))
                    {
                        if (arg.Contains("--no-"))
                        {
                            arg = arg.Replace("--no-", "--");
                            arg += "=no";
                        }
                        else
                            arg += "=yes";
                    }

                    string left = arg.Substring(2, arg.IndexOf("=") - 2);
                    string right = arg.Substring(left.Length + 3);

                    switch (left)
                    {
                        case "script":        left = "scripts";        break;
                        case "audio-file":    left = "audio-files";    break;
                        case "sub-file":      left = "sub-files";      break;
                        case "external-file": left = "external-files"; break;
                    }

                    if (preInit && preInitProperties.Contains(left))
                    {
                        ProcessProperty(left, right);

                        if (!App.ProcessProperty(left, right))
                            SetPropertyString(left, right);
                    }
                    else if (!preInit && !preInitProperties.Contains(left))
                    {
                        ProcessProperty(left, right);

                        if (!App.ProcessProperty(left, right))
                        {
                            SetPropertyString(left, right);

                            if (left == "shuffle" && right == "yes")
                                shuffle = true;
                        }
                    }
                }
            }

            if (!preInit)
            {
                List<string> files = new List<string>();

                foreach (string i in args)
                    if (!i.StartsWith("--") && (i == "-" || i.Contains("://") ||
                        i.Contains(":\\") || i.StartsWith("\\\\") || File.Exists(i)))

                        files.Add(i);

                LoadFiles(files.ToArray(), !App.Queue, Control.ModifierKeys.HasFlag(Keys.Control) || App.Queue);

                if (shuffle)
                {
                    Command("playlist-shuffle");
                    SetPropertyInt("playlist-pos", 0);
                }

                if (files.Count == 0 || files[0].Contains("://"))
                {
                    VideoSizeChanged?.Invoke(VideoSize);
                    VideoSizeAutoResetEvent.Set();
                }
            }
        }

        public DateTime LastLoad;

        public void LoadFiles(string[] files, bool loadFolder, bool append)
        {
            if (files is null || files.Length == 0)
                return;

            if ((DateTime.Now - LastLoad).TotalMilliseconds < 1000)
                append = true;

            LastLoad = DateTime.Now;

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];

                if (string.IsNullOrEmpty(file))
                    continue;

                if (file.Contains("|"))
                    file = file.Substring(0, file.IndexOf("|"));

                file = ConvertFilePath(file);

                string ext = file.Ext();

                switch (ext)
                {
                    case "avs": LoadAviSynth(); break;
                    case "lnk": file = GetShortcutTarget(file); break;
                }

                if(SubtitleTypes.Contains(ext))
                    CommandV("sub-add", file);
                else if (!IsMediaExtension(ext) && !file.Contains("://") && Directory.Exists(file) &&
                    File.Exists(System.IO.Path.Combine(file, "BDMV\\index.bdmv")))
                {
                    Command("stop");
                    Thread.Sleep(500);
                    SetPropertyString("bluray-device", file);
                    CommandV("loadfile", @"bd://");
                }
                else
                {
                    if (i == 0 && !append)
                        CommandV("loadfile", file);
                    else
                        CommandV("loadfile", file, "append");
                }
            }

            if (string.IsNullOrEmpty(GetPropertyString("path")))
                SetPropertyInt("playlist-pos", 0);
        }

        public string ConvertFilePath(string path)
        {
            if ((path.Contains(":/") && !path.Contains("://")) || (path.Contains(":\\") && path.Contains("/")))
                path = path.Replace("/", "\\");

            if (!path.Contains(":") && !path.StartsWith("\\\\") && File.Exists(path))
                path = System.IO.Path.GetFullPath(path);

            return path;
        }


        IEnumerable<string> GetMediaFiles(IEnumerable<string> files) => files.Where(i => IsMediaExtension(i.Ext()));

        bool IsMediaExtension(string ext)
        {
            return VideoTypes.Contains(ext) || AudioTypes.Contains(ext) || ImageTypes.Contains(ext);
        }

        bool WasAviSynthLoaded;

        void LoadAviSynth()
        {
            if (!WasAviSynthLoaded)
            {
                string dll = Environment.GetEnvironmentVariable("AviSynthDLL");

                if (File.Exists(dll))
                    Native.LoadLibrary(dll);
                else
                    Native.LoadLibrary("AviSynth.dll");

                WasAviSynthLoaded = true;
            }
        }

        string HistoryPath;

        void WriteHistory()
        {
            double totalMinutes = (DateTime.Now - HistoryTime).TotalMinutes;

            if (!string.IsNullOrEmpty(HistoryPath) && totalMinutes > 1 &&
                !HistoryDiscard() && File.Exists(ConfigFolder + "history.txt"))
            {
                string path = HistoryPath;

                if (path.Contains("://"))
                    path = GetPropertyString("media-title");

                string txt = DateTime.Now.ToString().Substring(0, 16) + " " +
                    Convert.ToInt32(totalMinutes).ToString().PadLeft(3) + " " + path + "\r\n";

                File.AppendAllText(ConfigFolder + "history.txt", txt);
            }

            HistoryPath = Path;
            HistoryTime = DateTime.Now;
        }

        public bool HistoryDiscard()
        {
            if (App.HistoryFilter != null)
                foreach (string filter in App.HistoryFilter)
                    if (HistoryPath.Contains(filter.Trim()))
                        return true;
            return false;
        }

        public void ShowLogo()
        {
            if (!App.ShowLogo || MainForm.Instance == null || Core.Handle == IntPtr.Zero)
                return;

            bool december = DateTime.Now.Month == 12;
            Rectangle cr = MainForm.Instance.ClientRectangle;
            int len = Convert.ToInt32(cr.Height / (december ? 4.5 : 5));

            if (len < 16 || cr.Height < 16)
                return;

            using (Bitmap bmp = new Bitmap(len, len))
            {
                using (Graphics gx = Graphics.FromImage(bmp))
                {
                    gx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gx.Clear(Color.Black);
                    Rectangle rect = new Rectangle(0, 0, len, len);
                    Bitmap bmp2 = (december && App.ShowSantaLogo) ? Properties.Resources.mpvnet_santa : Properties.Resources.mpvnet;
                    gx.DrawImage(bmp2, rect);
                    BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    int x = Convert.ToInt32((cr.Width - len) / (december ? 1.95 : 2));
                    int y = Convert.ToInt32((cr.Height - len) / 2.0 * (december ? 0.85 : 0.9));
                    CommandV("overlay-add", "0", $"{x}", $"{y}", "&" + bd.Scan0.ToInt64().ToString(), "0", "bgra", bd.Width.ToString(), bd.Height.ToString(), bd.Stride.ToString());
                    bmp.UnlockBits(bd);
                }
            }
        }

        void HideLogo() => Command("overlay-remove 0");

        public bool IsImage => ImageTypes.Contains(Path.Ext());
        
        public bool IsAudio => AudioTypes.Contains(Path.Ext());

        string GetLanguage(string id)
        {
            foreach (CultureInfo ci in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
                if (ci.ThreeLetterISOLanguageName == id || Convert(ci.ThreeLetterISOLanguageName) == id)
                    return ci.EnglishName;

            return id;

            string Convert(string id2)
            {
                switch (id2)
                {
                    case "bng": return "ben";
                    case "ces": return "cze";
                    case "deu": return "ger";
                    case "ell": return "gre";
                    case "eus": return "baq";
                    case "fra": return "fre";
                    case "hye": return "arm";
                    case "isl": return "ice";
                    case "kat": return "geo";
                    case "mya": return "bur";
                    case "nld": return "dut";
                    case "sqi": return "alb";
                    case "zho": return "chi";
                    default: return id2;
                }
            }
        }

        string GetNativeLanguage(string name)
        {
            foreach (CultureInfo ci in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
                if (ci.EnglishName == name)
                    return ci.NativeName;

            return name;
        }

        public static string GetShortcutTarget(string path)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic sh = Activator.CreateInstance(t);
            return sh.CreateShortcut(path).TargetPath;
        }

        public void RaiseScaleWindow(float value) => ScaleWindow(value);

        public void RaiseMoveWindow(string value) => MoveWindow(value);
        
        public void RaiseWindowScaleNET(float value) => WindowScaleNET(value);
        
        public void RaiseShowMenu() => ShowMenu();

        public void UpdateTracks()
        {
            string path = GetPropertyString("path");

            if (!path.ToLowerEx().StartsWithEx("bd://"))
                lock (BluRayTitles)
                    BluRayTitles.Clear();

            lock (MediaTracksLock)
            {
                if (App.MediaInfo && !path.Contains("://") && !path.Contains(@"\\.\pipe\") && File.Exists(path))
                    MediaTracks = GetMediaInfoTracks(path);
                else
                    MediaTracks = GetTracks();
            }
        }

        public List<Chapter> GetChapters() {
            List<Chapter> chapters = new List<Chapter>();
            int count = GetPropertyInt("chapter-list/count");

            for (int x = 0; x < count; x++)
            {
                string title = GetPropertyString($"chapter-list/{x}/title");
                double time = GetPropertyDouble($"chapter-list/{x}/time");

                if (string.IsNullOrEmpty(title) ||
                    (title.Length == 12 && title.Contains(":") && title.Contains(".")))

                    title = "Chapter " + (x + 1);

                chapters.Add(new Chapter() { Title = title, Time = time });
            }

            return chapters;
        }

        public void UpdateExternalTracks()
        { 
            int trackListTrackCount = GetPropertyInt("track-list/count");
            int editionCount = GetPropertyInt("edition-list/count");
            int count = MediaTracks.Where(i => i.Type != "g").Count();

            lock (MediaTracksLock)
            {
                if (count != (trackListTrackCount + editionCount))
                {
                    MediaTracks = MediaTracks.Where(i => !i.External).ToList();
                    MediaTracks.AddRange(GetTracks(false));
                }
            }
        }

        public List<MediaTrack> GetTracks(bool includeInternal = true, bool includeExternal = true)
        {
            List<MediaTrack> tracks = new List<MediaTrack>();

            int trackCount = GetPropertyInt("track-list/count");

            for (int i = 0; i < trackCount; i++)
            {
                bool external = GetPropertyBool($"track-list/{i}/external");

                if ((external && !includeExternal) || (!external && !includeInternal))
                    continue;

                string type = GetPropertyString($"track-list/{i}/type");
                string filename = GetPropertyString($"filename/no-ext");
                string title = GetPropertyString($"track-list/{i}/title").Replace(filename, "");

                title = Regex.Replace(title, @"^[\._\-]", "");

                if (type == "video")
                {
                    string codec = GetPropertyString($"track-list/{i}/codec").ToUpperEx();
                    if (codec == "MPEG2VIDEO")
                        codec = "MPEG2";
                    else if (codec == "DVVIDEO")
                        codec = "DV";
                    MediaTrack track = new MediaTrack();
                    Add(track, codec);
                    Add(track, GetPropertyString($"track-list/{i}/demux-w") + "x" + GetPropertyString($"track-list/{i}/demux-h"));
                    Add(track, GetPropertyString($"track-list/{i}/demux-fps").Replace(".000000", "") + " FPS");
                    Add(track, GetPropertyBool($"track-list/{i}/default") ? "Default" : null);
                    track.Text = "V: " + track.Text.Trim(' ', ',');
                    track.Type = "v";
                    track.ID = GetPropertyInt($"track-list/{i}/id");
                    tracks.Add(track);
                }
                else if (type == "audio")
                {
                    string codec = GetPropertyString($"track-list/{i}/codec").ToUpperEx();
                    if (codec.Contains("PCM"))
                        codec = "PCM";
                    MediaTrack track = new MediaTrack();
                    Add(track, GetLanguage(GetPropertyString($"track-list/{i}/lang")));
                    Add(track, codec);
                    Add(track, GetPropertyInt($"track-list/{i}/audio-channels") + " ch");
                    Add(track, GetPropertyInt($"track-list/{i}/demux-samplerate") / 1000 + " kHz");
                    Add(track, GetPropertyBool($"track-list/{i}/forced") ? "Forced" : null);
                    Add(track, GetPropertyBool($"track-list/{i}/default") ? "Default" : null);
                    Add(track, GetPropertyBool($"track-list/{i}/external") ? "External" : null);
                    Add(track, title);
                    track.Text = "A: " + track.Text.Trim(' ', ',');
                    track.Type = "a";
                    track.ID = GetPropertyInt($"track-list/{i}/id");
                    track.External = external;
                    tracks.Add(track);
                }
                else if (type == "sub")
                {
                    string codec = GetPropertyString($"track-list/{i}/codec").ToUpperEx();
                    if (codec.Contains("PGS"))
                        codec = "PGS";
                    else if (codec == "SUBRIP")
                        codec = "SRT";
                    else if (codec == "WEBVTT")
                        codec = "VTT";
                    else if (codec == "DVB_SUBTITLE")
                        codec = "DVB";
                    else if (codec == "DVD_SUBTITLE")
                        codec = "VOB";
                    MediaTrack track = new MediaTrack();
                    Add(track, GetLanguage(GetPropertyString($"track-list/{i}/lang")));
                    Add(track, codec);
                    Add(track, GetPropertyBool($"track-list/{i}/forced") ? "Forced" : null);
                    Add(track, GetPropertyBool($"track-list/{i}/default") ? "Default" : null);
                    Add(track, GetPropertyBool($"track-list/{i}/external") ? "External" : null);
                    Add(track, title);
                    track.Text = "S: " + track.Text.Trim(' ', ',');
                    track.Type = "s";
                    track.ID = GetPropertyInt($"track-list/{i}/id");
                    track.External = external;
                    tracks.Add(track);
                }
            }

            if (includeInternal)
            {
                int editionCount = GetPropertyInt("edition-list/count");

                for (int i = 0; i < editionCount; i++)
                {
                    string title = GetPropertyString($"edition-list/{i}/title");
                    if (string.IsNullOrEmpty(title))
                        title = "Edition " + i;
                    MediaTrack track = new MediaTrack();
                    track.Text = "E: " + title;
                    track.Type = "e";
                    track.ID = i;
                    tracks.Add(track);
                }
            }

            return tracks;
        }

        public List<MediaTrack> GetMediaInfoTracks(string path)
        {
            List<MediaTrack> tracks = new List<MediaTrack>();

            using (MediaInfo mi = new MediaInfo(path))
            {
                MediaTrack track = new MediaTrack();
                Add(track, mi.GetGeneral("Format"));
                Add(track, mi.GetGeneral("FileSize/String"));
                Add(track, mi.GetGeneral("Duration/String"));
                Add(track, mi.GetGeneral("OverallBitRate/String"));
                track.Text = "G: " + track.Text.Trim(' ', ',');
                track.Type = "g";
                tracks.Add(track);

                int videoCount = mi.GetCount(MediaInfoStreamKind.Video);

                for (int i = 0; i < videoCount; i++)
                {
                    string fps = mi.GetVideo(i, "FrameRate");

                    if (float.TryParse(fps, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                        fps = result.ToString(CultureInfo.InvariantCulture);

                    track = new MediaTrack();
                    Add(track, mi.GetVideo(i, "Format"));
                    Add(track, mi.GetVideo(i, "Format_Profile"));
                    Add(track, mi.GetVideo(i, "Width") + "x" + mi.GetVideo(i, "Height"));
                    Add(track, mi.GetVideo(i, "BitRate/String"));
                    Add(track, fps + " FPS");
                    Add(track, (videoCount > 1 && mi.GetVideo(i, "Default") == "Yes") ? "Default" : "");
                    track.Text = "V: " + track.Text.Trim(' ', ',');
                    track.Type = "v";
                    track.ID = i + 1;
                    tracks.Add(track);
                }

                int audioCount = mi.GetCount(MediaInfoStreamKind.Audio);

                for (int i = 0; i < audioCount; i++)
                {
                    string lang = mi.GetAudio(i, "Language/String");
                    string nativeLang = GetNativeLanguage(lang);
                    string title = mi.GetAudio(i, "Title");
                    string format = mi.GetAudio(i, "Format");

                    if (!string.IsNullOrEmpty(title))
                    {
                        if (title.ContainsEx("DTS-HD MA"))
                            format = "DTS-MA";

                        if (title.ContainsEx("DTS-HD MA"))
                            title = title.Replace("DTS-HD MA", "");

                        if (title.ContainsEx("Blu-ray"))
                            title = title.Replace("Blu-ray", "");

                        if (title.ContainsEx("UHD "))
                            title = title.Replace("UHD ", "");

                        if (title.ContainsEx("EAC"))
                            title = title.Replace("EAC", "E-AC");

                        if (title.ContainsEx("AC3"))
                            title = title.Replace("AC3", "AC-3");

                        if (title.ContainsEx(lang))
                            title = title.Replace(lang, "").Trim();

                        if (title.ContainsEx(nativeLang))
                            title = title.Replace(nativeLang, "").Trim();

                        if (title.ContainsEx("Surround"))
                            title = title.Replace("Surround", "");

                        if (title.ContainsEx("Dolby Digital"))
                            title = title.Replace("Dolby Digital", "");

                        if (title.ContainsEx("Stereo"))
                            title = title.Replace("Stereo", "");

                        if (title.StartsWithEx(format + " "))
                            title = title.Replace(format + " ", "");

                        foreach (string i2 in new [] { "2.0", "5.1", "6.1", "7.1" })
                            if (title.ContainsEx(i2))
                                title = title.Replace(i2, "").Trim();

                        if (title.ContainsEx("@ "))
                            title = title.Replace("@ ", "");

                        if (title.ContainsEx(" @"))
                            title = title.Replace(" @", "");

                        if (title.ContainsEx("()"))
                            title = title.Replace("()", "");

                        if (title.ContainsEx("[]"))
                            title = title.Replace("[]", "");

                        if (title.TrimEx() == format)
                            title = null;

                        if (!string.IsNullOrEmpty(title))
                            title = title.Trim(" _-".ToCharArray());
                    }

                    track = new MediaTrack();
                    Add(track, lang);
                    Add(track, format);
                    Add(track, mi.GetAudio(i, "Format_Profile"));
                    Add(track, mi.GetAudio(i, "BitRate/String"));
                    Add(track, mi.GetAudio(i, "Channel(s)") + " ch");
                    Add(track, mi.GetAudio(i, "SamplingRate/String"));
                    Add(track, mi.GetAudio(i, "Forced") == "Yes" ? "Forced" : "");
                    Add(track, (audioCount > 1 && mi.GetAudio(i, "Default") == "Yes") ? "Default" : "");
                    Add(track, title);

                    if (track.Text.Contains("MPEG Audio, Layer 2"))
                        track.Text = track.Text.Replace("MPEG Audio, Layer 2", "MP2");

                    if (track.Text.Contains("MPEG Audio, Layer 3"))
                        track.Text = track.Text.Replace("MPEG Audio, Layer 2", "MP3");

                    track.Text = "A: " + track.Text.Trim(' ', ',');
                    track.Type = "a";
                    track.ID = i + 1;
                    tracks.Add(track);
                }

                int subCount = mi.GetCount(MediaInfoStreamKind.Text);

                for (int i = 0; i < subCount; i++)
                {
                    string codec = mi.GetText(i, "Format").ToUpperEx();

                    if (codec == "UTF-8")
                        codec = "SRT";
                    else if (codec == "WEBVTT")
                        codec = "VTT";
                    else if (codec == "VOBSUB")
                        codec = "VOB";

                    string lang = mi.GetText(i, "Language/String");
                    string nativeLang = GetNativeLanguage(lang);
                    string title = mi.GetText(i, "Title");
                    bool forced = mi.GetText(i, "Forced") == "Yes";

                    if (!string.IsNullOrEmpty(title))
                    {
                        if (title.ContainsEx("VobSub"))
                            title = title.Replace("VobSub", "VOB");

                        if (title.ContainsEx(codec))
                            title = title.Replace(codec, "");

                        if (title.ContainsEx(lang.ToLowerEx()))
                            title = title.Replace(lang.ToLowerEx(), lang);

                        if (title.ContainsEx(nativeLang.ToLowerEx()))
                            title = title.Replace(nativeLang.ToLowerEx(), nativeLang).Trim();

                        if (title.ContainsEx(lang))
                            title = title.Replace(lang, "");

                        if (title.ContainsEx(nativeLang))
                            title = title.Replace(nativeLang, "").Trim();

                        if (title.ContainsEx("full"))
                            title = title.Replace("full", "").Trim();

                        if (title.ContainsEx("Full"))
                            title = title.Replace("Full", "").Trim();

                        if (title.ContainsEx("Subtitles"))
                            title = title.Replace("Subtitles", "").Trim();

                        if (title.ContainsEx("forced"))
                            title = title.Replace("forced", "Forced").Trim();

                        if (forced && title.ContainsEx("Forced"))
                            title = title.Replace("Forced", "").Trim();

                        if (title.ContainsEx("()"))
                            title = title.Replace("()", "");

                        if (title.ContainsEx("[]"))
                            title = title.Replace("[]", "");

                        if (!string.IsNullOrEmpty(title))
                            title = title.Trim(" _-".ToCharArray());
                    }

                    track = new MediaTrack();
                    Add(track, lang);
                    Add(track, codec);
                    Add(track, mi.GetText(i, "Format_Profile"));
                    Add(track, forced ? "Forced" : "");
                    Add(track, (subCount > 1 && mi.GetText(i, "Default") == "Yes") ? "Default" : "");
                    Add(track, title);
                    track.Text = "S: " + track.Text.Trim(' ', ',');
                    track.Type = "s";
                    track.ID = i + 1;
                    tracks.Add(track);
                }
            }

            int editionCount = GetPropertyInt("edition-list/count");

            for (int i = 0; i < editionCount; i++)
            {
                string title = GetPropertyString($"edition-list/{i}/title");
                if (string.IsNullOrEmpty(title))
                    title = "Edition " + i;
                MediaTrack track = new MediaTrack();
                track.Text = "E: " + title;
                track.Type = "e";
                track.ID = i;
                tracks.Add(track);
            }

            return tracks;
        }

        void Add(MediaTrack track, object value)
        {
            string str = value.ToStringEx().Trim();

            if (str != "" && !(track.Text != null && track.Text.Contains(str)))
                track.Text += " " + str + ",";
        }

        private string[] _ProfileNames;

        public string[] ProfileNames
        {
            get
            {
                if (_ProfileNames == null)
                {
                    string[] ignore = { "builtin-pseudo-gui", "encoding", "libmpv", "pseudo-gui", "default" };
                    string profileList = Core.GetPropertyString("profile-list");
                    var json = profileList.FromJson<List<Dictionary<string, object>>>();
                    _ProfileNames = json.Select(i => i["name"].ToString())
                                        .Where(i => !ignore.Contains(i)).ToArray();
                }

                return _ProfileNames;
            }
        }
    }
}
