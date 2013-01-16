using System;
using System.Collections.Generic;
using System.Text;

namespace SMTPLibrary
{
    public class MessageParser
    {
        public Dictionary<string, string> Headers { get; private set; }
        public string Body { get; private set; }

        private readonly string[] _separator = new[] { "\r\n" };

        public MessageParser(string rawMessage)
        {
            Headers = new Dictionary<string, string>();
            string[] lines = rawMessage.Split(_separator, StringSplitOptions.None);
            int i = 0;
            while (!string.IsNullOrEmpty(lines[i]))
            {
                string[] headers = lines[i].Split(':');
                Headers.Add(headers[0], headers[1].Substring(1));
                i++;
            }
            i++;
            StringBuilder sb = new StringBuilder(lines.Length - i);
            while (!string.IsNullOrEmpty(lines[i]))
            {
                sb.Append(lines[i]);
                i++;
            }
            Body = sb.ToString();
        }
    }
}
