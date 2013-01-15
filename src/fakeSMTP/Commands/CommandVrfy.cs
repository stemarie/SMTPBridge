using System;
using System.Collections.Generic;
using FakeSMTP;
using fakeSMTP.Properties;

namespace fakeSMTP.Commands
{
    public class CommandVrfy : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_vrfy(Context.CmdLine);
        }

        // VRFY/EXPN
        private string cmd_vrfy(string cmdLine)
        {
            SMTPSession.CmdID id = Context.Session.GetCommandID(cmdLine);
            Context.Session.VrfyCount++;
            List<string> parts = Context.Session.ParseCmdLine(id, cmdLine);
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
            Context.Session.LastCmd = id;
            if (id == SMTPSession.CmdID.Vrfy)
                return Resources.MSG_252_CannotVrfy;
            return String.Format("250 {0}", parts[1]);
        }
    }
}