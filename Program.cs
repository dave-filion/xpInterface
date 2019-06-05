using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.IO.Ports;
using System.Threading;
using System.Threading.Channels;

namespace XPInterface
{
    class Program
    {


        static SerialPort _arduinoSerialPort;
        static bool _continueArduinoRead = false;

        static bool _continue = false;

        const int outPort = 49000;
        const int inPort = 49003;

        static readonly object dataMapLock = new object();
        static Dictionary<int, XPData> dataMap;

        static Channel<string> eventChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        static ChannelWriter<string> eventChannelWriter = eventChannel.Writer;
        static ChannelReader<string> eventChannelReader = eventChannel.Reader;

        // AP alt commands
        const string apAltHoldArm = "sim/autopilot/altitude_arm";
        const string apAltHold = "sim/autopilot/altitude_hold";

        // heading
        const string apHeadingUp = "sim/autopilot/heading_up";
        const string apHeadingDown = "sim/autopilot/heading_down";
        // sets AP heading to current heading
        const string apHeadingSync = "sim/autopilot/heading_sync";
        const string apHeadingHold = "sim/autopilot/heading_hold";
        const string apHeadingSelect = "sim/autopilot/heading";

        const string apAltUp = "sim/autopilot/altitude_up";
        const string apAltDown = "sim/autopilot/altitude_down";

        // airspeed
        const string airspeedUp = "sim/autopilot/airspeed_up";
        const string airspeedDown = "sim/autopilot/airspeed_down";

        // vertical speed
        const string apVsDown = "sim/autopilot/vertical_speed_down";
        const string apVsUp = "sim/autopilot/vertical_speed_up";

        const string apAltDatarefPath = "sim/cockpit/autopilot/altitude"; // float, writable
        const string apAltDatarefPath2 = "sim/cpckpit2/autopilot/altitude_dial_ft";

        // course knob
        const string radioObsHsiUp = "sim/radios/obs_HSI_up";
        const string radioObsHsiDowbn = "sim/radios/obs_HSI_down";

        // vnav only for b737 need to figure out a way to make work for other planes
        const string apVNAVMode = "laminar/B738/autopilot/vnav_press";
        const string apVSMode = "laminar/B738/autopilot/vs_press";
        const string altHoldMode = "laminar/B738/autopilot/alt_hld_press";
        const string approachMode = "laminar/B738/autopilot/app_press";
        const string headingSelMode = "laminar/B738/autopilot/hdg_sel_press";
        const string speedMode = "laminar/B738/autopilot/speed_press";
        const string lnavMode = "laminar/B738/autopilot/lnav_press";


        // maps btn labels to commands per plane, this is for ZIBO737
        static Dictionary<string, string> btnCommandMap = new Dictionary<string, string>();

        static UdpClient toXPSocket;

        // for testing
        static List<string> commandsToRun = new List<string>
        {
            //apAltHoldArm,
            //altHoldMode
            //apHeadingUp,
            //apHeadingDown,
            //apHeadingSelect,
            //apHeadingSync,
            //apHeadingHold,
            //apAltUp,
            //apAltDown,
            //airspeedDown,
            //apVsDown,
            //apVsUp,
            //radioObsHsiUp,
            //radioObsHsiDowbn,
            //apVNAVMode
            //apVSMode
            //approachMode,
            //headingSelMode,
            //speedMode,
            //lnavMode
        };


        static void Main(string[] args)
        {
            Console.WriteLine("Starting");
            _continue = true;

            // make command map, should be better way to do this
            btnCommandMap["1"] = headingSelMode;
            btnCommandMap["2"] = apVSMode;
            btnCommandMap["3"] = apVNAVMode;
            btnCommandMap["4"] = altHoldMode;
            btnCommandMap["5"] = approachMode;
            btnCommandMap["6"] = lnavMode;
            btnCommandMap["7"] = apAltUp;
            btnCommandMap["8"] = apAltDown;


            // set up arduino connection
            _arduinoSerialPort = new SerialPort("COM4");
            _arduinoSerialPort.BaudRate = 9600;

            // open arduino read thread
            _continueArduinoRead = true;
            Thread arduinoReadThread = new Thread(ListenToArduino);

            try
            {
                _arduinoSerialPort.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine("Couldnt open arduino stream: {0}", e);
                System.Environment.Exit(1);
            }
            arduinoReadThread.Start();

            // Init data state
            dataMap = new Dictionary<int, XPData>();

            Thread readStateThread = new Thread(ListenToXP);
            readStateThread.Start();

            toXPSocket = new UdpClient("192.168.0.4", outPort);

            while (!Console.KeyAvailable)
            {
                processIncomingEvents();
            }


            _continue = false;
            _continueArduinoRead = false;

            _arduinoSerialPort.Close();

            Thread.Sleep(1000);
        }

        private static async void processIncomingEvents()
        {
            while (await eventChannelReader.WaitToReadAsync())
            {
                if (eventChannelReader.TryRead(out string ev))
                {
                    Console.WriteLine("Received event: {0}", ev);

                    try
                    {
                        string[] evPartts = ev.Split('_');
                        string evType = evPartts[1];
                        string val = evPartts[0];

                        string xpCmd = "noop";
                        switch(evType)
                        {
                            case "btn":
                                xpCmd = btnCommandMap[val];
                                break;
                            default:
                                Console.WriteLine("unknown event type: {0}", evType);
                                break;
                        }

                        Console.WriteLine("running command in xp: {0}", xpCmd);
                        sendToXP(xpCmd);

                    } catch(Exception e)
                    {
                        Console.WriteLine("error processing arduino event: {0}", e.Message);
                    }
                }
            }

        }

        private static void sendToXP(string cmd)
        {
            try
            {
                IEnumerable<byte> cmdbytes = getCommandBytes(cmd);
                toXPSocket.Send(cmdbytes.ToArray(), cmdbytes.Count());
            } catch (Exception e)
            {
                Console.WriteLine("Error sending command to XP: {0}", e.Message);
            }
        }

        private static void testCommands(UdpClient toXP)
        {
            foreach (string command in commandsToRun)
            {
                Console.WriteLine("Command: " + command);
                // try each 5 times
                for (int i = 0; i < 5; i++)
                {
                    Console.WriteLine("Try: " + i);
                    try
                    {
                        IEnumerable<byte> toSend = getCommandBytes(command);
                        toXP.Send(toSend.ToArray(), toSend.Count());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    Thread.Sleep(500);
                }

            }
            Console.WriteLine("Done with test");
        }

        private static IEnumerable<byte> getCommandBytes(string commandString)
        {
            byte[] zero = new byte[1] { 0 };
            string cmd = commandString;
            byte[] cmdB = Encoding.ASCII.GetBytes(cmd);
            return Encoding.ASCII.GetBytes("CMND").Concat(zero).Concat(cmdB);
        }


        private static IEnumerable<byte> getDREFBytes()
        {

            // prefix
            byte[] prefix = Encoding.ASCII.GetBytes("DREF");

            // next is 0
            byte[] zero = new byte[1] { 0 };

            // next is 4 byte value of 1
            float alt = 2200;
            byte[] val = BitConverter.GetBytes(alt);
            //for (int i = 0; i < val.Length; i++)
            //{
            //    Console.WriteLine("val[" + i + "] =" + val);
            //}

            // dref path
            byte[] drefPath = Encoding.ASCII.GetBytes(apAltDatarefPath2);

            IEnumerable<byte> withoutSpaces = prefix.Concat(zero).Concat(val).Concat(drefPath).Concat(zero);

            Console.WriteLine("without spaces length: " + withoutSpaces.Count());
            int neededSpaces = 509 - withoutSpaces.Count();
            Console.WriteLine(neededSpaces + " spaces required");
            byte[] spaces = new byte[neededSpaces];
            for (int i = 0; i < neededSpaces; i++)
            {
                spaces[i] = 32; // space character
            }

            return withoutSpaces.Concat(spaces);
        }

        private static XPData processMessage(byte[] data, int startIndex)
        {
            // This is an index number that corresponds to a specific dataset in x plane
            int datasetIndex = Convert.ToInt32(data[startIndex]);

            // gather up to 8 float values
            List<float> allVals = new List<float>();
            int j = 0;
            byte[] dataBytes = new byte[4];
            for (int i = (startIndex + 4); i < (startIndex + 4 + 32); i++)
            {
                if (j == 4)
                {
                    float val = BitConverter.ToSingle(dataBytes, 0);
                    allVals.Add(val);
                    j = 0;
                }

                byte b = data[i];
                //                Console.WriteLine("data[" + i + "] =" + Convert.ToString(b, 2));
                dataBytes[j] = b;
                j++;
            }

            string label = String.Empty;
            float actualVal = 0;
            switch (datasetIndex)
            {
                // speed
                case 3:
                    label = string.Format("SPEED: {0,3:##0}", allVals[0]) + " KIAS";
                    break;
                // flaps
                case 13:
                    label = "flaps: " + allVals[3];
                    break;
                // magnetic dir
                case 19:
                    label = string.Format("BRG: {0,2:##}", allVals[0]);
                    break;
                // long/lat/alt
                case 20:
                    label = "ALT";
                    break;
                // ap values
                case 118:
                    label = "DIALED ALT";
                    actualVal = allVals[3];
                    break;

                    // autopilot armed status: 
                    // 0->nav arm 
                    // 1-> alt arm 
                    // 2 -> app arm 
                    // 3-> vnam enable 
                    // 4->vnav arm 
                    // 5->vnav time 
                    // 6->gp enabl 
                case 116:
                    label = "AP ARM";
                    break;

                    // autopilot modesa
                    //0 auto throt
                    //1 mode heading
                    //2 mode alt
                    //3 EMPTY
                    //4 bac ?
                    //5 app ?
                    //6 empty
                    //7 sync btn 
                case 117:
                    label = "Heading active";
                    actualVal = allVals[1];
                    break;

                default:
                    label = "unknown dataindex: " + datasetIndex;
                    break;
            }

            //foreach (float val in allVals)
            //{
            //    Console.WriteLine("-> " + val);
            //}


            return new XPData(label, datasetIndex, actualVal);
        }


        // XP read state
        public static void ListenToXP()
        {

            UdpClient readXP = new UdpClient(inPort);
            IPEndPoint xplane = new IPEndPoint(IPAddress.Any, 0);

            Console.WriteLine("Reading thread started...");
            while (_continue)
            {
                try
                {
                    byte[] readData = readXP.Receive(ref xplane);
                    int numDataSets = (readData.Length - 5) / 36;
                    for (int i = 0; i < numDataSets; i++)
                    {
                        XPData xpdata = processMessage(readData, (i * 36) + 5);
                        lock (dataMapLock)
                        {
                            dataMap[xpdata.dataIndex] = xpdata;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception Reading from Arduino: {0}", e);
                }
            }
        }

        public static void ListenToArduino()
        {
            Console.WriteLine("Arduino listen thread started...");
            while (_continueArduinoRead)
            {
                try
                {
                    string message = _arduinoSerialPort.ReadLine();
                    List<string> events = parseArduinoMessage(message);
                    Console.WriteLine("ARD READ-> {0}", message);

                    // write events to channel
                    foreach (string e in events)
                    {
                        bool ok = eventChannelWriter.TryWrite(e);
                        if (!ok)
                        {
                            Console.WriteLine("Write to channel failed!");
                        }
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception Reading from Arduino: {0}", e);
                }
            }
        }

        public static List<string> parseArduinoMessage(string message)
        {
            List<string> events = new List<string>();

            string[] pieces = message.Split('|');
            foreach (string piece in pieces)
            {
                if (piece.StartsWith("DONE"))
                {
                    // DONE denotes end of message
                    break;
                }
                else
                {
                    // add events into event queue for main thread to process and send to x plane
                    events.Add(piece);
                }
            }
            return events;
        }
    }

    struct XPData
    {
        public string message;
        public int dataIndex;
        public float val;

        public XPData(string msg, int di, float value)
        {
            message = msg;
            dataIndex = di;
            val = value;
        }

    }



}

