namespace fakeSMTP.Commands
{
    public class CommandHelp : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_help(Context.CmdLine);
        }

        // HELP
        private string cmd_help(string cmdLine)
        {
            // dynamically build the help string for our commands list
            string cmd = null;
            int pos = -1;
            string buff = "211";
            for (int i = 1; i < Context.Session.CmdList.Length; i++)
            {
                cmd = Context.Session.CmdList[i];
                pos = cmd.IndexOf(' ');
                if (-1 != pos) cmd = cmd.Substring(0, pos);
                buff = buff + " " + cmd;
            }
            return buff;
        }
    }
}