﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

using static mpvnet.Global;

namespace mpvnet
{
    public static class App
    {
        public static List<string> TempFiles { get; } = new List<string>();

        public static string[] HistoryFilter { get; set; }

        public static string ConfPath { get => Core.ConfigFolder + "mpvnet.conf"; }
        public static string ProcessInstance { get; set; } = "single";
        public static string DarkMode { get; set; } = "always";
        public static string DarkTheme { get; set; } = "dark";
        public static string LightTheme { get; set; } = "light";
        public static string StartSize { get; set; } = "height-session";

        public static bool AutoLoadFolder { get; set; } = true;
        public static bool AutoPlay { get; set; }
        public static bool DebugMode { get; set; }
        public static bool Exit { get; set; }
        public static bool IsTerminalAttached { get; } = Environment.GetEnvironmentVariable("_started_from_console") == "yes";
        public static bool MediaInfo { get; set; } = true;
        public static bool Queue { get; set; }
        public static bool RememberVolume { get; set; } = true;
        public static bool RememberWindowPosition { get; set; }
        public static bool ShowLogo { get; set; } = true;
        public static bool ShowSantaLogo { get; set; } = true;

        public static int StartThreshold { get; set; } = 1500;
        public static int RecentCount { get; set; } = 15;

        public static float AutofitAudio { get; set; } = 0.7f;
        public static float AutofitImage { get; set; } = 0.8f;
        public static float MinimumAspectRatio { get; set; }
        public static float MinimumAspectRatioAudio { get; set; }
        public static float QuickBookmark { get; set; }

        public static Extension Extension { get; set; }

        public static bool IsDarkMode => (DarkMode == "system" && Sys.IsDarkTheme) || DarkMode == "always";

        static AppSettings _Settings;

        public static AppSettings Settings {
            get {
                if (_Settings == null)
                    _Settings = SettingsManager.Load();

                return _Settings;
            }
        }

        public static void Init()
        {
            var useless1 = Core.ConfigFolder;
            var useless2 = Core.Conf;

            foreach (var i in Conf)
                ProcessProperty(i.Key, i.Value, true);

            if (DebugMode)
            {
                try
                {
                    string filePath = Core.ConfigFolder + "mpvnet-debug.log";

                    if (File.Exists(filePath))
                        File.Delete(filePath);

                    Trace.Listeners.Add(new TextWriterTraceListener(filePath));
                    Trace.AutoFlush = true;

                    //if (App.DebugMode)
                    //    Trace.WriteLine("");
                }
                catch (Exception e)
                {
                }
            }

            InitTheme();

            Core.Shutdown += Core_Shutdown;
            Core.Initialized += Core_Initialized;
        }

        public static void InitTheme()
        {
            string themeContent = null;

            if (File.Exists(Core.ConfigFolder + "theme.conf"))
                themeContent = File.ReadAllText(Core.ConfigFolder + "theme.conf");

            Theme.Init(
                themeContent,
                Properties.Resources.theme,
                IsDarkMode ? DarkTheme : LightTheme);
        }

        public static void RunTask(Action action)
        {
            Task.Run(() => {
                try {
                    action.Invoke();
                }
                catch (Exception e) {
                    ShowException(e);
                }
            });
        }

        public static string Version => "Copyright (C) 2000-2022 mpv.net/mpv/mplayer\n" +
            $"mpv.net {Application.ProductVersion}" + GetLastWriteTime(Application.ExecutablePath) + "\n" +
            $"{Core.GetPropertyString("mpv-version")}" + GetLastWriteTime(Folder.Startup + "libmpv-2.dll") + "\n" +
            $"ffmpeg {Core.GetPropertyString("ffmpeg-version")}\n" +
            $"MediaInfo {FileVersionInfo.GetVersionInfo(Path.Combine(Application.StartupPath, "MediaInfo.dll")).FileVersion}" +
            GetLastWriteTime(Path.Combine(Application.StartupPath , "MediaInfo.dll")) + "\nGPL v2 License";

        static string GetLastWriteTime(string path)
        {
            if (IsStoreVrsion)
                return "";

            return $" ({File.GetLastWriteTime(path).ToShortDateString()})";
        }

        static bool IsStoreVrsion => Application.StartupPath.Contains("FrankSkare.mpv.net");

        public static void ShowException(object obj)
        {
            if (IsTerminalAttached)
                Terminal.WriteError(obj.ToString());
            else
            {

            }
        }

        public static void InvokeOnMainThread(Action action)
        {
            if (action == null)
                return;

            if (MainForm.Instance == null)
                action.Invoke();
            else
                MainForm.Instance.BeginInvoke(action);
        }

        public static void ShowInfo(string msg)
        {
            if (IsTerminalAttached)
            {
                if (msg != null)
                    Terminal.Write(msg);
            }
            else
            {

            }
        }

        public static void ShowError(string msg)
        {
            if (IsTerminalAttached)
            {
                if (msg != null)
                    Terminal.WriteError(msg);
            }
            else
            {

            }
        }

        static void Core_Initialized()
        {
            if (RememberVolume)
            {
                Core.SetPropertyInt("volume", Settings.Volume);
                Core.SetPropertyString("mute", Settings.Mute);
            }
        }

        static void Core_Shutdown()
        {
            Settings.Volume = Core.GetPropertyInt("volume");
            Settings.Mute = Core.GetPropertyString("mute");

            SettingsManager.Save(Settings);

            foreach (string file in TempFiles)
                FileHelp.Delete(file);
        }

        static Dictionary<string, string> _Conf;

        public static Dictionary<string, string> Conf {
            get {
                if (_Conf == null)
                {
                    _Conf = new Dictionary<string, string>();

                    if (File.Exists(ConfPath))
                        foreach (string i in File.ReadAllLines(ConfPath))
                            if (i.Contains("=") && !i.StartsWith("#"))
                                _Conf[i.Substring(0, i.IndexOf("=")).Trim()] = i.Substring(i.IndexOf("=") + 1).Trim();
                }
                return _Conf;
            }
        }

        public static bool ProcessProperty(string name, string value, bool writeError = false)
        {
            switch (name)
            {
                case "audio-file-extensions": CorePlayer.AudioTypes = value.Split(" ,;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries); return true;
                case "auto-load-folder": AutoLoadFolder = value == "yes"; return true;
                case "auto-play": AutoPlay = value == "yes"; return true;
                case "autofit-audio": AutofitAudio = value.Trim('%').ToInt() / 100f; return true;
                case "autofit-image": AutofitImage = value.Trim('%').ToInt() / 100f; return true;
                case "dark-mode": DarkMode = value; return true;
                case "dark-theme": DarkTheme = value.Trim('\'', '"'); return true;
                case "debug-mode": DebugMode = value == "yes"; return true;
                case "history-filter": HistoryFilter = value.Split(';'); return true;
                case "image-file-extensions": CorePlayer.ImageTypes = value.Split(" ,;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries); return true;
                case "light-theme": LightTheme = value.Trim('\'', '"'); return true;
                case "media-info": MediaInfo = value == "yes"; return true;
                case "minimum-aspect-ratio-audio": MinimumAspectRatioAudio = value.ToFloat(); return true;
                case "minimum-aspect-ratio": MinimumAspectRatio = value.ToFloat(); return true;
                case "process-instance": ProcessInstance = value; return true;
                case "queue": Queue = value == "yes"; return true;
                case "recent-count": RecentCount = value.ToInt(); return true;
                case "remember-volume": RememberVolume = value == "yes"; return true;
                case "remember-window-position": RememberWindowPosition = value == "yes"; return true;
                case "show-logo": ShowLogo = value == "yes"; return true;
                case "show-santa-logo": ShowSantaLogo = value == "yes"; return true;
                case "start-size": StartSize = value; return true;
                case "start-threshold": StartThreshold = value.ToInt(); return true;
                case "video-file-extensions": CorePlayer.VideoTypes = value.Split(" ,;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries); return true;

                default:
                    if (writeError)
                        Terminal.WriteError($"unknown mpvnet.conf property: {name}");
                    return false;
            }
        }

        public static void CopyMpvnetCom()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).AddSep() +
                "Microsoft\\WindowsApps\\";

            if (File.Exists(dir + "mpvnet.exe") && !File.Exists(dir + "mpvnet.com"))
                File.Copy(Folder.Startup + "mpvnet.com", dir + "mpvnet.com");
        }

        public static (string Title, string Path) GetTitleAndPath(string input)
        {
            if (input.Contains("|"))
            {
                var a = input.Split('|');
                return (a[1], a[0]);
            }

            return (input, input);
        }
    }
}
