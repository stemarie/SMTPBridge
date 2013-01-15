using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using fakeSMTP;
using fakeSMTP.Commands;
using fakeSMTP.Properties;

namespace FakeSMTP
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
        private readonly long _sessCount = 0;            // current session count
        private readonly string _sessionID = null;       // ID for this session
        private long _lastMsgID = -1;                    // last logged message #
        private bool _timedOut = false;                  // true = the connection timed out

        // local domains/mailboxes
        internal readonly List<string> MailDomains = new List<string>();         // list of locally handled domains
        internal readonly List<string> MailBoxes = new List<string>();           // list of locally handled mailboxes

        // session
        private DateTime _startDate = DateTime.UtcNow;       // session start datetime
        internal readonly string ClientIP = null;                   // remote IP
        private string _dnsListType = null;                // type of listing
        private string _dnsListName = null;                // name of DNS list flagging the IP
        private string _dnsListValue = null;               // value returned by the DNS list
        internal CmdID LastCmd = CmdID.Invalid;           // last cmd issued
        internal string HeloStr = null;                    // HELO/EHLO string
        internal string MailFrom = null;                   // MAIL FROM address
        internal List<string> RcptTo = new List<string>();       // RCPT TO list
        private long _msgCount = 0;                      // # of messages for this session
        private string _msgFile = null;                    // message file storage
        private bool _earlyTalker = false;               // true the client is a "early talker"
        internal int NoopCount = 0;                     // # of NOOP issued
        internal int ErrCount = 0;                      // # of errors
        internal int VrfyCount = 0;                     // # of VRFY/EXPN

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
                _writer = new StreamWriter(_stream);
                _writer.NewLine = "\r\n";
                _writer.AutoFlush = true;

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
                        ProcessMailMsg(mailMsg);
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
            for (int p = 0; p < domain.Length; p++)
            {
                if (string.IsNullOrEmpty(domain[p])) return false;
                if (domain[p].StartsWith("-")) return false;
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
            char chr;

            // turn control chars into spaces
            for (int c = 0; c < chars.Length; c++)
            {
                chr = chars[c];
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
            string queryString = null;
            for (int i = 0; i < lists.Length; i++)
            {
                queryString = BuildDnsListQuery(ip, lists[i]);
                string result = QueryDNS(queryString);
                if (!string.IsNullOrEmpty(result))
                {
                    _dnsListType = listType;
                    _dnsListName = lists[i];
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
            IPHostEntry entry = null;
            string result = null;

            try
            {
                entry = Dns.GetHostEntry(query);
                if (null != entry)
                {
                    List<string> buff = new List<string>();
                    for (int i = 0; i < entry.AddressList.Length; i++)
                        buff.Add(entry.AddressList[i].ToString());
                    result = string.Join("+", buff);
                }
            }
            catch
            {
                //
            }
            return result;
        }

        // stores a mail message to file, notice that the code doesn't even
        // try to deal with message headers and mime parts nor to check if
        // they're correct, this isn't the purpose for this code, but willing
        // to add such parsing/checks, you may either add them here or after
        // receiving the "." command at end of the DATA stage
        private void ProcessMailMsg(string msgData)
        {
            // bump the message counter
            _msgCount++;
            if (!AppGlobals.StoreData) return;

            try
            {
                // build the pathname of the file used to store this email
                string filePath = AppGlobals.StorePath;
                string fileName = "mailmsg-" + Path.GetRandomFileName().Replace('.', '-') + ".txt";

                // record the file name
                _msgFile = fileName;

                // open the file for writing
                using (StreamWriter fp = new StreamWriter(filePath + fileName, true))
                {
                    fp.WriteLine("X-FakeSMTP-HostName: {0}", AppGlobals.HostName);
                    fp.WriteLine("X-FakeSMTP-Sessions: count={0}, id={1}", _sessCount, _sessionID);
                    fp.WriteLine("X-FakeSMTP-MsgCount: {0}", _msgCount);
                    fp.WriteLine("X-FakeSMTP-SessDate: {0}", _startDate.ToString("u"));
                    fp.WriteLine("X-FakeSMTP-ClientIP: {0}", ClientIP);
                    if (null != _dnsListType)
                        fp.WriteLine("X-FakeSMTP-DnsList: type={0}, list={1}, result={2}", _dnsListType, _dnsListName, _dnsListValue);
                    else
                        fp.WriteLine("X-FakeSMTP-DnsList: type={0}, list={1}, result={2}", "notlisted", "none", "0.0.0.0");
                    fp.WriteLine("X-FakeSMTP-Helo: {0}", HeloStr);
                    fp.WriteLine("X-FakeSMTP-MailFrom: {0}", MailFrom);
                    fp.WriteLine("X-FakeSMTP-RcptCount: {0}", RcptTo.Count.ToString());
                    for (int i = 0; i < RcptTo.Count; i++)
                        fp.WriteLine("X-FakeSMTP-RcptTo-{0}: {1}", i + 1, RcptTo[i]);
                    fp.WriteLine("X-FakeSMTP-Counters: noop={0}, vrfy={1}, err={2}", NoopCount, VrfyCount, ErrCount);

                    // write the message data
                    fp.WriteLine(msgData);

                    // all done, flush and close
                    fp.Flush();
                    fp.Close();
                }
            }
            catch (Exception ex)
            {
                _msgFile = "write_error";
                Debug.WriteLine("storeMailMsg::Error: " + ex.Message);
            }
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
            List<string> cols = new List<string>();

            // current date/time
            cols.Add(DateTime.UtcNow.ToString("u"));

            // start date, session ID, client IP, helo
            cols.Add(_startDate.ToString("u"));
            cols.Add(_sessionID);
            cols.Add(ClientIP);
            cols.Add(HeloStr);

            // mail from
            if (!string.IsNullOrEmpty(MailFrom))
                cols.Add(MailFrom);
            else
                cols.Add("");

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
            if (!string.IsNullOrEmpty(_msgFile))
                cols.Add(_msgFile);
            else
                cols.Add("-no-file-");

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
            if (_earlyTalker)
                cols.Add("1");
            else
                cols.Add("0");

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
