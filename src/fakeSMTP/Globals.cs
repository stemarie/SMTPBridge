using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.IO;

namespace FakeSMTP
{
    public static class AppGlobals
    {
        #region "privateData"
        private static IPAddress _listenIP = IPAddress.Loopback;

        private static string _logPath = null;

        // sessions count
        private static readonly object LkSessions = new object();
        private static long _sessions = 0;

        private static readonly object LkSessID = new object();
        private static long _sessID = 0;

        private static readonly object LkAppLog = new object();
        private static readonly object LkSesLog = new object();

        static AppGlobals()
        {
            LocalMailBoxes = null;
            LocalDomains = null;
            ErrorDelay = 0;
            BannerDelay = 0;
            MaxSmtpRcpt = 0;
            MaxSmtpVrfy = 0;
            MaxSmtpNoop = 0;
            MaxSmtpErr = 0;
            BlackLists = null;
            WhiteLists = null;
            EarlyTalkers = false;
            StorePath = null;
            MaxDataSize = 0;
            StoreData = false;
            MaxMessages = 0;
            MaxSessions = 0;
            LogVerbose = false;
            DoTempFail = false;
            HostName = null;
            ReceiveTimeout = 0;
            ListenPort = 0;
            ListenAddress = null;
        }

        #endregion

        #region "properties"
        // application name
        public static string AppName
        {
            get
            {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            }
        }

        // application version
        public static string AppVersion
        {
            get
            {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        // runtime version
        public static string AppRuntime
        {
            get
            {
                return System.Reflection.Assembly.GetExecutingAssembly().ImageRuntimeVersion;
            }
        }

        // listen IP
        public static IPAddress ListenIP
        {
            get { return _listenIP; }
            set { _listenIP = value; }
        }

        // listen address (as a string)
        public static string ListenAddress { get; set; }

        // listen port
        public static int ListenPort { get; set; }

        // timeout for receiving commands/data (milliseconds)
        public static int ReceiveTimeout { get; set; }

        // host name (used for banner, if blank retrieved from network settings)
        public static string HostName { get; set; }

        // true = terminate sessions with a 4xx temporary failure
        public static bool DoTempFail { get; set; }

        // path for log file(s)
        public static string LogPath
        {
            get { return _logPath; }
            set { _logPath = value; }
        }

        // verbose logging
        public static bool LogVerbose { get; set; }

        // max # of parallel sessions allowed
        public static long MaxSessions { get; set; }

        // max # of messages in a single session
        public static int MaxMessages { get; set; }

        // true = store the email envelope/mime data to files
        public static bool StoreData { get; set; }

        // max size for a given mail message (DATA)
        public static long MaxDataSize { get; set; }

        // path to store the mail data
        public static string StorePath { get; set; }

        // early talkers check
        public static bool EarlyTalkers { get; set; }

        // whitelist to check incoming IPs
        public static string[] WhiteLists { get; set; }

        // blacklists to check incoming IPs
        public static string[] BlackLists { get; set; }

        // max # of smtp errors for a session
        public static int MaxSmtpErr { get; set; }

        // max # of smtp NOOP commands for a session
        public static int MaxSmtpNoop { get; set; }

        // max # of smtp VRFY commands for a session
        public static int MaxSmtpVrfy { get; set; }

        // max # of smtp RCPT TO for a session
        public static int MaxSmtpRcpt { get; set; }

        // delay before emitting the banner
        public static int BannerDelay { get; set; }

        // delay for responses after errors
        public static int ErrorDelay { get; set; }

        // locally handled domains
        public static List<string> LocalDomains { get; set; }

        // locally handled mailboxes
        public static List<string> LocalMailBoxes { get; set; }

        #endregion

        #region "methods"
        // increase the global session count
        public static long AddSession()
        {
            long ret;

            lock (LkSessions)
            {
                ret = ++_sessions;
            }
            return ret;
        }

        // decrease the global session count
        public static long RemoveSession()
        {
            long ret;
            lock (LkSessions)
            {
                if (--_sessions < 0) _sessions = 0;
                ret = _sessions;
            }
            return ret;
        }

        // get a session ID#
        public static string SessionID()
        {
            string ret;

            lock (LkSessID)
            {
                if (_sessID == long.MaxValue) _sessID = 0;
                ret = string.Format("{0:X}{1:X}", DateTime.Now.Ticks, ++_sessID);
            }
            return ret;
        }

        // writes a message to console
        public static void WriteConsole(string format, params object[] args)
        {
            try
            {
                Debug.WriteLine(string.Format(format, args));
                LogMessage(format, args);
                Console.Out.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.ffff") + " " + String.Format(format, args));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("writeConsole::Exception: " + ex.Message);
            }
        }

        // writes a message to the log file
        public static void LogMessage(string format, params object[] args)
        {
            lock (LkAppLog)
            {
                try
                {
                    Debug.WriteLine(string.Format(format, args));
                    string logFile = _logPath + "fakesmtp-" + DateTime.UtcNow.ToString("MM") + ".log";
                    RollFile(logFile);
                    using (StreamWriter fp = new StreamWriter(logFile, true))
                    {
                        fp.WriteLine(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " " + string.Format(format, args));
                        fp.Flush();
                        fp.Close();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("logMessage::Exception: " + ex.Message);
                }
            }
        }

        // writes a session record to the log file
        public static void LogSession(string format, params object[] args)
        {
            lock (LkSesLog)
            {
                try
                {
                    Debug.WriteLine(string.Format(format, args));
                    string logFile = _logPath + "smtpsess-" + DateTime.UtcNow.ToString("MM") + ".log";
                    RollFile(logFile);
                    using (StreamWriter fp = new StreamWriter(logFile, true))
                    {
                        fp.WriteLine(string.Format(format, args));
                        fp.Flush();
                        fp.Close();
                    }
                }
                catch (Exception ex)
                {
                    WriteConsole("logSession::Exception: {0}", ex.Message);
                }
            }
        }

        // loads a text file and returns it as a string list
        public static List<string> LoadFile(string fileName)
        {
            List<string> lines = new List<string>();
            try
            {
                using (StreamReader fp = new StreamReader(fileName))
                {
                    string buffer = null;
                    while (null != (buffer = fp.ReadLine()))
                    {
                        // skip empty lines and comment lines (#=comment sign)
                        if (!string.IsNullOrEmpty(buffer))
                            if (!buffer.StartsWith("#"))
                                lines.Add(buffer);
                    }
                    fp.Close();
                }
            }
            catch
            {
                lines = new List<string>();
            }
            return lines;
        }
        #endregion

        #region "privatecode"
        // checks if a file needs "rolling"
        private static void RollFile(string pathName)
        {
            try
            {
                if (File.Exists(pathName))
                {
                    DateTime lastWrite = File.GetLastWriteTime(pathName);
                    if (!DateTime.Now.Year.Equals(lastWrite.Year))
                    {
                        File.Delete(pathName);
                    }

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("rollFile::Exception: " + ex.Message);
            }
        }
        #endregion
    }
}
