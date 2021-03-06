﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace MahjongAI
{
    class MajsoulHelper
    {
        private static List<string> workingDirectories = new List<string> { "./MajsoulHelper", "../../../MajsoulHelper" };

        private Process process;

        public MajsoulHelper()
        {
            var workingDirectory = Path.GetFullPath(workingDirectories.First(path => Directory.Exists(path)));

            process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "node",
                    Arguments = "main.js",
                    WorkingDirectory = workingDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };

            process.Start();
            process.StandardInput.AutoFlush = true;
        }

        ~MajsoulHelper()
        {
            try
            {
                process.Kill();
            }
            catch { }
        }

        public byte[] encode(MajsoulMessage message)
        {
            Trace.TraceInformation("Send: {0}", JsonConvert.SerializeObject(message));
            send(JsonConvert.SerializeObject(new
            {
                action = "encode",
                methodName = message.MethodName,
                data = message.Data,
            }));
            string hex = recv();
            return StringToByteArray(hex);
        }

        public MajsoulMessage decode(byte[] buffer)
        {
            return decode(buffer, 0, buffer.Length);
        }

        public MajsoulMessage decode(byte[] buffer, int offset, int length)
        {
            string rawData = BitConverter.ToString(buffer, offset, length).Replace("-", "");
            send(JsonConvert.SerializeObject(new
            {
                action = "decode",
                data = rawData,
            }));

            return parseResult(recv());
        }

        public MajsoulMessage decodeActionPrototype(string actionName, byte[] data)
        {
            string rawData = BitConverter.ToString(data, 0, data.Length).Replace("-", "");
            send(JsonConvert.SerializeObject(new
            {
                action = "decodeActionPrototype",
                actionName,
                data = rawData,
            }));

            return parseResult(recv());
        }

        public MajsoulMessage parseResult(string str)
        {
            MajsoulMessage res;
            try
            {
                JObject obj = JObject.Parse(str);

                if ((bool)obj["success"])
                {
                    res = new MajsoulMessage
                    {
                        Success = true,
                        Type = (MajsoulMessageType)Enum.Parse(typeof(MajsoulMessageType), (string)obj["type"], true),
                        MethodName = (string)obj["methodName"],
                        Json = obj["data"],
                    };
                }
                else
                {
                    res = new MajsoulMessage
                    {
                        Success = false,
                        MethodName = (string)obj["methodName"],
                    };
                }
            }
            catch (Exception)
            {
                res = new MajsoulMessage
                {
                    Success = false,
                };
            }

            Trace.TraceInformation("Recv: {0}", JsonConvert.SerializeObject(res));
            return res;
        }

        private void send(string str)
        {
            lock (process)
            {
                //Console.WriteLine("Input: {0}", str);
                process.StandardInput.WriteLine(str);
            }
        }

        private string recv()
        {
            lock (process)
            {
                var str = process.StandardOutput.ReadLine();
                //Console.WriteLine("Output: {0}", str);
                return str;
            }
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2).Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16)).ToArray();
        }
    }

    class MajsoulMessage
    {
        public bool Success { get; set; } = true;
        public MajsoulMessageType Type { get; set; }
        public string MethodName { get; set; }
        public JToken Json { get; set; }
        public dynamic Data { get; set; }
    }

    enum MajsoulMessageType
    {
        REQUEST,
        RESPONSE,
        NOTIFICATION,
    }
}
