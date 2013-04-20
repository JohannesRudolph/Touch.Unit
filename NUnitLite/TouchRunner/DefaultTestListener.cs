using System;
using NUnit.Framework.Api;
using System.IO;

namespace MonoTouch.NUnit
{
	public class DefaultTestListener : ITestReporter
	{
		int passed;
		int failed;
		int ignored;
		int inconclusive;

		public DefaultTestListener (TextWriter writer)
		{
			this.Writer = writer;
		}
		
		public TextWriter Writer { get; private set; }

		public void TestStarted (ITest test)
		{
			if (test is TestSuite) {
				Writer.WriteLine ();
				Writer.WriteLine (test.Name);
			}
		}
		
		public void TestFinished (ITestResult r)
		{
			TestResult result = r as TestResult;
			TestSuite ts = result.Test as TestSuite;
			if (ts != null) {
				TestSuiteElement tse;
				if (suite_elements.TryGetValue (ts, out tse))
					tse.Update (result);
			} else {
				TestMethod tc = result.Test as TestMethod;
				if (tc != null)
					case_elements [tc].Update (result);
			}
			
			if (result.Test is TestSuite) {
				if (!result.IsFailure () && !result.IsSuccess () && !result.IsInconclusive () && !result.IsIgnored ())
					Writer.WriteLine ("\t[INFO] {0}", result.Message);
				
				string name = result.Test.Name;
				if (!String.IsNullOrEmpty (name))
					Writer.WriteLine ("{0} : {1} ms", name, result.Time * 1000);
			} else {
				if (result.IsSuccess ()) {
					Writer.Write ("\t[PASS] ");
					passed++;
				} else if (result.IsIgnored ()) {
					Writer.Write ("\t[IGNORED] ");
					ignored++;
				} else if (result.IsFailure ()) {
					Writer.Write ("\t[FAIL] ");
					failed++;
				} else if (result.IsInconclusive ()) {
					Writer.Write ("\t[INCONCLUSIVE] ");
					inconclusive++;
				} else {
					Writer.Write ("\t[INFO] ");
				}
				Writer.Write (result.Test.Name);
				
				string message = result.Message;
				if (!String.IsNullOrEmpty (message)) {
					Writer.Write (" : {0}", message.Replace ("\r\n", "\\r\\n"));
				}
				Writer.WriteLine ();
				
				string stacktrace = result.StackTrace;
				if (!String.IsNullOrEmpty (result.StackTrace)) {
					string[] lines = stacktrace.Split (new char [] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
					foreach (string line in lines)
						Writer.WriteLine ("\t\t{0}", line);
				}
			}
		}

		public void TestOutput (TestOutput testOutput)
		{
		}
	}
}
