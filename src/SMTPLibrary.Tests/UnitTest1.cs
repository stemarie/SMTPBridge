using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SMTPLibrary.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string text;
            using (var textStreamReader = new StreamReader(assembly.GetManifestResourceStream("SMTPLibrary.Tests.TestEMail.txt")))
            {
                text = textStreamReader.ReadToEnd();
            }
            MessageParser parser = new MessageParser(text);
        }
    }
}
