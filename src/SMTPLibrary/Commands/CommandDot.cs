namespace SMTPLibrary.Commands
{
    public class CommandDot : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_dot(Context.CmdLine);
        }

        // end of DATA (dot)
        private string cmd_dot(string cmdLine)
        {
            Context.Session.LastCmd = SMTPSession.CmdID.Noop;
            return "250 Queued mail for delivery";
        }
    }
}