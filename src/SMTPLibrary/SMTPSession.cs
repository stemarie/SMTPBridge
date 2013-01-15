using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using SMTPLibrary.Commands;
using SMTPLibrary.Properties;
using fakeSMTP.Commands;

namespace SMTPLibrary
{
    public class SMTPSession : IDisposable
    {
        #region "privatedata"
        // chars allowed in HELO/EHLO string
        internal const string HELO_CHARS = "[]0123456789.-abcdefghijklmnopqrstuvwxyz_";

        // for verbose logging
        const string DIR_TX = "SND";
        const string DIR_RX = "RCV";

        // SMTP command strings
        internal readonly string[] CmdList =
            {
                "\r\n",
                "HELO",
                "EHLO",
                "MAIL FROM:",
                "RCPT TO:",
                "DATA",
                "RSET",
                "QUIT",
                "VRFY",
                "EXPN",
                "HELP",
                "NOOP"
            };

        // command ID mapping codes (must match the list above)
        public enum CmdID
        {
            Invalid,
            Helo,
            Ehlo,
            MailFrom,
            RcptTo,
            Data,
            Rset,
            Quit,
            Vrfy,
            Expn,
            Help,
            Noop
        }

        // network/system
        private bool _initOk = false;                    // true = initialized
        internal readonly string HostName = null;        // local host name for banner
        private TcpClient _client;                       // remote client
        private readonly NetworkStream _stream;          // network stream for I/O
        private readonly StreamReader _reader;           // network reader
        private readonly StreamWriter _writer;           // network writer
        public readonly long _sessCount = 0;            // current session count
        public readonly string _sessionID = null;       // ID for this session
        private long _lastMsgID = -1;                    // last logged message #
        private bool _timedOut = false;                  // true = the connection timed out

        // local domains/mailboxes
        internal readonly List<string> MailDomains = new List<string>();         // list of locally handled domains
        internal readonly List<string> MailBoxes = new List<string>();           // list of locally handled mailboxes

        // session
        public DateTime _startDate = DateTime.UtcNow;       // session start datetime
        public readonly string ClientIP = null;                   // remote IP
        public string _dnsListType = null;                // type of listing
        public string _dnsListName = null;                // name of DNS list flagging the IP
        public string _dnsListValue = null;               // value returned by the DNS list
        internal CmdID LastCmd = CmdID.Invalid;           // last cmd issued
        public string HeloStr = null;                    // HELO/EHLO string
        public string MailFrom = null;                   // MAIL FROM address
        public List<string> RcptTo = new List<string>();       // RCPT TO list
        public long _msgCount = 0;                      // # of messages for this session
        public string _msgFile = null;                    // message file storage
        private bool _earlyTalker = false;               // true the client is a "early talker"
        public int NoopCount = 0;                     // # of NOOP issued
        public int ErrCount = 0;                      // # of errors
        public int VrfyCount = 0;                     // # of VRFY/EXPN

        // workareas
        internal string MailBox = null;                    // mailbox part of a mail address
        internal string MailDom = null;                    // domain part of a mail address
        #endregion

        #region "instance"
        // init
        public SMTPSession(TcpClient client)
        {
            try
            {
                _sessCount = AppGlobals.AddSession();
                _sessionID = AppGlobals.SessionID();
                HostName = AppGlobals.HostName;

                if (null != AppGlobals.LocalDomains)
                    MailDomains = AppGlobals.LocalDomains;
                if (null != AppGlobals.LocalMailBoxes)
                    MailBoxes = AppGlobals.LocalMailBoxes;

                _client = client;
                ClientIP = _client.Client.RemoteEndPoint.ToString();
                int i = ClientIP.IndexOf(':');
                if (-1 != i) ClientIP = ClientIP.Substring(0, i);
                _client.ReceiveTimeout = AppGlobals.ReceiveTimeout;

                _stream = _client.GetStream();
                _reader = new StreamReader(_stream);
                _writer = new StreamWriter(_stream) { NewLine = "\r\n", AutoFlush = true };

                AppGlobals.WriteConsole(Resources.MSG_Connected, ClientIP, _sessCount, _sessionID);
                _initOk = true;
            }
            catch (Exception ex)
            {
                AppGlobals.WriteConsole(Resources.MSG_Exception + ex.Message);
                CloseSession();
            }
        }
        #endregion

        #region "methods"
        public void HandleSession()
        {
            string cmdLine = "?";
            Context context = new Context { Session = this };
            CommandOk commandOk = new CommandOk();
            string response = commandOk.GetResponse();
            CmdID currCmd = CmdID.Invalid;
            bool connOk = true;

            if (false == _initOk)
            {
                CloseSession();
                return;
            }

            // sessions limit reached, reject session
            if (_sessCount > AppGlobals.MaxSessions)
            {
                if (connOk) SendLine(Resources.MSG_421_ServiceTemporarilyUnavailable);
                CloseSession();
                return;
            }

            // if the remote IP isn't a private one
            if (!IsPrivateIP(ClientIP))
            {
                // checks the incoming IP against whitelists, if listed skip blacklist checks
                bool isDnsListed = IsListed(ClientIP, AppGlobals.WhiteLists, "white");
                if (!isDnsListed)
                {
                    // check the IP against blacklists
                    isDnsListed = IsListed(ClientIP, AppGlobals.BlackLists, "black");
                    if ((isDnsListed) && (!AppGlobals.StoreData))
                    {
                        // if blacklisted and NOT storing messages
                        SendLine(string.Format(Resources.MSG_442_ConnectionTimedOut, ClientIP, _dnsListName));
                        CloseSession();
                        return;
                    }
                }
            }

            // add a short delay before banner and check for early talker
            // see http://wiki.asrg.sp.am/wiki/Early_talker_detection
            SleepDown(AppGlobals.BannerDelay);
            _earlyTalker = IsEarlyTalker();
            if (_earlyTalker)
            {
                SendLine(Resources.MSG_554_MisbehavedSMTPSession);
                CloseSession();
                return;
            }

            // all ok, send out our banner
            CommandBanner commandBanner = new CommandBanner { Context = context };
            connOk = SendLine(commandBanner.GetResponse());
            while ((null != cmdLine) && connOk)
            {
                if (LastCmd == CmdID.Data)
                {
                    string mailMsg = RecvData();
                    if (_timedOut)
                    {
                        // got a receive timeout during the DATA phase
                        if (connOk) SendLine(Resources.MSG_442_ConnectionTimedOut);
                        CloseSession();
                        return;
                    }
                    CommandDot commandDot = new CommandDot { Context = context };
                    response = commandDot.GetResponse();
                    if (String.IsNullOrEmpty(mailMsg))
                        response = Resources.MSG_422_MailboxExceededQuota;
                    else
                    {
                        IProcessor processor = new FileProcessor { Context = context };
                        processor.Process(mailMsg);
                        if (AppGlobals.DoTempFail)
                        {
                            // emit a tempfail AFTER storing the mail DATA
                            if (connOk) SendLine(Resources.MSG_421_ServiceTemporarilyUnavailable);
                            CloseSession();
                            return;
                        }
                    }
                    ResetSession();
                }
                else
                {
                    // read an SMTP command line and deal with the command
                    cmdLine = RecvLine();
                    if (null != cmdLine)
                    {
                        LogCmdAndResp(DIR_RX, cmdLine);
                        currCmd = GetCommandID(cmdLine);
                        context.CmdLine = cmdLine;

                        switch (currCmd)
                        {
                            case CmdID.Helo:            // HELO
                                CommandHelo commandHelo = new CommandHelo { Context = context };
                                response = commandHelo.GetResponse();
                                break;
                            case CmdID.Ehlo:            // EHLO
                                CommandHelo commandEhlo = new CommandHelo { Context = context };
                                response = commandEhlo.GetResponse();
                                break;
                            case CmdID.MailFrom:        // MAIL FROM:
                                CommandMailFrom commandMailFrom = new CommandMailFrom { Context = context };
                                response = commandMailFrom.GetResponse();
                                break;
                            case CmdID.RcptTo:          // RCPT TO:
                                CommandRcptTo commandRcptTo = new CommandRcptTo { Context = context };
                                response = commandRcptTo.GetResponse();
                                break;
                            case CmdID.Data:            // DATA
                                if ((AppGlobals.DoTempFail) && (!AppGlobals.StoreData))
                                {
                                    // emit a tempfail upon receiving the DATA command
                                    response = Resources.MSG_421_ServiceTemporarilyUnavailable;
                                    cmdLine = null;
                                    LastCmd = currCmd = CmdID.Quit;
                                }
                                else
                                {
                                    CommandData commandData = new CommandData { Context = context };
                                    response = commandData.GetResponse();
                                }
                                break;
                            case CmdID.Rset:            // RSET
                                CommandRset commandRset = new CommandRset { Context = context };
                                response = commandRset.GetResponse();
                                break;
                            case CmdID.Quit:            // QUIT
                                CommandQuit commandQuit = new CommandQuit { Context = context };
                                response = commandQuit.GetResponse();
                                cmdLine = null; // force closing
                                break;
                            case CmdID.Vrfy:            // VRFY
                                CommandVrfy commandVrfy = new CommandVrfy { Context = context };
                                response = commandVrfy.GetResponse();
                                break;
                            case CmdID.Expn:            // EXPN
                                CommandVrfy commandExpn = new CommandVrfy { Context = context };
                                response = commandExpn.GetResponse();
                                break;
                            case CmdID.Help:            // HELP
                                CommandHelp commandHelp = new CommandHelp { Context = context };
                                response = commandHelp.GetResponse();
                                break;
                            case CmdID.Noop:            // NOOP
                                CommandNoop commandNoop = new CommandNoop { Context = context };
                                response = commandNoop.GetResponse();
                                break;
                            default:                    // unkown/unsupported
                                CommandUnknown commandUnknown = new CommandUnknown { Context = context };
                                response = commandUnknown.GetResponse();
                                break;
                        }
                    }
                    else
                    {
                        // the read timed out (or we got an error), emit a message and drop the connection
                        response = Resources.MSG_442_ConnectionTimedOut;
                        currCmd = CmdID.Quit;
                    }
                }

                // send response
                if ((ErrCount > 0) && (CmdID.Quit != currCmd))
                {
                    // tarpit a bad client, time increases with error count
                    SleepDown(AppGlobals.ErrorDelay * ErrCount);
                }
                else
                {
                    // add a short delay
                    SleepDown(25);
                }

                // checks for early talkers
                _earlyTalker = IsEarlyTalker();

                // send out the response
                connOk = SendLine(response);

                // check/enforce hard limits (errors, vrfy ...)
                if ((CmdID.Quit != currCmd) && (connOk))
                {
                    string errMsg = null;
                    if (_msgCount > AppGlobals.MaxMessages)
                    {
                        // above max # of message in a single session
                        errMsg = Resources.MSG_451_SessionMessagesCountExceeded;
                    }
                    else if (ErrCount > AppGlobals.MaxSmtpErr)
                    {
                        // too many errors
                        errMsg = Resources.MSG_550_MaxErrors;
                    }
                    else if (VrfyCount > AppGlobals.MaxSmtpVrfy)
                    {
                        // tried to VRFY/EXPN too many addresses
                        errMsg = Resources.MSG_451_MaxRecipients;
                    }
                    else if (NoopCount > AppGlobals.MaxSmtpNoop)
                    {
                        // entered too many NOOP commands
                        errMsg = Resources.MSG_451_MaxNoop;
                    }
                    else if (RcptTo.Count > AppGlobals.MaxSmtpRcpt)
                    {
                        // too many recipients for a single message
                        errMsg = Resources.MSG_452_TooManyRecipients;
                    }
                    else if (_earlyTalker)
                    {
                        // early talker
                        errMsg = Resources.MSG_554_MisbehavedSMTPSession;
                    }
                    if (null != errMsg)
                    {
                        if (connOk) connOk = SendLine(errMsg);
                        cmdLine = null; // force closing
                    }
                }

                // check if connection Ok
                if (connOk) connOk = _client.Connected;
            } // while null...

            // close/reset this session
            CloseSession();
        }
        #endregion

        #region "privatecode"
        // retrieves the command ID from command line args
        internal CmdID GetCommandID(string cmdLine)
        {
            CmdID id = CmdID.Invalid;
            string tmpBuff = cmdLine.ToUpperInvariant();

            for (int i = 0; i < CmdList.Length; i++)
            {
                if (tmpBuff.StartsWith(CmdList[i]))
                {
                    id = (CmdID)i;
                    break;
                }
            }
            return id;
        }

        // resets the internal session values
        internal void ResetSession()
        {
            LogSession(); // logs the session/message to file (if data available) 
            MailFrom = null;
            RcptTo = new List<string>();
            _msgFile = null;
            NoopCount = 0;
            ErrCount = 0;
            VrfyCount = 0;
        }

        // closes the socket, terminates the session
        private void CloseSession()
        {
            if (null != _client)
            {
                if (_client.Connected)
                    SleepDown(25);
                try { _client.Close(); _client = null; }
                catch { }
                if (!string.IsNullOrEmpty(ClientIP))
                    AppGlobals.WriteConsole(Resources.MSG_Disconnected, ClientIP, _sessCount, _sessionID);
            }
            _initOk = false;
            long sesscount = AppGlobals.RemoveSession();
            ResetSession();
        }

        // coarse checks on the email address (todo: replace with regexp)
        internal bool CheckMailAddr(string mailAddr)
        {
            // init
            MailBox = MailDom = null;
            string email = CleanupString(mailAddr).ToLowerInvariant();

            // shouldn't be empy and must contain at least a @ and a dot
            if (string.IsNullOrEmpty(email)) return false;
            if (!email.Contains('@')) return false;
            if (!email.Contains('.')) return false;

            // if starting with a "<" must end with a ">"
            char[] chars = email.ToCharArray();
            if ('<' == chars[0])
            {
                if ('>' != chars[email.Length - 1]) return false;
                email = email.Replace('<', ' ');
                email = email.Replace('>', ' ');
                email = CleanupString(email);
                if (email.Length < 1) return false;
            }

            // can't contain a space
            if (email.Contains(' ')) return false;

            // the "@" must be unique
            string[] parts = email.Split('@');
            if (2 != parts.Length) return false;

            // cleanup and check parts
            for (int p = 0; p < parts.Length; p++)
            {
                parts[p] = CleanupString(parts[p]);
                if (string.IsNullOrEmpty(parts[p])) return false;
            }

            // formally checks domain (and TLD)
            if (!parts[1].Contains('.')) return false;
            if (parts[1].StartsWith(".")) return false;
            if (parts[1].EndsWith(".")) return false;
            string[] domain = parts[1].Split('.');
            if (domain.Length < 2) return false;
            foreach (string t in domain)
            {
                if (string.IsNullOrEmpty(t)) return false;
                if (t.StartsWith("-")) return false;
            }
            string TLD = domain[domain.Length - 1];
            if (TLD.Length < 2) return false;

            // store mailbox and domain
            MailBox = parts[0];
            MailDom = parts[1];

            return true;
        }

        // sends a line to remote
        private bool SendLine(string line)
        {
            try
            {
                LogCmdAndResp(DIR_TX, line);
                _writer.WriteLine(line);
                return true;
            }
            catch //(Exception ex)
            {
                //AppGlobals.writeConsole("sendLine(id={0},ip={1}): {2}", this._sessionID, this._clientIP, ex.Message);
                return false;
            }
        }

        // checks the receive buffer (used for early talkers)
        private bool RecvPeek()
        {
            bool result;

            try { result = _client.GetStream().DataAvailable; }
            catch { result = false; }
            return result;
        }


        // receives a line from remote
        private string RecvLine()
        {
            string line = null;

            try
            {
                if (_client.Connected)
                    line = _reader.ReadLine();
            }
            catch //(Exception ex)
            {
                //AppGlobals.writeConsole("recvLine(id={0},ip={1}): {2}", this._sessionID, this._clientIP, ex.Message);
                _timedOut = true;
                ErrCount++;
                line = null;
            }
            return line;
        }

        // receive a full data buffer from remote
        private string RecvData()
        {
            try
            {
                StringBuilder buff = new StringBuilder();
                string line = "?";
                bool aboveMaxSize = false;

                while (null != line)
                {
                    line = RecvLine();
                    if (null != line)
                    {
                        if (AppGlobals.StoreData)
                        {
                            if (!aboveMaxSize)
                            {
                                if (buff.Length < AppGlobals.MaxDataSize)
                                    buff.AppendLine(line);
                                else
                                    aboveMaxSize = true;
                            }
                        }
                        if (line.Equals(".", StringComparison.InvariantCultureIgnoreCase))
                            line = null;
                    }
                }
                if (aboveMaxSize) return null;
                if (!AppGlobals.StoreData) buff.AppendLine(".");
                return buff.ToString();
            }
            catch //(Exception ex)
            {
                //AppGlobals.writeConsole("recvData(id={0},ip={1}): {2}", this._sessionID, this._clientIP, ex.Message);
                return null;
            }
        }

        // splits an SMTP command into command and argument(s)
        internal List<string> ParseCmdLine(CmdID id, string cmdLine)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(cmdLine)) return parts;
            try
            {
                string cmdStr = CmdList[(int)id];
                string curCmd = CleanupString(cmdLine);

                int pos = -1;
                pos = cmdLine.IndexOf(cmdStr.Contains(':') ? ':' : ' ');
                if (-1 != pos)
                {
                    string cmd = CleanupString(cmdLine.Substring(0, pos));
                    string arg = CleanupString(cmdLine.Substring(pos + 1));
                    parts.Add(cmd.ToUpper());
                    parts.Add(arg);
                }
                else
                    parts.Add(CleanupString(cmdLine).ToUpper());
            }
            catch
            {
                parts = new List<string>();
            }

            return parts;
        }

        // cleans a string
        private static string CleanupString(string inputStr)
        {
            // setup...
            if (string.IsNullOrEmpty(inputStr)) return null;
            string strBuff = inputStr.Trim();
            char[] chars = strBuff.ToCharArray();

            // turn control chars into spaces
            for (int c = 0; c < chars.Length; c++)
            {
                char chr = chars[c];
                if ((char.IsWhiteSpace(chr) || char.IsControl(chr)) && (!chr.Equals(' ')))
                {
                    chars[c] = ' '; // turn controls/tabs/... into spaces
                }
            }

            // trim, remove double spaces, trim again
            string result = new string(chars).Trim();
            while (result.Contains("  "))
                result.Replace("  ", " ");
            return result.Trim();
        }

        // check for early talkers, that is clients which won't wait
        // for the response and keep sending in commands/stuff, those
        // are usually spambots or the like, so let's deal with them
        private bool IsEarlyTalker()
        {
            if (!AppGlobals.EarlyTalkers) return false;
            bool tooEarly = false;
            if (RecvPeek())
            {
                ErrCount++;
                tooEarly = true;
            }
            return tooEarly;
        }

        // "sleeps" for the given time
        private static void SleepDown(int milliSeconds)
        {
            Thread.Sleep(milliSeconds);
        }

        // checks an IPv4 against DNS lists
        // todo: add parallel lookups to speed things up, stop
        //       the lookups upon the first positive hit
        private bool IsListed(string ip, string[] lists, string listType)
        {
            if ((null == lists) || (lists.Length < 1)) return false;
            foreach (string t in lists)
            {
                string queryString = BuildDnsListQuery(ip, t);
                string result = QueryDNS(queryString);
                if (!string.IsNullOrEmpty(result))
                {
                    _dnsListType = listType;
                    _dnsListName = t;
                    _dnsListValue = result;
                    return true;
                }
            }
            return false;
        }

        // true = the IP falls into a private/reserved range
        // see RFC-1918, RFC-3330, RFC-3927 for details
        private static bool IsPrivateIP(string ip)
        {
            // 127/8, 10/8, 192.168/16, 169.254/16, 192.0.2/24
            if (ip.StartsWith("127.") ||
                ip.StartsWith("10.") ||
                ip.StartsWith("192.168.") ||
                ip.StartsWith("169.254.") ||
                ip.StartsWith("192.0.2.")
                ) return true;

            // 172.16/12
            string[] octets = ip.Split(".".ToCharArray(), 4);
            if (octets[0].Equals("172"))
            {
                int octet = int.Parse(octets[1]);
                if ((octet > 15) && (octet < 32)) return true;
            }

            return false;
        }

        // reverse an IPv4 and appends the domain name
        private static string BuildDnsListQuery(string ip, string domain)
        {
            string[] octets = ip.Split(".".ToCharArray(), 4);

            return JoinParts(octets[3], octets[2], octets[1], octets[0], domain);
        }

        // joins the given parms using dots as separators
        private static string JoinParts(params string[] args)
        {
            StringBuilder ret = new StringBuilder();
            foreach (String s in args)
                ret.AppendFormat("{0}.", s);

            return ret.ToString().Substring(0, ret.ToString().Length - 1);
        }

        // runs a DNS query
        private static string QueryDNS(string query)
        {
            string result = null;

            try
            {
                IPHostEntry entry = Dns.GetHostEntry(query);
                if (null != entry)
                {
                    List<string> buff = entry.AddressList.Select(t => t.ToString()).ToList();
                    result = string.Join("+", buff);
                }
            }
            catch
            {
                //
            }
            return result;
        }

        // if enabled, logs commands and replies
        private void LogCmdAndResp(string direction, string line)
        {
            if (AppGlobals.LogVerbose)
                AppGlobals.LogMessage("{0}:{1} {2}: {3}", ClientIP, _sessionID, direction, line);
        }

        // logs session infos to logfile (at each mail); if you want to change
        // the log record format, this is the place to do it, just change the
        // "cols.Add" to include the columns you want and there you'll go :-)
        private void LogSession()
        {
            // check if already logged
            if (_lastMsgID == _msgCount) return;
            _lastMsgID = _msgCount;

            // check if we got some data
            if (string.IsNullOrEmpty(HeloStr)) HeloStr = "-no-helo-";
            if (string.IsNullOrEmpty(MailFrom)) MailFrom = "-no-from-";
            // if (0 == this._rcptTo.Count) return;

            // build the log array
            List<string> cols = new List<string>
                {
                    DateTime.UtcNow.ToString("u"),
                    _startDate.ToString("u"),
                    _sessionID,
                    ClientIP,
                    HeloStr,
                    !string.IsNullOrEmpty(MailFrom) ? MailFrom : ""
                };

            // current date/time

            // start date, session ID, client IP, helo

            // mail from

            // rcpt to
            if (RcptTo.Count > 0)
            {
                cols.Add(RcptTo.Count.ToString());
                cols.Add(string.Join(",", RcptTo));
            }
            else
            {
                cols.Add("0");
                cols.Add("-no-rcpt-");
            }

            // message # and message file name (if any)
            cols.Add(_msgCount.ToString());
            cols.Add(!string.IsNullOrEmpty(_msgFile) ? _msgFile : "-no-file-");

            // dns listing
            if (!string.IsNullOrEmpty(_dnsListType))
            {
                cols.Add(_dnsListType);
                cols.Add(_dnsListName);
                cols.Add(_dnsListValue);
            }
            else
            {
                cols.Add("-not-listed-");
                cols.Add("-none-");
                cols.Add("0.0.0.0");
            }

            // early talker
            cols.Add(_earlyTalker ? "1" : "0");

            // noop/vrfy/err
            cols.Add(NoopCount.ToString());
            cols.Add(VrfyCount.ToString());
            cols.Add(ErrCount.ToString());

            // builds and logs the record
            //string logRec = string.Join("|", cols);
            //AppGlobals.logSession("{0}", logRec);

            // builds the log record format string
            StringBuilder logFmt = new StringBuilder("{0}");
            for (int i = 1; i < cols.Count; i++)
                logFmt.Append("|{" + i + "}");

            // log the record
            AppGlobals.LogSession(logFmt.ToString(), cols.ToArray<string>());
        }
        #endregion

        #region IDisposable

        bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SMTPSession()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                CloseSession();
                if (_reader != null)
                    _reader.Dispose();

                if (_stream != null)
                    _stream.Dispose();

                if (_writer != null)
                    _writer.Dispose();

                // free other managed objects that implement
                // IDisposable only
            }

            // release any unmanaged objects
            // set the object references to null

            _disposed = true;
        }

        #endregion
    }
}
