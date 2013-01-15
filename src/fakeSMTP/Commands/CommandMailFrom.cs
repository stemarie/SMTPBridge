using System;
using System.Collections.Generic;
using FakeSMTP;
using fakeSMTP.Properties;

namespace fakeSMTP.Commands
{
    public class CommandMailFrom : ICommand
    {
        public Context Context { get; set; }

        public string GetResponse()
        {
            return cmd_mail(Context.CmdLine);
        }

        // MAIL FROM:
        private string cmd_mail(string cmdLine)
        {
            if (string.IsNullOrEmpty(Context.Session.HeloStr))
            {
                Context.Session.ErrCount++;
                return Resources.MSG_503_NoHeloEhlo;
            }
            if (!string.IsNullOrEmpty(Context.Session.MailFrom))
            {
                Context.Session.ErrCount++;
                return Resources.MSG_503_NestedMailCommand;
            }
            List<string> parts = Context.Session.ParseCmdLine(SMTPSession.CmdID.MailFrom, cmdLine);
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
            Context.Session.MailFrom = parts[1];
            Context.Session.LastCmd = SMTPSession.CmdID.MailFrom;
            return string.Format(Resources.MSG_250_SenderOk, parts[1]);
        }
    }
}