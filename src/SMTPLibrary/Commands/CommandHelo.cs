using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using SMTPLibrary.Properties;

namespace SMTPLibrary.Commands
{
    public class CommandHelo : ICommand
    {
        public Context Context { get; set; }

        public string GetResponse()
        {
            return cmd_helo(Context.CmdLine);
        }

        // HELO/EHLO
        private string cmd_helo(string cmdLine)
        {
            SMTPSession.CmdID id = Context.Session.GetCommandID(cmdLine);
            List<string> parts = Context.Session.ParseCmdLine(id, cmdLine);
            if (2 != parts.Count)
            {
                Context.Session.ErrCount++;
                return String.Format(Resources.MSG_501_NeedsArgument, parts[0]);
            }
            if (!string.IsNullOrEmpty(Context.Session.HeloStr))
            {
                Context.Session.ErrCount++;
                return string.Format(Resources.MSG_503_AlreadySent, parts[0]);
            }
            if (!CheckHelo(parts[1]))
            {
                Context.Session.ErrCount++;
                return String.Format(Resources.MSG_501_Invalid, parts[0]);
            }
            if (parts[1].ToLower().Equals("localhost") ||
                parts[1].ToLower().Equals(AppGlobals.HostName) ||
                parts[1].StartsWith("[127.") ||
                parts[1].Equals("[" + AppGlobals.ListenAddress + "]")
                )
            {
                Context.Session.ErrCount++;
                return String.Format(Resources.MSG_501_Spoofed, parts[0]);
            }

            Context.Session.HeloStr = parts[1];
            Context.Session.LastCmd = id;
            if (id == SMTPSession.CmdID.Helo)
                return String.Format(Resources.MSG_250_1, parts[1], Context.Session.ClientIP);
            return String.Format(Resources.MSG_250_2, parts[1], Context.Session.ClientIP);
        }

        // coarse checks on the HELO string (todo: replace with regexp)
        internal bool CheckHelo(string heloStr)
        {
            // can't be empty
            if (String.IsNullOrEmpty(heloStr)) return false;

            // can't start with a dot or hypen
            char[] heloChars = heloStr.ToLowerInvariant().ToCharArray();
            if ((heloChars[0] == '.') || (heloChars[0] == '-')) return false;

            // must contain at least a dot
#if !DEBUG
            if (!heloStr.Contains('.')) return false;
#endif

            // can only contain valid chars
            if (heloChars.Any(t => !SMTPSession.HELO_CHARS.Contains(t)))
            {
                return false;
            }

            // if starts with "[" the bracket must match and the
            // enclosed string must be a valid IP address (and
            // match the connecting IP address)
            if ('[' == heloChars[0])
            {
                if (']' != heloChars[heloChars.Length - 1]) return false;
                string ipAddr = heloStr.Replace('[', ' ');
                ipAddr = ipAddr.Replace(']', ' ').Trim();
                IPAddress ip;
                //if (!ipAddr.Equals(this._clientIP)) return false;
                if (!IPAddress.TryParse(ipAddr, out ip)) return false;
                //if (IsPrivateIP(ipAddr)) return false;
            }
#if !DEBUG
            else
            {
                // run a check on the domain
                bool result = Context.Session.CheckMailAddr("postmaster@" + heloStr);
                if (false == result) return false;
            }
#endif

            return true;
        }
    }
}