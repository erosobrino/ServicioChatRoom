using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServicioChatRoom
{
    public partial class ServicioChatRoom : ServiceBase
    {
        List<ClientData> data;
        private static readonly object l = new object();
        private int time = 0;
        int puertoDefecto = 31415;
        bool stop = false;
        public ServicioChatRoom()
        {
            InitializeComponent();
        }
        protected override void OnStart(string[] args)
        {
            escribeEvento("Ejecutando OnStart");
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = 60000; //Cada 60 segundos
            timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
            timer.Start();

            data = new List<ClientData>();

            Thread hiloPrincipal = new Thread(() => startMainPogram());
            hiloPrincipal.IsBackground = true;
            hiloPrincipal.Start();
        }

        protected override void OnStop()
        {
            escribeEvento("Deteniendo Servicio");
            stop = true;
            time = 0;
        }

        public void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
        {
            time += 60;
            escribeEvento(string.Format("Servicio en ejecución durante {0} segundos", time));
        }

        public void escribeEvento(string mensaje)
        {
            string nombre = "ChatRoom";
            string logDestino = "Application";
            if (!EventLog.SourceExists(nombre))
            {
                EventLog.CreateEventSource(nombre, logDestino);
            }
            EventLog.WriteEntry(nombre, mensaje);
        }

        private void startMainPogram()
        {
            string path = Environment.GetEnvironmentVariable("allusersprofile") + "\\configChatRoom.txt";
            IPEndPoint ie;
            Socket s;
            int puerto = -1;
            try
            {
                using (StreamReader sr = new StreamReader(path))
                {
                    try
                    {
                        puerto = Convert.ToInt32(sr.ReadLine());
                    }
                    catch (FormatException)
                    {
                        escribeEvento("Error en el archivo de configuracion en " + path + ", se utilizara el puerto por defecto");
                    }
                    catch (OverflowException)
                    {
                        escribeEvento("Error el archivo de configuracion en " + path + ", se utilizara el puerto por defecto");
                    }
                }
            }
            catch (FileNotFoundException)
            {
                escribeEvento("Falta el archivo de configuracion en " + path + ", se utilizara el puerto por defecto");
            }
            try
            {
                if (puerto == -1)
                    puerto = puertoDefecto;

                ie = new IPEndPoint(IPAddress.Any, puerto);
                s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                s.Bind(ie);
                s.Listen(10);
                lanzaClientes(s, puerto);

            }
            catch (SocketException)
            {
                escribeEvento("Error en el Puerto, se usará predefinido, puedes modificarlo en " + path);
                try
                {
                    puerto = puertoDefecto;
                    ie = new IPEndPoint(IPAddress.Any, puerto);
                    s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    s.Bind(ie);
                    s.Listen(10);
                    lanzaClientes(s, puerto);
                }
                catch (SocketException)
                {
                    escribeEvento("Puerto por defecto ocupado");
                    this.Stop();
                }
            }

        }

        private void lanzaClientes(Socket s, int puerto)
        {
            escribeEvento("ChatRoom lanzado en el puerto " + puerto);
            while (!stop)
            {
                Socket client = s.Accept();
                Thread thread = new Thread(() => ClientThread(client));
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private void ClientThread(Socket client)
        {
            bool exit = false;
            string name = "";
            string message;
            try
            {
                ClientData clientdata;
                IPEndPoint ieCliente = (IPEndPoint)client.RemoteEndPoint;
                NetworkStream ns = new NetworkStream(client);
                StreamReader sr = new StreamReader(ns);
                StreamWriter sw = new StreamWriter(ns);
                string welcome = "Welcome to the chatroom, introduce your name to continue";
                sw.WriteLine(welcome);
                sw.Flush();
                string auxNombre = sr.ReadLine();
                name = auxNombre + "@" + ieCliente.Address;
                sw.WriteLine("Your name will be: " + name);
                sw.Flush();
                lock (l)
                {
                    clientdata = new ClientData(name, sw);
                    data.Add(clientdata);
                    foreach (ClientData clientdt in data)
                    {
                        if (auxNombre!=null)
                        {
                            if (clientdt.sw != sw)
                            {
                                try
                                {
                                    clientdt.sw.WriteLine(name + " has join");
                                    clientdt.sw.Flush();
                                }
                                catch (IOException) { }
                            }
                        }
                    }
                }
                while (!exit)
                {
                    try
                    {
                        message = sr.ReadLine();
                        if (message != null)
                        {
                            if (message.Length > 0)
                            {
                                switch (message)
                                {
                                    case "#exit":
                                        exit = true;
                                        break;
                                    case "#list":
                                        lock (l)
                                        {
                                            sw.WriteLine("\nThese are the clients");
                                            foreach (ClientData clientdt in data)
                                            {
                                                sw.WriteLine(clientdt.user);
                                            }
                                            sw.WriteLine();
                                        }
                                        sw.Flush();
                                        break;
                                    default:
                                        lock (l)
                                        {
                                            foreach (ClientData clientDt in data)
                                            {
                                                if (sw != clientDt.sw)
                                                {
                                                    clientDt.sw.WriteLine("{0}: {1}", name, message);
                                                    clientDt.sw.Flush();
                                                }
                                            }
                                        }
                                        sw.Flush();
                                        break;
                                }
                            }
                        }
                        else
                        {
                            break;//exit=true
                        }
                    }
                    catch (IOException)
                    {
                        break;
                    }
                }
                lock (l)
                {
                    data.Remove(clientdata);
                    if (clientdata.user.Length > 0)
                    {
                        if (auxNombre!=null)
                        {
                            foreach (ClientData clientDt in data)
                            {
                                try
                                {
                                    clientDt.sw.WriteLine(name + " has left");
                                    clientDt.sw.Flush();
                                }
                                catch (IOException) { }
                            }
                        }
                    }
                }
                sw.Flush();
                sw.Close();
                sr.Close();
                ns.Close();
            }
            catch (IOException) { }
            client.Close();
        }

        class ClientData
        {
            public string user;
            public StreamWriter sw;

            public ClientData(string user, StreamWriter sw)
            {
                this.user = user;
                this.sw = sw;
            }
        }
    }
}
