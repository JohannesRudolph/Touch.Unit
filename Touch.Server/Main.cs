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

            var listener = new SimpleListener(parsedIp, parsedPort);
            listener.Start(); // we start the socket, but do not yet block on it 


            var listenerTask = Task.Factory.StartNew( () => listener.ListenForTestRun() );
            var processTask = Task.Factory.StartNew( () => RunProcess( command, arguments, listener ) );
           
            // Wait for the process runner to finish
            processTask.Wait();

            // wait for the listener to exit gracefully
            bool success = listenerTask.Wait( TimeSpan.FromSeconds( 10 ) ); // we give the listener 10 more seconds to finish, after that we cancel it no matter what the process returned
            if (!success)
            {
                Console.WriteLine( "Listener did not receive a connection or did not finnish processing in 10 seconds after process exited" );
                return 1;
            }

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

    static int RunProcess( string command, string arguments, SimpleListener listener )
    {
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
            proc.WaitForExit();

            // cancel the listener task if the process failed
            if (proc.ExitCode != 0)
                listener.Cancel();

            return proc.ExitCode;
        }
    }
}