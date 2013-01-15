using System;
using System.Diagnostics;
using System.IO;

namespace SMTPLibrary
{
    public class FileProcessor : IProcessor
    {
        // stores a mail message to file, notice that the code doesn't even
        // try to deal with message headers and mime parts nor to check if
        // they're correct, this isn't the purpose for this code, but willing
        // to add such parsing/checks, you may either add them here or after
        // receiving the "." command at end of the DATA stage
        private void StoreMailMsg(string msgData)
        {
            // bump the message counter
            Context.Session._msgCount++;
            if (!AppGlobals.StoreData) return;

            try
            {
                // build the pathname of the file used to store this email
                string filePath = AppGlobals.StorePath;
                string fileName = "mailmsg-" + Path.GetRandomFileName().Replace('.', '-') + ".txt";

                // record the file name
                Context.Session._msgFile = fileName;

                // open the file for writing
                using (StreamWriter fp = new StreamWriter(filePath + fileName, true))
                {
                    fp.WriteLine("X-FakeSMTP-HostName: {0}", AppGlobals.HostName);
                    fp.WriteLine("X-FakeSMTP-Sessions: count={0}, id={1}", Context.Session._sessCount, Context.Session._sessionID);
                    fp.WriteLine("X-FakeSMTP-MsgCount: {0}", Context.Session._msgCount);
                    fp.WriteLine("X-FakeSMTP-SessDate: {0}", Context.Session._startDate.ToString("u"));
                    fp.WriteLine("X-FakeSMTP-ClientIP: {0}", Context.Session.ClientIP);
                    if (null != Context.Session._dnsListType)
                        fp.WriteLine("X-FakeSMTP-DnsList: type={0}, list={1}, result={2}", Context.Session._dnsListType, Context.Session._dnsListName, Context.Session._dnsListValue);
                    else
                        fp.WriteLine("X-FakeSMTP-DnsList: type={0}, list={1}, result={2}", "notlisted", "none", "0.0.0.0");
                    fp.WriteLine("X-FakeSMTP-Helo: {0}", Context.Session.HeloStr);
                    fp.WriteLine("X-FakeSMTP-MailFrom: {0}", Context.Session.MailFrom);
                    fp.WriteLine("X-FakeSMTP-RcptCount: {0}", Context.Session.RcptTo.Count.ToString());
                    for (int i = 0; i < Context.Session.RcptTo.Count; i++)
                        fp.WriteLine("X-FakeSMTP-RcptTo-{0}: {1}", i + 1, Context.Session.RcptTo[i]);
                    fp.WriteLine("X-FakeSMTP-Counters: noop={0}, vrfy={1}, err={2}", Context.Session.NoopCount, Context.Session.VrfyCount, Context.Session.ErrCount);

                    // write the message data
                    fp.WriteLine(msgData);

                    // all done, flush and close
                    fp.Flush();
                    fp.Close();
                }
            }
            catch (Exception ex)
            {
                Context.Session._msgFile = "write_error";
                Debug.WriteLine("storeMailMsg::Error: " + ex.Message);
            }
        }

        public Context Context { get; set; }

        public void Process(string message)
        {
            StoreMailMsg(message);
        }
    }
}
