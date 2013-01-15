using System;
using System.Collections.Generic;
using SMTPLibrary.Properties;
using fakeSMTP.Commands;

namespace SMTPLibrary.Commands
{
    public class CommandRcptTo : ICommand
    {
        public Context Context { get; set; }

        public string GetResponse()
        {
            return cmd_rcpt(Context.CmdLine);
        }

        // RCPT TO:
        private string cmd_rcpt(string cmdLine)
        {
            if (string.IsNullOrEmpty(Context.Session.MailFrom))
            {
                Context.Session.ErrCount++;
                return Resources.MSG_503_NeedMailBeforeRcpt;
            }
            List<string> parts = Context.Session.ParseCmdLine(SMTPSession.CmdID.RcptTo, cmdLine);
            if (2 != parts.Count)
            {
                Context.Session.ErrCount++;
                return String.Format(Resources.MSG_501_NeedsArgument, parts[0]);
            }
            if (!Context.Session.CheckMailAddr(parts[1]))
            {
                Context.Session.ErrCount++;
                return String.Format(Resources.MSG_553_InvalidAddress, parts[1]);
            }

            if (!IsLocalDomain(Context.Session.MailDom))
            {
                // relaying not allowed...
                Context.Session.ErrCount++;
                return Resources.MSG_530_RelayingNotAllowed;
            }
            if (!IsLocalBox(Context.Session.MailBox, Context.Session.MailDom))
            {
                // unkown/invalid recipient
                Context.Session.ErrCount++;
                return String.Format(Resources.MSG_553_UnknownEmailAddress, parts[1]);
            }

            Context.Session.RcptTo.Add(parts[1]);
            Context.Session.LastCmd = SMTPSession.CmdID.RcptTo;
            return string.Format(Resources.MSG_250_RecipientOk, parts[1]);
        }

        // checks if a domain is local
        internal bool IsLocalDomain(string maildomain)
        {
            // if no domain, treat as "all domains are ok"
            if (Context.Session.MailDomains.Count < 1) return true;
            for (int d = 0; d < Context.Session.MailDomains.Count; d++)
            {
                if (maildomain.Equals(Context.Session.MailDomains[d], StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }
            return false;
        }

        // checks if a mailbox is local / exists
        internal bool IsLocalBox(string mailbox, string maildomain)
        {
            // check if domain is local
            // if (!isLocalDomain(maildomain)) return false;

            // if no mailbox, treat as "all mailboxes are ok"
            if (Context.Session.MailBoxes.Count < 1) return true;

            // check if the mailbox exists
            string tmpAddr = mailbox + "@" + maildomain;
            for (int b = 0; b < Context.Session.MailBoxes.Count; b++)
            {
                if (tmpAddr.Equals(Context.Session.MailBoxes[b], StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}