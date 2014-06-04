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
class SimpleListener
{
    static byte[] _receiveBuffer = new byte [16 * 1024];
    TcpListener _tcp;

    public SimpleListener( IPAddress address, int port )
    {
        Console.WriteLine( "Touch.Unit Simple Server configured for : {0}:{1}", address, port );
        _tcp = new TcpListener( address, port );
    }

    bool canceled = false;

    public void Cancel()
    {
        try
        {
            Console.WriteLine( "Canceling the Server" );
            canceled = true;
            _tcp.Stop();
        }
        catch
        {
            // We might have stopped already, so just swallow any exceptions.
        }
    }

    public void Start()
    {
        Console.WriteLine( "Touch.Unit Simple Server listening" );
        _tcp.Start();
    }

    public int ListenForTestRun()
    {
        try
        {
            using (TcpClient client = _tcp.AcceptTcpClient())
            {
                ProcessTestRun( client );
            }
           
        }
        catch (Exception e)
        {
            if (!canceled)
                Console.WriteLine( "[{0}] : {1}", DateTime.Now, e );
            return 1;
        }
        finally
        {
            _tcp.Stop();
        }

        return 0;
    }

    void ProcessTestRun( TcpClient client )
    {
        var timeout = new System.Threading.Timer( _ => 
        {
            Console.WriteLine( "Timeout waiting for test-runner output for 10 seconds. Looks like the runner process has tied (if not, it should send heartbeats)." );
            Cancel();
        }, null, TimeSpan.FromSeconds( 10 ), TimeSpan.Zero );

        string remote = client.Client.RemoteEndPoint.ToString();
        Console.WriteLine( "Connection from {0}", remote );

        var output = Console.Out;
        {
            // a few extra bits of data only available from this side
            string header = String.Format( "[Local Date/Time:\t{1}]{0}[Remote Address:\t{2}]{0}", Environment.NewLine, DateTime.Now, remote );

            output.WriteLine( header );
            output.Flush();

            // now simply copy what we receive
            int i;
            int total = 0;
            NetworkStream stream = client.GetStream();

            do
            {
                i = stream.Read( _receiveBuffer, 0, _receiveBuffer.Length );
                timeout.Change(TimeSpan.FromSeconds( 10 ), TimeSpan.Zero );

                output.Write( output.Encoding.GetString( _receiveBuffer, 0, i ) );
                output.Flush();
                total += i;
            } while (i != 0);
        }
    }
}
