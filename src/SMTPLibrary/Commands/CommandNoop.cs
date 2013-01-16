using System.Collections.Generic;
using SMTPLibrary.Properties;

namespace SMTPLibrary.Commands
{
    public class CommandNoop : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_noop(Context.CmdLine);
        }

        // NOOP
        private string cmd_noop(string cmdLine)
        {
            Context.Session.NoopCount++;
            List<string> parts = Context.Session.ParseCmdLine(SMTPSession.CmdID.Noop, cmdLine);
            if (parts.Count > 1)
            {
                // NOOP may have args...
                return string.Format(Resources.MSG_250_OK, parts[1]);
            }
            return Resources.MSG_250_OK2;
        }
    }
}