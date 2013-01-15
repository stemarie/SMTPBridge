using System.Net.Mail;

namespace FakeClient
{
    class Program
    {
        static void Main(string[] args)
        {
            SmtpClient client = new SmtpClient("localhost");
            client.Send("stemarie@brainbankinc.com",
                        "stemarie@brainbankinc.com",
                        "subject",
                        "body");
        }
    }
}
