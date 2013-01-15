using fakeSMTP.Commands;

namespace SMTPLibrary.Commands
{
    public class CommandData:ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_data(Context.CmdLine);
        }

        // DATA
        private string cmd_data(string cmdLine)
        {
            if (Context.Session.RcptTo.Count < 1)
            {
                Context.Session.ErrCount++;
                return "471 Bad or missing RCPT command";
            }
            Context.Session.LastCmd = SMTPSession.CmdID.Data;
            return "354 Start mail input; end with <CRLF>.<CRLF>";
        }
    }
}