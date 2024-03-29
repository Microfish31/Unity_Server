﻿using IP_Detect;
using Server_TCP;
using Server_UDP;
using Struct_Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Unity_Data;

// 沒有網際網路連線也可正常 需偵測

namespace VR_Server
{
    class VR_Server
    {
        // 建立伺服端 65536--> byte size
        static ServerTCP TcpServer = new ServerTCP(65536);
        static ServerUDP UdpServer = new ServerUDP(4096);

        // 定義集合，存客戶端資料
        static Dictionary<string, Socket> clients_socket = new Dictionary<string, Socket> { };
        static Dictionary<string, UnityClientStruct> clients_data_struct = new Dictionary<string, UnityClientStruct> { };
        static Dictionary<int, IPEndPoint> clients_udpSockets = new Dictionary<int, IPEndPoint> { };

        // 定義集合，存取物件資料
        static Dictionary<string, UnityObjectData> object_data = new Dictionary<string, UnityObjectData> { };

        static void Main(string[] args)
        {
            UnityObjectInitial();
            IpDetect ip_detect = new IpDetect();
            string now_path = System.Environment.CurrentDirectory;
            string server_ip = ip_detect.Read_Ip_Fromtxt(now_path);
            int server_port = 3000;
            StructJson js = new StructJson();

            if (TcpServer.TcpINI(server_ip, server_port) == 1)
            {
                TcpServer.TCPReceiveStart(Watch_Connecting);
                UdpServer.UdpInI(server_ip, server_port);
                UdpServer.UdpReceiveStart(UDPGetMsg);
                Console.WriteLine("port >> {0}", UdpServer.local_port);

                Socket[] socket_array;
                UnityClientStruct[] client_array;
                int client_count;
                object lock_clone = new object();

                while (true)
                {
                    lock (lock_clone)
                    {
                        socket_array = clients_socket.Values.ToArray<Socket>();
                        client_array = clients_data_struct.Values.ToArray<UnityClientStruct>();
                        client_count = socket_array.Length;
                    }
                    if (client_count != 0)
                        TcpServer.Server_Send(0, socket_array, js.StructToBytes<UnityClientStruct>(client_array), client_count);
                    Thread.Sleep(30);
                }
            }
        }

        static void Watch_Connecting()
        {
            int client_count;
            string remoteEndPoint;
            object locker = new Object();

            while (true)
            {
                try
                {
                    TcpServer.client = TcpServer.server.Accept();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    break;
                }

                TcpServer.TCPReceiveStart(Client_Receive, TcpServer.client);

                remoteEndPoint = TcpServer.client.RemoteEndPoint.ToString();

                client_count = clients_socket.Count + 1;

                TcpServer.Server_Send(3, TcpServer.client, (client_count - 1).ToString());

                clients_socket[remoteEndPoint] = TcpServer.client;

                Console.WriteLine("[ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ff") + " ] " + "( " + remoteEndPoint + " ) : Connection Success！ #" + client_count + "\n");
            }
        }

        static void Client_Receive(object client_para)
        {
            ServerTCP connecter = new ServerTCP(65536);
            connecter.client = client_para as Socket;
            connecter.client.ReceiveBufferSize = 65536;
            connecter.client.SendBufferSize = 65536;
            connecter.client.ReceiveTimeout = 2000;     
            
            int work_num;
            string addr = connecter.client.RemoteEndPoint.ToString();
            string port = (connecter.client.RemoteEndPoint as IPEndPoint).Port.ToString();

            Socket[] socket_array;
            UnityClientStruct[] client_array;
            UnityClientStruct unity_client_struct = new UnityClientStruct();
            UnityObjectStruct unity_obj_struct = new UnityObjectStruct();
            StructJson js = new StructJson();
            object locker = new object();

            while (true)
            {
                work_num = connecter.Server_Get();

                if (work_num != 0)
                {
                    switch (work_num)
                    {
                        case 1:
                            //Console.WriteLine("工作代號 0: 請求更新玩家資料");
                            unity_client_struct = js.BytesToStruct<UnityClientStruct>(connecter.get_byte_innner);
                            lock (locker) 
                            {
                                clients_data_struct[addr] = unity_client_struct;
                            }
                            break;
                        case 2 :
                            //Console.WriteLine("工作代號 1: 請求發送訊息給所有客戶端");
                            break;
                        case 3:
                            //Console.WriteLine("工作代號 2: 請求生成角色");  客戶端已經初始化
                            unity_client_struct = js.BytesToStruct<UnityClientStruct>(connecter.get_byte_innner);
                            lock (locker)
                            {
                                clients_data_struct[addr] = unity_client_struct;
                                socket_array = clients_socket.Values.ToArray<Socket>();
                            }
                            connecter.Server_Send(4,socket_array,connecter.get_byte_innner, socket_array.Length);
                            break;
                        case 4:
                            //Console.WriteLine("工作代號 3: 請求加載其他客戶端資料");
                            lock (locker)
                            {
                                socket_array = clients_socket.Values.ToArray<Socket>();
                                client_array = clients_data_struct.Values.ToArray<UnityClientStruct>();
                            }
                            connecter.Server_Send(5,socket_array, js.StructToBytes<UnityClientStruct>(client_array), socket_array.Length);
                            break;
                        case 5:
                            //Console.WriteLine("工作代號 4: 請求更新其他客戶端物件資料");  // 外殼拆除
                            unity_obj_struct = js.BytesToStruct<UnityObjectStruct>(connecter.get_byte_innner);
                            object_data[unity_obj_struct.Objname].Data = unity_obj_struct;
                            lock (locker)
                            {
                                socket_array = clients_socket.Values.ToArray<Socket>();
                            }
                            connecter.Server_Send(6,socket_array, connecter.get_byte_innner, socket_array.Length);
                            break;
                        case 6:
                            Console.WriteLine("工作代號 5: 請求更新其他客戶端物件資料(抓取軸)"); 
                            lock (locker)
                            {
                                socket_array = clients_socket.Values.ToArray<Socket>();
                            }
                            connecter.Server_Send(7, socket_array, "Start Renew Shaft", socket_array.Length);
                            break;
                    }
                }    
                else
                {
                    connecter.client.Close();
                    clients_socket.Remove(addr);
                    clients_data_struct.Remove(addr);
                    Console.WriteLine("[ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ff") + " ] ( " + addr + " ) : Has been disconnected!！ #" + clients_socket.Count + "\n");
                    lock (locker)
                    {
                        socket_array = clients_socket.Values.ToArray<Socket>();
                    }
                    TcpServer.Server_Send(2, socket_array, port, socket_array.Length);
                    break;
                }
            }
        }

        static void UDPGetMsg()
        {
            int work_num = -1;
            EndPoint[] clients_port;
            UnityObjectStruct unity_obj_struct = new UnityObjectStruct();
            StructJson js = new StructJson();
            object locker = new Object();
            Msg getMsg = new Msg();

            while (true)
            {
                work_num = UdpServer.Server_Get();

                if (work_num != 0)
                {
                    try
                    {
                        switch (work_num)
                        {
                            case 1:
                                //Console.WriteLine("工作代號 0: renew shaft pos");  
                                unity_obj_struct = js.BytesToStruct<UnityObjectStruct>(UdpServer.get_byte_innner);
                                unity_obj_struct.Port = UdpServer.GetRemotePort();
                                object_data[unity_obj_struct.Objname].Data = unity_obj_struct;
                                //Console.WriteLine("{0},{1},{2}",unity_obj_struct.Position.X, unity_obj_struct.Position.Y, unity_obj_struct.Position.Z);
                                clients_port = clients_udpSockets.Values.ToArray<EndPoint>();
                                js.StructFileWrite<UnityObjectStruct>(unity_obj_struct, "shaft_data.txt");
                                UdpServer.SendToClient(0, js.StructToBytes<UnityObjectStruct>(unity_obj_struct), clients_port);
                                break;
                            case 2:
                                //Console.WriteLine("工作代號 1: A new UDP enter");
                                getMsg = js.BytesToStruct<Msg>(UdpServer.get_byte_innner);
                                Console.WriteLine(getMsg.msg);
                                clients_udpSockets[UdpServer.GetRemotePort()] = UdpServer.PortDirection(UdpServer.GetRemotePort());
                                break;
                            case 3:
                                Console.WriteLine("工作代號 2: Client Ask Shaft Data");
                                //UdpServer.SendMessageToClient(0, socket_array, UdpServer.get_byte_innner, socket_array.Length);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        UdpServer.UdpServer.Close();
                        break;
                    }
                }
                else
                {
                    UdpServer.UdpServer.Close();
                    break;
                }
            }
        }

        static void UnityObjectInitial()
        {
            UnityObjectData obj = new UnityObjectData();

            obj.Data_ini("Cover", null, -1);
            object_data[obj.Data.Objname] = obj;

            obj.Data_ini("Shaft", null, -1);
            object_data[obj.Data.Objname] = obj;
        }
    }
}
