using SMTPLibrary.Properties;

namespace SMTPLibrary.Commands
{
    public class CommandRset : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_rset(Context.CmdLine);
        }

        // RSET
        private string cmd_rset(string cmdLine)
        {
            Context.Session.ResetSession();
            Context.Session.LastCmd = SMTPSession.CmdID.Rset;
            return Resources.MSG_250_ResetOk;
        }
    }
}