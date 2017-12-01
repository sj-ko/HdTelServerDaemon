using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Diagnostics;
using System.Web.Script.Serialization;
using System.Net;

namespace HdTelNvrDaemon
{
    class ClientHandler
    {
        TcpClient clientSocket;
        int clientNo;
        string[] channelList;

        public void startClient(TcpClient ClientSocket, int clientNo, string[] chList)
        {
            this.clientSocket = ClientSocket;
            this.clientNo = clientNo;
            this.channelList = chList;

            Thread t_hanlder = new Thread(doHandle);
            t_hanlder.IsBackground = true;
            t_hanlder.Start();
        }

        public delegate void MessageDisplayHandler(string text);
        public event MessageDisplayHandler OnReceived;

        public delegate void CalculateClientCounter();
        public event CalculateClientCounter OnCalculated;

        private void doHandle()
        {
            string descriptionMsg = "{\"ProductDescription\":{\"Vendor\":\"gbu\" , \"Device\":\"axxonnext\" , \"Version\":\"1.00\"}, \"AvailableCommand\":[\"StartStream\", \"StopStream\", \"Heartbeat\"]}";

            string thisSessionId = String.Empty;
            int requestedVideoChannel = 0;
            int heartbeatTimeout = 60;

            NetworkStream stream = null;

            try
            {
                byte[] buffer = new byte[1024];
                string msg = string.Empty;
                int bytes = 0;
                int MessageCount = 0;

                bool isRunning = true;
                //bool isDescriptionDone = false;

                DateTime sessionStartTime;

                Process ffmpegProcess = new Process();

                // get remote client IP
                IPEndPoint ipep = (IPEndPoint)clientSocket.Client.RemoteEndPoint;
                IPAddress ipa = ipep.Address;

                // send descripton message first
                SendDescription(clientSocket.GetStream(), descriptionMsg);
                //isDescriptionDone = true;

                while (isRunning)
                {
                    MessageCount++;
                    stream = clientSocket.GetStream();
                    bytes = stream.Read(buffer, 0, buffer.Length);

                    if (bytes > 0)
                    {
                        //msg = Encoding.Unicode.GetString(buffer, 0, bytes);
                        //msg = msg.Substring(0, msg.IndexOf("$"));
                        msg = "Data Received " + bytes.ToString() + " bytes from IP " + ipa.ToString();

                        if (OnReceived != null)
                            OnReceived(msg);

                        // handle request
                        byte responseType;
                        UInt32 responseLength;
                        string responseValue = "";
                        byte[] responseData = new byte[1024];
                        switch (buffer[0])
                        {
                            case 0x00: // description
                                       // 1) Make response data (description data)
                                responseType = 0x00;
                                responseLength = (UInt32)descriptionMsg.Length;
                                responseValue = descriptionMsg;
                                responseData[0] = responseType;
                                Buffer.BlockCopy(BitConverter.GetBytes(responseLength), 0, responseData, 1, sizeof(UInt32));
                                Buffer.BlockCopy(Encoding.UTF8.GetBytes(responseValue), 0, responseData, 5, responseValue.Length);

                                msg = "description response message : " + responseValue;
                                if (OnReceived != null)
                                    OnReceived(msg);

                                //isDescriptionDone = true;

                                break;

                            case 0x01: // request
                                       // 1) Get request data
                                UInt32 requestLength = BitConverter.ToUInt32(buffer, 1);
                                string requestValue = System.Text.Encoding.UTF8.GetString(buffer, 5, (int)requestLength);

                                msg = "request message : " + requestValue;
                                if (OnReceived != null)
                                    OnReceived(msg);

                                // 2) Do work
                                var deserializer = new JavaScriptSerializer();
                                var results = deserializer.Deserialize<Value>(requestValue);
                                Value requestValueClass = results;
                                Value responseValueClass = new Value();
                                switch (requestValueClass.Command)
                                {
                                    case "StartStream":
                                        thisSessionId = requestValueClass.SessionID;
                                        requestedVideoChannel = requestValueClass.Params.Channel;
                                        heartbeatTimeout = requestValueClass.Params.Heartbeat;

                                        responseValueClass.SessionID = thisSessionId;
                                        responseValueClass.TransactionID = requestValueClass.TransactionID;
                                        responseValueClass.Result = "true";
                                        responseValueClass.Params = requestValueClass.Params;
                                        responseValueClass.Params.Resolution = "QVGA";
                                        responseValueClass.Params.VideoCODEC = "h.264";
                                        responseValueClass.Params.FrameRate = 10;
                                        responseValueClass.Params.MaxFrameSize = requestValueClass.Params.MaxFrameSize;
                                        responseValueClass.Params.Log = "streaming start";

                                        msg = "startstream starts gstreamer...";
                                        if (OnReceived != null)
                                            OnReceived(msg);

                                        // ffmpeg
                                        ProcessStartInfo startInfo = new ProcessStartInfo();
                                        //startInfo.UseShellExecute = false;
                                        startInfo.WorkingDirectory = @"C:\gstreamer\1.0\x86_64\bin";
                                        startInfo.FileName = "gst-launch-1.0.exe";
                                        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                                        startInfo.Arguments = "rtspsrc location=" + channelList[requestedVideoChannel - 1] + 
                                            " ! queue ! rtph264depay ! h264parse ! rtph264pay config-interval=1 pt=97 mtu=1200 ! udpsink host=" + ipa.ToString() + " port=" + 
                                            responseValueClass.Params.MediaPortForVideo + " sync=0";
                                        //startInfo.Arguments = "-i " + channelList[requestedVideoChannel-1] + " -strict -2 -vcodec copy -an -payload_type 97 -f rtp rtp://" + ipa.ToString() + ":" + responseValueClass.Params.MediaPortForVideo +"?pkt_size=1200";
                                        ffmpegProcess = Process.Start(startInfo);
                                        //

                                        msg = "gstreamer ok";
                                        if (OnReceived != null)
                                            OnReceived(msg);

                                        var serializer = new JavaScriptSerializer();
                                        //responseValue = serializer.Serialize(responseValueClass);

                                        responseValue = "{\"SessionID\":" + thisSessionId + ", \"TransactionID\":" + requestValueClass.TransactionID + ", \"Result\":true, \"Params\":{\"MaxFrameSize\":40000, \"Log\":\"streaming start.\"}}";

                                        responseType = 0x02;
                                        responseLength = (UInt32)responseValue.Length;
                                        responseData[0] = responseType;
                                        Buffer.BlockCopy(BitConverter.GetBytes(responseLength), 0, responseData, 1, sizeof(UInt32));
                                        Buffer.BlockCopy(Encoding.UTF8.GetBytes(responseValue), 0, responseData, 5, responseValue.Length);

                                        msg = "response message : " + responseValue;
                                        if (OnReceived != null)
                                            OnReceived(msg);

                                        sessionStartTime = DateTime.UtcNow;

                                        break;
                                    case "StopStream":
                                        responseValue = "{\"SessionID\":" + thisSessionId + ", \"TransactionID\":" + requestValueClass.TransactionID + ", \"Result\":true}";
                                        responseType = 0x02;
                                        responseLength = (UInt32)responseValue.Length;
                                        responseData[0] = responseType;
                                        Buffer.BlockCopy(BitConverter.GetBytes(responseLength), 0, responseData, 1, sizeof(UInt32));
                                        Buffer.BlockCopy(Encoding.UTF8.GetBytes(responseValue), 0, responseData, 5, responseValue.Length);

                                        // kill ffmpeg
                                        if (ffmpegProcess != null)
                                        {
                                            ffmpegProcess.Kill();
                                        }
                                        //

                                        msg = "response message : " + responseValue;
                                        if (OnReceived != null)
                                            OnReceived(msg);

                                        isRunning = false; // To close TcpClient...
                                        break;
                                    case "Heartbeat":
                                        responseValue = "{\"SessionID\":" + thisSessionId + ", \"TransactionID\":" + requestValueClass.TransactionID + ", \"Result\":true}";
                                        responseType = 0x02;
                                        responseLength = (UInt32)responseValue.Length;
                                        responseData[0] = responseType;
                                        Buffer.BlockCopy(BitConverter.GetBytes(responseLength), 0, responseData, 1, sizeof(UInt32));
                                        Buffer.BlockCopy(Encoding.UTF8.GetBytes(responseValue), 0, responseData, 5, responseValue.Length);

                                        msg = "response message : " + responseValue;
                                        if (OnReceived != null)
                                            OnReceived(msg);

                                        break;
                                    default:
                                        msg = "request command not found";
                                        if (OnReceived != null)
                                            OnReceived(msg);
                                        break;
                                }

                                break;
                            default:
                                msg = "command not found";
                                if (OnReceived != null)
                                    OnReceived(msg);
                                break;
                        }
                        //

                        // response to client
                        msg = "Server to client(" + clientNo.ToString() + ") " + MessageCount.ToString();
                        if (OnReceived != null)
                            OnReceived(msg);

                        byte[] sbuffer = responseData; // Encoding.Unicode.GetBytes(msg);
                        stream.Write(sbuffer, 0, 1 + sizeof(UInt32) + responseValue.Length); //sbuffer.Length);
                        stream.Flush();

                        msg = " >> " + msg;
                        if (OnReceived != null)
                        {
                            OnReceived(msg);
                            OnReceived("");
                        }
                    }
#if false
                    else if (bytes == 0 && isDescriptionDone == false)
                    {
                        msg = "No Description Done and Zero data received from IP " + ipa.ToString() + ", Try to send description data...";

                        if (OnReceived != null)
                            OnReceived(msg);

                        byte responseType;
                        UInt32 responseLength;
                        string responseValue;
                        byte[] responseData = new byte[1024];

                        // Make response data (description data)
                        responseType = 0x00;
                        responseLength = (UInt32)descriptionMsg.Length;
                        responseValue = descriptionMsg;
                        responseData[0] = responseType;
                        Buffer.BlockCopy(BitConverter.GetBytes(responseLength), 0, responseData, 1, sizeof(UInt32));
                        Buffer.BlockCopy(Encoding.UTF8.GetBytes(responseValue), 0, responseData, 5, responseValue.Length);

                        msg = "description response message : " + responseValue;
                        if (OnReceived != null)
                            OnReceived(msg);

                        isDescriptionDone = true;

                        // response to client
                        msg = "Server to client(" + clientNo.ToString() + ") " + MessageCount.ToString();
                        if (OnReceived != null)
                            OnReceived(msg);

                        byte[] sbuffer = responseData; // Encoding.Unicode.GetBytes(msg);
                        stream.Write(sbuffer, 0, 1 + sizeof(UInt32) + responseValue.Length); //sbuffer.Length);
                        stream.Flush();

                        msg = " >> " + msg;
                        if (OnReceived != null)
                        {
                            OnReceived(msg);
                            OnReceived("");
                        }
                    }
#endif
                }
            }
            catch (SocketException se)
            {
                //Trace.WriteLine(string.Format("doHandle - SocketException : {0}", se.Message));
                string msg = "doHandle - SocketException " + se.Message;
                if (OnReceived != null)
                    OnReceived(msg);

                if (clientSocket != null)
                {
                    clientSocket.Close();
                    stream.Close();
                }

                if (OnCalculated != null)
                    OnCalculated();
            }
            catch (Exception ex)
            {
                //Trace.WriteLine(string.Format("doHandle - Exception : {0}", ex.Message));
                string msg = "doHandle - Exception " + ex.Message;
                if (OnReceived != null)
                    OnReceived(msg);

                if (clientSocket != null)
                {
                    clientSocket.Close();
                    stream.Close();
                }

                if (OnCalculated != null)
                    OnCalculated();
            }

            // close TcpClient
            if (OnReceived != null)
                OnReceived(">> Closed client " + clientNo);

            if (clientSocket != null)
            {
                clientSocket.Close();
                stream.Close();
            }
        }

        private void SendDescription(NetworkStream stream, string descriptionMsg)
        {
            string msg = "Connection from client. Try to send description data...";

            if (OnReceived != null)
                OnReceived(msg);

            byte responseType;
            UInt32 responseLength;
            string responseValue;
            byte[] responseData = new byte[1024];

            // Make response data (description data)
            responseType = 0x00;
            responseLength = (UInt32)descriptionMsg.Length;
            responseValue = descriptionMsg;
            responseData[0] = responseType;
            Buffer.BlockCopy(BitConverter.GetBytes(responseLength), 0, responseData, 1, sizeof(UInt32));
            Buffer.BlockCopy(Encoding.UTF8.GetBytes(responseValue), 0, responseData, 5, responseValue.Length);

            msg = "description response message : " + responseValue;
            if (OnReceived != null)
                OnReceived(msg);

            //isDescriptionDone = true;

            // response to client
            msg = "Server to client(" + clientNo.ToString() + ") Description First";
            if (OnReceived != null)
                OnReceived(msg);

            byte[] sbuffer = responseData; // Encoding.Unicode.GetBytes(msg);
            stream.Write(sbuffer, 0, 1 + sizeof(UInt32) + responseValue.Length); //sbuffer.Length);
            stream.Flush();

            msg = " >> " + msg;
            if (OnReceived != null)
            {
                OnReceived(msg);
                OnReceived("");
            }
        }
    }

    public class Value
    {
        public string Command { get; set; }
        public string SessionID { get; set; }
        public string TransactionID { get; set; }
        public string Result { get; set; } 
        public Params Params { get; set; }
    }

    public class Params
    {
        public int FrameRate { get; set; }
        public string Resolution { get; set; }
        public bool KeyFrameOnly { get; set; }
        public int KeyFramePerSec { get; set; }
        public string VideoCODEC { get; set; }
        public string AudioCODEC { get; set; }
        public int Channel { get; set; }
        public int TargetKBPS { get; set; }
        public int Heartbeat { get; set; }
        public int MaxFrameSize { get; set; }
        public string Log { get; set; }
        public int MediaPortForVideo { get; set; }
        public int MediaPortForAudio { get; set; }
    }

}
