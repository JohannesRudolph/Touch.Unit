// Main.cs: Touch.Unit Simple Server
//
// Authors:
//  Sebastien Pouliot  <sebastien@xamarin.com>
//
// Copyright 2011-2012 Xamarin Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;

// a simple, blocking (i.e. one device/app at the time), listener
static class Program
{
    public static int Main( string[] args )
    { 
        Console.WriteLine( "Touch.Unit Simple Server" );
        Console.WriteLine( "Copyright 2011, Xamarin Inc. All rights reserved." );
        
        bool help = false;
        string address = null;
        string port = null;
        string command = null;
        string arguments = null;

        var os = new OptionSet()
        {
            { "h|?|help", "Display help", v => help = true },
            { "ip", "IP address to listen (default: Any)", v => address = v },
            { "port", "TCP port to listen (default: 16384)", v => port = v },
            { "command=", "The command to execute", v => command = v },
            { "arguments=", "The arguments to pass to command", v => arguments = v },
        };
        
        try
        {
            os.Parse( args );
            if (help)
            {
                ShowHelp( os );
                return 0;
            }

            // launch in background as we will block the main thread with the TCPListener
            if (command == null || arguments == null)
            {
                Console.Write( "command and arguments are required parameters" );
                return 1;
            }

            IPAddress parsedIp = ParseIp( address );                      
            ushort parsedPort = ParsePort(port);

            var runnerCancellation = new CancellationTokenSource();
            Task<int> listenerTask = StartListener( parsedIp, parsedPort, TimeSpan.FromSeconds(10) );
            Task<int> runnerTask = StartTestRunner(command, arguments, runnerCancellation.Token);
           
            // Wait for the process runner to finish
            Task.WaitAny(listenerTask, runnerTask);

            bool runnerCompletedWithError = runnerTask.IsCompleted && runnerTask.Result != 0;
            if (runnerCompletedWithError)
            {
                // when the runner completes with an error (e.g. adb timeout), ignore this an observe what the listener has to say
                return listenerTask.Result; 
            }
                
            // wait for the listener to complete
            listenerTask.Wait();

            // if the runner task hasn't finished by now we need to kill it, and wait for it to be killed
            // http://stackoverflow.com/questions/1491674/when-a-parent-process-is-killed-by-kill-9-will-subprocess-also-be-killed
            runnerCancellation.Cancel();
            runnerTask.Wait();

            // return the listener status
            return listenerTask.Result;
        }
        catch (OptionException oe)
        {
            Console.WriteLine( "{0} for options '{1}'", oe.Message, oe.OptionName );
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine( ex );
            return 1;
        }
    }

    static void ShowHelp( OptionSet os )
    {
        Console.WriteLine( "Usage: mono Touch.Server.exe [options]" );
        os.WriteOptionDescriptions( Console.Out );
    }

    static Task<int> StartListener( IPAddress parsedIp, ushort parsedPort, TimeSpan silenceTimeout )
    {
        var listener = new SimpleListener( parsedIp, parsedPort, silenceTimeout );
        listener.Start();

        // we start the socket, but do not yet block on it 
        return Task.Factory.StartNew( () => listener.ListenForTestRun() );
    }

    static Task<int> StartTestRunner( string command, string arguments, CancellationToken cancel )
    {
        return Task.Run( async () => await RunProcessAsync( command, arguments, cancel ) );
    }

    static async Task<int> RunProcessAsync( string command, string arguments, CancellationToken cancel )
    {
        // we only use async to get this thing wrapped up in a task
        await Task.FromResult( 0 );

        using (Process proc = new Process())
        {
            proc.StartInfo.FileName = command;
            proc.StartInfo.Arguments = arguments;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true; // prevent from reading stdin in teamcity builds
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e )
            {
                Console.Error.WriteLine( "Test-Runner process error: " + e.Data );
            };
            proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e )
            {
                Console.WriteLine( "Test-Runner process stdout:" + e.Data );
            };
            proc.Start();
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();

            bool hasExited = false;
            while (!hasExited)
            {
                if (cancel.IsCancellationRequested)
                {
                    proc.Kill();
                    Console.WriteLine( "Test-Runner process cancelled" );
                    return -1;
                }

                hasExited = proc.WaitForExit( 100 );
            }

            Console.WriteLine( "Test-Runner process exited with code {0} after {1}s", proc.ExitCode, (proc.ExitTime - proc.StartTime).TotalSeconds );

            return proc.ExitCode;
        }
    }

    static ushort ParsePort(string port)
    {
        ushort p;
        if (UInt16.TryParse( port, out p ))
            return p;
        else
            return 16384;
    }

    static IPAddress ParseIp( string address )
    {
        IPAddress ip;

        if (String.IsNullOrEmpty( address ) || !IPAddress.TryParse( address, out ip ))
        {
            ip = IPAddress.Any;
        }

        return ip;
    }

}