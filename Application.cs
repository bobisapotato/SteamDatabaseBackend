﻿/*
 * Copyright (c) 2013-2015, SteamDB. All rights reserved.
 * Use of this source code is governed by a BSD-style license that can be
 * found in the LICENSE file.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Amib.Threading;
using MySql.Data.MySqlClient;
using Timer = System.Timers.Timer;

namespace SteamDatabaseBackend
{
    static class Application
    {
        public static readonly List<GCIdler> GCIdlers;

        public static Timer ChangelistTimer { get; private set; }

        public static SmartThreadPool ProcessorPool { get; private set; }
        public static SmartThreadPool SecondaryPool { get; private set; }

        public static Dictionary<uint, byte> OwnedApps { get; set; }
        public static Dictionary<uint, byte> OwnedSubs { get; set; }

        public static Dictionary<uint, byte> ImportantApps { get; private set; }
        public static Dictionary<uint, byte> ImportantSubs { get; private set; }

        public static ConcurrentDictionary<uint, IWorkItemResult> ProcessedApps { get; private set; }
        public static ConcurrentDictionary<uint, IWorkItemResult> ProcessedSubs { get; private set; }

        static Application()
        {
            ProcessorPool = new SmartThreadPool(new STPStartInfo { WorkItemPriority = WorkItemPriority.Highest, MaxWorkerThreads = 50 });
            SecondaryPool = new SmartThreadPool();

            ProcessorPool.Name = "Processor Pool";
            SecondaryPool.Name = "Secondary Pool";

            OwnedApps = new Dictionary<uint, byte>();
            OwnedSubs = new Dictionary<uint, byte>();

            ImportantApps = new Dictionary<uint, byte>();
            ImportantSubs = new Dictionary<uint, byte>();

            ProcessedApps = new ConcurrentDictionary<uint, IWorkItemResult>();
            ProcessedSubs = new ConcurrentDictionary<uint, IWorkItemResult>();

            GCIdlers = new List<GCIdler>();

            ChangelistTimer = new Timer();
            ChangelistTimer.Elapsed += Tick;
            ChangelistTimer.Interval = TimeSpan.FromSeconds(1).TotalMilliseconds;
        }

        public static void Init()
        {
            var thread = new Thread(new ThreadStart(Steam.Instance.Tick));
            thread.Name = "Steam";
            thread.Start();

            if (Settings.IsFullRun)
            {
                return;
            }

            ReloadImportant();

            var commandHandler = new CommandHandler();

            Steam.Instance.RegisterCommandHandlers(commandHandler);

            if (Settings.Current.IRC.Enabled)
            {
                thread = new Thread(new ThreadStart(IRC.Instance.Connect));
                thread.Name = "IRC";
                thread.Start();

                IRC.Instance.RegisterCommandHandlers(commandHandler);
            }

            foreach (var appID in Settings.Current.GameCoordinatorIdlers)
            {
                var instance = new GCIdler(appID);

                thread = new Thread(new ThreadStart(instance.Run));
                thread.Name = string.Format("GC Idler {0}", appID);
                thread.Start();

                GCIdlers.Add(instance);
            }
        }

        private static void Tick(object sender, System.Timers.ElapsedEventArgs e)
        {
            Steam.Instance.Apps.PICSGetChangesSince(Steam.Instance.PICSChanges.PreviousChangeNumber, true, true);
        }

        public static void ReloadImportant(CommandArguments command)
        {
            ReloadImportant();

            CommandHandler.ReplyToCommand(command, "Reloaded {0} important apps and {1} packages", ImportantApps.Count, ImportantSubs.Count);
        }

        private static void ReloadImportant()
        {
            using (MySqlDataReader Reader = DbWorker.ExecuteReader("SELECT `AppID` FROM `ImportantApps` WHERE `Announce` = 1"))
            {
                ImportantApps.Clear();

                while (Reader.Read())
                {
                    ImportantApps.Add(Reader.GetUInt32("AppID"), 1);
                }
            }

            using (MySqlDataReader Reader = DbWorker.ExecuteReader("SELECT `SubID` FROM `ImportantSubs`"))
            {
                ImportantSubs.Clear();

                while (Reader.Read())
                {
                    ImportantSubs.Add(Reader.GetUInt32("SubID"), 1);
                }
            }

            Log.WriteInfo("Application", "Loaded {0} important apps and {1} packages", ImportantApps.Count, ImportantSubs.Count);
        }
    }
}