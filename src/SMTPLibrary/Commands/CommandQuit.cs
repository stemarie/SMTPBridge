﻿using SMTPLibrary.Properties;

namespace SMTPLibrary.Commands
{
    public class CommandQuit : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_quit(Context.CmdLine);
        }

        // QUIT
        private string cmd_quit(string cmdLine)
        {
            Context.Session.LastCmd = SMTPSession.CmdID.Quit;
            return Resources.MSG_221_ClosingConnection;
        }
    }
}