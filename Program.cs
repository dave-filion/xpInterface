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

namespace XPInterface
{
    class Program
    {
        static bool _continue = false;

        const int outPort = 49000;
        const int inPort = 49003;

        static readonly object dataMapLock = new object();
        static Dictionary<int, XPData> dataMap;

        // AP alt commands
        const string apAltUp = "sim/autopilot/altitude_up";
        const string apAltDown = "sim/autopilot/altitude_down";

        // airspeed
        const string airspeedDown = "sim/autopilot/airspeed_down";

        // vertical speed
        const string apVsDown = "sim/autopilot/vertical_speed_down";
        const string apVsUp = "sim/autopilot/vertical_speed_up";

        const string apAltDatarefPath = "sim/cockpit/autopilot/altitude"; // float, writable
        const string apAltDatarefPath2 = "sim/cpckpit2/autopilot/altitude_dial_ft";



        static void Main(string[] args)
        {
            Console.WriteLine("Starting");
            _continue = true;

            dataMap = new Dictionary<int, XPData>();

            Thread readStateThread = new Thread(Read);
            readStateThread.Start();

            UdpClient toXP = new UdpClient("192.168.0.4", outPort);

            // reading state in other thread
            // Writing
            // Increase altitude 5 times
            for (int i = 0; i < 10; i++)
            {
                // 118 is autopilot data
                if (dataMap.ContainsKey(118))
                {
                    XPData altState = dataMap[118];
                    Console.WriteLine("current: " + altState.message + " : " + altState.val);
                } else
                {
                    Console.WriteLine("No current alt");
                }

                try
                {
                    IEnumerable<byte> toSend = getCommandBytes(apAltUp);
                    toXP.Send(toSend.ToArray(), toSend.Count());
                } catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                Thread.Sleep(2000);
            }

            // decrease alt 2 times 
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    IEnumerable<byte> toSend = getCommandBytes(apAltDown);
                    toXP.Send(toSend.ToArray(), toSend.Count());
                } catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                Thread.Sleep(2000);
            }
            Console.WriteLine("Done with test");

            _continue = false;

            Thread.Sleep(1000);

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
        public static void Read()
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
                        lock(dataMapLock)
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

