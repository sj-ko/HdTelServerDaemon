﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Xml;

namespace HdTelNvrDaemon
{
    public partial class Form1 : Form
    {
        static int counter = 0;
        TcpListener serverSocket = null;
        TcpClient clientSocket = null;

        string[] channelUrlList = new string[4]; // max 4 channel

        public Form1()
        {
            InitializeComponent();

            ReadChannelList();

            // socket start
            new Thread(delegate ()
            {
                TcpServer(IPAddress.Any, 9100);
            }).Start();
        }

        private void TcpServer(IPAddress serverIp, int port)
        {
            serverSocket = new TcpListener(serverIp, port);
            clientSocket = default(TcpClient);
            serverSocket.Start();
            DisplayText(">> Server Started");

            while (true)
            {
                try
                {
                    counter++;
                    clientSocket = serverSocket.AcceptTcpClient();
                    DisplayText(">> Accept connection from client");

                    ClientHandler h_client = new ClientHandler();
                    h_client.OnReceived += new ClientHandler.MessageDisplayHandler(DisplayText);
                    h_client.OnCalculated += new ClientHandler.CalculateClientCounter(CalculateCounter);
                    h_client.startClient(clientSocket, counter, channelUrlList);
                }
                catch (SocketException se)
                {
                    Trace.WriteLine(string.Format("InitSocket - SocketException : {0}", se.Message));
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(string.Format("InitSocket - Exception : {0}", ex.Message));
                    break;
                }
            }
        }

        private void CalculateCounter()
        {
            counter--;
        }

        private void DisplayText(string text)
        {
            if (richTextBox_log.InvokeRequired)
            {
                richTextBox_log.BeginInvoke(new MethodInvoker(delegate
                {
                    richTextBox_log.AppendText(text + Environment.NewLine);
                }));
            }
            else
                richTextBox_log.AppendText(text + Environment.NewLine);

        }

        private void ReadChannelList()
        {
            XmlDocument xml = new XmlDocument();
            xml.Load(@".\CameraChannel.xml");
            XmlNodeList nodeList = xml.GetElementsByTagName("Channel");

            foreach (XmlNode node in nodeList)
            {
                int number = Convert.ToInt32(node["Number"].InnerText, 10);
                channelUrlList[number - 1] = node["Address"].InnerText;
            }

            foreach (string addr in channelUrlList)
            {
                DisplayText("Channel : " + addr);
            }

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (clientSocket != null)
            {
                clientSocket.Close();
                clientSocket = null;
            }

            if (serverSocket != null)
            {
                serverSocket.Stop();
                serverSocket = null;
            }
        }
    }
}