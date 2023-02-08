﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

using WinForms = System.Windows.Forms;

using static mpvnet.Global;

namespace mpvnet
{
    public class Commands
    {
        public static void Execute(string id, string[] args)
        {
            switch (id)
            {
                case "cycle-audio": CycleAudio(); break;
                case "cycle-subtitles": CycleSubtitles(); break;
                case "load-audio": LoadAudio(); break;
                case "load-sub": LoadSubtitle(); break;
                case "move-window": MoveWindow(args[0]); break;
                case "open-clipboard": OpenFromClipboard(); break;
                case "open-conf-folder": ProcessHelp.ShellExecute(Core.ConfigFolder); break;
                case "open-files": OpenFiles(args); break;
                case "play-pause": PlayPause(); break;
                case "playlist-add": PlaylistAdd(Convert.ToInt32(args[0])); break;
                case "playlist-first": PlaylistFirst(); break;
                case "playlist-last": PlaylistLast(); break;
                case "playlist-random": PlaylistRandom(); break;
                case "quick-bookmark": QuickBookmark(); break;
                case "scale-window": ScaleWindow(float.Parse(args[0], CultureInfo.InvariantCulture)); break;
                case "shell-execute": ProcessHelp.ShellExecute(args[0]); break;
                case "show-info": ShowInfo(); break;
                case "show-progress": ShowProgress(); break;
                case "show-text": ShowText(args[0], Convert.ToInt32(args[1]), Convert.ToInt32(args[2])); break;
                case "window-scale": WindowScale(float.Parse(args[0], CultureInfo.InvariantCulture)); break;

                // deprecated 2019
                case "add-files-to-playlist": OpenFiles("append"); break;

                // deprecated 2020

                // deprecated 2022
                case "open-url": OpenFromClipboard(); break;
            }
        }

        public static void ShowTextWithEditor(string name, string text)
        {
            string file = Path.Combine(Path.GetTempPath(), name + ".txt");
            App.TempFiles.Add(file);
            File.WriteAllText(file, BR + text.Trim() + BR);
            ProcessHelp.ShellExecute(file);
        }

        public static void ShowDialog(Type winType) => App.InvokeOnMainThread(() =>
        {
            Window win = Activator.CreateInstance(winType) as Window;
            new WindowInteropHelper(win).Owner = MainForm.Instance.Handle;
            win.ShowDialog();
        });

        public static void OpenFiles(params string[] args)
        {
            bool append = Control.ModifierKeys.HasFlag(Keys.Control);

            foreach (string arg in args)
                if (arg == "append")
                    append = true;

            App.InvokeOnMainThread(new Action(() => {
                using (var d = new OpenFileDialog() { Multiselect = true })
                    if (d.ShowDialog() == DialogResult.OK)
                        Core.LoadFiles(d.FileNames, true, append);
            }));
        }

        public static void PlaylistFirst()
        {
            if (Core.PlaylistPos != 0)
                Core.SetPropertyInt("playlist-pos", 0);
        }

        public static void PlaylistLast()
        {
            int count = Core.GetPropertyInt("playlist-count");

            if (Core.PlaylistPos < count - 1)
                Core.SetPropertyInt("playlist-pos", count - 1);
        }

        public static void PlayPause()
        {
            int count = Core.GetPropertyInt("playlist-count");

            if (count > 0)
                Core.Command("cycle pause");
            else if (App.Settings.RecentFiles.Count > 0)
            {
                foreach (string i in App.Settings.RecentFiles)
                {
                    if (i.Contains("://") || File.Exists(i))
                    {
                        Core.LoadFiles(new[] { i }, true, false);
                        break;
                    }
                }
            }
        }



        public static void ShowInfo()
        {
            if (Core.PlaylistPos == -1)
                return;

            string text;
            long fileSize = 0;
            string path = Core.GetPropertyString("path");

            if (File.Exists(path))
            {
                if (CorePlayer.AudioTypes.Contains(path.Ext()))
                {
                    text = Core.GetPropertyOsdString("filtered-metadata");
                    Core.CommandV("show-text", text, "5000");
                    return;
                }
                else if (CorePlayer.ImageTypes.Contains(path.Ext()))
                {
                    fileSize = new FileInfo(path).Length;
                    text = "Width: " + Core.GetPropertyInt("width") + "\n" +
                           "Height: " + Core.GetPropertyInt("height") + "\n" +
                           "Size: " + Convert.ToInt32(fileSize / 1024.0) + " KB\n" +
                           "Type: " + path.Ext().ToUpper();

                    Core.CommandV("show-text", text, "5000");
                    return;
                }
                else
                {
                    Core.Command("script-message-to mpvnet show-media-info osd");
                    return;
                }
            }

            if (path.Contains("://")) path = Core.GetPropertyString("media-title");
            string videoFormat = Core.GetPropertyString("video-format").ToUpper();
            string audioCodec = Core.GetPropertyString("audio-codec-name").ToUpper();
            int width = Core.GetPropertyInt("video-params/w");
            int height = Core.GetPropertyInt("video-params/h");
            TimeSpan len = TimeSpan.FromSeconds(Core.GetPropertyDouble("duration"));
            text = path.FileName() + "\n";
            text += FormatTime(len.TotalMinutes) + ":" + FormatTime(len.Seconds) + "\n";
            if (fileSize > 0) text += Convert.ToInt32(fileSize / 1024.0 / 1024.0) + " MB\n";
            text += $"{width} x {height}\n";
            text += $"{videoFormat}\n{audioCodec}";
            Core.CommandV("show-text", text, "5000");
        }

        static string FormatTime(double value) => ((int)value).ToString("00");

        public static void ShowProgress()
        {
            TimeSpan position = TimeSpan.FromSeconds(Core.GetPropertyDouble("time-pos"));
            TimeSpan duration = TimeSpan.FromSeconds(Core.GetPropertyDouble("duration"));

            string text = FormatTime(position.TotalMinutes) + ":" +
                          FormatTime(position.Seconds) + " / " +
                          FormatTime(duration.TotalMinutes) + ":" +
                          FormatTime(duration.Seconds) + "    " +
                          DateTime.Now.ToString("H:mm dddd d MMMM", CultureInfo.InvariantCulture);

            Core.CommandV("show-text", text, "5000");
        }

        public static void OpenFromClipboard() => App.InvokeOnMainThread(() =>
        {
            if (WinForms.Clipboard.ContainsFileDropList())
            {
                string[] files = WinForms.Clipboard.GetFileDropList().Cast<string>().ToArray();
                Core.LoadFiles(files, false, Control.ModifierKeys.HasFlag(Keys.Control));
            }
            else
            {
                string clipboard = WinForms.Clipboard.GetText();
                List<string> files = new List<string>();

                foreach (string i in clipboard.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
                    if (i.Contains("://") || File.Exists(i))
                        files.Add(i);

                if (files.Count == 0)
                {
                    App.ShowError("The clipboard does not contain a valid URL or file.");
                    return;
                }

                Core.LoadFiles(files.ToArray(), false, Control.ModifierKeys.HasFlag(Keys.Control));
            }
        });

        public static void LoadSubtitle() => App.InvokeOnMainThread(() =>
        {
            using (var d = new OpenFileDialog())
            {
                string path = Core.GetPropertyString("path");

                if (File.Exists(path))
                    d.InitialDirectory = Path.GetDirectoryName(path);

                d.Multiselect = true;

                if (d.ShowDialog() == DialogResult.OK)
                    foreach (string filename in d.FileNames)
                        Core.CommandV("sub-add", filename);
            }
        });

        public static void LoadAudio() => App.InvokeOnMainThread(() =>
        {
            using (var d = new OpenFileDialog())
            {
                string path = Core.GetPropertyString("path");

                if (File.Exists(path))
                    d.InitialDirectory = Path.GetDirectoryName(path);

                d.Multiselect = true;

                if (d.ShowDialog() == DialogResult.OK)
                    foreach (string i in d.FileNames)
                        Core.CommandV("audio-add", i);
            }
        });

        public static void CycleAudio()
        {
            Core.UpdateExternalTracks();

            lock (Core.MediaTracksLock)
            {
                MediaTrack[] tracks = Core.MediaTracks.Where(track => track.Type == "a").ToArray();

                if (tracks.Length < 1)
                {
                    Core.CommandV("show-text", "No audio tracks");
                    return;
                }

                int aid = Core.GetPropertyInt("aid");

                if (tracks.Length > 1)
                {
                    if (++aid > tracks.Length)
                        aid = 1;

                    Core.SetPropertyInt("aid", aid);
                }

                Core.CommandV("show-text", aid + "/" + tracks.Length + ": " + tracks[aid - 1].Text.Substring(3), "5000");
            }
        }

        public static void CycleSubtitles()
        {
            Core.UpdateExternalTracks();

            lock (Core.MediaTracksLock)
            {
                MediaTrack[] tracks = Core.MediaTracks.Where(track => track.Type == "s").ToArray();

                if (tracks.Length < 1)
                {
                    Core.CommandV("show-text", "No subtitles");
                    return;
                }

                int sid = Core.GetPropertyInt("sid");

                if (tracks.Length > 1)
                {
                    if (++sid > tracks.Length)
                        sid = 0;

                    Core.SetPropertyInt("sid", sid);
                }

                if (sid == 0)
                    Core.CommandV("show-text", "No subtitle");
                else
                    Core.CommandV("show-text", sid + "/" + tracks.Length + ": " + tracks[sid - 1].Text.Substring(3), "5000");
            }
        }

        public static void ShowCommands()
        {
            string jsonString = Core.GetPropertyString("command-list");
            var jsonObject = jsonString.FromJson<List<Dictionary<string, object>>>().OrderBy(i => i["name"]);
            StringBuilder sb = new StringBuilder();

            foreach (Dictionary<string, object> dic in jsonObject)
            {
                sb.AppendLine();
                sb.AppendLine(dic["name"].ToString());

                foreach (Dictionary<string, object> i2 in dic["args"] as List<object>)
                {
                    string value = i2["name"].ToString() + " <" + i2["type"].ToString().ToLower() + ">";

                    if ((bool)i2["optional"] == true)
                        value = "[" + value + "]";

                    sb.AppendLine("    " + value);
                }
            }

            ShowTextWithEditor("command-list", sb.ToString());
        }

        public static void ScaleWindow(float factor) => Core.RaiseScaleWindow(factor);

        public static void WindowScale(float value) => Core.RaiseWindowScaleNET(value);

        public static void ShowText(string text, int duration = 0, int fontSize = 0)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (duration == 0)
                duration = Core.GetPropertyInt("osd-duration");

            if (fontSize == 0)
                fontSize = Core.GetPropertyInt("osd-font-size");

            Core.Command("show-text \"${osd-ass-cc/0}{\\\\fs" + fontSize +
                "}${osd-ass-cc/1}" + text + "\" " + duration);
        }



        public static void PlaylistAdd(int value)
        {
            int pos = Core.PlaylistPos;
            int count = Core.GetPropertyInt("playlist-count");

            if (count < 2)
                return;

            pos = pos + value;

            if (pos < 0)
                pos = count - 1;

            if (pos > count - 1)
                pos = 0;

            Core.SetPropertyInt("playlist-pos", pos);
        }

        public static void PlaylistRandom()
        {
            int count = Core.GetPropertyInt("playlist-count");
            Core.SetPropertyInt("playlist-pos", new Random().Next(count));
        }

        public static void QuickBookmark()
        {
            if (App.QuickBookmark == 0)
            {
                App.QuickBookmark = (float)Core.GetPropertyDouble("time-pos");

                if (App.QuickBookmark != 0)
                    Core.Command("show-text 'Bookmark Saved'");
            }
            else
            {
                Core.SetPropertyDouble("time-pos", App.QuickBookmark);
                App.QuickBookmark = 0;
            }
        }

        public static void MoveWindow(string direction) => Core.RaiseMoveWindow(direction);
    }
}
