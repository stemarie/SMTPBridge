using SMTPLibrary.Properties;

namespace SMTPLibrary.Commands
{
    public class CommandUnknown : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_unknown(Context.CmdLine);
        }

        // unknown/unsupported
        private string cmd_unknown(string cmdLine)
        {
            Context.Session.ErrCount++;
            Context.Session.LastCmd = SMTPSession.CmdID.Invalid;
            if (string.IsNullOrEmpty(cmdLine))
                return Resources.MSG_500_CommandUnrecognized1;
            return string.Format(Resources.MSG_500_CommandUnrecognized2, cmdLine);
        }
    }
}