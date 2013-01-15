using FakeSMTP;

namespace fakeSMTP
{
    public class Context
    {
        public SMTPSession Session { get; set; }

        public string CmdLine { get; set; }
    }
}