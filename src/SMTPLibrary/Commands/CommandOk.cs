using System.Collections.Generic;
using SMTPLibrary.Properties;
using fakeSMTP.Commands;

namespace SMTPLibrary.Commands
{
    public class CommandOk : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_ok(null);
        }

        // misc command, fake support
        private string cmd_ok(string cmdLine)
        {
            if (!string.IsNullOrEmpty(cmdLine))
            {
                List<string> parts = Context.Session.ParseCmdLine(SMTPSession.CmdID.Noop, cmdLine);
                if (parts.Count > 1)
                {
                    return string.Format(Resources.MSG_250_OK3, parts[0]);
                }
            }
            return Resources.MSG_250_OK2;
        }
    }
}