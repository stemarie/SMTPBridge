using System;
using SMTPLibrary.Properties;

namespace SMTPLibrary.Commands
{
    public class CommandBanner : ICommand
    {
        public Context Context { get; set; }
        public string GetResponse()
        {
            return cmd_banner(Context.CmdLine);
        }

        // banner string (not a real command)
        private string cmd_banner(string cmdLine)
        {
            string banner = String.Format(Resources.MSG_220_Banner, Context.Session.HostName, DateTime.UtcNow.ToString("R"));
            return banner;
        }
    }
}