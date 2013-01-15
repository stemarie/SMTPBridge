using FakeSMTP;
using fakeSMTP.Properties;

namespace fakeSMTP.Commands
{
    public class CommandDot : ICommand
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
                return Resources.MSG_471_BadOrMissingRcpt;
            }
            Context.Session.LastCmd = SMTPSession.CmdID.Data;
            return Resources.MSG_354_StartMailInput;
        }

        // end of DATA (dot)
        private string cmd_dot(string cmdLine)
        {
            Context.Session.LastCmd = SMTPSession.CmdID.Noop;
            return "250 Queued mail for delivery";
        }
    }
}